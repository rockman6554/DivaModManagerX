using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DivaModManager.Models;
using DivaModManager.Helpers;

namespace DivaModManager.Services
{
    /// <summary>
    /// DivaModArchive (DMA) API client. DMA is a community mod archive at divamodarchive.com.
    ///
    /// API endpoints:
    ///   GET /api/v1/posts?sort=time:desc&offset=0&limit=30&query=...
    ///       → List posts. Supports filters like &filter=post_type=Song
    ///   GET /api/v1/posts/count?query=...&filter=...&limit=30
    ///       → Returns total post count for the query (plain text number, used to
    ///         calculate total pages). Respeta filter y query.
    ///   GET /api/v1/posts/{id}
    ///       → Single post
    ///   GET /api/v1/posts/{id}/download/{fileIndex}
    ///       → Download a file (returns the actual archive)
    ///
    /// Note: <c>query</c> is a Meilisearch full-text search across ALL indexed fields
    /// (name, text, author name/display_name, dependencies). Searching "miku" will also
    /// match posts by author "mikurisu39". This matches upstream TekkaGB behaviour.
    /// </summary>
    public class DmaService
    {
        // Shared HttpClient — prevents socket exhaustion when reopening the browser
        private static readonly HttpClient _http;

        static DmaService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DivaModManagerLinux/1.3.1 (+https://github.com/TekkaGB/DivaModManager)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        private readonly ZipExtractor _extractor = new();

        public event Action<string, float, long, long>? DownloadProgress;

        /// <summary>
        /// Result of a feed fetch. Carries the posts plus pagination metadata
        /// read from the <c>/posts/count</c> endpoint.
        /// </summary>
        public class DmaFeedResult
        {
            public List<DivaModArchivePost> Posts { get; set; } = new();
            public int TotalRecords { get; set; }
            public int TotalPages { get; set; }
        }

        // ---- Feed cache (15-minute TTL, LRU capped at 15 entries — mirrors upstream) ----
        private static readonly Dictionary<string, DivaModArchiveModList> _feedCache = new();
        private static readonly object _cacheLock = new();

        public async Task<DmaFeedResult> FetchFeedAsync(
            int page = 1, int perPage = 20,
            DmaFeedSort sort = DmaFeedSort.Latest,
            DmaFeedFilter filter = DmaFeedFilter.None,
            string? search = null)
        {
            var url = BuildFeedUrl(page, perPage, sort, filter, search);

            // Cache check
            lock (_cacheLock)
            {
                if (_feedCache.TryGetValue(url, out var cached) && cached.IsValid)
                {
                    return new DmaFeedResult
                    {
                        Posts = cached.Posts?.ToList() ?? new(),
                        TotalPages = (int)cached.TotalPages
                    };
                }
            }

            try
            {
                Global.logger?.WriteLine($"[DMA] Fetching: {url}", LoggerType.Info);
                var json = await _http.GetStringAsync(url);
                Global.logger?.WriteLine($"[DMA] Response: {json.Length} chars", LoggerType.Info);
                var posts = JsonSerializer.Deserialize<List<DivaModArchivePost>>(json) ?? new();
                Global.logger?.WriteLine($"[DMA] Deserialized: {posts.Count} posts", LoggerType.Info);

                // Fetch total record count from /posts/count for accurate pagination.
                // Upstream TekkaGB calls this with the SAME query; we also pass the filter
                // (which upstream forgets, causing wrong totals when a type filter is active).
                var totalRecords = await FetchTotalCountAsync(perPage, sort, filter, search);
                var totalPages = perPage > 0 ? (int)Math.Ceiling(totalRecords / (double)perPage) : 1;
                if (totalPages < 1) totalPages = posts.Count > 0 ? page : 1;

                // Store in cache
                var entry = new DivaModArchiveModList
                {
                    Posts = new ObservableCollection<DivaModArchivePost>(posts),
                    TotalPages = totalPages,
                    TimeFetched = DateTime.UtcNow
                };
                lock (_cacheLock)
                {
                    if (_feedCache.Count >= 15)
                    {
                        var oldest = _feedCache.OrderByDescending(k => k.Value.TimeFetched).Last();
                        _feedCache.Remove(oldest.Key);
                    }
                    _feedCache[url] = entry;
                }

                return new DmaFeedResult { Posts = posts, TotalRecords = totalRecords, TotalPages = totalPages };
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"[DMA] feed fetch FAILED: {ex.GetType().Name}: {ex.Message}", LoggerType.Error);
                return new DmaFeedResult();
            }
        }

        /// <summary>
        /// Build the /posts URL. Mirrors upstream <c>DMAFeedGenerator.GenerateUrl</c>,
        /// with proper URL-encoding of the query (upstream is raw, which breaks on spaces).
        /// </summary>
        private static string BuildFeedUrl(int page, int perPage, DmaFeedSort sort, DmaFeedFilter filter, string? search)
        {
            var url = "https://divamodarchive.com/api/v1/posts?sort=" + sort switch
            {
                DmaFeedSort.Latest => "time:desc",
                DmaFeedSort.Downloads => "download_count:desc",
                DmaFeedSort.Likes => "like_count:desc",
                _ => "time:desc"
            };
            if (filter != DmaFeedFilter.None)
            {
                var typeStr = filter switch
                {
                    DmaFeedFilter.Song => "Song",
                    DmaFeedFilter.Cover => "Cover",
                    DmaFeedFilter.Module => "Module",
                    DmaFeedFilter.Ui => "UI",
                    DmaFeedFilter.Plugin => "Plugin",
                    DmaFeedFilter.Other => "Other",
                    _ => null
                };
                if (typeStr != null) url += $"&filter=post_type={typeStr}";
            }
            if (!string.IsNullOrWhiteSpace(search))
                url += $"&query={Uri.EscapeDataString(search.Trim())}";
            var offset = (page - 1) * perPage;
            url += $"&offset={offset}&limit={perPage}";
            return url;
        }

        /// <summary>
        /// Query /posts/count for the total record count matching the current query+filter.
        /// The endpoint returns a plain-text number (not JSON).
        /// </summary>
        private async Task<int> FetchTotalCountAsync(int limit, DmaFeedSort sort, DmaFeedFilter filter, string? search)
        {
            try
            {
                var url = $"https://divamodarchive.com/api/v1/posts/count?limit={limit}";
                if (!string.IsNullOrWhiteSpace(search))
                    url += $"&query={Uri.EscapeDataString(search!.Trim())}";
                if (filter != DmaFeedFilter.None)
                {
                    var typeStr = filter switch
                    {
                        DmaFeedFilter.Song => "Song",
                        DmaFeedFilter.Cover => "Cover",
                        DmaFeedFilter.Module => "Module",
                        DmaFeedFilter.Ui => "UI",
                        DmaFeedFilter.Plugin => "Plugin",
                        DmaFeedFilter.Other => "Other",
                        _ => null
                    };
                    if (typeStr != null) url += $"&filter=post_type={typeStr}";
                }
                var text = await _http.GetStringAsync(url);
                if (double.TryParse(text.Trim(), out var n)) return (int)n;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"DMA count fetch failed (pagination will be heuristic): {ex.Message}", LoggerType.Warning);
            }
            return 0;
        }

        public async Task<DivaModArchivePost?> FetchPostAsync(int postId)
        {
            try
            {
                var json = await _http.GetStringAsync($"https://divamodarchive.com/api/v1/posts/{postId}");
                return JsonSerializer.Deserialize<DivaModArchivePost>(json);
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"DMA post fetch failed: {ex.Message}", LoggerType.Error);
                return null;
            }
        }

        /// <summary>
        /// Download and install a DMA post. Extracts the archive looking for subfolders
        /// containing config.toml (these are the actual mods).
        /// </summary>
        public async Task<bool> InstallPostAsync(DivaModArchivePost post, int fileIndex, string modsFolder,
            CancellationTokenSource cts)
        {
            if (post.Files == null || fileIndex >= post.Files.Count)
            {
                Global.logger?.WriteLine("DMA post has no downloadable files.", LoggerType.Error);
                return false;
            }

            var downloadUrl = post.Files[fileIndex].ToString();
            var fileName = post.FileNames != null && fileIndex < post.FileNames.Count
                ? post.FileNames[fileIndex]
                : $"dma-{post.ID}.zip";

            var downloadsDir = Path.Combine(Global.assemblyLocation, "Downloads", "DMA");
            Directory.CreateDirectory(downloadsDir);
            var archivePath = Path.Combine(downloadsDir, fileName);
            if (File.Exists(archivePath)) try { File.Delete(archivePath); } catch { }

            Global.logger?.WriteLine($"Downloading {fileName} from DMA...", LoggerType.Info);
            using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await _http.DownloadAsync(downloadUrl, fs, fileName,
                    new Progress<DownloadProgress>(p =>
                        DownloadProgress?.Invoke(fileName, p.Percentage, p.DownloadedBytes, p.TotalBytes)),
                    cts.Token);
            }

            return await ExtractAndInstallAsync(archivePath, modsFolder, post, cts.Token);
        }

        /// <summary>
        /// Extract the archive and move subfolders containing config.toml into the mods folder.
        /// Writes a mod.json metadata file alongside (matching original DMM behaviour).
        /// </summary>
        private async Task<bool> ExtractAndInstallAsync(string archivePath, string modsFolder, DivaModArchivePost post, CancellationToken cancellationToken = default)
        {
            var tempDir = Path.Combine(Path.GetDirectoryName(archivePath)!, "_extract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                await _extractor.ExtractPackageAsync(archivePath, tempDir, null, cancellationToken);
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Failed to extract {archivePath}: {ex.Message}", LoggerType.Error);
                Directory.Delete(tempDir, true);
                return false;
            }

            // Find all subfolders containing config.toml — these are the actual mods
            var modFolders = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories)
                .Where(d => File.Exists(Path.Combine(d, "config.toml")))
                .ToList();

            if (modFolders.Count == 0)
            {
                // Fallback: if no config.toml found, treat the whole archive as a single mod
                var modName = !string.IsNullOrEmpty(post.Name)
                    ? SanitizeFolderName(post.Name)
                    : Path.GetFileNameWithoutExtension(archivePath);
                var dest = Path.Combine(modsFolder, modName);
                dest = EnsureUniquePath(dest);
                Directory.Move(tempDir, dest);
                WriteMetadata(dest, post);
                Global.logger?.WriteLine($"Installed mod (no config.toml found): {modName}", LoggerType.Info);
                try { File.Delete(archivePath); } catch { }
                return true;
            }

            foreach (var folder in modFolders)
            {
                var folderName = Path.GetFileName(folder);
                var dest = Path.Combine(modsFolder, folderName);
                dest = EnsureUniquePath(dest);
                Directory.Move(folder, dest);
                WriteMetadata(dest, post);
                Global.logger?.WriteLine($"Installed mod: {folderName}", LoggerType.Info);
            }

            // Cleanup
            try { Directory.Delete(tempDir, true); File.Delete(archivePath); } catch { }
            return true;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!Directory.Exists(path)) return path;
            var index = 2;
            while (Directory.Exists($"{path} ({index})")) index++;
            return $"{path} ({index})";
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private static void WriteMetadata(string modDir, DivaModArchivePost post)
        {
            var metaPath = Path.Combine(modDir, "mod.json");
            if (File.Exists(metaPath)) return;
            var meta = new Metadata
            {
                id = post.ID,
                submitter = post.Authors?.FirstOrDefault()?.DisplayNameOrName,
                description = post.Text,
                preview = post.Images?.FirstOrDefault(),
                homepage = post.Link,
                avi = post.Authors?.FirstOrDefault()?.Avatar,
                cat = post.PostType,
                lastupdate = post.Time
            };
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }
    }
}
