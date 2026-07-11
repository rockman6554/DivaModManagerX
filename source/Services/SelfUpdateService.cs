using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Octokit;
using DivaModManager.Models;
using DivaModManager.Helpers;

namespace DivaModManager.Services
{
    /// <summary>
    /// Self-update for DMM on Linux. Replaces Onova (which ships a Windows-only Updater.exe).
    ///
    /// Flow:
    ///   1. Query TekkaGB/DivaModManager GitHub releases
    ///   2. If a newer version exists, download the asset (assume it's a zip with a build of DMM)
    ///   3. Extract into a "staging" dir next to the running binary
    ///   4. Spawn a shell helper that swaps the staging dir over the running binary's directory
    ///      after the current process exits, then re-launches DMM.
    ///
    /// NOTE: This implementation checks for *asset filenames ending in -linux-x64.zip* so that
    /// when a Linux build is published, it's auto-picked up. If only Windows assets exist, the
    /// updater reports "no Linux build available for this release".
    /// </summary>
    public class SelfUpdateService
    {
        private const string Owner = "TekkaGB";
        private const string Repo = "DivaModManager";
        private readonly GitHubClient _gh = new(new ProductHeaderValue("DivaModManagerLinux"));
        private readonly HttpClient _http = new();
        private readonly ZipExtractor _extractor = new();

        public event Action<string, float, long, long>? Progress;
        public event Action<string>? LogInfo;
        public event Action<string>? LogError;

        public async Task<bool> CheckAndApplyUpdateAsync(CancellationTokenSource cts)
        {
            var localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
            try
            {
                var release = await _gh.Repository.Release.GetLatest(Owner, Repo);
                var match = Regex.Match(release.TagName, @"(?<version>([0-9]+\.?)+)[^a-zA-Z]");
                var onlineVersion = match.Success ? match.Value : release.TagName;
                if (!DmlUpdateService.UpdateAvailable(onlineVersion, localVersion))
                {
                    LogInfo?.Invoke($"DMM v{localVersion} is up to date (latest release: v{onlineVersion}).");
                    return false;
                }

                // Look for a Linux asset. We accept:
                //   -linux-x64.zip
                //   -linux-musl-x64.zip
                //   linux-x64.tar.gz
                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                     a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)));
                if (asset == null)
                {
                    LogInfo?.Invoke($"Release v{onlineVersion} has no Linux build. Skipping self-update. " +
                                    "If you installed via xbps, update with `xbps-install -u divamodmanager` instead.");
                    return false;
                }

                LogInfo?.Invoke($"Downloading DMM v{onlineVersion} ({asset.Name})...");
                var stagingDir = Path.Combine(Global.assemblyLocation, "Downloads", "DMMUpdate");
                Directory.CreateDirectory(stagingDir);
                var archivePath = Path.Combine(stagingDir, asset.Name);
                if (File.Exists(archivePath)) try { File.Delete(archivePath); } catch { }

                using (var fs = new FileStream(archivePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    await _http.DownloadAsync(asset.BrowserDownloadUrl, fs, asset.Name,
                        new Progress<DownloadProgress>(p => Progress?.Invoke(asset.Name, p.Percentage, p.DownloadedBytes, p.TotalBytes)),
                        cts.Token);
                }

                var extractDir = Path.Combine(stagingDir, "extracted");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                await _extractor.ExtractPackageAsync(archivePath, extractDir, null, cts.Token);

                var updaterScript = WriteUpdaterScript(stagingDir, extractDir, Global.assemblyLocation);
                LogInfo?.Invoke($"Update staged at {extractDir}. Restart DMM to apply (or run: bash {updaterScript}).");

                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"Self-update check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes a small bash script that:
        ///   1. Waits for the current DMM process to exit (caller passes its PID)
        ///   2. rsyncs the extracted dir over the install dir
        ///   3. Re-launches DMM
        /// </summary>
        private string WriteUpdaterScript(string stagingDir, string extractDir, string installDir)
        {
            var scriptPath = Path.Combine(stagingDir, "apply-update.sh");
            var pid = Environment.ProcessId;
            var exe = Path.Combine(installDir, "DivaModManager");
            var content = $@"#!/usr/bin/env bash
# Auto-generated by DivaModManager. Apply staged self-update.
set -euo pipefail
PID={pid}
EXTRACT_DIR=""{extractDir}""
INSTALL_DIR=""{installDir}""
EXE=""{exe}""

echo ""Waiting for DMM (PID $PID) to exit...""
while kill -0 $PID 2>/dev/null; do sleep 1; done

echo ""Applying update...""
mkdir -p ""$INSTALL_DIR""
# Preserve Config.json, Downloads/, Logs/ across the swap
rsync -a --delete --exclude=Config.json --exclude=Downloads --exclude=Logs --exclude=*.log \
    ""$EXTRACT_DIR/"" ""$INSTALL_DIR/""

echo ""Update applied. Restarting DMM...""
exec ""$EXE""
";
            File.WriteAllText(scriptPath, content);
            try { System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit(2000); } catch { }
            return scriptPath;
        }
    }
}
