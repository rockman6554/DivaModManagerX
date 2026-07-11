using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivaModManager.Models;
using DivaModManager.Services;

namespace DivaModManager.ViewModels;

public partial class LogEntry : ObservableObject
{
    public DateTime Timestamp { get; set; }
    public LoggerType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TypeTag => Type.ToString().ToUpperInvariant();
    public string Color => Type switch
    {
        LoggerType.Info => "#52FF00",
        LoggerType.Warning => "#FFFF00",
        LoggerType.Error => "#FFB0B0",
        _ => "#F2F2F2"
    };
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ModService _mods;
    private readonly DmlUpdateService _dml;
    private readonly SelfUpdateService _self;
    private readonly GameBananaService _gb;
    private readonly DmaService _dma;
    private readonly LaunchService _launch;
    private readonly SetupService _setup;
    private readonly SteamLaunchOptionsService _steamOpts;

    public ObservableCollection<Mod> ModList => _mods.ModList;
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<string> Loadouts { get; } = new();

    [ObservableProperty] private string _currentGame = "Project DIVA Mega Mix+";
    [ObservableProperty] private string? _selectedLoadout;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private string _progressLabel = string.Empty;
    [ObservableProperty] private Mod? _selectedMod;
    [ObservableProperty] private string _gameExePath = string.Empty;
    [ObservableProperty] private string _modsFolderPath = string.Empty;
    [ObservableProperty] private string _dmlVersion = "Not installed";
    [ObservableProperty] private string _steamStatus = "Unknown";

    private string? _pendingDownloadUrl;

    public MainWindowViewModel(string? pendingDownloadUrl = null)
    {
        _pendingDownloadUrl = pendingDownloadUrl;
        _mods = new ModService();
        _dml = new DmlUpdateService();
        _self = new SelfUpdateService();
        _gb = new GameBananaService();
        _dma = new DmaService();
        _launch = new LaunchService();
        _setup = new SetupService(_dml, _mods);
        _steamOpts = new SteamLaunchOptionsService();

        // Wire logger to LogEntries
        Global.logger!.OnLog += (ts, type, msg) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(new LogEntry { Timestamp = ts, Type = type, Message = msg });
                if (LogEntries.Count > 1000) LogEntries.RemoveAt(0);
            });
        };

        // Wire DML/self-update progress
        _dml.Progress += (name, pct, dl, total) => Dispatcher.UIThread.Post(() =>
        {
            ProgressVisible = true;
            ProgressLabel = $"Downloading {name}...";
            ProgressValue = pct * 100;
        });
        _dml.LogInfo += msg => Global.logger.WriteLine(msg, LoggerType.Info);
        _dml.LogError += msg => Global.logger.WriteLine(msg, LoggerType.Error);

        _self.Progress += (name, pct, dl, total) => Dispatcher.UIThread.Post(() =>
        {
            ProgressVisible = true;
            ProgressLabel = $"Downloading {name}...";
            ProgressValue = pct * 100;
        });
        _self.LogInfo += msg => Global.logger.WriteLine(msg, LoggerType.Info);
        _self.LogError += msg => Global.logger.WriteLine(msg, LoggerType.Error);

        Initialize();
    }

    private void Initialize()
    {
        Global.logger.WriteLine($"Launched Diva Mod Manager (Linux port) v1.3.1!", LoggerType.Info);
        Global.logger.WriteLine("Sandbox note: build environment is Debian 13; target distro is Void Linux.", LoggerType.Info);

        var cfg = Global.config!;
        var gameCfg = cfg.Configs![CurrentGame]!;

        // Populate loadouts
        Loadouts.Clear();
        if (gameCfg.Loadouts != null)
            foreach (var name in gameCfg.Loadouts.Keys)
                Loadouts.Add(name);
        if (string.IsNullOrEmpty(gameCfg.CurrentLoadout))
            gameCfg.CurrentLoadout = "Default";
        SelectedLoadout = gameCfg.CurrentLoadout;

        GameExePath = gameCfg.Launcher ?? "(not set)";
        ModsFolderPath = gameCfg.ModsFolder ?? "(not set)";
        DmlVersion = gameCfg.ModLoaderVersion ?? "Not installed";

        // Bind ModList to Global so ConfigService.UpdateConfig picks it up
        Global.ModList = _mods.ModList;
        Global.LoadoutItems = Loadouts;

        // Refresh mod list from disk
        if (!string.IsNullOrEmpty(gameCfg.ModsFolder) && System.IO.Directory.Exists(gameCfg.ModsFolder))
        {
            _mods.Refresh(gameCfg.ModsFolder);
            Global.logger.WriteLine($"Loaded {_mods.ModList.Count} mods from {gameCfg.ModsFolder}", LoggerType.Info);
        }
        else
        {
            if (gameCfg.FirstOpen)
                Global.logger.WriteLine("Click Setup to detect the game and install DivaModLoader.", LoggerType.Warning);
        }

        // Auto-detect game if first run
        if (gameCfg.FirstOpen)
        {
            var detected = _setup.AutoDetectGameExe();
            if (detected != null)
            {
                Global.logger.WriteLine($"Auto-detected game at {detected}", LoggerType.Info);
                GameExePath = detected;
            }
            else
            {
                Global.logger.WriteLine("Could not auto-detect the game. Click Setup and pick DivaMegaMix.exe manually.", LoggerType.Warning);
            }
        }

        // Check Steam launch options status
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var (found, _, _) = _steamOpts.CheckLaunchOptions();
            SteamStatus = found ? "Configured (DML will load)" : "NOT configured (mods won't load)";
            if (!found)
                Global.logger.WriteLine("Steam launch options missing WINEDLLOVERRIDES. Click 'Configure Steam' to fix.", LoggerType.Warning);
        });

        // Background update checks (non-blocking)
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await CheckForDmlUpdateAsync();
            await CheckForSelfUpdateAsync();
        });

        // Handle pending 1-click install URL
        if (!string.IsNullOrEmpty(_pendingDownloadUrl))
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await HandleOneClickInstallAsync(_pendingDownloadUrl!);
                _pendingDownloadUrl = null;
            });
        }
    }

    [RelayCommand]
    private async Task SetupAsync()
    {
        var detected = _setup.AutoDetectGameExe();
        if (detected == null)
        {
            // File picker
            var storage = App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = storage?.MainWindow;
            if (mainWindow != null)
            {
                var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select DivaMegaMix.exe from your Steam install folder",
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Executable")
                        {
                            Patterns = new[] { "DivaMegaMix.exe", "*.exe" }
                        }
                    },
                    AllowMultiple = false
                });
                if (files.Count > 0)
                    detected = files[0].Path.LocalPath;
            }
        }

        if (string.IsNullOrEmpty(detected))
        {
            Global.logger.WriteLine("Setup cancelled.", LoggerType.Warning);
            return;
        }

        IsBusy = true;
        ProgressVisible = true;
        ProgressLabel = "Running setup...";
        ProgressValue = 0;
        try
        {
            var cts = new System.Threading.CancellationTokenSource();
            var ok = await _setup.RunSetupAsync(detected, new Progress<string>(s =>
            {
                Dispatcher.UIThread.Post(() => ProgressLabel = s);
            }), cts);
            if (ok)
            {
                GameExePath = Global.config!.Configs![CurrentGame]!.Launcher!;
                ModsFolderPath = Global.config!.Configs![CurrentGame]!.ModsFolder!;
                DmlVersion = Global.config!.Configs![CurrentGame]!.ModLoaderVersion ?? "Not installed";
                _mods.Refresh(ModsFolderPath);
            }
        }
        catch (Exception ex)
        {
            Global.logger.WriteLine($"Setup failed: {ex.Message}", LoggerType.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressVisible = false;
        }
    }

    [RelayCommand]
    private async Task CheckForDmlUpdateAsync()
    {
        var gameCfg = Global.config!.Configs![CurrentGame]!;
        if (string.IsNullOrEmpty(gameCfg.Launcher) || !System.IO.File.Exists(gameCfg.Launcher))
        {
            Global.logger.WriteLine("Skipping DML update check: game exe not set.", LoggerType.Info);
            return;
        }
        var gameDir = System.IO.Path.GetDirectoryName(gameCfg.Launcher)!;
        Global.logger.WriteLine("Checking for DivaModLoader updates...", LoggerType.Info);
        await _dml.CheckAndInstallAsync(gameDir, gameCfg.ModLoaderVersion, true, new System.Threading.CancellationTokenSource());
        DmlVersion = gameCfg.ModLoaderVersion ?? "Not installed";
    }

    [RelayCommand]
    private async Task CheckForSelfUpdateAsync()
    {
        Global.logger.WriteLine("Checking for DMM self-update...", LoggerType.Info);
        await _self.CheckAndApplyUpdateAsync(new System.Threading.CancellationTokenSource());
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        var gameCfg = Global.config!.Configs![CurrentGame]!;
        Global.UpdateConfig();

        // Apply loadout to DML's config.toml before launching
        if (!string.IsNullOrEmpty(gameCfg.Launcher))
        {
            var gameDir = System.IO.Path.GetDirectoryName(gameCfg.Launcher)!;
            _mods.ApplyLoadoutToDml(gameDir);
        }

        // Pre-launch verification
        var (ok, failures, fixes) = _launch.VerifyLaunch(gameCfg.Launcher);
        if (!ok)
        {
            var msg = "Cannot launch:\n\n" + string.Join("\n", failures);
            if (fixes.Any(f => f.Contains("Auto-configure")))
                msg += "\n\nDo you want DMM to auto-configure Steam launch options now? (Steam will need to be restarted.)";
            Global.logger.WriteLine(msg, LoggerType.Error);
            // Try auto-config if that's the only fix
            if (fixes.Any(f => f.Contains("Auto-configure")) &&
                !failures.Any(f => f.Contains("dinput8.dll") || f.Contains("config.toml")))
            {
                var configured = _launch.AutoConfigureSteam();
                if (configured)
                {
                    var (ok2, _, _) = _launch.VerifyLaunch(gameCfg.Launcher);
                    if (!ok2)
                    {
                        Global.logger.WriteLine("Steam configured but other issues remain. See log above.", LoggerType.Warning);
                        return;
                    }
                    SteamStatus = "Configured (restart Steam to apply)";
                }
                else
                {
                    Global.logger.WriteLine("Auto-config failed. Set the launch option manually in Steam: WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%", LoggerType.Error);
                    return;
                }
            }
            else
            {
                return;
            }
        }

        _launch.LaunchViaSteam();
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        _launch.OpenModsFolder(Global.config!.Configs![CurrentGame]!.ModsFolder);
    }

    [RelayCommand]
    private void ToggleMod(Mod? mod)
    {
        if (mod == null) return;
        mod.enabled = !mod.enabled;
        Global.UpdateConfig();
        var gameCfg = Global.config!.Configs![CurrentGame]!;
        if (!string.IsNullOrEmpty(gameCfg.Launcher))
        {
            var gameDir = System.IO.Path.GetDirectoryName(gameCfg.Launcher)!;
            _mods.ApplyLoadoutToDml(gameDir);
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedMod == null) return;
        var idx = _mods.ModList.IndexOf(SelectedMod);
        if (idx > 0) _mods.Reorder(idx, idx - 1);
        Global.UpdateConfig();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedMod == null) return;
        var idx = _mods.ModList.IndexOf(SelectedMod);
        if (idx >= 0 && idx < _mods.ModList.Count - 1) _mods.Reorder(idx, idx + 1);
        Global.UpdateConfig();
    }

    [RelayCommand]
    private void SortAlphabetical()
    {
        var sorted = _mods.ModList.OrderBy(m => m.name, new Helpers.NaturalSort()).ToList();
        _mods.ModList.Clear();
        foreach (var m in sorted) _mods.ModList.Add(m);
        Global.UpdateConfig();
    }

    [RelayCommand]
    private void DeleteMod()
    {
        if (SelectedMod == null) return;
        var modsFolder = Global.config!.Configs![CurrentGame]!.ModsFolder;
        _mods.DeleteMod(modsFolder, SelectedMod.name);
        Global.UpdateConfig();
        Global.logger.WriteLine($"Deleted mod: {SelectedMod.name}", LoggerType.Warning);
    }

    [RelayCommand]
    private void RefreshMods()
    {
        var modsFolder = Global.config!.Configs![CurrentGame]!.ModsFolder;
        _mods.Refresh(modsFolder);
        Global.logger.WriteLine($"Refreshed: {_mods.ModList.Count} mods", LoggerType.Info);
    }

    [RelayCommand]
    private void AddLoadout()
    {
        // Simple prompt via a dialog (we'd typically use a real dialog; here we use a default name)
        var name = $"Loadout {Loadouts.Count + 1}";
        if (!Loadouts.Contains(name))
        {
            Loadouts.Add(name);
            Global.config!.Configs![CurrentGame]!.Loadouts![name] = new ObservableCollection<Mod>();
            SelectedLoadout = name;
            Global.UpdateConfig();
        }
    }

    [RelayCommand]
    private void DeleteLoadout()
    {
        if (SelectedLoadout == null || SelectedLoadout == "Default")
        {
            Global.logger.WriteLine("Cannot delete the Default loadout.", LoggerType.Warning);
            return;
        }
        var name = SelectedLoadout;
        Global.config!.Configs![CurrentGame]!.Loadouts!.Remove(name);
        Loadouts.Remove(name);
        SelectedLoadout = "Default";
        Global.UpdateConfig();
    }

    partial void OnSelectedLoadoutChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var gameCfg = Global.config!.Configs![CurrentGame]!;
        gameCfg.CurrentLoadout = value;
        if (gameCfg.Loadouts!.ContainsKey(value))
        {
            // Swap ModList contents
            _mods.ModList.Clear();
            foreach (var m in gameCfg.Loadouts[value])
                _mods.ModList.Add(m);
        }
        Global.UpdateConfig();
    }

    [RelayCommand]
    private void OpenGameBananaBrowser()
    {
        var window = new Views.GameBananaBrowserWindow();
        var desktop = App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow != null)
            window.ShowDialog(desktop.MainWindow);
        else
            window.Show();
        // Refresh mod list when browser closes
        window.Closed += (s, e) => RefreshMods();
    }

    [RelayCommand]
    private void OpenDmaBrowser()
    {
        var window = new Views.DmaBrowserWindow();
        var desktop = App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow != null)
            window.ShowDialog(desktop.MainWindow);
        else
            window.Show();
        window.Closed += (s, e) => RefreshMods();
    }

    [RelayCommand]
    private void ConfigureSteam()
    {
        var ok = _launch.AutoConfigureSteam();
        if (ok)
        {
            var (found, _, _) = _steamOpts.CheckLaunchOptions();
            SteamStatus = found ? "Configured (restart Steam to apply)" : "NOT configured";
        }
    }

    [RelayCommand]
    private void ForceKillGame()
    {
        Global.logger.WriteLine("Force-killing any running game processes...", LoggerType.Warning);
        _launch.ForceKillGame();
    }

    [RelayCommand]
    private async Task InstallFromUrlAsync()
    {
        var desktop = App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        if (mainWindow == null) return;

        var textBox = new Avalonia.Controls.TextBox
        {
            Watermark = "Paste a GameBanana mod URL (https://gamebanana.com/mods/...) or DMA URL (https://divamodarchive.com/posts/...)",
            MinWidth = 500,
            AcceptsReturn = false
        };
        var dialog = new Avalonia.Controls.Window
        {
            Title = "Install mod from URL",
            Width = 600,
            Height = 150,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
        };
        var installBtn = new Avalonia.Controls.Button
        {
            Content = "Install",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        installBtn.Click += (s, e) => dialog.Close(textBox.Text);
        dialog.Content = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 8,
            Children = { textBox, installBtn }
        };
        var url = await dialog.ShowDialog<string?>(mainWindow);
        if (string.IsNullOrEmpty(url)) return;
        await HandleOneClickInstallAsync(url);
    }

    private async Task HandleOneClickInstallAsync(string url)
    {
        // Detect URL type
        string? source = null;
        int id = 0;
        string? directUrl = null;

        if (url.StartsWith("divamodmanager:", StringComparison.OrdinalIgnoreCase))
        {
            (source, id, directUrl) = GameBananaService.ParseProtocolUrl(url);
        }
        else if (url.Contains("gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            // Extract mod ID from URL like https://gamebanana.com/mods/693226
            var match = System.Text.RegularExpressions.Regex.Match(url, @"/mods/(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out id))
                source = "gamebanana";
        }
        else if (url.Contains("divamodarchive.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(url, @"/posts/(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out id))
                source = "dma";
        }

        if (source == null)
        {
            Global.logger.WriteLine($"Unrecognized URL format: {url}", LoggerType.Error);
            return;
        }

        var modsFolder = Global.config?.Configs?[CurrentGame]?.ModsFolder;
        if (string.IsNullOrEmpty(modsFolder) || !System.IO.Directory.Exists(modsFolder))
        {
            Global.logger.WriteLine("Mods folder not set. Run Setup first.", LoggerType.Warning);
            return;
        }

        ProgressVisible = true;
        ProgressValue = 0;
        var cts = new System.Threading.CancellationTokenSource();
        try
        {
            if (source == "gamebanana")
            {
                _gb.DownloadProgress += OnInstallProgress;
                var item = await _gb.FetchItemAsync(id);
                if (item == null || item.Files == null || item.Files.Count == 0)
                {
                    Global.logger.WriteLine($"GameBanana mod {id} has no downloadable files.", LoggerType.Error);
                    return;
                }
                var file = item.Files[0];
                var record = new GameBananaRecord
                {
                    Title = item.Title,
                    Owner = new GameBananaMember { Name = item.Owner?.Name, Avatar = item.Owner?.Avatar, Upic = item.Owner?.Upic },
                    Description = item.Description,
                    Link = item.Link,
                    Category = item.Category,
                    RootCategory = item.RootCategory,
                    Media = item.Media,
                    AllFiles = item.Files,
                    DateUpdatedLong = (long)(item.DateUpdatedLong ?? 0)
                };
                var ok = await _gb.InstallFromFileAsync(file.DownloadUrl!, file.FileName!, modsFolder, record, cts);
                Global.logger.WriteLine(ok ? $"Installed '{item.Title}'." : $"Failed to install '{item.Title}'.", ok ? LoggerType.Info : LoggerType.Error);
                _gb.DownloadProgress -= OnInstallProgress;
            }
            else if (source == "dma")
            {
                _dma.DownloadProgress += OnInstallProgress;
                var post = await _dma.FetchPostAsync(id);
                if (post == null)
                {
                    Global.logger.WriteLine($"DMA post {id} not found.", LoggerType.Error);
                    return;
                }
                var ok = await _dma.InstallPostAsync(post, 0, modsFolder, cts);
                Global.logger.WriteLine(ok ? $"Installed '{post.Name}'." : $"Failed to install '{post.Name}'.", ok ? LoggerType.Info : LoggerType.Error);
                _dma.DownloadProgress -= OnInstallProgress;
            }
            RefreshMods();
        }
        catch (Exception ex)
        {
            Global.logger.WriteLine($"Install failed: {ex.Message}", LoggerType.Error);
        }
        finally
        {
            ProgressVisible = false;
        }
    }

    private void OnInstallProgress(string name, float pct, long dl, long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressVisible = true;
            ProgressLabel = $"Downloading {name}...";
            ProgressValue = pct * 100;
        });
    }
}
