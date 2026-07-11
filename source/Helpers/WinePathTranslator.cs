using System;
using System.Collections.Generic;
using System.Linq;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Translates Linux paths to/from Wine's Z:\ drive mapping used inside the Proton prefix.
    ///
    /// On a Proton prefix the entire Linux filesystem is reachable via Z:\, so a Linux path like
    ///   /home/z/games/MikuMegaMix
    /// maps to Wine notation:
    ///   Z:\home\z\games\MikuMegaMix
    ///
    /// DMM's Config.json historically stored Z:\ paths because that was how the Wine file picker
    /// exposed them. The Linux port stores Linux-native paths in Config.json and only translates
    /// to Wine notation when invoking processes inside the prefix.
    /// </summary>
    public static class WinePathTranslator
    {
        /// <summary>
        /// Convert a Linux path to a Wine Z:\ path. e.g. /home/z/x → Z:\home\z\x
        /// </summary>
        public static string LinuxToWine(string linuxPath)
        {
            if (string.IsNullOrEmpty(linuxPath)) return linuxPath;
            // Already a Wine path?
            if (linuxPath.Length >= 2 && (linuxPath[1] == ':' || linuxPath.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)))
                return linuxPath;
            // Drive-absolute Linux path
            if (linuxPath.StartsWith("/"))
            {
                var parts = linuxPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return "Z:\\" + string.Join("\\", parts);
            }
            return linuxPath;
        }

        /// <summary>
        /// Convert a Wine Z:\ path back to a Linux path. e.g. Z:\home\z\x → /home/z/x
        /// Also handles lowercase z:.
        /// </summary>
        public static string WineToLinux(string winePath)
        {
            if (string.IsNullOrEmpty(winePath)) return winePath;
            if (winePath.Length >= 2 && winePath[1] == ':')
            {
                var drive = char.ToUpperInvariant(winePath[0]);
                var rest = winePath.Substring(2).Replace('\\', '/');
                // Z: maps to /, C: maps to drive_c/ (caller must resolve relative to prefix)
                if (drive == 'Z')
                {
                    if (rest.StartsWith("/")) return rest;
                    return "/" + rest;
                }
                // For C: and other drives, return as-is — caller resolves prefix
                return winePath;
            }
            return winePath;
        }

        /// <summary>
        /// Detect whether a path appears to already be in Wine notation.
        /// </summary>
        public static bool IsWinePath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Length >= 2 && path[1] == ':';
        }
    }
}
