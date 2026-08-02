using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Resolves the active main window for dialog ownership. ViewModels need a window
    /// reference to show modal dialogs but shouldn't hold a direct UI dependency, so this
    /// central helper fetches it from the running <see cref="IClassicDesktopStyleApplicationLifetime"/>.
    /// </summary>
    public static class MainWindowProvider
    {
        public static Window? GetMainWindow()
        {
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
    }
}
