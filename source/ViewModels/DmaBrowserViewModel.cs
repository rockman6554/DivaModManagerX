using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DivaModManager.Models;
using DivaModManager.Services;

namespace DivaModManager.ViewModels;

public partial class DmaPostViewModel : ObservableObject
{
    public DivaModArchivePost Post { get; }
    public string Title => Post.Name ?? "(untitled)";
    public string AuthorLabel => $"By {Post.Authors?.FirstOrDefault()?.DisplayNameOrName ?? "Unknown"}";
    public string TypeLabel => Post.PostType ?? "Unknown";
    public string DateLabel => $"Added {Helpers.StringConverters.FormatTimeAgo(DateTime.UtcNow - Post.Time)}";
    public string DownloadsLabel => $"Downloads: {Post.DownloadString}";
    public string LikesLabel => $"Likes: {Post.LikeString}";
    public string FilesLabel => $"{Post.Files?.Count ?? 0} file(s)";
    public string SizeLabel
    {
        get
        {
            if (Post.FileSizes == null || Post.FileSizes.Count == 0) return "";
            var total = Post.FileSizes.Sum();
            return $"Size: {Helpers.StringConverters.FormatSize(total)}";
        }
    }

    public string? ThumbnailUrl
    {
        get
        {
            try
            {
                if (Post.Images != null && Post.Images.Count > 0)
                    return Post.Images[0].ToString();
            }
            catch { }
            return null;
        }
    }

    public DmaPostViewModel(DivaModArchivePost post) { Post = post; }
}

public partial class DmaBrowserViewModel : ObservableObject
{
    private readonly DmaService _dma = new();
    private int _page = 1;
    private int _perPage = 20;
    private CancellationTokenSource _loadCts = new();

    public ObservableCollection<DmaPostViewModel> Posts { get; } = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private DmaPostViewModel? _selectedPost;
    [ObservableProperty] private int _selectedSortIndex = 0; // Latest
    [ObservableProperty] private int _selectedFilterIndex = 0; // All
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _pageLabel = "Page 1";
    [ObservableProperty] private string _resultCount = string.Empty;
    [ObservableProperty] private string _selectedDetail = "Select a mod to see details.";

    public bool CanGoPrev => _page > 1;
    public bool CanGoNext => Posts.Count >= _perPage;

    public event Action<string, float, long, long>? DownloadProgress;
    public event Action? InstallComplete;

    public DmaBrowserViewModel()
    {
        _dma.DownloadProgress += (name, pct, dl, total) =>
            DownloadProgress?.Invoke(name, pct, dl, total);
    }

    public void CancelLoads()
    {
        try { _loadCts?.Cancel(); } catch { }
        _loadCts = new CancellationTokenSource();
    }

    partial void OnSelectedPostChanged(DmaPostViewModel? value)
    {
        if (value == null)
        {
            SelectedDetail = "Select a mod to see details.";
            return;
        }
        var p = value.Post;
        var files = p.FileNames != null
            ? string.Join("\n", p.FileNames.Select((n, i) =>
            {
                var size = p.FileSizes != null && i < p.FileSizes.Count
                    ? $" ({Helpers.StringConverters.FormatSize(p.FileSizes[i])})"
                    : "";
                return $"  - {n}{size}";
            }))
            : "(no files)";
        var desc = (p.Text ?? "").Length > 300 ? (p.Text ?? "").Substring(0, 300) + "..." : (p.Text ?? "");
        SelectedDetail = $"{p.Name}\nBy {p.Authors?.FirstOrDefault()?.DisplayNameOrName ?? "Unknown"} — {p.PostType}\n\n{desc}\n\nFiles:\n{files}";
    }

    partial void OnSelectedSortIndexChanged(int value) => _ = RefreshAsync();
    partial void OnSelectedFilterIndexChanged(int value) => _ = RefreshAsync();

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
        Posts.Clear();
        var token = _loadCts.Token;
        try
        {
            var sort = (DmaFeedSort)SelectedSortIndex;
            var filter = (DmaFeedFilter)SelectedFilterIndex;
            var posts = await _dma.FetchFeedAsync(_page, _perPage, sort, filter, SearchQuery);
            if (token.IsCancellationRequested) return;
            foreach (var p in posts)
                Posts.Add(new DmaPostViewModel(p));
            ResultCount = $"{Posts.Count} mods on this page";
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
        if (SelectedPost == null)
        {
            Global.logger?.WriteLine("No post selected for install.", LoggerType.Warning);
            return;
        }
        var post = SelectedPost.Post;
        if (post.Files == null || post.Files.Count == 0)
        {
            Global.logger?.WriteLine("This post has no downloadable files.", LoggerType.Warning);
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
            Global.logger?.WriteLine($"Installing '{post.Name}' from DMA...", LoggerType.Info);
            var ok = await _dma.InstallPostAsync(post, 0, modsFolder, cts);
            Global.logger?.WriteLine(ok ? $"Successfully installed '{post.Name}'." : $"Failed to install '{post.Name}'.", ok ? LoggerType.Info : LoggerType.Error);
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
