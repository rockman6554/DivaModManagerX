using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DivaModManager.Models;
using DivaModManager.Helpers;

namespace DivaModManager.Services
{
    /// <summary>
    /// GameBanana API client — replicates the upstream TekkaGB DivaModManager search
    /// strategy from <c>FeedGenerator.cs</c>.
    ///
    /// We use the apiv6 list endpoints which filter SERVER-SIDE by game and (optionally)
    /// by name. This means ONE HTTP request per page navigation instead of the hundreds
    /// of per-mod fan-out requests the previous implementation did (which tripped
    /// Cloudflare's HTTP 429 rate limit).
    ///
    ///   - Browse (no query): GET /apiv6/Mod/ByGame?_aGameRowIds[]=16522&...
    ///   - Search (query):    GET /apiv6/Mod/ByName?_sName=*{query}*&_idGameRow=16522&...
    ///
    /// The server returns full records (name, submitter, files, media, dates, counts)
    /// in a single response. The total record count is read from the response header
    /// <c>X-GbApi-Metadata_nRecordCount</c> for accurate pagination.
    ///
    /// Game ID for Project DIVA Mega Mix+ is 16522.
    /// </summary>
    public class GameBananaService
    {
        public const string MegaMixGameId = "16522";

        // Shared HttpClient — prevents socket exhaustion when reopening the browser
        private static readonly HttpClient _http;

        static GameBananaService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DivaModManagerLinux/1.3.1 (+https://github.com/TekkaGB/DivaModManager)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        private readonly ZipExtractor _extractor = new();

        public event Action<string, float, long, long>? DownloadProgress;

        /// <summary>
        /// Result of a feed fetch. Carries the records plus pagination metadata
        /// read from the <c>X-GbApi-Metadata_nRecordCount</c> response header.
        /// </summary>
        public class FeedResult
        {
            public List<GameBananaRecord> Records { get; set; } = new();
            public int TotalRecords { get; set; }
            public int TotalPages { get; set; }
        }

        // ---- Feed cache (15-minute TTL, LRU capped at 15 entries — mirrors upstream) ----
        private static readonly Dictionary<string, GameBananaModList> _feedCache = new();
        private static readonly object _cacheLock = new();

        /// <summary>
        /// Fetch one page of mods for the Mega Mix+ game, optionally filtered by name.
        /// Issues exactly ONE HTTP request (or zero on cache hit).
        /// </summary>
        public async Task<FeedResult> FetchRecordsAsync(string gameId, int page = 1, int perPage = 20, string? search = null)
        {
            var url = BuildFeedUrl(gameId, page, perPage, search);

            // Cache check
            lock (_cacheLock)
            {
                if (_feedCache.TryGetValue(url, out var cached) && cached.IsValid)
                {
                    return new FeedResult
                    {
                        Records = cached.Records?.ToList() ?? new(),
                        TotalPages = (int)cached.TotalPages
                    };
                }
            }

            try
            {
                Global.logger?.WriteLine($"[GB] Fetching: {url.Substring(0, Math.Min(120, url.Length))}...", LoggerType.Info);
                using var resp = await _http.GetAsync(url);
                Global.logger?.WriteLine($"[GB] Status: {(int)resp.StatusCode} {resp.StatusCode}", LoggerType.Info);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync();
                Global.logger?.WriteLine($"[GB] Response: {json.Length} chars", LoggerType.Info);
                var records = JsonSerializer.Deserialize<List<GameBananaRecord>>(json) ?? new();
                Global.logger?.WriteLine($"[GB] Deserialized: {records.Count} records", LoggerType.Info);

                // Parse total record count from metadata header
                var totalRecords = 0;
                if (resp.Headers.TryGetValues("X-GbApi-Metadata_nRecordCount", out var values))
                {
                    var headerVal = values.FirstOrDefault();
                    if (headerVal != null && int.TryParse(headerVal, out var tr)) totalRecords = tr;
                }
                var totalPages = perPage > 0 ? (int)Math.Ceiling(totalRecords / (double)perPage) : 1;
                if (totalPages < 1) totalPages = records.Count > 0 ? page : 1;

                // Store in cache
                var entry = new GameBananaModList
                {
                    Records = new ObservableCollection<GameBananaRecord>(records),
                    TotalPages = totalPages,
                    TimeFetched = DateTime.UtcNow
                };
                lock (_cacheLock)
                {
                    if (_feedCache.Count >= 15)
                    {
                        // Evict the oldest entry (LRU-ish by fetch time)
                        var oldest = _feedCache.OrderByDescending(k => k.Value.TimeFetched).Last();
                        _feedCache.Remove(oldest.Key);
                    }
                    _feedCache[url] = entry;
                }

                return new FeedResult { Records = records, TotalRecords = totalRecords, TotalPages = totalPages };
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"[GB] feed fetch FAILED: {ex.GetType().Name}: {ex.Message}", LoggerType.Error);
                return new FeedResult();
            }
        }

        /// <summary>
        /// Build the apiv6 feed URL. Replicates <c>FeedGenerator.GenerateUrl</c> from the
        /// upstream TekkaGB DivaModManager.
        /// </summary>
        private static string BuildFeedUrl(string gameId, int page, int perPage, string? search)
        {
            var hasSearch = !string.IsNullOrWhiteSpace(search);
            var url = hasSearch
                ? $"https://gamebanana.com/apiv6/Mod/ByName?_sName=*{Uri.EscapeDataString(search!.Trim())}*&_idGameRow={gameId}&"
                : $"https://gamebanana.com/apiv6/Mod/ByGame?_aGameRowIds[]={gameId}&";

            url += "_csvProperties=_sName,_sModelName,_sProfileUrl,_aSubmitter,_tsDateUpdated,_tsDateAdded," +
                   "_aPreviewMedia,_sText,_sDescription,_aCategory,_aRootCategory,_aGame," +
                   "_nViewCount,_nLikeCount,_nDownloadCount,_aFiles,_aModManagerIntegrations," +
                   "_bIsNsfw,_aAlternateFileSources";
            url += $"&_nPerpage={perPage}";
            // _aArgs value must be URL-encoded — it contains spaces ("_sbIsNsfw = false").
            // Leaving it raw produces an invalid URI that HttpClient rejects (the request never
            // reaches GameBanana and the catch returns an empty list — appearing as "no results").
            url += $"&_aArgs[]={Uri.EscapeDataString("_sbIsNsfw = false")}"; // hide NSFW by default
            url += "&_sOrderBy=_tsDateUpdated,DESC"; // sort by recent
            url += $"&_nPage={page}";
            return url;
        }

        /// <summary>
        /// Fetch a single mod via apiv6 (the modern endpoint that returns full data).
        /// Used for 1-click install / "From URL" install.
        /// </summary>
        public async Task<GameBananaAPIV4?> FetchItemAsync(int modId)
        {
            try
            {
                var url = $"https://gamebanana.com/apiv6/Mod/{modId}" +
                          "?_csvProperties=_sName,_sProfileUrl,_aPreviewMedia,_sDescription,_aSubmitter," +
                          "_aCategory,_aGame,_aFiles,_tsDateAdded,_tsDateModified,_aAlternateFileSources";
                var json = await _http.GetStringAsync(url);
                return JsonSerializer.Deserialize<GameBananaAPIV4>(json);
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"GameBanana apiv6 item fetch failed: {ex.Message}", LoggerType.Warning);
                return null;
            }
        }

        /// <summary>
        /// Parse a divamodmanager:// 1-click install URL.
        /// GameBanana format: divamodmanager:&lt;direct_download_url&gt;,&lt;ModType&gt;,&lt;ModID&gt;
        /// DMA format:        divamodmanager:dma/&lt;post_id&gt;
        /// </summary>
        public static (string? source, int id, string? directUrl) ParseProtocolUrl(string protocolUrl)
        {
            try
            {
                var line = protocolUrl.Replace("divamodmanager:", "");
                var data = line.Split(',');
                if (data.Length > 1)
                {
                    var directUrl = data[0];
                    var modType = data[1];
                    var modIdStr = data.Length > 2 ? data[2] : Regex.Match(directUrl, @"\d+$").Value;
                    if (int.TryParse(modIdStr, out var modId))
                        return ("gamebanana", modId, directUrl);
                }
                else if (data.Length == 1)
                {
                    var s = data[0];
                    if (s.StartsWith("dma/"))
                    {
                        var idStr = s.Substring(4);
                        if (int.TryParse(idStr, out var postId))
                            return ("dma", postId, null);
                    }
                }
            }
            catch { }
            return (null, 0, null);
        }

        public async Task<bool> InstallFromFileAsync(string downloadUrl, string fileName, string modsFolder,
            GameBananaRecord? record, CancellationTokenSource cts)
        {
            if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder))
            {
                Global.logger?.WriteLine("Mods folder not set. Run Setup first.", LoggerType.Warning);
                return false;
            }

            var downloadsDir = Path.Combine(Global.assemblyLocation, "Downloads", "GameBanana");
            Directory.CreateDirectory(downloadsDir);
            var archivePath = Path.Combine(downloadsDir, fileName);
            if (File.Exists(archivePath)) try { File.Delete(archivePath); } catch { }

            Global.logger?.WriteLine($"Downloading {fileName} from GameBanana...", LoggerType.Info);
            using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await _http.DownloadAsync(downloadUrl, fs, fileName,
                    new Progress<DownloadProgress>(p =>
                        DownloadProgress?.Invoke(fileName, p.Percentage, p.DownloadedBytes, p.TotalBytes)),
                    cts.Token);
            }

            return await ExtractAndInstallAsync(archivePath, modsFolder, record, cts.Token);
        }

        private async Task<bool> ExtractAndInstallAsync(string archivePath, string modsFolder, GameBananaRecord? record, CancellationToken cancellationToken = default)
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
                try { Directory.Delete(tempDir, true); } catch { }
                return false;
            }

            var modFolders = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories)
                .Where(d => File.Exists(Path.Combine(d, "config.toml")))
                .ToList();

            if (modFolders.Count == 0)
            {
                var modName = !string.IsNullOrEmpty(record?.Title)
                    ? SanitizeFolderName(record.Title)
                    : Path.GetFileNameWithoutExtension(archivePath);
                var dest = Path.Combine(modsFolder, modName);
                dest = EnsureUniquePath(dest);
                try { Directory.Move(tempDir, dest); }
                catch (Exception moveEx)
                {
                    Global.logger?.WriteLine($"Failed to move mod folder to {dest}: {moveEx.Message}", LoggerType.Error);
                    return false;
                }
                if (record != null) WriteMetadata(dest, record);
                Global.logger?.WriteLine($"Installed mod (no config.toml found): {modName}", LoggerType.Info);
                try { File.Delete(archivePath); } catch { }
                return true;
            }

            var anyMoved = false;
            foreach (var folder in modFolders)
            {
                var folderName = Path.GetFileName(folder);
                var dest = Path.Combine(modsFolder, folderName);
                dest = EnsureUniquePath(dest);
                try { Directory.Move(folder, dest); anyMoved = true; }
                catch (Exception moveEx)
                {
                    Global.logger?.WriteLine($"Failed to move mod folder {folderName} to {dest}: {moveEx.Message}", LoggerType.Error);
                    continue;
                }
                if (record != null) WriteMetadata(dest, record);
                Global.logger?.WriteLine($"Installed mod: {folderName}", LoggerType.Info);
            }

            try { Directory.Delete(tempDir, true); File.Delete(archivePath); } catch { }
            return anyMoved;
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

        private static void WriteMetadata(string modDir, GameBananaRecord record)
        {
            var metaPath = Path.Combine(modDir, "mod.json");
            if (File.Exists(metaPath)) return;
            var meta = new Metadata
            {
                submitter = record.Owner?.Name,
                description = record.Description,
                preview = record.Image,
                homepage = record.Link,
                avi = record.Owner?.Avatar,
                upic = record.Owner?.Upic,
                cat = record.CategoryName,
                caticon = record.Category?.Icon,
                lastupdate = record.DateUpdated
            };
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }
    }
}
