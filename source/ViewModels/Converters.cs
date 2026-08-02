using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DivaModManager.ViewModels;

/// <summary>
/// Returns true when a collection's Count is 0 — used to show an empty-state overlay
/// (visible when there are no mods, hidden when there are).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count) return count == 0;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
