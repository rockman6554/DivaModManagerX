using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Octokit;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using Tomlyn;
using Tomlyn.Model;
using DivaModManager.Models;
using DivaModManager.Helpers;

namespace DivaModManager.Services
{
    /// <summary>
    /// Manages DivaModLoader (DML): queries the blueskythlikesclouds/DivaModLoader GitHub repo
    /// for the latest release, downloads it, and extracts dinput8.dll + config.toml into the
    /// game's install directory.
    ///
    /// DML is a Windows dinput8.dll proxy — it loads when the game process starts. On Linux,
    /// Proton honours WINEDLLOVERRIDES=dinput8.dll=n,b to make the game load DML's DLL instead
    /// of Wine's builtin. We configure this via the LaunchService when invoking the game.
    /// </summary>
    public class DmlUpdateService
    {
        private const string Owner = "blueskythlikesclouds";
        private const string Repo = "DivaModLoader";
        private readonly GitHubClient _gh = new(new ProductHeaderValue("DivaModManagerLinux"));
        private readonly HttpClient _http = new();
        private readonly ZipExtractor _extractor = new();

        public event Action<string, float, long, long>? Progress;      // fileName, pct, downloaded, total
        public event Action<string>? LogInfo;
        public event Action<string>? LogError;

        public async Task<(string? version, string? downloadUrl, string? fileName, string? body)> GetLatestReleaseAsync()
        {
            try
            {
                var release = await _gh.Repository.Release.GetLatest(Owner, Repo);
                var versionMatch = Regex.Match(release.TagName, @"(?<version>([0-9]+\.?)+)[^a-zA-Z]");
                var version = versionMatch.Success ? versionMatch.Value : release.TagName;
                var asset = release.Assets.FirstOrDefault();
                return (version, asset?.BrowserDownloadUrl, asset?.Name, release.Body);
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"GitHub API error fetching DML release: {ex.Message}");
                return (null, null, null, null);
            }
        }

        public async Task<bool> CheckAndInstallAsync(string gameExeDir, string? localVersion,
            bool notifyOnSkip, CancellationTokenSource cts)
        {
            var (onlineVersion, url, fileName, body) = await GetLatestReleaseAsync();
            if (onlineVersion == null || url == null) return false;

            if (!UpdateAvailable(onlineVersion, localVersion))
            {
                if (notifyOnSkip) LogInfo?.Invoke($"DML is up-to-date (v{localVersion}).");
                return true;
            }

            LogInfo?.Invoke($"Downloading DivaModLoader v{onlineVersion}...");
            var downloadsDir = Path.Combine(Global.assemblyLocation, "Downloads", "DML");
            Directory.CreateDirectory(downloadsDir);
            var downloadPath = Path.Combine(downloadsDir, $"{onlineVersion}{Path.GetExtension(fileName)}");
            if (File.Exists(downloadPath)) try { File.Delete(downloadPath); } catch { }

            using (var fs = new FileStream(downloadPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                await _http.DownloadAsync(url, fs, fileName,
                    new Progress<DownloadProgress>(p => Progress?.Invoke(fileName ?? "DML", p.Percentage, p.DownloadedBytes, p.TotalBytes)),
                    cts.Token);
            }

            await _extractor.ExtractDmlFilesAsync(downloadPath, gameExeDir, cts.Token);
            try { File.Delete(downloadPath); } catch { }

            // Verify
            var dll = Path.Combine(gameExeDir, "dinput8.dll");
            var toml = Path.Combine(gameExeDir, "config.toml");
            if (!File.Exists(dll) || !File.Exists(toml))
            {
                LogError?.Invoke("DML install failed: dinput8.dll or config.toml missing after extraction.");
                return false;
            }

            // Make sure config.toml has the required fields
            EnsureDefaultConfigToml(toml);

            Global.config!.Configs![Global.CurrentGame]!.ModLoaderVersion = onlineVersion;
            Global.UpdateConfig();
            LogInfo?.Invoke($"DivaModLoader v{onlineVersion} installed to {gameExeDir}");
            return true;
        }

        private static void EnsureDefaultConfigToml(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);
            if (!Toml.TryToModel(text, out TomlTable? config, out _)) config = new TomlTable();
            if (!config.ContainsKey("enabled")) config["enabled"] = true;
            if (!config.ContainsKey("console")) config["console"] = false;
            if (!config.ContainsKey("mods")) config["mods"] = "mods";
            File.WriteAllText(tomlPath, Toml.FromModel(config));
        }

        public static bool UpdateAvailable(string? onlineVersion, string? localVersion)
        {
            if (onlineVersion is null) return false;
            if (localVersion is null) return true;
            var online = onlineVersion.Split('.');
            var local = localVersion.Split('.');
            var len = Math.Max(online.Length, local.Length);
            for (int i = 0; i < len; i++)
            {
                int o = i < online.Length && int.TryParse(online[i], out var ov) ? ov : 0;
                int l = i < local.Length && int.TryParse(local[i], out var lv) ? lv : 0;
                if (o > l) return true;
                if (o < l) return false;
            }
            return false;
        }
    }
}
