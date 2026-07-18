# DivaModManager X (v1.3.1)

Native Avalonia UI port of TekkaGB's DivaModManager for Void Linux + Steam Proton.

## What's new in this update

**Fixed: Steam launch now actually loads mods.** DMM now reads and writes Steam's `localconfig.vdf` to automatically set `WINEDLLOVERRIDES="dinput8.dll=n,b"` in the game's launch options. Before launching, DMM verifies three things: (1) DML's `dinput8.dll` is in the game directory, (2) `config.toml` exists with the priority list written, (3) Steam's launch options contain the WINEDLLOVERRIDES. If any check fails, DMM tells you exactly what's wrong and offers to fix the Steam config automatically.

**Removed: Wine direct-launch option.** The game has Denuvo DRM which requires Steam's runtime to validate the license. The only supported launch mode is now `steam://rungameid/1761390`.

**Added: GameBanana mod browser.** Search, paginate, and install mods directly from GameBanana without leaving DMM. Uses the apiv4 endpoint with automatic fallback to the legacy Core API.

**Added: DivaModArchive (DMA) mod browser.** Browse, filter by type (Song/Cover/Module/UI/Plugin/Other), sort (Latest/Downloads/Likes), and install mods from divamodarchive.com.

**Added: Install from URL.** Paste any GameBanana or DMA mod URL (or a `divamodmanager://` 1-click install URL) and DMM downloads and installs it.

**Improved: Mod extraction.** Archives are now scanned for subfolders containing `config.toml` (the actual mod folders), matching upstream DMM behavior. A `mod.json` metadata file is written alongside each installed mod with author, description, preview image URL, etc.

## What's in this bundle

| Path | What |
|---|---|
| `DMM_Linux_Port_Companion_Guide.pdf` | 13-page guide: architecture, install, Proton config, troubleshooting |
| `DMM-Linux-Port/source/` | Full C# source (Avalonia 11 + net8.0) |
| `DMM-Linux-Port/bin-linux-x64-selfcontained/` | Prebuilt binary, 88 MB, no system deps (recommended) |
| `DMM-Linux-Port/source/packaging/void-linux/` | xbps-src template + .desktop entry + symlink-trick script |

## Quick start

```bash
cd DMM-Linux-Port/bin-linux-x64-selfcontained
./DivaModManager
```

1. Click **Setup** — DMM auto-detects the game via Steam and installs DivaModLoader.
2. The **Steam Launch Status** panel in the right sidebar shows whether WINEDLLOVERRIDES is configured. If it says "NOT configured", click **Configure Steam**.
3. **Restart Steam** (Steam caches localconfig.vdf — changes won't take effect until restart).
4. Click **GameBanana** or **DMA** to browse and install mods.
5. Click **Launch** — DMM verifies everything is in place, then launches via Steam.

## License

GPL-3.0-or-later (inherited from upstream TekkaGB/DivaModManager).
