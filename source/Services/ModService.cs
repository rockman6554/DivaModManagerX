using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;
using DivaModManager.Models;

namespace DivaModManager.Services
{
    /// <summary>
    /// Manages the on-disk mods folder and the in-memory ModList.
    ///
    /// On disk, the layout is:
    ///   {ModsFolder}/<ModName>/mod.toml        (mod metadata)
    ///   {ModsFolder}/<ModName>/preview.png     (optional preview image)
    ///   {ModsFolder}/<ModName>/...             (mod files)
    ///
    /// DML reads config.toml (next to the game exe) which lists the active mods + priority.
    /// We rewrite that file whenever the user toggles/reorders mods.
    /// </summary>
    public class ModService
    {
        public ObservableCollection<Mod> ModList { get; private set; } = new();

        /// <summary>
        /// Refresh the in-memory ModList from disk. Folders that exist on disk but not in the
        /// list get added (enabled=true by default). Folders in the list that no longer exist
        /// on disk get removed.
        /// </summary>
        public void Refresh(string modsFolder)
        {
            if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder))
            {
                ModList.Clear();
                return;
            }

            var onDisk = new List<string>();
            foreach (var dir in Directory.GetDirectories(modsFolder))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".")) continue;
                onDisk.Add(name);
            }
            onDisk.Sort(new Helpers.NaturalSort());

            // Remove mods that no longer exist on disk
            for (int i = ModList.Count - 1; i >= 0; i--)
                if (!onDisk.Contains(ModList[i].name))
                    ModList.RemoveAt(i);

            // Add new mods at the end (preserving any existing order), reading each mod's
            // category from its mod.json (preferred) or mod.toml on disk.
            foreach (var name in onDisk)
                if (!ModList.Any(m => m.name == name))
                {
                    var mod = new Mod { name = name, enabled = true };
                    mod.Category = ReadModCategory(Path.Combine(modsFolder, name));
                    ModList.Add(mod);
                }

            // Also refresh the category of existing mods (mod.json may have been edited
            // externally since the last load).
            foreach (var mod in ModList)
                mod.Category = ReadModCategory(Path.Combine(modsFolder, mod.name));
        }

        /// <summary>
        /// Read a mod's category from its mod.json (preferred — written by GameBanana/DMA
        /// install flows) or mod.toml fallback. Returns a canonical category
        /// (Song/Cover/Module/UI/Plugin/Patch/Other) via <see cref="Helpers.CategoryNormalizer"/>.
        /// </summary>
        private static string ReadModCategory(string modDir)
        {
            string? raw = null;
            // Prefer mod.json (JSON), the format written by the install flows.
            var modJson = Path.Combine(modDir, "mod.json");
            if (File.Exists(modJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(modJson));
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("cat", out var catEl) &&
                        catEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        raw = catEl.GetString();
                }
                catch { }
            }

            // Fallback: mod.toml (written by CreateMod / older DMM).
            if (raw == null)
            {
                var modToml = Path.Combine(modDir, "mod.toml");
                if (File.Exists(modToml))
                {
                    try
                    {
                        var text = File.ReadAllText(modToml);
                        if (Toml.TryToModel(text, out TomlTable? table, out _) &&
                            table.TryGetValue("category", out var c))
                            raw = c?.ToString();
                    }
                    catch { }
                }
            }

            return Helpers.CategoryNormalizer.Normalize(raw);
        }

        /// <summary>
        /// Re-read each in-memory mod's category from its on-disk mod.json/mod.toml.
        /// Used after a loadout swap, because mods deserialized from Config.json only
        /// carry name + enabled (Category is [JsonIgnore]) and would otherwise all fall
        /// back to "Other".
        /// </summary>
        public void RefreshCategories(string modsFolder)
        {
            if (string.IsNullOrEmpty(modsFolder)) return;
            foreach (var mod in ModList)
                mod.Category = ReadModCategory(Path.Combine(modsFolder, mod.name));
        }

        /// <summary>
        /// Read a mod's metadata (mod.toml) if present.
        /// </summary>
        public Metadata? ReadMetadata(string modsFolder, string modName)
        {
            var modToml = Path.Combine(modsFolder, modName, "mod.toml");
            if (!File.Exists(modToml)) return null;
            try
            {
                var text = File.ReadAllText(modToml);
                if (Toml.TryToModel(text, out TomlTable? table, out _))
                {
                    var meta = new Metadata();
                    if (table.TryGetValue("author", out var a)) meta.submitter = a?.ToString();
                    if (table.TryGetValue("description", out var d)) meta.description = d?.ToString();
                    if (table.TryGetValue("category", out var c)) meta.cat = c?.ToString();
                    if (table.TryGetValue("homepage", out var h) && Uri.TryCreate(h?.ToString(), UriKind.Absolute, out var uri))
                        meta.homepage = uri;
                    return meta;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Reorder the mod list (move item at fromIndex to toIndex).
        /// </summary>
        public void Reorder(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= ModList.Count) return;
            if (toIndex < 0 || toIndex >= ModList.Count) return;
            if (fromIndex == toIndex) return;
            var item = ModList[fromIndex];
            ModList.RemoveAt(fromIndex);
            ModList.Insert(toIndex, item);
        }

        /// <summary>
        /// Write the active mod priority list to {gameExeDir}/config.toml (the file DML reads at startup).
        /// </summary>
        public void ApplyLoadoutToDml(string gameExeDir)
        {
            var configPath = Path.Combine(gameExeDir, "config.toml");
            if (!File.Exists(configPath))
            {
                Global.logger?.WriteLine($"Unable to find {configPath}", LoggerType.Error);
                return;
            }

            var text = File.ReadAllText(configPath);
            if (!Toml.TryToModel(text, out TomlTable? config, out _))
            {
                config = new TomlTable();
                config["enabled"] = true;
                config["console"] = false;
                config["mods"] = "mods";
            }

            var priorityList = ModList.Where(m => m.enabled).Select(m => m.name).ToList();
            config["priority"] = priorityList.ToArray();

            File.WriteAllText(configPath, Toml.FromModel(config));
            Global.logger?.WriteLine($"Wrote priority list ({priorityList.Count} mods enabled) to {configPath}", LoggerType.Info);
        }

        public void DeleteMod(string modsFolder, string modName)
        {
            var dir = Path.Combine(modsFolder, modName);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
                var mod = ModList.FirstOrDefault(m => m.name == modName);
                if (mod != null) ModList.Remove(mod);
            }
        }

        /// <summary>
        /// Create a new empty mod folder with a stub mod.toml.
        /// </summary>
        public Mod CreateMod(string modsFolder, string name, string? description = null, string? author = null)
        {
            var dir = Path.Combine(modsFolder, name);
            Directory.CreateDirectory(dir);
            var table = new TomlTable
            {
                ["name"] = name,
                ["description"] = description ?? "",
                ["author"] = author ?? "",
                ["category"] = "Misc",
                ["priority"] = ModList.Count,
            };
            File.WriteAllText(Path.Combine(dir, "mod.toml"), Toml.FromModel(table));
            var mod = new Mod { name = name, enabled = true };
            ModList.Add(mod);
            return mod;
        }
    }
}
