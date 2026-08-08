using System;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Maps free-form category strings (from GameBanana's <c>mod.json</c> field <c>cat</c>,
    /// or DMA's <c>post_type</c>) into a fixed set of 7 categories used for grouping the
    /// installed-mods list.
    ///
    /// Canonical categories (DMA enum + Patch, which DMA lacks):
    ///   Song, Cover, Module, UI, Plugin, Patch, Other
    ///
    /// GameBanana emits free-form leaf labels like "Modules Customization", "Custom Song",
    /// "Restorations &amp; Fix", "Other/Misc". DMA emits one of Song/Cover/Module/UI/Plugin/Other.
    /// We normalize everything via case-insensitive substring heuristics so mods from both
    /// sources land in the same bucket.
    /// </summary>
    public static class CategoryNormalizer
    {
        public const string Song = "Song";
        public const string Cover = "Cover";
        public const string Module = "Module";
        public const string Ui = "UI";
        public const string Plugin = "Plugin";
        public const string Patch = "Patch";
        public const string Other = "Other";

        /// <summary>
        /// All canonical categories in display order.
        /// </summary>
        public static readonly string[] OrderedCategories =
        {
            Song, Cover, Module, Ui, Plugin, Patch, Other
        };

        /// <summary>
        /// Normalize a raw category string into one of the canonical categories.
        /// Null/empty/whitespace → Other.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Other;

            var s = raw.Trim().ToLowerInvariant();

            // Order matters: check more specific markers first so e.g.
            // "Custom Song" → Song (not Other) and "Restorations & Fix" → Patch (not Other).
            if (s.Contains("module") || s.Contains("customization") || s.Contains("reskin") || s.Contains("model"))
                return Module;
            if (s.Contains("cover"))
                return Cover;
            if (s.Contains("song") || s.Contains("sound"))
                return Song;
            if (s.Contains("patch") || s.Contains("fix") || s.Contains("restoration"))
                return Patch;
            if (s.Contains("plugin"))
                return Plugin;
            if (s == "ui" || s.Contains("interface") || s.Contains("hud"))
                return Ui;

            // DMA post_type values that match the canonical name directly
            switch (s)
            {
                case "song": return Song;
                case "cover": return Cover;
                case "module": return Module;
                case "ui": return Ui;
                case "plugin": return Plugin;
                case "other": return Other;
            }

            return Other;
        }

        /// <summary>
        /// Display sort order for a canonical category. Lower = shown first.
        /// Unknown categories sort last.
        /// </summary>
        public static int Order(string category)
        {
            return Array.IndexOf(OrderedCategories, category) switch
            {
                int i when i >= 0 => i,
                _ => OrderedCategories.Length
            };
        }

        /// <summary>
        /// Icon per canonical category for the expander header. Returns empty string (no emoji)
        /// — the category name alone is enough and reads cleaner.
        /// </summary>
        public static string Icon(string category) => string.Empty;
    }
}
