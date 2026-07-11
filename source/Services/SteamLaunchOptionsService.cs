using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DivaModManager.Helpers;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// Reads and writes Steam's per-game launch options so DML's dinput8.dll proxy is
    /// actually loaded by Proton.
    ///
    /// On Linux, Steam stores per-user, per-game config at:
    ///   ~/.steam/steam/userdata/&lt;steamid&gt;/config/localconfig.vdf
    ///
    /// The structure contains a nested "apps" dictionary keyed by Steam AppID, and each
    /// app entry can have a "LaunchOptions" string that Steam inserts into the launch
    /// command's environment.
    ///
    /// For DML to load, we need:
    ///   "LaunchOptions" "WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%"
    ///
    /// If the user already has launch options set (e.g. for a different tool), we APPEND
    /// the WINEDLLOVERRIDES to their existing options rather than overwriting.
    /// </summary>
    public class SteamLaunchOptionsService
    {
        public const string MegaMixAppId = "1761390";
        public const string RequiredWineOverride = "WINEDLLOVERRIDES=\"dinput8.dll=n,b\"";

        /// <summary>
        /// Find all SteamID directories under userdata/. Returns empty list if Steam isn't installed.
        /// </summary>
        public List<string> FindSteamUsers()
        {
            var users = new List<string>();
            foreach (var root in Helpers.ProtonPrefixLocator.SteamRootCandidates())
            {
                var userdata = Path.Combine(root, "userdata");
                if (!Directory.Exists(userdata)) continue;
                foreach (var dir in Directory.GetDirectories(userdata))
                {
                    var name = Path.GetFileName(dir);
                    if (long.TryParse(name, out _)) users.Add(dir);
                }
            }
            return users;
        }

        /// <summary>
        /// Pick the most recently active Steam user (the one whose localconfig.vdf was touched last).
        /// </summary>
        public string? FindPrimaryUser()
        {
            var users = FindSteamUsers();
            if (users.Count == 0) return null;
            if (users.Count == 1) return users[0];

            string? best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var u in users)
            {
                var cfg = Path.Combine(u, "config", "localconfig.vdf");
                if (!File.Exists(cfg)) continue;
                var t = File.GetLastWriteTimeUtc(cfg);
                if (t > bestTime)
                {
                    bestTime = t;
                    best = u;
                }
            }
            return best ?? users[0];
        }

        public string? GetLocalConfigPath(string? userDir = null)
        {
            userDir ??= FindPrimaryUser();
            if (userDir == null) return null;
            var cfg = Path.Combine(userDir, "config", "localconfig.vdf");
            return File.Exists(cfg) ? cfg : null;
        }

        /// <summary>
        /// Check whether the launch options for appid 1761390 already contain the
        /// required WINEDLLOVERRIDES. Returns (found, currentOptions, configPath).
        /// </summary>
        public (bool found, string? currentOptions, string? configPath) CheckLaunchOptions()
        {
            var cfg = GetLocalConfigPath();
            if (cfg == null) return (false, null, null);

            try
            {
                var text = File.ReadAllText(cfg);
                var root = Helpers.VdfParser.Parse(text);
                var apps = root.GetChild("UserLocalConfigStore")
                               .GetChild("Software")
                               .GetChild("Valve")
                               .GetChild("Steam")
                               .GetChild("apps");
                var appEntry = apps.GetChild(MegaMixAppId);
                var opts = appEntry.GetChild("LaunchOptions").StringValue;
                var found = opts != null && opts.Contains("dinput8.dll=n,b");
                return (found, opts, cfg);
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Couldn't parse localconfig.vdf: {ex.Message}", LoggerType.Error);
                return (false, null, cfg);
            }
        }

        /// <summary>
        /// Write the WINEDLLOVERRIDES into Steam's localconfig.vdf for appid 1761390.
        /// Preserves any existing launch options (appends the override if not already present).
        /// Returns true on success.
        ///
        /// IMPORTANT: Steam must be restarted for changes to take effect. Steam caches
        /// localconfig.vdf in memory and overwrites it on exit — if Steam is running
        /// when we write, our changes will be lost.
        /// </summary>
        public bool EnsureLaunchOptions()
        {
            var cfg = GetLocalConfigPath();
            if (cfg == null)
            {
                Global.logger?.WriteLine("Could not find Steam's localconfig.vdf. Is Steam installed and have you logged in at least once?", LoggerType.Error);
                return false;
            }

            try
            {
                // Back up the file before modifying
                var backup = cfg + ".dmm-bak";
                if (!File.Exists(backup)) File.Copy(cfg, backup, overwrite: true);

                var text = File.ReadAllText(cfg);
                var root = Helpers.VdfParser.Parse(text);

                // Navigate to / create the apps dictionary
                var ulcs = root.GetChild("UserLocalConfigStore");
                if (!ulcs.IsObject)
                {
                    ulcs.StringValue = null;
                    ulcs.Children = new Dictionary<string, VdfValue>();
                    root.SetChild("UserLocalConfigStore", ulcs);
                }
                var sw = ulcs.GetChild("Software");
                if (!sw.IsObject) { sw.Children = new(); ulcs.SetChild("Software", sw); }
                var valve = sw.GetChild("Valve");
                if (!valve.IsObject) { valve.Children = new(); sw.SetChild("Valve", valve); }
                var steam = valve.GetChild("Steam");
                if (!steam.IsObject) { steam.Children = new(); valve.SetChild("Steam", steam); }
                var apps = steam.GetChild("apps");
                if (!apps.IsObject) { apps.Children = new(); steam.SetChild("apps", apps); }

                var appEntry = apps.GetChild(MegaMixAppId);
                if (!appEntry.IsObject) { appEntry.Children = new(); apps.SetChild(MegaMixAppId, appEntry); }

                var opts = appEntry.GetChild("LaunchOptions").StringValue ?? "";
                if (opts.Contains("dinput8.dll=n,b"))
                {
                    Global.logger?.WriteLine("Steam launch options already configured correctly.", LoggerType.Info);
                    return true;
                }

                // Append the override to existing options
                var newOpts = string.IsNullOrEmpty(opts)
                    ? $"{RequiredWineOverride} %command%"
                    : $"{RequiredWineOverride} {opts}";
                appEntry.SetChild("LaunchOptions", VdfValue.String(newOpts));

                // Serialize back, preserving the top-level structure
                var newText = Helpers.VdfParser.Serialize(root);
                File.WriteAllText(cfg, newText);

                Global.logger?.WriteLine($"Wrote WINEDLLOVERRIDES to Steam launch options for appid {MegaMixAppId}.", LoggerType.Info);
                Global.logger?.WriteLine("IMPORTANT: restart Steam for the change to take effect.", LoggerType.Warning);
                Global.logger?.WriteLine($"Backup of original config saved to {cfg}.dmm-bak", LoggerType.Info);
                return true;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Failed to write Steam launch options: {ex.Message}", LoggerType.Error);
                return false;
            }
        }
    }
}
