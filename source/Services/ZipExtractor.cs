using System;
using System.Collections.Generic;
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
    /// Extracts mod archives. Drops the original SevenZipExtractor dependency (which loaded a
    /// Windows-native 7z.dll) in favour of SharpCompress's pure-C# 7z reader.
    /// </summary>
    public class ZipExtractor
    {
        public async Task ExtractPackageAsync(string sourceFilePath, string destDirPath,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(destDirPath);
                if (Path.GetExtension(sourceFilePath).Equals(".7z", StringComparison.InvariantCultureIgnoreCase))
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
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Global.logger?.WriteLine($"Failed to extract {sourceFilePath} ({e.Message})", LoggerType.Error);
            }
            try { File.Delete(sourceFilePath); } catch { }
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
