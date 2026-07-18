using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// First-run setup wizard: locate the game, install DML, create the Mods folder.
    ///
    /// On Linux, this replaces the original DMM's Setup.Generic() which:
    ///   1. Looked up the install path in the Windows registry (we use ProtonPrefixLocator instead)
    ///   2. Showed a Win32 OpenFileDialog (we use Avalonia's StorageProvider)
    ///   3. Downloaded DML and extracted it (we use DmlUpdateService, which uses SharpCompress
    ///      instead of the Windows-native 7z.dll)
    /// </summary>
    public class SetupService
    {
        private readonly DmlUpdateService _dml;
        private readonly ModService _mods;

        public SetupService(DmlUpdateService dml, ModService mods)
        {
            _dml = dml;
            _mods = mods;
        }

        /// <summary>
        /// Try to auto-detect the game install. Returns the game exe path on success, null on failure.
        /// </summary>
        public string? AutoDetectGameExe()
        {
            return Helpers.ProtonPrefixLocator.FindGameExe();
        }

        /// <summary>
        /// Run setup against a user-provided game exe path. Returns true on success.
        /// </summary>
        public async Task<bool> RunSetupAsync(string gameExePath, IProgress<string>? progress, CancellationTokenSource cts)
        {
            if (!File.Exists(gameExePath))
            {
                progress?.Report($"Game exe not found: {gameExePath}");
                return false;
            }

            var gameDir = Path.GetDirectoryName(gameExePath)!;
            progress?.Report($"Game directory: {gameDir}");

            // Save the launcher path
            Global.config!.Configs![Global.CurrentGame]!.Launcher = gameExePath;
            Global.config!.Configs![Global.CurrentGame]!.GamePath = gameDir;
            Global.config!.Configs![Global.CurrentGame]!.FirstOpen = false;

            // Check for existing DML
            var dll = Path.Combine(gameDir, "dinput8.dll");
            var toml = Path.Combine(gameDir, "config.toml");
            if (!File.Exists(dll) || !File.Exists(toml))
            {
                Global.config.Configs[Global.CurrentGame]!.ModLoaderVersion = null;
            }

            Global.UpdateConfig();

            // Install / update DML
            progress?.Report("Checking for DivaModLoader updates...");
            var ok = await _dml.CheckAndInstallAsync(gameDir, Global.config.Configs[Global.CurrentGame]!.ModLoaderVersion, false, cts);
            if (!ok)
            {
                progress?.Report("DML install failed. Check the log for details.");
                return false;
            }

            // Ensure config.toml has the mods field
            if (File.Exists(toml))
            {
                var text = File.ReadAllText(toml);
                if (Tomlyn.Toml.TryToModel(text, out Tomlyn.Model.TomlTable? config, out _))
                {
                    if (!config.ContainsKey("mods"))
                    {
                        config["enabled"] = true;
                        config["console"] = false;
                        config["mods"] = "mods";
                        File.WriteAllText(toml, Tomlyn.Toml.FromModel(config));
                    }
                }
            }

            // Create the Mods folder
            var modsFolder = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(modsFolder);
            Global.config.Configs[Global.CurrentGame]!.ModsFolder = modsFolder;
            Global.UpdateConfig();

            progress?.Report($"Setup complete! Mods folder: {modsFolder}");
            return true;
        }
    }
}
