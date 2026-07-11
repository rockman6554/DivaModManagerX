using System;
using System.IO;
using System.Text.Json;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// Loads/saves Config.json from the application directory.
    /// Handles migration from old Windows-format configs (Z:\ paths) to Linux-native paths.
    /// </summary>
    public static class ConfigService
    {
        private static readonly string ConfigPath = Path.Combine(Global.assemblyLocation, "Config.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null
        };

        public static Config Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var configString = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<Config>(configString, JsonOpts) ?? new Config();
                    MigrateWindowsPaths(config);
                    return config;
                }
                catch (Exception e)
                {
                    Global.logger?.WriteLine($"Couldn't read Config.json ({e.Message})", LoggerType.Error);
                }
            }
            return CreateDefault();
        }

        public static Config CreateDefault()
        {
            var cfg = new Config
            {
                CurrentGame = "Project DIVA Mega Mix+",
                Configs = new System.Collections.Generic.Dictionary<string, GameConfig>
                {
                    ["Project DIVA Mega Mix+"] = new GameConfig
                    {
                        FirstOpen = true,
                        CurrentLoadout = "Default",
                        Loadouts = new System.Collections.Generic.Dictionary<string, System.Collections.ObjectModel.ObservableCollection<Mod>>
                        {
                            ["Default"] = new()
                        }
                    }
                },
                LeftGridWidth = 1.8,
                RightGridWidth = 1,
                TopGridHeight = 1.6,
                BottomGridHeight = 1,
                Height = 750,
                Width = 1280,
                Maximized = false
            };
            return cfg;
        }

        public static void Save(Config config)
        {
            Global.config = config;
            Global.UpdateConfig();
        }

        /// <summary>
        /// If the user imported an old Config.json from Wine/Windows, the GamePath/Launcher/ModsFolder
        /// values will be Z:\ paths. Translate them to Linux paths so the native UI can manipulate them.
        /// </summary>
        public static void MigrateWindowsPaths(Config config)
        {
            if (config.Configs == null) return;
            foreach (var kv in config.Configs)
            {
                var gc = kv.Value;
                if (!string.IsNullOrEmpty(gc.Launcher) && Helpers.WinePathTranslator.IsWinePath(gc.Launcher))
                    gc.Launcher = Helpers.WinePathTranslator.WineToLinux(gc.Launcher);
                if (!string.IsNullOrEmpty(gc.GamePath) && Helpers.WinePathTranslator.IsWinePath(gc.GamePath))
                    gc.GamePath = Helpers.WinePathTranslator.WineToLinux(gc.GamePath);
                if (!string.IsNullOrEmpty(gc.ModsFolder) && Helpers.WinePathTranslator.IsWinePath(gc.ModsFolder))
                    gc.ModsFolder = Helpers.WinePathTranslator.WineToLinux(gc.ModsFolder);
            }
        }
    }
}
