using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace DivaModManager.Models
{
    /// <summary>
    /// DivaModArchive (DMA) post model. DMA is a community mod archive at divamodarchive.com
    /// with a simple JSON API at /api/v1/posts.
    /// </summary>
    public class DivaModArchivePost
    {
        [JsonPropertyName("id")]
        public int ID { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("images")]
        public List<Uri>? Images { get; set; }
        [JsonPropertyName("files")]
        public List<Uri>? Files { get; set; }
        [JsonPropertyName("file_names")]
        public List<string>? FileNames { get; set; }
        [JsonPropertyName("file_sizes")]
        public List<long>? FileSizes { get; set; }
        [JsonPropertyName("time")]
        public DateTime Time { get; set; }
        [JsonIgnore]
        public string DateUpdatedAgo => $"Updated {Helpers.StringConverters.FormatTimeAgo(DateTime.UtcNow - Time)}";
        [JsonPropertyName("post_type")]
        public string? PostType { get; set; }
        [JsonIgnore]
        public Uri Link => new Uri($"https://divamodarchive.com/posts/{ID}");
        [JsonPropertyName("like_count")]
        public int Likes { get; set; }
        [JsonPropertyName("download_count")]
        public int Downloads { get; set; }
        [JsonIgnore]
        public string DownloadString => Helpers.StringConverters.FormatNumber(Downloads);
        [JsonIgnore]
        public string LikeString => Helpers.StringConverters.FormatNumber(Likes);
        [JsonPropertyName("authors")]
        public List<DivaModArchiveUser>? Authors { get; set; }
        [JsonPropertyName("dependencies")]
        public List<int>? Dependencies { get; set; }
        [JsonPropertyName("explicit")]
        public bool Explicit { get; set; }
    }

    public class DivaModArchiveUser
    {
        [JsonPropertyName("id")]
        public double ID { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("avatar")]
        public Uri? Avatar { get; set; }
        [JsonIgnore]
        public string DisplayNameOrName => DisplayName ?? Name ?? "Unknown";
    }

    public class DivaModArchiveModList
    {
        public ObservableCollection<DivaModArchivePost>? Posts { get; set; }
        public double TotalPages { get; set; }
        public DateTime TimeFetched = DateTime.UtcNow;
        public bool IsValid => (DateTime.UtcNow - TimeFetched).TotalMinutes < 15;
    }

    public enum DmaFeedSort
    {
        Latest,
        Downloads,
        Likes,
    }

    public enum DmaFeedFilter
    {
        None,
        Song,
        Cover,
        Module,
        Ui,
        Plugin,
        Other
    }
}
