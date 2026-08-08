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

    /// <summary>
    /// The image currently shown in the large preview area. Set by clicking a gallery thumbnail.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewUrl))]
    private string? _selectedImageUrl;

    public string? PreviewUrl => SelectedImageUrl ?? ThumbnailUrl;

    /// <summary>Gallery thumbnails when a post has multiple images.</summary>
    public List<string> AllImageUrls
    {
        get
        {
            var urls = new List<string>();
            if (Post.Images != null)
                foreach (var img in Post.Images)
                    if (img != null) urls.Add(img.ToString());
            return urls;
        }
    }

    /// <summary>The DMA profile URL for the "View on DMA" button.</summary>
    public string ProfileUrl => $"https://divamodarchive.com/posts/{Post.ID}";

    public DmaPostViewModel(DivaModArchivePost post)
    {
        Post = post;
        var first = AllImageUrls.FirstOrDefault();
        if (first != null) SelectedImageUrl = first;
    }

    /// <summary>Swap the large preview image when a gallery thumbnail is clicked.</summary>
    public void SelectImage(string url) => SelectedImageUrl = url;
}

public partial class DmaBrowserViewModel : ObservableObject
{
    private readonly DmaService _dma = new();
    private int _page = 1;
    private int _perPage = 20;
    private CancellationTokenSource _loadCts = new();
    private CancellationTokenSource? _installCts;

    public ObservableCollection<DmaPostViewModel> Posts { get; } = new();

    /// <summary>
    /// Same posts as <see cref="Posts"/> but bucketed by normalized category — used by the
    /// collapsible category expanders above the flat results list.
    /// </summary>
    public ObservableCollection<BrowserCategoryGroup> GroupedPosts { get; } = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private DmaPostViewModel? _selectedPost;
    [ObservableProperty] private int _selectedSortIndex = 0; // Latest
    [ObservableProperty] private int _selectedFilterIndex = 0; // All
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
        ShowEmpty = false;
        ProgressValue = 0;
        Posts.Clear();
        var token = _loadCts.Token;
        try
        {
            var sort = (DmaFeedSort)SelectedSortIndex;
            var filter = (DmaFeedFilter)SelectedFilterIndex;
            var feed = await _dma.FetchFeedAsync(_page, _perPage, sort, filter, SearchQuery);
            if (token.IsCancellationRequested) return;
            TotalPages = feed.TotalPages > 0 ? feed.TotalPages : 1;
            foreach (var p in feed.Posts)
                Posts.Add(new DmaPostViewModel(p));
            RebuildBrowserGroups();
            ResultCount = feed.TotalRecords > 0
                ? $"{feed.TotalRecords} mods total — page {_page} of {TotalPages}"
                : $"{Posts.Count} mods on this page";
            EmptyMessage = "No mods found";
            EmptyHint = string.IsNullOrWhiteSpace(SearchQuery)
                ? "DivaModArchive returned no results for this filter."
                : $"Nothing matches \"{SearchQuery}\". Try a different search or filter.";
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
                ShowEmpty = Posts.Count == 0;
                PageLabel = $"Page {_page}";
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    /// <summary>
    /// Rebuild <see cref="GroupedPosts"/> from <see cref="Posts"/>. Each DMA post's
    /// <c>PostType</c> is normalized to a canonical category. Buckets are ordered in the
    /// canonical Song→Cover→Module→UI→Plugin→Patch→Other order.
    /// </summary>
    private void RebuildBrowserGroups()
    {
        var expandedState = GroupedPosts.ToDictionary(g => g.Category, g => g.IsExpanded);
        GroupedPosts.Clear();

        var groups = Posts
            .GroupBy(p => Helpers.CategoryNormalizer.Normalize(p.Post.PostType))
            .Select(g => new BrowserCategoryGroup
            {
                Category = g.Key,
                Items = new ObservableCollection<object>(g.Cast<object>()),
                IsExpanded = expandedState.TryGetValue(g.Key, out var wasExpanded) ? wasExpanded : true
            })
            .OrderBy(g => Helpers.CategoryNormalizer.Order(g.Category));

        foreach (var g in groups)
            GroupedPosts.Add(g);
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
            Global.logger?.WriteLine($"Installing '{post.Name}' from DMA...", LoggerType.Info);
            var ok = await _dma.InstallPostAsync(post, 0, modsFolder, _installCts);
            if (ok)
            {
                InstallStatus = $"✓ Installed '{post.Name}'";
                InstallStatusColor = "#4ADE80";
                Global.logger?.WriteLine($"Successfully installed '{post.Name}'.", LoggerType.Info);
            }
            else
            {
                InstallStatus = $"✗ Failed to install '{post.Name}' — see log";
                InstallStatusColor = "#F87171";
                Global.logger?.WriteLine($"Failed to install '{post.Name}'.", LoggerType.Error);
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
