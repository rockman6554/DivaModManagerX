using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// Global state for DMM. Kept as a static class (matching original DMM) for compatibility
    /// with ported code paths that reference Global.config / Global.logger / etc.
    /// </summary>
    public static class Global
    {
        public static Config? config;
        public static Logger? logger;
        public static char s = Path.DirectorySeparatorChar;
        public static string assemblyLocation = AppDomain.CurrentDomain.BaseDirectory;
        public static List<string>? games;
        public static ObservableCollection<Mod>? ModList;
        public static ObservableCollection<string>? LoadoutItems;

        public static string CurrentGame => config?.CurrentGame ?? "Project DIVA Mega Mix+";

        public static void UpdateConfig()
        {
            if (config == null || config.Configs == null || config.CurrentGame == null) return;
            if (config.Configs[config.CurrentGame].Loadouts != null &&
                config.Configs[config.CurrentGame].CurrentLoadout != null &&
                ModList != null)
            {
                config.Configs[config.CurrentGame].Loadouts[config.Configs[config.CurrentGame].CurrentLoadout!] = ModList;
            }
            string configString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var isReady = false;
            while (!isReady)
            {
                try
                {
                    File.WriteAllText(Path.Combine(assemblyLocation, "Config.json"), configString);
                    isReady = true;
                }
                catch (Exception e)
                {
                    if (e.GetType() != typeof(IOException))
                    {
                        logger?.WriteLine($"Couldn't write to Config.json ({e.Message})", LoggerType.Error);
                        break;
                    }
                }
            }
        }
    }

    public enum LoggerType
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Logger that broadcasts log lines to any number of subscribed handlers.
    /// The Avalonia UI subscribes a handler that appends to an ObservableCollection bound to a ListBox.
    /// </summary>
    public class Logger
    {
        public event Action<DateTime, LoggerType, string>? OnLog;

        public Logger() { }

        public void WriteLine(string text, LoggerType type)
        {
            OnLog?.Invoke(DateTime.Now, type, text);
        }
    }
}
