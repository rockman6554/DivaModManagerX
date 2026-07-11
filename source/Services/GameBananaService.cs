using System;
using System.Collections.Generic;
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
    /// GameBanana API client.
    ///
    /// API versions:
    ///   - apiv4 (modern): /apiv4/Mod/Index returns an ARRAY of records directly.
    ///                     /apiv4/Mod/{id}?_csvProperties=... returns a single object.
    ///                     Valid csvProperties include: _sName, _sProfileUrl, _aPreviewMedia,
    ///                     _aSubmitter, _aFiles, _tsDateAdded, _aGame, _aCategory, _sDescription.
    ///                     INVALID (cause 400): _aRootCategory, _aAlternateFileSources,
    ///                     _bHasUpdates, _aLatestUpdates.
    ///   - Core (legacy):  /Core/List/New returns [["Mod", 12345], ...] (type+ID pairs).
    ///                     /Core/Item/Data?fields=name,Files().aFiles() returns field values.
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

        public async Task<List<GameBananaRecord>> FetchRecordsAsync(string gameId, int page = 1, int perPage = 20, string? search = null)
        {
            // Try apiv4 first — returns an ARRAY of records directly
            try
            {
                var url = $"https://gamebanana.com/apiv4/Mod/Index?_aFilters[Generic_Game]={gameId}" +
                          $"&_nPage={page}&_nPerpage={perPage}&_sSort=default";
                if (!string.IsNullOrEmpty(search))
                    url += $"&_sName={Uri.EscapeDataString(search)}";

                var json = await _http.GetStringAsync(url);
                if (!string.IsNullOrEmpty(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var records = new List<GameBananaRecord>();

                    // apiv4 returns an array at the root
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in doc.RootElement.EnumerateArray())
                        {
                            var record = JsonSerializer.Deserialize<GameBananaRecord>(r.GetRawText());
                            if (record != null) records.Add(record);
                        }
                    }
                    // Some endpoints wrap in {_aRecords: [...]} — handle both
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                             doc.RootElement.TryGetProperty("_aRecords", out var arr))
                    {
                        foreach (var r in arr.EnumerateArray())
                        {
                            var record = JsonSerializer.Deserialize<GameBananaRecord>(r.GetRawText());
                            if (record != null) records.Add(record);
                        }
                    }

                    if (records.Count > 0) return records;
                }
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"GameBanana apiv4 list fetch failed ({ex.Message}), falling back to Core API...", LoggerType.Warning);
            }

            // Fallback: legacy Core API
            return await FetchRecordsLegacyAsync(gameId, page, perPage);
        }

        private async Task<List<GameBananaRecord>> FetchRecordsLegacyAsync(string gameId, int page, int perPage)
        {
            try
            {
                var listUrl = $"https://api.gamebanana.com/Core/List/New?itemtype=Mod&gameid={gameId}&page={page}";
                var listJson = await _http.GetStringAsync(listUrl);
                using var listDoc = JsonDocument.Parse(listJson);
                var modIds = new List<int>();
                foreach (var entry in listDoc.RootElement.EnumerateArray())
                {
                    if (entry.GetArrayLength() >= 2 && entry[1].TryGetInt32(out var id))
                        modIds.Add(id);
                    if (modIds.Count >= perPage) break;
                }

                var records = new List<GameBananaRecord>();
                foreach (var id in modIds)
                {
                    // Use the Core/Item/Data endpoint (NOT apiv4) for the fallback
                    var record = await FetchRecordViaCoreApiAsync(id);
                    if (record != null) records.Add(record);
                }
                return records;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"GameBanana Core API fetch failed: {ex.Message}", LoggerType.Error);
                return new();
            }
        }

        /// <summary>
        /// Fetch a single mod via the legacy Core/Item/Data endpoint.
        /// Returns null on failure.
        /// </summary>
        private async Task<GameBananaRecord?> FetchRecordViaCoreApiAsync(int modId)
        {
            try
            {
                // Core API uses a different field format: comma-separated field names
                // Valid fields: name, ProfileUrl, Preview().sStructuredDataFullsizeUrl(),
                //               Files().aFiles(), Submitter().sName(), Submitter().sAvatarUrl(),
                //               Category().sName(), Category().sIconUrl(), dateline
                var url = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={modId}" +
                          "&fields=name,ProfileUrl,Preview().sStructuredDataFullsizeUrl()," +
                          "Files().aFiles(),Submitter().sName(),Submitter().sAvatarUrl()," +
                          "Submitter().sUpicUrl(),Category().sName(),Category().sIconUrl(),dateline,updatedate";
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
                var arr = doc.RootElement.EnumerateArray().ToList();
                if (arr.Count < 10) return null;

                var record = new GameBananaRecord
                {
                    Title = arr[0].ValueKind == JsonValueKind.String ? arr[0].GetString() : $"Mod {modId}",
                    Link = arr[1].ValueKind == JsonValueKind.String ? new Uri(arr[1].GetString()!) : null,
                    Owner = new GameBananaMember
                    {
                        Name = arr[4].ValueKind == JsonValueKind.String ? arr[4].GetString() : null,
                        Avatar = arr[5].ValueKind == JsonValueKind.String && Uri.TryCreate(arr[5].GetString(), UriKind.Absolute, out var av) ? av : null,
                        Upic = arr[6].ValueKind == JsonValueKind.String && Uri.TryCreate(arr[6].GetString(), UriKind.Absolute, out var up) ? up : null,
                    },
                    Category = new GameBananaCategory
                    {
                        Name = arr[7].ValueKind == JsonValueKind.String ? arr[7].GetString() : null,
                        Icon = arr[8].ValueKind == JsonValueKind.String && Uri.TryCreate(arr[8].GetString(), UriKind.Absolute, out var ic) ? ic : null,
                    },
                    DateAddedLong = arr[9].ValueKind == JsonValueKind.Number ? arr[9].GetInt64() : 0,
                    DateUpdatedLong = arr.Count > 10 && arr[10].ValueKind == JsonValueKind.Number ? arr[10].GetInt64() : 0,
                };

                // Preview image
                if (arr[2].ValueKind == JsonValueKind.String && Uri.TryCreate(arr[2].GetString(), UriKind.Absolute, out var prev))
                {
                    var pathPart = prev.GetLeftPart(UriPartial.Path);
                    var baseUri = new Uri(pathPart.Substring(0, pathPart.LastIndexOf('/') + 1));
                    var filePart = Uri.UnescapeDataString(prev.Segments.Last());
                    record.Media = new List<GameBananaImage>
                    {
                        new() { Type = "image", Base = baseUri, File = new Uri(filePart, UriKind.Relative) }
                    };
                }

                // Files
                if (arr[3].ValueKind == JsonValueKind.Object)
                {
                    record.AllFiles = new List<GameBananaItemFile>();
                    foreach (var fProp in arr[3].EnumerateObject())
                    {
                        var f = fProp.Value;
                        if (f.ValueKind != JsonValueKind.Object) continue;
                        var file = new GameBananaItemFile();
                        if (f.TryGetProperty("_sFile", out var sf) && sf.ValueKind == JsonValueKind.String)
                            file.FileName = sf.GetString();
                        if (f.TryGetProperty("_nFilesize", out var ns) && ns.ValueKind == JsonValueKind.Number)
                            file.Filesize = ns.GetInt64();
                        if (f.TryGetProperty("_sDownloadUrl", out var sd) && sd.ValueKind == JsonValueKind.String)
                            file.DownloadUrl = sd.GetString();
                        if (f.TryGetProperty("_tsDateAdded", out var ts) && ts.ValueKind == JsonValueKind.Number)
                            file.DateAddedLong = ts.GetInt64();
                        record.AllFiles.Add(file);
                    }
                }

                return record;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"GameBanana Core item fetch for {modId} failed: {ex.Message}", LoggerType.Warning);
                return null;
            }
        }

        public async Task<GameBananaAPIV4?> FetchItemAsync(int modId)
        {
            // Use apiv4 with ONLY valid csvProperties (avoid 400 errors)
            try
            {
                var url = $"https://gamebanana.com/apiv4/Mod/{modId}" +
                          "?_csvProperties=_sName,_sProfileUrl,_aPreviewMedia,_sDescription,_aSubmitter,_aCategory,_aGame,_aFiles,_tsDateAdded,_tsDateModified";
                var json = await _http.GetStringAsync(url);
                return JsonSerializer.Deserialize<GameBananaAPIV4>(json);
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"GameBanana apiv4 item fetch failed: {ex.Message}", LoggerType.Warning);
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
                try { Directory.Move(tempDir, dest); } catch { }
                if (record != null) WriteMetadata(dest, record);
                Global.logger?.WriteLine($"Installed mod (no config.toml found): {modName}", LoggerType.Info);
                try { File.Delete(archivePath); } catch { }
                return true;
            }

            foreach (var folder in modFolders)
            {
                var folderName = Path.GetFileName(folder);
                var dest = Path.Combine(modsFolder, folderName);
                dest = EnsureUniquePath(dest);
                try { Directory.Move(folder, dest); } catch { }
                if (record != null) WriteMetadata(dest, record);
                Global.logger?.WriteLine($"Installed mod: {folderName}", LoggerType.Info);
            }

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
