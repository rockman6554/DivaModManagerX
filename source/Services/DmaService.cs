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
    ///   GET /api/v1/posts/count?query=...&limit=30
    ///       → Returns total post count for the query (used to calculate total pages)
    ///   GET /api/v1/posts/{id}
    ///       → Single post
    ///   GET /api/v1/posts/{id}/download/{fileIndex}
    ///       → Download a file (returns the actual archive)
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

        public async Task<List<DivaModArchivePost>> FetchFeedAsync(
            int page = 1, int perPage = 20,
            DmaFeedSort sort = DmaFeedSort.Latest,
            DmaFeedFilter filter = DmaFeedFilter.None,
            string? search = null)
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
            if (!string.IsNullOrEmpty(search))
                url += $"&query={Uri.EscapeDataString(search)}";
            var offset = (page - 1) * perPage;
            url += $"&offset={offset}&limit={perPage}";

            try
            {
                var json = await _http.GetStringAsync(url);
                var posts = JsonSerializer.Deserialize<List<DivaModArchivePost>>(json) ?? new();
                return posts;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"DMA feed fetch failed: {ex.Message}", LoggerType.Error);
                return new();
            }
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
