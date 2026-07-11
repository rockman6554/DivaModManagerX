using System;
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
            try
            {
                if (Record.Media != null && Record.Media.Count > 0)
                {
                    var img = Record.Media.FirstOrDefault(m => m?.Type == "image");
                    if (img != null && img.Base != null && img.File != null)
                        return new Uri(img.Base, img.File).ToString();
                }
            }
            catch { }
            return null;
        }
    }

    public GameBananaRecordViewModel(GameBananaRecord record) { Record = record; }
}

public partial class GameBananaBrowserViewModel : ObservableObject
{
    private readonly GameBananaService _gb = new();
    private int _page = 1;
    private int _perPage = 20;
    private CancellationTokenSource _loadCts = new();

    public ObservableCollection<GameBananaRecordViewModel> Records { get; } = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private GameBananaRecordViewModel? _selectedRecord;
    [ObservableProperty] private int _selectedPerPageIndex = 1; // 20
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _pageLabel = "Page 1";
    [ObservableProperty] private string _resultCount = string.Empty;
    [ObservableProperty] private string _selectedDetail = "Select a mod to see details.";

    public bool CanGoPrev => _page > 1;
    public bool CanGoNext => Records.Count >= _perPage;

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

    private async Task LoadAsync()
    {
        IsLoading = true;
        ProgressValue = 0;
        Records.Clear();
        var token = _loadCts.Token;
        try
        {
            var records = await _gb.FetchRecordsAsync(GameBananaService.MegaMixGameId, _page, _perPage, SearchQuery);
            if (token.IsCancellationRequested) return;
            foreach (var r in records)
                Records.Add(new GameBananaRecordViewModel(r));
            ResultCount = $"{Records.Count} mods on this page";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            ResultCount = $"Error: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
                PageLabel = $"Page {_page}";
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
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
            return;
        }

        var modsFolder = Global.config?.Configs?[Global.CurrentGame]?.ModsFolder;
        if (string.IsNullOrEmpty(modsFolder) || !System.IO.Directory.Exists(modsFolder))
        {
            Global.logger?.WriteLine("Mods folder not set. Run Setup first.", LoggerType.Warning);
            return;
        }

        IsLoading = true;
        ProgressValue = 0;
        var cts = new CancellationTokenSource();
        try
        {
            Global.logger?.WriteLine($"Installing '{record.Title}' from GameBanana...", LoggerType.Info);
            var file = record.AllFiles[0];
            var ok = await _gb.InstallFromFileAsync(file.DownloadUrl!, file.FileName ?? $"gb-{record.Title}.zip", modsFolder, record, cts);
            Global.logger?.WriteLine(ok ? $"Successfully installed '{record.Title}'." : $"Failed to install '{record.Title}'.", ok ? LoggerType.Info : LoggerType.Error);
        }
        catch (Exception ex)
        {
            Global.logger?.WriteLine($"Install error: {ex.Message}", LoggerType.Error);
        }
        finally
        {
            IsLoading = false;
            InstallComplete?.Invoke();
        }
    }
}
