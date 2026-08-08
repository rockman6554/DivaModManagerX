using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DivaModManager.Helpers;
using DivaModManager.Models;

namespace DivaModManager.ViewModels;

/// <summary>
/// A group of installed mods sharing a canonical category (Song/Cover/Module/UI/Plugin/
/// Patch/Other). Rendered as a collapsible Expander in the main window's mod list.
/// </summary>
public partial class ModCategoryGroup : ObservableObject
{
    public string Category { get; set; } = CategoryNormalizer.Other;

    [ObservableProperty] private ObservableCollection<Mod> _mods = new();
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Emoji icon for the expander header (🎵/🎨/👗/🖥️/🔌/🔧/📦).</summary>
    public string Icon => CategoryNormalizer.Icon(Category);

    /// <summary>"Song (5)" style label shown next to the icon in the header.</summary>
    public string HeaderLabel => $"({Mods.Count})";

    partial void OnModsChanged(ObservableCollection<Mod> value)
        => OnPropertyChanged(nameof(HeaderLabel));
}
