using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DivaModManager.Models;
using DivaModManager.Services;

namespace DivaModManager.ViewModels;

public partial class GameBananaRecordViewModel : ObservableObject
{
    public GameBananaRecord Record { get; }
    public string Title => Record.Title ?? "(untitled)";
    public string AuthorLabel => $"By {Record.Owner?.Name ?? "Unknown"}";
    public string CategoryLabel => Record.Category?.Name ?? "Uncategorized";
    public string DateLabel => Record.DateAddedFormatted;
    public string DownloadsLabel => $"Downloads: {Record.DownloadString}";
    public string LikesLabel => $"Likes: {Record.LikeString}";
    public string FilesLabel => $"{Record.AllFiles?.Count ?? 0} file(s)";

    public string? ThumbnailUrl
    {
        get
        {
            var firstImage = Record.Media?.FirstOrDefault(m => m?.Type == "image");
            return firstImage?.ThumbnailUrl;
        }
    }

    /// <summary>
    /// The image currently shown in the large preview area. Defaults to the first image's
    /// 530px variant. Set <see cref="SelectedImageUrl"/> to swap when the user clicks a
    /// gallery thumbnail.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewUrl))]
    private string? _selectedImageUrl;

    public string? PreviewUrl => SelectedImageUrl;

    /// <summary>All screenshot URLs for a gallery view (530px quality).</summary>
    public List<string> AllImageUrls
    {
        get
        {
            var urls = new List<string>();
            if (Record.Media == null) return urls;
            foreach (var img in Record.Media)
            {
                if (img?.Type != "image" || string.IsNullOrEmpty(img.Base)) continue;
                var file = !string.IsNullOrEmpty(img.File530) ? img.File530 : img.File;
                if (!string.IsNullOrEmpty(file))
                    urls.Add($"{img.Base}/{file}");
            }
            return urls;
        }
    }

    /// <summary>The GameBanana profile URL for the "View on GameBanana" button.</summary>
    public string? ProfileUrl => Record.Link?.ToString();

    public GameBananaRecordViewModel(GameBananaRecord record)
    {
        Record = record;
        // Initialize the large preview to the first image.
        var first = AllImageUrls.FirstOrDefault();
        if (first != null) SelectedImageUrl = first;
    }

    /// <summary>Swap the large preview image when a gallery thumbnail is clicked.</summary>
    public void SelectImage(string url) => SelectedImageUrl = url;
}

public partial class GameBananaBrowserViewModel : ObservableObject
{
    private readonly GameBananaService _gb = new();
    private int _page = 1;
    private int _perPage = 20;
    private CancellationTokenSource _loadCts = new();
    private CancellationTokenSource? _installCts;

    public ObservableCollection<GameBananaRecordViewModel> Records { get; } = new();

    /// <summary>
    /// Same records as <see cref="Records"/> but bucketed by normalized category — used by the
    /// collapsible category expanders above the flat results list.
    /// </summary>
    public ObservableCollection<BrowserCategoryGroup> GroupedRecords { get; } = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private GameBananaRecordViewModel? _selectedRecord;
    [ObservableProperty] private int _selectedPerPageIndex = 1; // 20
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _pageLabel = "Page 1";
    [ObservableProperty] private string _resultCount = string.Empty;
    [ObservableProperty] private string _selectedDetail = "Select a mod to see details.";
    [ObservableProperty] private string _installStatus = string.Empty;
    [ObservableProperty] private string _installStatusColor = "#9A9AA4";
    [ObservableProperty] private bool _showEmpty;
    [ObservableProperty] private string _emptyMessage = "No mods found";
    [ObservableProperty] private string _emptyHint = "Try a different search.";

    public bool CanGoPrev => _page > 1 && !IsLoading;
    public bool CanGoNext => _page < _totalPages && !IsLoading;
    public bool CanInstall => !IsInstalling;

    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(CanInstall));
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    public event Action<string, float, long, long>? DownloadProgress;
    public event Action? InstallComplete;

    public GameBananaBrowserViewModel()
    {
        // Link the service's progress to our event
        _gb.DownloadProgress += (name, pct, dl, total) =>
            DownloadProgress?.Invoke(name, pct, dl, total);
    }

    /// <summary>
    /// Cancel any in-flight loads. Called when the window closes.
    /// </summary>
    public void CancelLoads()
    {
        try { _loadCts?.Cancel(); } catch { }
        _loadCts = new CancellationTokenSource();
    }

    partial void OnSelectedRecordChanged(GameBananaRecordViewModel? value)
    {
        if (value == null)
        {
            SelectedDetail = "Select a mod to see details.";
            return;
        }
        var r = value.Record;
        var files = r.AllFiles != null
            ? string.Join("\n", r.AllFiles.Select(f => $"  - {f.FileName} ({Helpers.StringConverters.FormatSize(f.Filesize)})"))
            : "(no files)";
        SelectedDetail = $"{r.Title}\nBy {r.Owner?.Name ?? "Unknown"} — {r.Category?.Name ?? "?"}\n\nFiles:\n{files}";
    }

    partial void OnSelectedPerPageIndexChanged(int value)
    {
        _perPage = value switch { 0 => 10, 1 => 20, 2 => 30, 3 => 50, _ => 20 };
        _page = 1;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        CancelLoads();
        _page = 1;
        await LoadAsync();
    }

    public async Task PrevPageAsync()
    {
        if (_page <= 1) return;
        _page--;
        CancelLoads();
        await LoadAsync();
    }

    public async Task NextPageAsync()
    {
        _page++;
        CancelLoads();
        await LoadAsync();
    }

    private int _totalPages = 1;
    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
                OnPropertyChanged(nameof(CanGoNext));
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ShowEmpty = false;
        ProgressValue = 0;
        Records.Clear();
        var token = _loadCts.Token;
        try
        {
            var feed = await _gb.FetchRecordsAsync(GameBananaService.MegaMixGameId, _page, _perPage, SearchQuery);
            if (token.IsCancellationRequested) return;
            TotalPages = feed.TotalPages > 0 ? feed.TotalPages : 1;
            foreach (var r in feed.Records)
                Records.Add(new GameBananaRecordViewModel(r));
            RebuildBrowserGroups();
            ResultCount = feed.TotalRecords > 0
                ? $"{feed.TotalRecords} mods total — page {_page} of {TotalPages}"
                : $"{Records.Count} mods on this page";
            EmptyMessage = "No mods found";
            EmptyHint = string.IsNullOrWhiteSpace(SearchQuery)
                ? "GameBanana returned no results for this page."
                : $"Nothing matches \"{SearchQuery}\". Try a different search.";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            ResultCount = $"Error: {ex.Message}";
            EmptyMessage = "Couldn't load mods";
            EmptyHint = "Check your internet connection, then hit Search to retry.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
                ShowEmpty = Records.Count == 0;
                PageLabel = $"Page {_page}";
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    /// <summary>
    /// Rebuild <see cref="GroupedRecords"/> from <see cref="Records"/>. Each GameBanana
    /// record's free-form <c>CategoryName</c> is normalized to a canonical category.
    /// </summary>
    private void RebuildBrowserGroups()
    {
        var expandedState = GroupedRecords.ToDictionary(g => g.Category, g => g.IsExpanded);
        GroupedRecords.Clear();

        var groups = Records
            .GroupBy(r => Helpers.CategoryNormalizer.Normalize(r.Record.CategoryName))
            .Select(g => new BrowserCategoryGroup
            {
                Category = g.Key,
                Items = new ObservableCollection<object>(g.Cast<object>()),
                IsExpanded = expandedState.TryGetValue(g.Key, out var wasExpanded) ? wasExpanded : true
            })
            .OrderBy(g => Helpers.CategoryNormalizer.Order(g.Category));

        foreach (var g in groups)
            GroupedRecords.Add(g);
    }

    public async Task InstallSelectedAsync()
    {
        if (SelectedRecord == null)
        {
            Global.logger?.WriteLine("No mod selected for install.", LoggerType.Warning);
            return;
        }
        var record = SelectedRecord.Record;
        if (record.AllFiles == null || record.AllFiles.Count == 0)
        {
            Global.logger?.WriteLine("This mod has no downloadable files.", LoggerType.Warning);
            InstallStatus = "✗ No downloadable files";
            InstallStatusColor = "#F87171";
            return;
        }

        var modsFolder = Global.config?.Configs?[Global.CurrentGame]?.ModsFolder;
        if (string.IsNullOrEmpty(modsFolder) || !System.IO.Directory.Exists(modsFolder))
        {
            Global.logger?.WriteLine("Mods folder not set. Run Setup first.", LoggerType.Warning);
            InstallStatus = "✗ Mods folder not set — run Setup first";
            InstallStatusColor = "#F87171";
            return;
        }

        IsInstalling = true;
        ProgressValue = 0;
        InstallStatus = "Preparing download…";
        InstallStatusColor = "#39C5BB";
        _installCts = new CancellationTokenSource();
        try
        {
            Global.logger?.WriteLine($"Installing '{record.Title}' from GameBanana...", LoggerType.Info);
            var file = record.AllFiles[0];
            var ok = await _gb.InstallFromFileAsync(file.DownloadUrl!, file.FileName ?? $"gb-{record.Title}.zip", modsFolder, record, _installCts);
            if (ok)
            {
                InstallStatus = $"✓ Installed '{record.Title}'";
                InstallStatusColor = "#4ADE80";
                Global.logger?.WriteLine($"Successfully installed '{record.Title}'.", LoggerType.Info);
            }
            else
            {
                InstallStatus = $"✗ Failed to install '{record.Title}' — see log";
                InstallStatusColor = "#F87171";
                Global.logger?.WriteLine($"Failed to install '{record.Title}'.", LoggerType.Error);
            }
        }
        catch (Exception ex)
        {
            InstallStatus = $"✗ Install error: {ex.Message}";
            InstallStatusColor = "#F87171";
            Global.logger?.WriteLine($"Install error: {ex.Message}", LoggerType.Error);
        }
        finally
        {
            IsInstalling = false;
            _installCts = null;
            InstallComplete?.Invoke();
            _ = Task.Delay(6000).ContinueWith(_ =>
            {
                if (InstallStatus.StartsWith("✓"))
                {
                    InstallStatus = string.Empty;
                }
            });
        }
    }

    /// <summary>
    /// Cancel an in-progress install (download + extraction). No-op if nothing is installing.
    /// </summary>
    public void CancelInstall()
    {
        if (!IsInstalling || _installCts == null) return;
        try { _installCts.Cancel(); } catch { }
        InstallStatus = "✗ Cancelled";
        InstallStatusColor = "#F87171";
        Global.logger?.WriteLine("Install cancelled by user.", LoggerType.Warning);
    }
}
