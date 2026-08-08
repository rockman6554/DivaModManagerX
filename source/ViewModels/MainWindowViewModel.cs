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
        LoggerType.Info => "#4ADE80",
        LoggerType.Warning => "#FBBF24",
        LoggerType.Error => "#F87171",
        _ => "#E8E8EC"
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

    /// <summary>
    /// Mods grouped by canonical category for the collapsible UI. Rebuilt whenever
    /// <see cref="ModList"/> changes (loadout swap, refresh, sort, toggle, delete).
    /// </summary>
    public ObservableCollection<ModCategoryGroup> GroupedMods { get; } = new();

    [ObservableProperty] private string _currentGame = "Project DIVA Mega Mix+";
    [ObservableProperty] private string? _selectedLoadout;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private string _progressLabel = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteModCommand))]
    private Mod? _selectedMod;
    [ObservableProperty] private string _gameExePath = string.Empty;
    [ObservableProperty] private string _modsFolderPath = string.Empty;
    [ObservableProperty] private string _dmlVersion = "Not installed";
    [ObservableProperty] private string _steamStatus = "Unknown";
    [ObservableProperty] private string _modCountLabel = string.Empty;

    // ---- Selected mod metadata (read from mod.json on disk) ----
    [ObservableProperty] private string _selectedModAuthor = string.Empty;
    [ObservableProperty] private string _selectedModDescription = string.Empty;
    [ObservableProperty] private string? _selectedModPreviewUrl;
    [ObservableProperty] private string _selectedModCategory = string.Empty;
    [ObservableProperty] private string _selectedModHomepage = string.Empty;

    private string? _pendingDownloadUrl;

    public MainWindowViewModel(string? pendingDownloadUrl = null)
    {
        _pendingDownloadUrl = pendingDownloadUrl;
        _mods = new ModService();
        // Keep per-mod PropertyChanged hooks attached across refreshes/loadout swaps so a
        // checkbox/switch toggle persists immediately (config + DML's config.toml). Also
        // rebuild the category groups so the Expander UI stays in sync.
        _mods.ModList.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
                foreach (Mod m in e.NewItems) AttachMod(m);
            UpdateModCounts();
            RebuildGroups();
        };
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
        Global.logger.WriteLine($"Launched DivaModManager X v1.3.1!", LoggerType.Info);
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
            // Refresh populates Category for newly-added mods, but mods that came from the
            // loadout config (deserialized) still have the "Other" default — overwrite all.
            _mods.RefreshCategories(gameCfg.ModsFolder);
            // Force-expand all categories on initial load so the user sees their mods.
            RebuildGroupsExpanded();
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

        UpdateModCounts();
    }

    private void AttachMod(Mod mod)
    {
        // -=/+= keeps the subscription single even when the same instance is re-added
        // (alphabetical sort and loadout swaps clear + re-add existing Mod objects).
        mod.PropertyChanged -= OnModPropertyChanged;
        mod.PropertyChanged += OnModPropertyChanged;
    }

    private void OnModPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Mod.enabled))
        {
            PersistModState();
            UpdateModCounts();
        }
        // Note: Category changes are handled by explicit RebuildGroups/RebuildGroupsExpanded
        // calls after RefreshCategories. We do NOT rebuild here to avoid redundant rebuilds
        // during the batch category update (which fires PropertyChanged per mod).
    }

    private void UpdateModCounts()
    {
        var total = _mods.ModList.Count;
        var enabled = _mods.ModList.Count(m => m.enabled);
        ModCountLabel = total == 0 ? string.Empty : $"{total} installed · {enabled} enabled";
    }

    /// <summary>
    /// Rebuild <see cref="GroupedMods"/> from <see cref="ModList"/>. Mods are bucketed by their
    /// canonical category and the buckets are ordered Song→Cover→Module→UI→Plugin→Patch→Other.
    /// Called automatically when ModList changes.
    /// </summary>
    private void RebuildGroups()
    {
        // Preserve which categories the USER collapsed so a refresh doesn't undo their choice.
        // Categories not seen before default to expanded (IsExpanded = true).
        var expandedState = GroupedMods.ToDictionary(g => g.Category, g => g.IsExpanded);

        GroupedMods.Clear();
        if (ModList.Count == 0) return;

        var groups = ModList
            .GroupBy(m => m.Category)
            .Select(g => new ModCategoryGroup
            {
                Category = g.Key,
                Mods = new ObservableCollection<Mod>(g),
                // Default to EXPANDED unless the user previously collapsed this category.
                IsExpanded = expandedState.TryGetValue(g.Key, out var wasExpanded) ? wasExpanded : true
            })
            .OrderBy(g => Helpers.CategoryNormalizer.Order(g.Category));

        foreach (var g in groups)
            GroupedMods.Add(g);
    }

    /// <summary>
    /// Force a full group rebuild with all categories expanded. Used during initial load
    /// to ensure mods are visible (not hidden behind collapsed expanders).
    /// </summary>
    private void RebuildGroupsExpanded()
    {
        GroupedMods.Clear();
        if (ModList.Count == 0) return;

        var groups = ModList
            .GroupBy(m => m.Category)
            .Select(g => new ModCategoryGroup
            {
                Category = g.Key,
                Mods = new ObservableCollection<Mod>(g),
                IsExpanded = true
            })
            .OrderBy(g => Helpers.CategoryNormalizer.Order(g.Category));

        foreach (var g in groups)
            GroupedMods.Add(g);
    }

    /// <summary>
    /// Save the current mod list to Config.json and mirror it into DML's config.toml so the
    /// UI state and what the game will actually load never drift apart.
    /// </summary>
    private void PersistModState()
    {
        Global.UpdateConfig();
        var gameCfg = Global.config!.Configs![CurrentGame]!;
        if (string.IsNullOrEmpty(gameCfg.Launcher)) return;
        var gameDir = System.IO.Path.GetDirectoryName(gameCfg.Launcher)!;
        // Skip quietly when DML isn't installed yet — ApplyLoadoutToDml would log an error.
        if (System.IO.File.Exists(System.IO.Path.Combine(gameDir, "config.toml")))
            _mods.ApplyLoadoutToDml(gameDir);
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
                msg += "\n\nThe Steam launch option has been copied to your clipboard. Paste it into Steam → Properties → Launch Options, then relaunch.";
            Global.logger.WriteLine(msg, LoggerType.Error);
            // If Steam launch options are the only blocker, copy the override to the clipboard
            // so the user can paste it into Steam's Launch Options field manually.
            if (fixes.Any(f => f.Contains("Auto-configure")) &&
                !failures.Any(f => f.Contains("dinput8.dll") || f.Contains("config.toml")))
            {
                var text = $"{SteamLaunchOptionsService.RequiredWineOverride} %command%";
                var session = Helpers.ClipboardHelper.IsWaylandSession() ? "Wayland" : "X11";
                var copied = await Helpers.ClipboardHelper.CopyAsync(text);
                if (copied)
                {
                    Global.logger.WriteLine($"Copied launch option to clipboard ({session}): {text}", LoggerType.Info);
                    Global.logger.WriteLine("Paste it into Steam → Properties → Launch Options, then relaunch.", LoggerType.Warning);
                    SteamStatus = "Copied — paste in Steam Launch Options";
                    await Helpers.DialogHelper.ShowInfoAsync(Helpers.MainWindowProvider.GetMainWindow(),
                        "Steam launch option copied",
                        "The launch option was copied to your clipboard.\n\nIn Steam: right-click the game → Properties → Launch Options → paste it (Ctrl+V), then come back and launch again.");
                }
                else
                {
                    Global.logger.WriteLine($"Set the launch option manually: {text}", LoggerType.Error);
                }
            }
            else
            {
                // Other failures (missing DML, missing config.toml, missing exe) — show an error dialog.
                await Helpers.DialogHelper.ShowErrorAsync(Helpers.MainWindowProvider.GetMainWindow(),
                    "Cannot launch the game",
                    string.Join("\n", failures));
            }
            return;
        }

        _launch.LaunchViaSteam();
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        _launch.OpenModsFolder(Global.config!.Configs![CurrentGame]!.ModsFolder);
    }

    private bool HasSelectedMod() => SelectedMod != null;

    [RelayCommand(CanExecute = nameof(HasSelectedMod))]
    private void MoveUp()
    {
        if (SelectedMod == null) return;
        var idx = _mods.ModList.IndexOf(SelectedMod);
        if (idx > 0) _mods.Reorder(idx, idx - 1);
        PersistModState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMod))]
    private void MoveDown()
    {
        if (SelectedMod == null) return;
        var idx = _mods.ModList.IndexOf(SelectedMod);
        if (idx >= 0 && idx < _mods.ModList.Count - 1) _mods.Reorder(idx, idx + 1);
        PersistModState();
    }

    [RelayCommand]
    private void SortAlphabetical()
    {
        var selected = SelectedMod;
        var sorted = _mods.ModList.OrderBy(m => m.name, new Helpers.NaturalSort()).ToList();
        _mods.ModList.Clear();
        foreach (var m in sorted) _mods.ModList.Add(m);
        SelectedMod = selected;
        PersistModState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMod))]
    private async Task DeleteModAsync()
    {
        if (SelectedMod == null) return;
        var modName = SelectedMod.name;
        var owner = Helpers.MainWindowProvider.GetMainWindow();
        var confirm = await Helpers.DialogHelper.ShowConfirmDestructiveAsync(owner,
            "Delete mod?",
            $"This will permanently delete the mod folder \"{modName}\" from your mods directory.\n\nThis cannot be undone.",
            "Delete", "Cancel");
        if (!confirm)
        {
            Global.logger.WriteLine("Delete cancelled.", LoggerType.Info);
            return;
        }
        var modsFolder = Global.config!.Configs![CurrentGame]!.ModsFolder;
        _mods.DeleteMod(modsFolder, modName);
        PersistModState();
        Global.logger.WriteLine($"Deleted mod: {modName}", LoggerType.Warning);
    }

    [RelayCommand]
    private void RefreshMods()
    {
        var modsFolder = Global.config!.Configs![CurrentGame]!.ModsFolder;
        _mods.Refresh(modsFolder);
        Global.logger.WriteLine($"Refreshed: {_mods.ModList.Count} mods", LoggerType.Info);
    }

    [RelayCommand]
    private async Task AddLoadoutAsync()
    {
        var owner = Helpers.MainWindowProvider.GetMainWindow();
        var name = await Helpers.DialogHelper.ShowInputAsync(owner,
            "New loadout",
            "Name for the new loadout:",
            watermark: "e.g. Song packs, Vanilla+…",
            initial: $"Loadout {Loadouts.Count + 1}",
            okText: "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (Loadouts.Contains(name))
        {
            Global.logger.WriteLine($"A loadout named \"{name}\" already exists.", LoggerType.Warning);
            return;
        }
        Loadouts.Add(name);
        Global.config!.Configs![CurrentGame]!.Loadouts![name] = new ObservableCollection<Mod>();
        SelectedLoadout = name;
        Global.UpdateConfig();
        Global.logger.WriteLine($"Created loadout \"{name}\".", LoggerType.Info);
    }

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (LogEntries.Count == 0) return;
        var text = string.Join("\n", LogEntries.Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.TypeTag} {e.Message}"));
        var ok = await Helpers.ClipboardHelper.CopyAsync(text);
        Global.logger.WriteLine(ok ? "Log copied to clipboard." : "Could not access the clipboard.",
            ok ? LoggerType.Info : LoggerType.Error);
    }

    [RelayCommand]
    private async Task DeleteLoadoutAsync()
    {
        if (SelectedLoadout == null || SelectedLoadout == "Default")
        {
            Global.logger.WriteLine("Cannot delete the Default loadout.", LoggerType.Warning);
            return;
        }
        var name = SelectedLoadout;
        var owner = Helpers.MainWindowProvider.GetMainWindow();
        var confirm = await Helpers.DialogHelper.ShowConfirmDestructiveAsync(owner,
            "Delete loadout?",
            $"Delete the loadout \"{name}\"? Mods in it will remain on disk but won't be grouped under this loadout anymore.",
            "Delete", "Cancel");
        if (!confirm) return;
        Global.config!.Configs![CurrentGame]!.Loadouts!.Remove(name);
        Loadouts.Remove(name);
        SelectedLoadout = "Default";
        Global.UpdateConfig();
    }

    /// <summary>
    /// When the selected mod changes, load its metadata (author, description, preview,
    /// category) from the mod.json on disk so the Mod Info panel can display it.
    /// </summary>
    partial void OnSelectedModChanged(Mod? value)
    {
        if (value == null)
        {
            SelectedModAuthor = string.Empty;
            SelectedModDescription = string.Empty;
            SelectedModPreviewUrl = null;
            SelectedModCategory = string.Empty;
            SelectedModHomepage = string.Empty;
            return;
        }

        SelectedModCategory = $"📦 {value.Category}";
        var modsFolder = Global.config?.Configs?[CurrentGame]?.ModsFolder;
        if (string.IsNullOrEmpty(modsFolder)) return;

        var modDir = System.IO.Path.Combine(modsFolder, value.name);
        var modJson = System.IO.Path.Combine(modDir, "mod.json");
        if (!System.IO.File.Exists(modJson)) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(modJson));
            var root = doc.RootElement;
            SelectedModAuthor = root.TryGetProperty("submitter", out var sub) && sub.ValueKind == System.Text.Json.JsonValueKind.String
                ? $"By {sub.GetString()}" : string.Empty;
            SelectedModDescription = root.TryGetProperty("description", out var desc) && desc.ValueKind == System.Text.Json.JsonValueKind.String
                ? desc.GetString() ?? string.Empty : string.Empty;
            SelectedModPreviewUrl = root.TryGetProperty("preview", out var prev) && prev.ValueKind == System.Text.Json.JsonValueKind.String
                ? prev.GetString() : null;
            if (root.TryGetProperty("homepage", out var home) && home.ValueKind == System.Text.Json.JsonValueKind.String)
                SelectedModHomepage = home.GetString() ?? string.Empty;
            else
                SelectedModHomepage = string.Empty;
        }
        catch { }
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
            // Mods from Config.json only have name+enabled — re-read their categories
            // from disk so the category grouping stays correct after a loadout swap.
            _mods.RefreshCategories(gameCfg.ModsFolder);
            RebuildGroupsExpanded();
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
    private async Task ConfigureSteamAsync()
    {
        // Copy the WINEDLLOVERRIDES launch option to the clipboard so the user can paste it
        // into Steam's per-game Properties → Launch Options field.
        //
        // Why clipboard instead of auto-writing localconfig.vdf:
        //   Steam caches localconfig.vdf in memory and overwrites it on exit, so writing it
        //   while Steam is running is silently lost. Bubblewrap-sandboxed Steam installs (e.g.
        //   Void's steam-nk) put the file behind a mount that the manager process can't always
        //   reach. Letting the user paste it via Steam's own UI is the only reliable flow.
        var text = $"{SteamLaunchOptionsService.RequiredWineOverride} %command%";
        var session = Helpers.ClipboardHelper.IsWaylandSession() ? "Wayland" : "X11";

        var ok = await Helpers.ClipboardHelper.CopyAsync(text);
        if (ok)
        {
            Global.logger.WriteLine($"Copied launch option to clipboard ({session}):", LoggerType.Info);
            Global.logger.WriteLine($"    {text}", LoggerType.Info);
            SteamStatus = "Copied — paste in Steam Launch Options";
            await Helpers.DialogHelper.ShowInfoAsync(Helpers.MainWindowProvider.GetMainWindow(),
                "Copied to clipboard",
                "The Steam launch option is now in your clipboard:\n\n" + text + "\n\nOpen Steam → right-click \"Hatsune Miku: Project DIVA Mega Mix+\" → Properties → Launch Options → paste (Ctrl+V).");
        }
        else
        {
            Global.logger.WriteLine("Could not access the clipboard. Set the launch option manually:", LoggerType.Error);
            Global.logger.WriteLine($"    {text}", LoggerType.Info);
            SteamStatus = "Clipboard unavailable — set manually";
            await Helpers.DialogHelper.ShowErrorAsync(Helpers.MainWindowProvider.GetMainWindow(),
                "Clipboard unavailable",
                "Could not access the clipboard. Set this launch option manually in Steam → Properties → Launch Options:\n\n" + text);
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
        var owner = Helpers.MainWindowProvider.GetMainWindow();
        var url = await Helpers.DialogHelper.ShowInputAsync(owner,
            "Install mod from URL",
            "Paste a GameBanana mod link or a DivaModArchive post link:",
            watermark: "https://gamebanana.com/mods/…  or  https://divamodarchive.com/posts/…",
            okText: "Install");
        if (string.IsNullOrWhiteSpace(url)) return;
        await HandleOneClickInstallAsync(url.Trim());
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
