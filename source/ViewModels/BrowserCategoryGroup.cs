using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DivaModManager.Helpers;

namespace DivaModManager.ViewModels;

/// <summary>
/// A category bucket used by the DMA and GameBanana browser result lists. Each bucket
/// holds the post/record ViewModels that fell into one canonical category (after
/// <see cref="CategoryNormalizer"/> normalization), plus the icon + header label for display.
///
/// Rendered as a collapsible Expander above the flat results list, so the user can either
/// browse the whole page flat or drill into one category at a time.
/// </summary>
public partial class BrowserCategoryGroup : ObservableObject
{
    public string Category { get; set; } = CategoryNormalizer.Other;

    [ObservableProperty] private ObservableCollection<object> _items = new();
    [ObservableProperty] private bool _isExpanded = true;

    public string Icon => CategoryNormalizer.Icon(Category);
    public string HeaderLabel => $"({Items.Count})";
}
