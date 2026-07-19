using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DivaModManager.Models
{
    public class GameBananaItem
    {
        [JsonPropertyName("Game().name")]
        public string? Game { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("views")]
        public int? Views { get; set; }
        [JsonPropertyName("downloads")]
        public int? Downloads { get; set; }
        [JsonPropertyName("likes")]
        public int? Likes { get; set; }
        [JsonPropertyName("Owner().name")]
        public string? Owner { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("RootCategory().name")]
        public string? RootCat { get; set; }
        [JsonPropertyName("Preview().sSubFeedImageUrl()")]
        public Uri? SubFeedImage { get; set; }
        [JsonPropertyName("Preview().sStructuredDataFullsizeUrl()")]
        public Uri? EmbedImage { get; set; }
        [JsonPropertyName("Updates().bSubmissionHasUpdates()")]
        public bool? HasUpdates { get; set; }

        [JsonPropertyName("Updates().aGetLatestUpdates()")]
        public GameBananaItemUpdate[]? Updates { get; set; }
        [JsonPropertyName("Files().aFiles()")]
        public Dictionary<string, GameBananaItemFile>? Files { get; set; }
    }

    public class GameBananaItemFile
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);
        [JsonPropertyName("_idRow")]
        public string? Id { get; set; }
        [JsonPropertyName("_sFile")]
        public string? FileName { get; set; }

        [JsonPropertyName("_nFilesize")]
        public long Filesize { get; set; }
        [JsonIgnore]
        public string ConvertedFileSize => Helpers.StringConverters.FormatSize(Filesize);

        [JsonPropertyName("_sDownloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("_sDescription")]
        public string? Description { get; set; }
        [JsonPropertyName("_bContainsExe")]
        public bool ContainsExe { get; set; }
        [JsonPropertyName("_nDownloadCount")]
        public int Downloads { get; set; }
        [JsonIgnore]
        public string DownloadString => Helpers.StringConverters.FormatNumber(Downloads);

        [JsonPropertyName("_tsDateAdded")]
        public long DateAddedLong { get; set; }

        [JsonIgnore]
        public DateTime DateAdded => Epoch.AddSeconds(DateAddedLong);

        [JsonIgnore]
        public string TimeSinceUpload => Helpers.StringConverters.FormatTimeAgo(DateTime.UtcNow - DateAdded);
    }

    public class GameBananaGame
    {
        [JsonPropertyName("_sName")]
        public string? Name { get; set; }
    }

    public class GameBananaAPIV4
    {
        [JsonPropertyName("_sName")]
        public string? Title { get; set; }
        [JsonPropertyName("_aGame")]
        public GameBananaGame? Game { get; set; }
        [JsonPropertyName("_sProfileUrl")]
        public Uri? Link { get; set; }
        [JsonIgnore]
        public Uri Image
        {
            get
            {
                var firstImage = Media?.FirstOrDefault(x => x?.Type == "image");
                var url = firstImage?.FullImageUrl ?? "https://images.gamebanana.com/static/img/DefaultEmbeddables/Sound.jpg";
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : new Uri("https://images.gamebanana.com/static/img/DefaultEmbeddables/Sound.jpg");
            }
        }
        [JsonPropertyName("_aPreviewMedia")]
        public List<GameBananaImage>? Media { get; set; }
        [JsonPropertyName("_sDescription")]
        public string? Description { get; set; }
        [JsonPropertyName("_aSubmitter")]
        public GameBananaMember? Owner { get; set; }
        [JsonPropertyName("_aCategory")]
        public GameBananaCategory? Category { get; set; }
        [JsonPropertyName("_aSuperCategory")]
        public GameBananaCategory? RootCategory { get; set; }
        [JsonIgnore]
        public string CategoryName => RootCategory == null
            ? Helpers.StringConverters.FormatSingular(null, Category?.Name ?? "")
            : Helpers.StringConverters.FormatSingular(RootCategory.Name, Category?.Name ?? "");
        [JsonPropertyName("_aFiles")]
        public List<GameBananaItemFile>? Files { get; set; }
        [JsonPropertyName("_tsDateUpdated")]
        public long? DateUpdatedLong { get; set; }
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        [JsonIgnore]
        public DateTime? DateUpdated => DateUpdatedLong != null ? Epoch.AddSeconds((long)DateUpdatedLong) : null;
        [JsonPropertyName("_aAlternateFileSources")]
        public List<GameBananaAlternateFileSource>? AlternateFileSources { get; set; }
        [JsonPropertyName("_bHasUpdates")]
        public bool? HasUpdates { get; set; }
        [JsonPropertyName("_aLatestUpdates")]
        public GameBananaItemUpdate[]? Updates { get; set; }
    }

    public class GameBananaInstallerIntegration
    {
        [JsonPropertyName("_sDownloadUrl")]
        public string? Download { get; set; }
    }

    public class GameBananaCategory
    {
        [JsonPropertyName("_idRow")]
        public int? ID { get; set; }
        [JsonPropertyName("_idParentCategoryRow")]
        public int? RootID { get; set; }
        [JsonPropertyName("_sModelName")]
        public string? Model { get; set; }
        [JsonPropertyName("_sName")]
        public string? Name { get; set; }
        [JsonPropertyName("_sIconUrl")]
        public Uri? Icon { get; set; }
        [JsonIgnore]
        public bool HasIcon => Icon?.OriginalString.Length > 0;
    }

    public class GameBananaMember
    {
        [JsonPropertyName("_sName")]
        public string? Name { get; set; }
        [JsonPropertyName("_sAvatarUrl")]
        public Uri? Avatar { get; set; }
        [JsonPropertyName("_sUpicUrl")]
        public Uri? Upic { get; set; }
        [JsonIgnore]
        public bool HasUpic => Upic?.OriginalString.Length > 0;
    }

    public class GameBananaItemUpdate
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);
        [JsonPropertyName("_sTitle")]
        public string? Title { get; set; }
        [JsonPropertyName("_sVersion")]
        public string? Version { get; set; }

        [JsonPropertyName("_aChangeLog")]
        public GameBananaItemUpdateChange[]? Changes { get; set; }

        [JsonPropertyName("_sText")]
        public string? Text { get; set; }

        [JsonPropertyName("_tsDateAdded")]
        public long DateAddedLong { get; set; }

        [JsonIgnore]
        public DateTime DateAdded => Epoch.AddSeconds(DateAddedLong);
    }

    public class GameBananaItemUpdateChange
    {
        [JsonPropertyName("cat")]
        public string? Category { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class GameBananaRecord
    {
        [JsonPropertyName("_sName")]
        public string? Title { get; set; }
        [JsonIgnore]
        public bool IsSpoiler => (Title ?? "").ToUpperInvariant().StartsWith("(SPOILER)");
        [JsonPropertyName("_sProfileUrl")]
        public Uri? Link { get; set; }
        [JsonPropertyName("_aAlternateFileSources")]
        public List<GameBananaAlternateFileSource>? AlternateFileSources { get; set; }
        [JsonIgnore]
        public bool HasAltLinks => AlternateFileSources != null;
        [JsonIgnore]
        public Uri Image
        {
            get
            {
                var firstImage = Media?.FirstOrDefault(x => x?.Type == "image");
                var url = firstImage?.FullImageUrl ?? "https://images.gamebanana.com/static/img/DefaultEmbeddables/Sound.jpg";
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : new Uri("https://images.gamebanana.com/static/img/DefaultEmbeddables/Sound.jpg");
            }
        }
        [JsonPropertyName("_aPreviewMedia")]
        public List<GameBananaImage>? Media { get; set; }
        [JsonPropertyName("_sDescription")]
        public string? Description { get; set; }
        [JsonIgnore]
        public bool HasDescription => (Description?.Length ?? 0) > 40;
        [JsonPropertyName("_sText")]
        public string? Text { get; set; }
        [JsonIgnore]
        public string ConvertedText => ConvertHtmlToText(Text ?? "");
        [JsonPropertyName("_nViewCount")]
        public int Views { get; set; }
        [JsonPropertyName("_nLikeCount")]
        public int Likes { get; set; }
        [JsonPropertyName("_nDownloadCount")]
        public int Downloads { get; set; }
        [JsonIgnore]
        public string DownloadString => Helpers.StringConverters.FormatNumber(Downloads);
        [JsonIgnore]
        public string ViewString => Helpers.StringConverters.FormatNumber(Views);
        [JsonIgnore]
        public string LikeString => Helpers.StringConverters.FormatNumber(Likes);
        [JsonPropertyName("_aSubmitter")]
        public GameBananaMember? Owner { get; set; }
        [JsonPropertyName("_aFiles")]
        public List<GameBananaItemFile>? AllFiles { get; set; }
        [JsonPropertyName("_aCategory")]
        public GameBananaCategory? Category { get; set; }
        [JsonPropertyName("_aRootCategory")]
        public GameBananaCategory? RootCategory { get; set; }
        [JsonIgnore]
        public string CategoryName => Helpers.StringConverters.FormatSingular(RootCategory?.Name, Category?.Name ?? "");
        [JsonIgnore]
        public bool HasLongCategoryName => CategoryName.Length > 30;
        [JsonIgnore]
        public bool Compatible => (AllFiles?.Count ?? 0) > 0;

        [JsonPropertyName("_tsDateUpdated")]
        public long DateUpdatedLong { get; set; }
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        [JsonIgnore]
        public DateTime DateUpdated => Epoch.AddSeconds(DateUpdatedLong);
        [JsonPropertyName("_tsDateAdded")]
        public long DateAddedLong { get; set; }

        [JsonIgnore]
        public DateTime DateAdded => Epoch.AddSeconds(DateAddedLong);
        [JsonIgnore]
        public string DateAddedFormatted => $"Added {Helpers.StringConverters.FormatTimeAgo(DateTime.UtcNow - DateAdded)}";
        [JsonIgnore]
        public bool HasUpdates => DateAdded.CompareTo(DateUpdated) != 0;
        [JsonIgnore]
        public string DateUpdatedAgo => $"Updated {Helpers.StringConverters.FormatTimeAgo(DateTime.UtcNow - DateUpdated)}";

        private string ConvertHtmlToText(string html)
        {
            html = html.Replace("<br>", "\n");
            html = html.Replace("</li>", "\n");
            html = html.Replace("</h3>", "\n");
            html = html.Replace("</h2>", "\n");
            html = html.Replace("</h1>", "\n");
            html = html.Replace("<ul>", "\n");
            html = html.Replace("<li>", "\u2022 ");
            html = html.Replace("&nbsp;", " ");
            html = html.Replace("\\u00a0", " ");
            html = html.Replace("&amp;", "&");
            html = html.Replace("&gt;", ">");
            html = html.Replace("\t", string.Empty);
            html = Regex.Replace(html, "<.*?>", string.Empty);
            html = Regex.Replace(html, "[\\r\\n]{3,}", "\n\n", RegexOptions.Multiline);
            return html.Trim();
        }

        [JsonPropertyName("_bIsNsfw")]
        public bool IsNsfw { get; set; }
    }

    public class GameBananaModList
    {
        public ObservableCollection<GameBananaRecord>? Records { get; set; }
        public double TotalPages { get; set; }
        public DateTime TimeFetched = DateTime.UtcNow;
        public bool IsValid => (DateTime.UtcNow - TimeFetched).TotalMinutes < 15;
    }

    public class GameBananaAlternateFileSource
    {
        [JsonPropertyName("url")]
        public Uri? Url { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; } = "Mirror";
    }

    public class GameBananaImage
    {
        [JsonPropertyName("_sType")]
        public string? Type { get; set; }
        [JsonPropertyName("_sUrl")]
        public Uri? Audio { get; set; }
        [JsonPropertyName("_sBaseUrl")]
        public string? Base { get; set; }
        [JsonPropertyName("_sFile")]
        public string? File { get; set; }
        // Smaller thumbnail variants — _sFile220 is 220px wide, ideal for list display (~11KB vs ~300KB)
        [JsonPropertyName("_sFile220")]
        public string? File220 { get; set; }
        [JsonPropertyName("_sFile530")]
        public string? File530 { get; set; }
        [JsonPropertyName("_sFile100")]
        public string? File100 { get; set; }
        [JsonPropertyName("_sCaption")]
        public string? Caption { get; set; }

        /// <summary>
        /// Returns the smallest available thumbnail URL (prefer _sFile220 for list display).
        /// Falls back to _sFile (full size) if no thumbnail variant exists.
        /// Returns null if Base or any File variant is missing.
        ///
        /// IMPORTANT: We use string concatenation ($"{Base}/{File}") instead of
        /// `new Uri(baseUri, relativeUri)` because the Uri constructor treats the base
        /// as a file path (not a directory) when it doesn't end with '/', causing
        /// the last path segment to be replaced. String concat is the correct approach.
        /// </summary>
        [JsonIgnore]
        public string? ThumbnailUrl
        {
            get
            {
                if (string.IsNullOrEmpty(Base)) return null;
                // Prefer 220px thumbnail (smallest, fastest to load)
                if (!string.IsNullOrEmpty(File220)) return $"{Base}/{File220}";
                if (!string.IsNullOrEmpty(File100)) return $"{Base}/{File100}";
                if (!string.IsNullOrEmpty(File530)) return $"{Base}/{File530}";
                if (!string.IsNullOrEmpty(File)) return $"{Base}/{File}";
                return null;
            }
        }

        /// <summary>
        /// Full-size image URL (for the detail panel).
        /// </summary>
        [JsonIgnore]
        public string? FullImageUrl
        {
            get
            {
                if (string.IsNullOrEmpty(Base) || string.IsNullOrEmpty(File)) return null;
                return $"{Base}/{File}";
            }
        }
    }
}
