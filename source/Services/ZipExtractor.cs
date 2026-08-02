using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// Extracts mod archives.
    ///
    /// Strategy:
    ///   1. For .7z files, use SharpCompress's SevenZipArchive (pure C#, no native deps).
    ///   2. For .zip/.tar/.gz, use SharpCompress's ReaderFactory (pure C#).
    ///   3. For .rar files (especially RAR v5, which SharpCompress does NOT support),
    ///      shell out to the system's <c>unrar</c> or <c>7z</c> tool if available.
    ///   4. As a last resort, try <c>7z</c> for any format SharpCompress can't handle.
    ///
    /// Errors are propagated to the caller (no silent swallowing) so the install flow
    /// can report a real failure to the user instead of silently leaving nothing installed.
    /// </summary>
    public class ZipExtractor
    {
        public async Task ExtractPackageAsync(string sourceFilePath, string destDirPath,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destDirPath);
            var ext = Path.GetExtension(sourceFilePath);

            // RAR files (esp. RAR5) are not supported by SharpCompress — use system tools.
            if (ext.Equals(".rar", StringComparison.InvariantCultureIgnoreCase))
            {
                var ok = await TryExtractWithSystemToolAsync(sourceFilePath, destDirPath, cancellationToken);
                if (ok)
                {
                    TryDelete(sourceFilePath);
                    return;
                }
                // Fall through to SharpCompress (works for old RAR4 sometimes) and let it throw.
            }

            Exception? sharpCompressError = null;
            try
            {
                if (ext.Equals(".7z", StringComparison.InvariantCultureIgnoreCase))
                {
                    using var archive = SevenZipArchive.Open(sourceFilePath);
                    var reader = archive.ExtractAllEntries();
                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!reader.Entry.IsDirectory)
                        {
                            reader.WriteEntryToDirectory(destDirPath, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }
                else
                {
                    using Stream stream = File.OpenRead(sourceFilePath);
                    using var reader = ReaderFactory.Open(stream);
                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!reader.Entry.IsDirectory)
                        {
                            reader.WriteEntryToDirectory(destDirPath, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }
                TryDelete(sourceFilePath);
                return; // success
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                sharpCompressError = e;
                Global.logger?.WriteLine($"SharpCompress failed on {Path.GetFileName(sourceFilePath)}: {e.Message}", LoggerType.Warning);
            }

            // SharpCompress failed — try 7z as a universal fallback for any format.
            Global.logger?.WriteLine($"Trying system 7z/unrar as fallback for {Path.GetFileName(sourceFilePath)}...", LoggerType.Info);
            var fallbackOk = await TryExtractWithSystemToolAsync(sourceFilePath, destDirPath, cancellationToken, forceAny: true);
            if (fallbackOk)
            {
                TryDelete(sourceFilePath);
                return;
            }

            // Everything failed — propagate the original SharpCompress error so the caller
            // reports a real failure to the user instead of silently installing nothing.
            throw new IOException(
                $"Could not extract {Path.GetFileName(sourceFilePath)}. " +
                $"SharpCompress: {sharpCompressError?.Message ?? "n/a"}. " +
                "For RAR5 archives, install 'unrar' or '7z' on your system.",
                sharpCompressError);
        }

        /// <summary>
        /// Try to extract using a system tool (unrar for .rar, 7z for anything else).
        /// Returns true on success, false if no suitable tool is available or it failed.
        /// </summary>
        private async Task<bool> TryExtractWithSystemToolAsync(string archivePath, string destDir, CancellationToken ct, bool forceAny = false)
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            string? tool = null;
            var args = "";

            if (ext == ".rar")
            {
                // Prefer unrar (best RAR support), fall back to 7z.
                if (File.Exists(ResolveToolPath("unrar")))
                {
                    tool = ResolveToolPath("unrar");
                    args = $"x -y -o+ \"{archivePath}\" \"{destDir}{Path.DirectorySeparatorChar}\"";
                }
                else if (File.Exists(ResolveToolPath("7z")))
                {
                    tool = ResolveToolPath("7z");
                    args = $"x -y -o\"{destDir}\" \"{archivePath}\"";
                }
            }
            else if (forceAny)
            {
                if (File.Exists(ResolveToolPath("7z")))
                {
                    tool = ResolveToolPath("7z");
                    args = $"x -y -o\"{destDir}\" \"{archivePath}\"";
                }
            }

            if (tool == null)
            {
                Global.logger?.WriteLine($"No system extraction tool found for {ext} archives.", LoggerType.Warning);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Global.logger?.WriteLine($"Running: {tool} {args}", LoggerType.Info);
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode != 0)
                {
                    var stderr = await proc.StandardError.ReadToEndAsync();
                    Global.logger?.WriteLine($"{Path.GetFileName(tool)} exited with code {proc.ExitCode}: {stderr}", LoggerType.Warning);
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Global.logger?.WriteLine($"System tool {Path.GetFileName(tool)} failed: {e.Message}", LoggerType.Warning);
                return false;
            }
        }

        /// <summary>Resolve a tool name to a full path, searching common locations.</summary>
        private static string ResolveToolPath(string name)
        {
            // Check PATH first
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
                // On some systems tools live in sbin
            }
            // Hardcoded fallbacks for common Linux locations
            var fallbacks = new[]
            {
                $"/usr/bin/{name}",
                $"/usr/sbin/{name}",
                $"/usr/local/bin/{name}",
                $"/bin/{name}",
            };
            foreach (var f in fallbacks)
                if (File.Exists(f)) return f;
            return name; // let the caller's File.Exists decide
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        /// <summary>
        /// Extract only the dinput8.dll + config.toml from a DML release archive (used by Setup).
        /// </summary>
        public async Task ExtractDmlFilesAsync(string archivePath, string outputDir, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(outputDir);
                try
                {
                    if (Path.GetExtension(archivePath).Equals(".7z", StringComparison.InvariantCultureIgnoreCase))
                    {
                        using var archive = SevenZipArchive.Open(archivePath);
                        var reader = archive.ExtractAllEntries();
                        while (reader.MoveToNextEntry())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (reader.Entry.IsDirectory) continue;
                            var key = (reader.Entry.Key ?? "").ToLowerInvariant();
                            if (key == "dinput8.dll" ||
                                (key == "config.toml" && !File.Exists(Path.Combine(outputDir, "config.toml"))))
                            {
                                reader.WriteEntryToDirectory(outputDir, new ExtractionOptions
                                {
                                    ExtractFullPath = false,
                                    Overwrite = true
                                });
                            }
                        }
                    }
                    else
                    {
                        using Stream stream = File.OpenRead(archivePath);
                        using var reader = ReaderFactory.Open(stream);
                        while (reader.MoveToNextEntry())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (reader.Entry.IsDirectory) continue;
                            var key = (reader.Entry.Key ?? "").ToLowerInvariant();
                            if (key == "dinput8.dll" ||
                                (key == "config.toml" && !File.Exists(Path.Combine(outputDir, "config.toml"))))
                            {
                                reader.WriteEntryToDirectory(outputDir, new ExtractionOptions
                                {
                                    ExtractFullPath = false,
                                    Overwrite = true
                                });
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Global.logger?.WriteLine($"Couldn't extract {archivePath} ({e.Message})", LoggerType.Error);
                }
            }, cancellationToken);
        }
    }
}
