using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DivaModManager.Models
{
    // Property names stay lowercase because they are serialized as-is into Config.json.
    public class Mod : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _enabled;

        public string name
        {
            get => _name;
            set { if (_name != value) { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(name))); } }
        }

        public bool enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(enabled))); } }
        }

        /// <summary>
        /// Canonical category (Song/Cover/Module/UI/Plugin/Patch/Other), derived from the
        /// mod's mod.json on disk at load time. Not serialized to Config.json — it is always
        /// re-derived from the installed files so it stays in sync.
        /// </summary>
        [JsonIgnore]
        public string Category
        {
            get => _category;
            set { if (_category != value) { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category))); } }
        }
        private string _category = "Other";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class Metadata
    {
        public int? id { get; set; }
        public Uri? preview { get; set; }
        public string? submitter { get; set; }
        public Uri? avi { get; set; }
        public Uri? upic { get; set; }
        public Uri? caticon { get; set; }
        public string? cat { get; set; }
        public string? description { get; set; }
        public Uri? homepage { get; set; }
        public DateTime? lastupdate { get; set; }
    }

    public class Config
    {
        public string? CurrentGame { get; set; }
        public Dictionary<string, GameConfig>? Configs { get; set; }
        public double? LeftGridWidth { get; set; }
        public double? RightGridWidth { get; set; }
        public double? TopGridHeight { get; set; }
        public double? BottomGridHeight { get; set; }
        public double? Height { get; set; }
        public double? Width { get; set; }
        public bool Maximized { get; set; }
    }

    public class GameConfig
    {
        public string? Launcher { get; set; }
        public string? GamePath { get; set; }
        public bool LauncherOption { get; set; }
        public int LauncherOptionIndex { get; set; }
        public bool LauncherOptionConverted { get; set; }
        public bool FirstOpen { get; set; } = true;
        public string? ModsFolder { get; set; }
        public string? ModLoaderVersion { get; set; }
        public string? CurrentLoadout { get; set; } = "Default";
        public Dictionary<string, ObservableCollection<Mod>>? Loadouts { get; set; }
    }

    public class Choice
    {
        public string? OptionText { get; set; }
        public string? OptionSubText { get; set; }
        public int Index { get; set; }
    }
}
