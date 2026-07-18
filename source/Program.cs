using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Threading;

namespace DivaModManager;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle 1-click install URLs: divamodmanager://<url>
        if (args.Length > 1 && args[0] == "-download")
            PendingDownloadUrl = args[1];

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static string? PendingDownloadUrl { get; private set; }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 1024 * 600 * 4 * 12 })
            .LogToTrace();
}
