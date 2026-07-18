using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DivaModManager.Services;
using DivaModManager.ViewModels;

namespace DivaModManager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Bootstrap services
            var config = ConfigService.Load();
            Global.config = config;
            Global.logger = new Logger();

            // Pick up an optional 1-click install URL passed via divamodmanager:// protocol
            var pendingUrl = Program.PendingDownloadUrl;

            var mainVm = new MainWindowViewModel(pendingUrl);
            desktop.MainWindow = new Views.MainWindow
            {
                DataContext = mainVm,
                Width = config.Width ?? 1280,
                Height = config.Height ?? 750,
                WindowState = config.Maximized ? WindowState.Maximized : WindowState.Normal
            };

            // Persist window size on close
            desktop.MainWindow.Closing += (s, e) =>
            {
                config.Width = desktop.MainWindow.Width;
                config.Height = desktop.MainWindow.Height;
                config.Maximized = desktop.MainWindow.WindowState == WindowState.Maximized;
                ConfigService.Save(config);
            };

            desktop.Exit += (s, e) =>
            {
                // Final config save
                if (Global.config != null) ConfigService.Save(Global.config);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
