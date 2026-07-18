using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Detects Steam Proton prefixes for a given Steam AppID.
    ///
    /// On Linux, Steam stores per-game Proton prefixes at:
    ///   ~/.steam/steam/steamapps/compatdata/{appid}/pfx      (newer layout)
    ///   ~/.local/share/Steam/steamapps/compatdata/{appid}/pfx
    ///   $STEAM_COMPAT_DATA_PATH/{appid}/pfx                   (Steam Play variable)
    ///
    /// The game itself is installed at:
    ///   ~/.steam/steam/steamapps/common/{game folder name}/
    /// </summary>
    public static class ProtonPrefixLocator
    {
        public const string MegaMixAppId = "1761390";
        public const string MegaMixGameFolder = "Hatsune Miku Project DIVA Mega Mix+";

        /// <summary>
        /// Standard search paths for the user's Steam library root.
        /// The first existing directory wins. Override with $STEAM_ROOT if needed.
        /// </summary>
        public static IEnumerable<string> SteamRootCandidates()
        {
            var env = Environment.GetEnvironmentVariable("STEAM_ROOT");
            if (!string.IsNullOrEmpty(env)) yield return env;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".steam", "root");
            // Flatpak Steam
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
        }

        /// <summary>
        /// Locate the Proton prefix directory for a Steam AppID. Returns null if not found.
        /// </summary>
        public static string? FindProtonPrefix(string appId)
        {
            // Honour $STEAM_COMPAT_DATA_PATH first (Steam itself sets this for the game process)
            var env = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            {
                // $STEAM_COMPAT_DATA_PATH already points at the per-game compatdata dir
                var pfx = Path.Combine(env, "pfx");
                if (Directory.Exists(pfx)) return pfx;
            }

            foreach (var root in SteamRootCandidates())
            {
                var compat = Path.Combine(root, "steamapps", "compatdata", appId);
                if (Directory.Exists(compat))
                {
                    var pfx = Path.Combine(compat, "pfx");
                    if (Directory.Exists(pfx)) return pfx;
                }
            }
            return null;
        }

        /// <summary>
        /// Locate the game install directory by scanning Steam library folders for the
        /// expected game subfolder. Returns null if not found.
        /// </summary>
        public static string? FindGameInstall()
        {
            foreach (var root in SteamRootCandidates())
            {
                // steamapps/libraryfolders.vdf lists all library paths
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                var libs = new List<string> { root };
                if (File.Exists(vdf))
                {
                    foreach (var line in File.ReadAllLines(vdf))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                        {
                            // e.g. "path"		"/mnt/games/SteamLibrary"
                            var idx = trimmed.IndexOf('"', 6);
                            if (idx > 0)
                            {
                                var end = trimmed.IndexOf('"', idx + 1);
                                if (end > 0)
                                {
                                    var p = trimmed.Substring(idx + 1, end - idx - 1)
                                        .Replace("\\\\", "\\").Replace("\\", "/");
                                    if (Directory.Exists(p)) libs.Add(p);
                                }
                            }
                        }
                    }
                }

                foreach (var lib in libs.Distinct())
                {
                    var candidate = Path.Combine(lib, "steamapps", "common", MegaMixGameFolder);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// Find the game's .exe inside the install directory.
        /// The Project DIVA Mega Mix+ exe is called "DivaMegaMix.exe" (literally, no "Mega Mix+" suffix).
        /// </summary>
        public static string? FindGameExe(string? installDir = null)
        {
            installDir ??= FindGameInstall();
            if (installDir == null) return null;
            var exe = Path.Combine(installDir, "DivaMegaMix.exe");
            return File.Exists(exe) ? exe : null;
        }
    }
}
