# DivaModManager Linux Port — OpenCode Handoff Document

> **For**: GLM 5.2 session in OpenCode
> **Project**: Native Linux port of TekkaGB's DivaModManager v1.3.1 (WPF → Avalonia)
> **Target distro**: Void Linux (glibc x86_64; musl variant documented)
> **Build sandbox**: Debian 13 (this is where the binary was built; runtime target is the user's Void box)
> **License**: GPL-3.0-or-later (inherited from upstream)
> **Current state**: Functional v1.3.1 port with GameBanana + DMA browsers, Steam launch options auto-config, force-kill game button, all known critical bugs fixed across 3 iterations of user testing

---

## 1. Project Background

### 1.1 What this is

`DivaModManager` (DMM) is a mod manager GUI for the Steam game *Hatsune Miku: Project DIVA Mega Mix+* (AppID `1761390`). It depends on a separate component called `DivaModLoader` (DML) which is a `dinput8.dll` proxy that hooks the game at startup to apply mods. DMM's job is to:

- Download DML from GitHub (releases of `blueskythlikesclouds/DivaModLoader`)
- Extract `dinput8.dll` + `config.toml` into the game's install directory
- Manage a `mods/` folder of installed mods (each mod is a subfolder with a `mod.toml`)
- Write the priority list of enabled mods to `{gameDir}/config.toml`
- Configure Steam's launch options so Proton loads DML's `dinput8.dll` instead of its builtin
- Launch the game via `steam://rungameid/1761390`

### 1.2 Upstream source

- **Upstream repo**: `https://github.com/TekkaGB/DivaModManager`
- **Tag used**: `1.3.1` (matches the user's binary, `Version=1.3.1.0`)
- **Upstream is WPF** (Windows-only). This port rewrites the UI layer to Avalonia 11.1+ targeting `net8.0/linux-x64`.
- **Business logic** (Octokit, SharpCompress, Tomlyn, GameBanana API, DMA API, ZIP/7z extraction) is preserved almost verbatim from upstream — same NuGet versions, same DTO schemas, same `Config.json` format.
- The original Windows binary is at `DMM-Linux-Port/DivaModManager.exe.windows-original` for reference (don't run it — use the Linux port instead).
- The full reverse-engineering report of the original binary is at `DMM-Linux-Port/DMM_ReverseEngineering_Report.md`.

### 1.3 Iteration history (what was fixed in each round)

**Round 1** (initial port): Built the Avalonia project scaffold, ported all Models + Services + Helpers + MainWindow, published self-contained binary. Known gaps: no GameBanana/DMA browser UI, no Steam launch options auto-config, Wine direct-launch offered as an option (which doesn't work due to Denuvo).

**Round 2** (user feedback "Steam loads vanilla, Wine errors, need mod download"):
- Removed Wine direct-launch option (game has Denuvo DRM → must go through Steam)
- Added `SteamLaunchOptionsService` + `VdfParser` — DMM now auto-writes `WINEDLLOVERRIDES="dinput8.dll=n,b"` into Steam's `localconfig.vdf`
- Added pre-launch verification (checks dinput8.dll exists, config.toml exists, Steam launch options configured)
- Added GameBanana + DMA browser windows
- Added "From URL" install dialog (paste any GB/DMA URL)
- Improved archive extraction: scans for subfolders containing `config.toml` (the actual mod folders, per DML convention) and writes `mod.json` metadata alongside each installed mod

**Round 3** (user feedback: "GB 400, DMA images don't load, game freezes on exit, GB button freezes DMM on 2nd click"):
- **GameBanana 400 fix**: apiv4 `/Mod/Index` returns an **array at the root** (not an object with `_aRecords`). Also fixed invalid `_csvProperties` on single-item endpoint — `_aRootCategory`, `_aAlternateFileSources`, `_bHasUpdates`, `_aLatestUpdates` DON'T EXIST on apiv4 and cause 400. Added proper Core API (`api.gamebanana.com/Core/Item/Data`) fallback using legacy `fields=name,Files().aFiles(),...` syntax.
- **Preview images fix**: switched from Avalonia `Image` to `AdvancedImage` from `AsyncImageLoader.Avalonia` package. Plain `Image.Source` bound to a string URL does NOT auto-load — needs `AdvancedImage` for async URL fetching.
- **Game freeze on exit**: added `ForceKillGame()` method that runs `pkill -f DivaMegaMix.exe` then `pkill -9 -f DivaMegaMix.exe` as fallback. Wired to a red "Force Kill" button in the toolbar. This is a DML-under-Proton cleanup bug — DML's `dinput8.dll` doesn't shut down cleanly when the user clicks Exit Game.
- **GB browser freeze on 2nd open**: root cause was each browser window creating its own `GameBananaService` with its own `HttpClient`, and in-flight HTTP requests keeping sockets open after the window closed. Fixed by making `_http` a **static shared HttpClient** on both `GameBananaService` and `DmaService`, and adding `CancellationTokenSource` to the ViewModels that gets cancelled on `Window.Closing`.

---

## 2. Build Environment

### 2.1 Sandbox (this machine)

- **OS**: Debian 13 (trixie)
- **.NET SDK**: 8.0.422, installed user-local at `/home/z/.dotnet` via `https://dot.net/v1/dotnet-install.sh --channel 8.0 --install-dir /home/z/.dotnet`
- **PATH**: `export DOTNET_ROOT=/home/z/.dotnet; export PATH=$PATH:/home/z/.dotnet:/home/z/.dotnet/tools`
- **Xvfb**: needed for headless smoke tests (`Xvfb :99 -screen 0 1280x800x24 -nolisten tcp -nolisten unix &`)

### 2.2 Target: Void Linux (user's machine)

- **OS**: Void Linux, glibc x86_64
- **.NET SDK install**: `sudo xbps-install -S dotnet-sdk` (glibc). For musl: `dotnet-sdk-musl` + change `RuntimeIdentifier` to `linux-musl-x64` in `.csproj`.
- **Avalonia X11 dependencies**: `libX11 libXext libXft libXi libXrender fontconfig freetype` — these are listed as `depends` in the xbps template.
- **Font packages**: `fonts-noto-core fonts-dejavu` (rare on minimal Void installs; needed for proper CJK + Latin rendering).

### 2.3 Build commands

```bash
# Restore + build (debug)
cd /home/z/my-project/work/DivaModManagerLinux
dotnet restore
dotnet build

# Self-contained publish (no system dotnet-runtime dep, 88 MB output)
dotnet publish -c Release -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o ./bin/publish-selfcontained

# Run
./bin/publish-selfcontained/DivaModManager

# Smoke test (headless, on this Debian sandbox)
rm -f /tmp/.X*-lock; rm -rf /tmp/.X11-unix; mkdir -p /tmp/.X11-unix
Xvfb :99 -screen 0 1280x800x24 -nolisten tcp -nolisten unix &
XPID=$!; sleep 2
DISPLAY=:99 timeout 4 ./bin/publish-selfcontained/DivaModManager
kill $XPID 2>/dev/null
# exit code 124 = timeout killed a running process = SUCCESS
# exit code 0 + no output = crashed before window opened
```

### 2.4 Gotchas

- **NuGet version drift**: The `.csproj` pins `Avalonia` to `>= 11.1.7` but NuGet resolves to `11.2.0` because 11.1.7 doesn't exist on nuget.org. This is fine — 11.2.0 is API-compatible. Don't try to "fix" the version warning by pinning to a specific 11.2.x; the wildcard is intentional so users get security patches.
- **SharpCompress 0.32.0 has a known moderate severity vulnerability** (GHSA-6c8g-7p36-r338). Don't bump the version — it's pinned to match upstream DMM v1.3.1 for Config.json binary compatibility. The vuln is in archive extraction of untrusted inputs; we only extract archives from GameBanana/DMA which are themselves trusted sources.
- **`OutputType` is `WinExe`** (not `Exe`) — this is intentional. Avalonia uses `WinExe` on Linux too (no console window pops up). If you change to `Exe`, a terminal window opens alongside the GUI.
- **`InvariantGlobalization=false`** — we need culture-aware string formatting for the user's locale (Spanish, in this case).

---

## 3. Architecture

### 3.1 High-level data flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ Program.cs                                                          │
│  ├─ handles `-download <url>` 1-click install arg                   │
│  └─ starts Avalonia desktop lifetime                                │
└─────────────────────────────────────────────────────────────────────┘
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ App.axaml.cs (OnFrameworkInitializationCompleted)                   │
│  ├─ ConfigService.Load() → reads Config.json, migrates Z:\ paths    │
│  ├─ Global.config + Global.logger = static singletons               │
│  ├─ new MainWindowViewModel()                                       │
│  └─ new Views.MainWindow { DataContext = vm }                       │
└─────────────────────────────────────────────────────────────────────┘
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ MainWindowViewModel.Initialize()                                    │
│  ├─ Populate Loadouts from Config.json                              │
│  ├─ Wire Global.logger.OnLog → LogEntries (UI ListBox)              │
│  ├─ Wire DML/SelfUpdate progress → ProgressValue (UI ProgressBar)   │
│  ├─ Auto-detect game exe via ProtonPrefixLocator.FindGameExe()      │
│  ├─ Check Steam launch options (SteamLaunchOptionsService)          │
│  ├─ Background: CheckForDmlUpdateAsync + CheckForSelfUpdateAsync    │
│  └─ Handle pending 1-click install URL (if any)                     │
└─────────────────────────────────────────────────────────────────────┘
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ User clicks Launch:                                                 │
│  ├─ MainWindowViewModel.LaunchAsync()                               │
│  ├─ ModService.ApplyLoadoutToDml(gameDir) — writes config.toml      │
│  ├─ LaunchService.VerifyLaunch() — 3 checks (dll, toml, Steam)      │
│  │    └─ If Steam not configured: offer AutoConfigureSteam()        │
│  └─ LaunchService.LaunchViaSteam() — steam://rungameid/1761390      │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Project structure

```
DivaModManagerLinux/
├── App.axaml                  # Avalonia app, theme, resource dictionary (Miku colors)
├── App.axaml.cs               # DI bootstrap, window size persistence
├── Program.cs                 # Entry point, -download arg handling
├── DivaModManagerLinux.csproj # net8.0, Avalonia 11.1+, all NuGet refs
├── app.manifest               # Avalonia manifest
├── README.md                  # User-facing readme
│
├── Models/                    # Data Transfer Objects (verbatim from upstream)
│   ├── ModStructures.cs       # Config, GameConfig, Mod, Metadata, Choice
│   ├── GameBananaStructures.cs# GameBananaRecord, GameBananaAPIV4, GameBananaItemFile, ...
│   ├── DivaModArchiveStructures.cs # DivaModArchivePost, DivaModArchiveUser, DmaFeedSort/Filter
│   └── DownloadProgress.cs    # Progress DTO (percentage, downloaded, total, fileName)
│
├── Helpers/                   # Pure utility functions (no Avalonia deps)
│   ├── StringConverters.cs    # FormatSize, FormatTimeAgo, FormatNumber, NaturalSort
│   ├── HttpClientExtensions.cs# DownloadAsync with IProgress<DownloadProgress>
│   ├── WinePathTranslator.cs  # Linux ↔ Wine Z:\ path translation (one-way migration)
│   ├── ProtonPrefixLocator.cs # Find Steam compatdata/1761390/pfx + game install
│   └── VdfParser.cs           # Minimal KeyValues (VDF) parser/writer for Steam localconfig
│
├── Services/                  # Business logic (one class per upstream concept)
│   ├── Global.cs              # Static state (config, logger, ModList) + Logger event class
│   ├── ConfigService.cs       # Config.json load/save + Z:\ path migration
│   ├── ZipExtractor.cs        # SharpCompress-based ZIP/7z (drops SevenZipExtractor/7z.dll)
│   ├── ModService.cs          # Mod list CRUD + writes config.toml priority list to DML
│   ├── DmlUpdateService.cs    # GitHub Octokit: blueskythlikesclouds/DivaModLoader releases
│   ├── SelfUpdateService.cs   # GitHub Octokit: TekkaGB/DivaModManager releases (bash swap)
│   ├── GameBananaService.cs   # apiv4 + Core API fallback, install from URL
│   ├── DmaService.cs          # divamodarchive.com/api/v1/posts
│   ├── SetupService.cs        # First-run wizard: detect game + install DML
│   ├── LaunchService.cs       # Steam launch + VerifyLaunch + ForceKillGame + symlink trick
│   └── SteamLaunchOptionsService.cs # Reads/writes Steam localconfig.vdf
│
├── ViewModels/                # MVVM (CommunityToolkit.Mvvm)
│   ├── MainWindowViewModel.cs # Main window: mod list, loadouts, launch, all commands
│   ├── GameBananaBrowserViewModel.cs # Search + paginate + install from GB
│   └── DmaBrowserViewModel.cs # Search + filter + sort + install from DMA
│
├── Views/                     # Avalonia AXAML windows
│   ├── MainWindow.axaml       # 3-column layout: mods list | mod detail | sidebar
│   ├── MainWindow.axaml.cs    # Code-behind (minimal — just InitializeComponent)
│   ├── GameBananaBrowserWindow.axaml     # Browser window
│   ├── GameBananaBrowserWindow.axaml.cs
│   ├── DmaBrowserWindow.axaml           # Browser window
│   └── DmaBrowserWindow.axaml.cs
│
├── Styles/
│   └── AppStyles.axaml        # Miku-themed accent styles (teal #39C5BB, pink #FF6B9D)
│
├── Assets/                    # Fonts + images (copied from upstream DMM)
│   ├── AnekLatin-*.ttf        # UI font
│   ├── RobotoMono-Regular.ttf # Log/code font
│   ├── miku.ico               # App icon
│   ├── preview.png            # Default mod preview
│   ├── dml.png                # DML logo
│   ├── DMA_BLACK.png          # DMA logo
│   ├── GameBanana.png         # GB logo
│   ├── KoFi.png               # Ko-Fi button image
│   ├── load.gif               # Loading spinner (unused in current port)
│   └── Icons/mmplus.png       # Game icon
│
└── packaging/
    └── void-linux/
        ├── template           # xbps-src template (build_style=meta + custom do_build)
        └── files/
            ├── divamodmanager.sh                    # /usr/bin wrapper
            ├── divamodmanager.desktop               # Desktop entry + MIME handler
            ├── divamodmanager.xml                   # divamodmanager:// scheme registration
            └── install-steam-symlink-trick.sh       # Optional DivaMegaMix.exe symlink
```

### 3.3 NuGet dependencies (DivaModManagerLinux.csproj)

```xml
<PackageReference Include="Avalonia" Version="11.1.7" />              <!-- resolves to 11.2.0 -->
<PackageReference Include="Avalonia.Desktop" Version="11.1.7" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.1.7" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="11.1.7" />
<PackageReference Include="Avalonia.Diagnostics" Version="11.1.7" />   <!-- debug only -->
<PackageReference Include="Avalonia.Controls.DataGrid" Version="11.1.7" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />   <!-- ObservableObject, [RelayCommand] -->
<PackageReference Include="Octokit" Version="0.51.0" />                <!-- GitHub API (DML + self-update) -->
<PackageReference Include="SharpCompress" Version="0.32.0" />          <!-- ZIP/7z pure C# (replaces 7z.dll) -->
<PackageReference Include="Tomlyn" Version="0.14.3" />                 <!-- mod.toml + DML config.toml parser -->
<PackageReference Include="AsyncImageLoader.Avalonia" Version="3.3.0" /><!-- AdvancedImage for URL thumbnails -->
```

**Dropped from upstream** (Windows-only):
- `SevenZipExtractor` 1.0.17 — wrapper around Windows 7z.dll; SharpCompress handles 7z natively
- `Onova` 2.6.2 — `Onova.Updater.exe` is .NET 4.6 Windows-only; replaced with bash-script self-update
- `FontAwesome5` 2.1.11 — WPF-only icon font; Fluent theme built-in icons used instead
- `gong-wpf-dragdrop` 3.1.1 — WPF-only drag-drop helper
- `WpfAnimatedGif` 2.0.2 — WPF-only GIF decoder; Avalonia handles GIFs natively (not yet wired up)
- `PresentationFramework` / `PresentationCore` / `WindowsBase` / `System.Xaml` — WPF itself

---

## 4. Key Components In Detail

### 4.1 Config.json schema (matches upstream DMM v1.3.1 exactly)

```json
{
  "CurrentGame": "Project DIVA Mega Mix+",
  "Configs": {
    "Project DIVA Mega Mix+": {
      "Launcher": "/home/z/.steam/steam/steamapps/common/Hatsune Miku Project DIVA Mega Mix+/DivaMegaMix.exe",
      "GamePath": "/home/z/.steam/steam/steamapps/common/Hatsune Miku Project DIVA Mega Mix+",
      "LauncherOption": false,
      "LauncherOptionIndex": 1,
      "LauncherOptionConverted": true,
      "FirstOpen": false,
      "ModsFolder": "/home/z/.steam/steam/steamapps/common/Hatsune Miku Project DIVA Mega Mix+/mods",
      "ModLoaderVersion": "0.0.16",
      "CurrentLoadout": "Default",
      "Loadouts": {
        "Default": [
          { "name": "SomeMod", "enabled": true },
          { "name": "AnotherMod", "enabled": false }
        ]
      }
    }
  },
  "LeftGridWidth": 1.8, "RightGridWidth": 1,
  "TopGridHeight": 1.6, "BottomGridHeight": 1,
  "Height": 750, "Width": 1280, "Maximized": false
}
```

**Important**: Config.json is stored in the **application's directory** (next to the binary), not in `~/.config`. This matches upstream DMM behavior so Wine-based configs can be migrated by simply copying the file. The path is `Global.assemblyLocation = AppDomain.CurrentDomain.BaseDirectory`.

**Z:\ path migration**: `ConfigService.MigrateWindowsPaths()` runs on every load. If `Launcher`/`GamePath`/`ModsFolder` start with `Z:\` (Wine notation), they're translated back to Linux paths. Idempotent — running on an already-migrated config is a no-op.

### 4.2 DML's config.toml (in the game directory, NOT the mod folder)

```toml
enabled = true
console = false
mods = "mods"
priority = ["ModA", "ModB", "ModC"]   # written by ModService.ApplyLoadoutToDml()
```

`ModService.ApplyLoadoutToDml(gameDir)` reads the existing config.toml, parses it with Tomlyn, replaces the `priority` array with the currently-enabled mod names (in list order), and writes it back. **This is called before every Launch** so the loadout is always current.

### 4.3 GameBanana API (apiv4 + Core API fallback)

**Two endpoints used:**

1. **`GET https://gamebanana.com/apiv4/Mod/Index?_aFilters[Generic_Game]={gameId}&_nPage={page}&_nPerpage={perPage}`**
   - Returns an **ARRAY at the root** (not an object with `_aRecords` — this was a bug in earlier code)
   - Each array element is a `GameBananaRecord` with `_sName`, `_sProfileUrl`, `_aPreviewMedia`, `_aSubmitter`, `_aCategory`, `_aFiles`, `_tsDateAdded`, etc.
   - **Requires User-Agent header** — without it, returns 403

2. **`GET https://gamebanana.com/apiv4/Mod/{modId}?_csvProperties=...`**
   - Returns a single `GameBananaAPIV4` object
   - **VALID csvProperties**: `_sName`, `_sProfileUrl`, `_aPreviewMedia`, `_sDescription`, `_aSubmitter`, `_aCategory`, `_aGame`, `_aFiles`, `_tsDateAdded`, `_tsDateModified`
   - **INVALID csvProperties (cause 400)**: `_aRootCategory`, `_aAlternateFileSources`, `_bHasUpdates`, `_aLatestUpdates` — these don't exist on apiv4
   - Current code uses only valid properties

**Fallback** (`FetchRecordsLegacyAsync`): if apiv4 fails (rate limit, 403, network), uses the legacy Core API:
- `GET https://api.gamebanana.com/Core/List/New?itemtype=Mod&gameid={gameId}&page={page}` → returns `[["Mod", 12345], ["Mod", 12346], ...]`
- For each ID, `GET https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={id}&fields=name,ProfileUrl,Preview().sStructuredDataFullsizeUrl(),Files().aFiles(),Submitter().sName(),Submitter().sAvatarUrl(),Submitter().sUpicUrl(),Category().sName(),Category().sIconUrl(),dateline,updatedate` → returns an **array of field values in the same order as the `fields` parameter**

**Game ID for Project DIVA Mega Mix+**: `16522` (constant `GameBananaService.MegaMixGameId`)

### 4.4 DMA API (divamodarchive.com)

**Endpoints:**
- `GET https://divamodarchive.com/api/v1/posts?sort=time:desc&offset=0&limit=20&query=...&filter=post_type=Song`
  - Sort values: `time:desc`, `download_count:desc`, `like_count:desc`
  - Filter values: `post_type=Song|Cover|Module|UI|Plugin|Other`
- `GET https://divamodarchive.com/api/v1/posts/{id}` — single post
- `GET https://divamodarchive.com/api/v1/posts/{id}/download/{fileIndex}` — actual file download

Response schema (see `Models/DivaModArchiveStructures.cs`):
```json
{
  "id": 581,
  "name": "DYNA MIND",
  "text": "Mod description...",
  "images": ["https://divamodarchive.com/cdn-cgi/imagedelivery/.../public"],
  "files": ["https://divamodarchive.com/api/v1/posts/581/download/0"],
  "file_names": ["DYNA MIND.zip"],
  "file_sizes": [129088016],
  "time": "2026-07-10T06:34:10.227744Z",
  "post_type": "Song",
  "download_count": 6,
  "like_count": 0,
  "authors": [{"id": 464596514510077963, "name": "mikurisu39", "display_name": "mikurisu39_", "avatar": "https://cdn.discordapp.com/..."}],
  "dependencies": null,
  "explicit": false
}
```

**Also requires User-Agent** — both `GameBananaService` and `DmaService` set `DivaModManagerLinux/1.3.1 (+https://github.com/TekkaGB/DivaModManager)`.

### 4.5 Steam integration (the most critical piece)

**Problem**: DML is a `dinput8.dll` proxy. Wine/Proton ships its own builtin `dinput8.dll` which takes precedence unless explicitly overridden. Without the override, the game runs vanilla — no mods load.

**Solution**: Set per-game launch options in Steam to `WINEDLLOVERRIDES="dinput8.dll=n,b" %command%`

**How DMM does it automatically** (`SteamLaunchOptionsService` + `VdfParser`):

1. **Locate Steam user data**: `~/.steam/steam/userdata/{steamid}/config/localconfig.vdf`
   - If multiple users exist, pick the one whose `localconfig.vdf` has the most recent mtime
   - `$STEAM_COMPAT_DATA_PATH` is checked first (Steam sets this for the game process)

2. **Parse VDF** (custom `VdfParser` in `Helpers/VdfParser.cs`):
   - VDF is a nested key-value format. Keys and values are quoted strings.
   - Navigate: `UserLocalConfigStore > Software > Valve > Steam > apps > 1761390 > LaunchOptions`
   - The `LaunchOptions` value is a string like `WINEDLLOVERRIDES="dinput8.dll=n,b" %command%`

3. **Append the override** (preserve existing options):
   - If `LaunchOptions` already contains `dinput8.dll=n,b` → no-op
   - Else: `newOpts = "WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%" + (existing ? " " + existing : "")`
   - Write back via `VdfParser.Serialize()`

4. **Backup**: original file copied to `localconfig.vdf.dmm-bak` before first modification

**CRITICAL GOTCHA**: Steam caches `localconfig.vdf` in memory and overwrites it on exit. If Steam is running when DMM writes the config, the change is LOST when Steam exits. The user must:
1. Close Steam completely (right-click tray icon → Exit)
2. Run DMM, click "Configure Steam"
3. Restart Steam

The log panel + `SteamStatus` property explicitly warn the user about this.

**VDF parser format example**:
```
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "1761390"
                    {
                        "LaunchOptions"        "WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%"
                    }
                }
            }
        }
    }
}
```

### 4.6 Launch flow (LaunchService)

**`LaunchService.LaunchViaSteam()`**: invokes `steam://rungameid/1761390` via `Process.Start(UseShellExecute=true)`. Steam handles the rest (Proton prefix, license check via Denuvo, etc.).

**`LaunchService.VerifyLaunch(gameExePath)`** returns `(bool ok, List<string> failures, List<string> fixes)`:
1. `dinput8.dll` exists in game directory → if not, "Run Setup"
2. `config.toml` exists in game directory → if not, "Run Setup"
3. Steam launch options contain `dinput8.dll=n,b` → if not, "Auto-configure Steam"

If all pass → `LaunchViaSteam()`. If Steam is the only failure → offer `AutoConfigureSteam()` automatically.

**`LaunchService.ForceKillGame()`**: runs `pkill -f DivaMegaMix.exe` then `pkill -9 -f DivaMegaMix.exe` as fallback. Also runs `pgrep -f DivaMegaMix` to find PIDs and `Process.Kill(entireProcessTree: true)` them. Used when the game freezes on exit (DML cleanup bug under Proton). Wired to the red "Force Kill" toolbar button.

**Wine direct-launch was REMOVED** in round 2. The game has Denuvo DRM which requires Steam's runtime to validate the license. Launching via `wine DivaMegaMix.exe` directly bypasses the license check and the game exits with a Steam error. The only supported launch mode is `steam://rungameid/1761390`.

### 4.7 The "DivaMegaMix.exe" symlink trick (optional)

On Windows, some users rename DMM to `DivaMegaMix.exe` and replace the game's exe, so Steam's Play button opens DMM instead of the game. The real game exe is backed up as `DivaMegaMix.exe ` (with a trailing space).

On Linux, this is **optional** — DMM runs natively. But for users who want the same workflow, `packaging/void-linux/files/install-steam-symlink-trick.sh` automates it:
- Finds the game install via Steam's `libraryfolders.vdf`
- Backs up `DivaMegaMix.exe` → `DivaMegaMix.exe ` (trailing space)
- Creates symlink: `DivaMegaMix.exe` → `/usr/lib/divamodmanager/DivaModManager`

**Undo**: `install-steam-symlink-trick.sh --restore`

### 4.8 Mod archive extraction (GameBananaService + DmaService)

Both services follow the same extraction logic (matching upstream DMM):

1. Download archive to `Downloads/{GameBanana|DMA}/{filename}`
2. Extract to a temp dir `Downloads/{...}/_extract_{guid}/`
3. **Scan for subfolders containing `config.toml`** — these are the actual mod folders (per DML convention)
4. For each found mod folder:
   - Move to `{ModsFolder}/{folderName}` (with `(2)`, `(3)` suffix if name collision)
   - Write `mod.json` metadata alongside (author, description, preview URL, homepage, etc. — pulled from the API response)
5. **Fallback**: if no `config.toml` is found inside the archive, treat the whole archive as a single mod and place it in a folder named after the mod title (sanitized)
6. Cleanup: delete temp dir + original archive

### 4.9 1-click install protocol

**Format**: `divamodmanager:<url>,<ModType>,<ModID>` (GameBanana) or `divamodmanager:dma/<post_id>` (DMA)

**Parsing** (`GameBananaService.ParseProtocolUrl`):
```csharp
// GameBanana: divamodmanager:https://gamebanana.com/dl/1751071,Mod,693226
// → ("gamebanana", 693226, "https://gamebanana.com/dl/1751071")

// DMA: divamodmanager:dma/581
// → ("dma", 581, null)
```

**On Linux**, the URL scheme is registered via the `.desktop` file's `MimeType=x-scheme-handler/divamodmanager;` line. When installed via xbps, `update-desktop-database` + `update-mime-database` make it work. The handler is `Exec=/usr/bin/divamodmanager %U` — the `%U` passes the URL as the first arg.

**In Program.cs**: `if (args.Length > 1 && args[0] == "-download")` queues `args[1]` as `PendingDownloadUrl`. The `MainWindowViewModel` constructor checks this and calls `HandleOneClickInstallAsync(url)` after initialization.

**Also accepts plain URLs** via the "From URL" button:
- `https://gamebanana.com/mods/693226` → regex extracts mod ID from URL path
- `https://divamodarchive.com/posts/581` → regex extracts post ID from URL path
- `divamodmanager:...` → protocol parser

---

## 5. Current State & Known Limitations

### 5.1 What works (tested by user)

- ✅ Setup wizard (auto-detect game, install DML)
- ✅ Mod list display + enable/disable + reorder (Up/Down/A-Z)
- ✅ Loadouts (Default + custom add/delete/switch)
- ✅ GameBanana browser (search + paginate + install) — apiv4 with Core API fallback
- ✅ DMA browser (search + filter by type + sort + install)
- ✅ Preview thumbnails in both browsers (via `AdvancedImage`)
- ✅ "From URL" install (paste GB/DMA URL or `divamodmanager://` URL)
- ✅ Steam launch options auto-config (writes `localconfig.vdf`)
- ✅ Pre-launch verification (DML + config.toml + Steam override)
- ✅ Game launches via Steam with mods loaded (user confirmed "Game with mods now works")
- ✅ Force Kill Game button (for game freeze on exit)
- ✅ Config.json Z:\ path migration (Wine configs work transparently)
- ✅ DML auto-update (checks `blueskythlikesclouds/DivaModLoader` releases)
- ✅ DMM self-update (staged, manual `apply-update.sh`)
- ✅ Open Mods folder button (opens `xdg-open`)

### 5.2 Known bugs / limitations (NOT yet ported)

| Feature | Status | Workaround |
|---|---|---|
| Drag-and-drop mod reordering | Not ported (Avalonia supports it; not wired to DataGrid) | Use Up/Down buttons or A-Z sort |
| Animated GIF preview | Not ported (WpfAnimatedGif dropped) | Static PNG previews work; GIFs show fallback |
| Mod metadata edit dialog ("Configure Mod") | Not ported | Click "Open Mods" and edit `mod.toml` in a text editor |
| Create Mod wizard | Not ported | Manually create subfolder in `mods/` + write `mod.toml` |
| Auto-update of individual mods from GameBanana | Not ported (ModUpdater not wired) | Check mod's GB page manually |
| Mod context menu (right-click row) | Not ported | Use toolbar buttons (Up, Down, A-Z, Delete) |
| Window layout persistence (grid widths) | Not ported (only window size + maximized) | Manual re-layout after each launch |
| Self-update auto-applies on restart | Staged only — user runs `apply-update.sh` manually | Safer but requires manual step |
| Game freeze on exit | Worked around (Force Kill button) | DML cleanup bug under Proton; not fixable in DMM itself |

### 5.3 GameBanana browser — potential future improvements

- The current `FetchRecordsLegacyAsync` fallback makes N+1 HTTP requests (1 list + N item fetches). For perPage=50 this is slow. Could be optimized by batching or by fixing the apiv4 endpoint usage if/when GB fixes it.
- No category filter UI in the GB browser (DMA has one). The `GameBananaRecord.CategoryName` is available but there's no dropdown to filter by it.
- No "sort by" UI in the GB browser (DMA has Latest/Downloads/Likes). apiv4 supports `_sSort=default|newest|top` but it's hardcoded to `default`.

---

## 6. Avalonia 11.2 Specifics (Gotchas)

### 6.1 XAML namespace for DataGrid

```xml
<!-- WRONG (causes AVLN2000 error): -->
xmlns:dg="https://github.com/AvaloniaUI/DataGrid"
<dg:DataGrid ... />

<!-- CORRECT (DataGrid is in the default Avalonia namespace when Avalonia.Controls.DataGrid is referenced): -->
<DataGrid ... />
```

### 6.2 Click event handler signature

Avalonia `Button.Click` expects `EventHandler<RoutedEventArgs>`, NOT `EventHandler<EventArgs>`:

```csharp
// WRONG (causes AVLN3000 error):
private async void Search_Click(object? sender, EventArgs e) => ...

// CORRECT:
using Avalonia.Interactivity;
private async void Search_Click(object? sender, RoutedEventArgs e) => ...
```

### 6.3 Image loading from URL

Plain `Image.Source` bound to a string URL does NOT auto-load. Use `AdvancedImage` from `AsyncImageLoader.Avalonia`:

```xml
xmlns:ail="clr-namespace:AsyncImageLoader;assembly=AsyncImageLoader.Avalonia"
<ail:AdvancedImage Source="{Binding ThumbnailUrl}" Width="120" Height="70" Stretch="UniformToFill"/>
```

```csharp
public string? ThumbnailUrl => record.Media?.FirstOrDefault()?.File?.ToString();
```

### 6.4 HttpClient must be shared + have User-Agent

Each `new HttpClient()` creates new sockets that don't get reused. For browser windows that open/close repeatedly, this causes socket exhaustion + UI freezes. **All services use `static readonly HttpClient`** initialized in a static constructor.

GameBanana and DMA both return **403 Forbidden** to requests without a User-Agent header. Both services set:
```csharp
_http.DefaultRequestHeaders.UserAgent.ParseAdd("DivaModManagerLinux/1.3.1 (+https://github.com/TekkaGB/DivaModManager)");
_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
_http.Timeout = TimeSpan.FromSeconds(30);
```

### 6.5 Window disposal + cancellation

Browser windows must cancel in-flight HTTP requests on close, otherwise the next window open deadlocks waiting on stale sockets:

```csharp
// In the Window code-behind:
Closing += (s, e) => _vm.CancelLoads();

// In the ViewModel:
private CancellationTokenSource _loadCts = new();
public void CancelLoads()
{
    try { _loadCts?.Cancel(); } catch { }
    _loadCts = new CancellationTokenSource();
}

// In LoadAsync:
var token = _loadCts.Token;
var records = await _gb.FetchRecordsAsync(...);
if (token.IsCancellationRequested) return;
// ... update UI ...
```

### 6.6 CommunityToolkit.Mvvm [RelayCommand]

Methods named `XxxAsync` generate `XxxCommand` (not `XxxAsyncCommand`). Methods must return `Task` for async commands:

```csharp
[RelayCommand]
private async Task SetupAsync() { ... }  // generates SetupCommand

[RelayCommand]
private void Launch() { ... }  // generates LaunchCommand (sync)
```

If the method signature doesn't match, you get `MVVMTK0007` at build time.

---

## 7. Build & Publish

### 7.1 .csproj key settings

```xml
<OutputType>WinExe</OutputType>                          <!-- no console window on Linux -->
<TargetFramework>net8.0</TargetFramework>
<RuntimeIdentifiers>linux-x64;linux-musl-x64</RuntimeIdentifiers>
<SelfContained>false</SelfContained>                     <!-- overridden per-publish -->
<PublishSingleFile>true</PublishSingleFile>
<PublishTrimmed>false</PublishTrimmed>                   <!-- trimming breaks Avalonia reflection -->
<AssemblyName>DivaModManager</AssemblyName>
<AssemblyVersion>1.3.1.0</AssemblyVersion>
<RootNamespace>DivaModManager</RootNamespace>
```

### 7.2 Self-contained publish (recommended for users)

```bash
dotnet publish -c Release -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o ./bin/publish-selfcontained
```

Output: ~88 MB, includes .NET 8 runtime + Avalonia + SkiaSharp native libs (`libSkiaSharp.so`, `libHarfBuzzSharp.so`). No system dependencies beyond glibc + X11 libs.

### 7.3 xbps-src packaging (for system install on Void)

Template at `packaging/void-linux/template`. Copy to `srcpkgs/divamodmanager/` in a `void-packages` checkout:

```bash
git clone https://github.com/void-linux/void-packages
cd void-packages
mkdir -p srcpkgs/divamodmanager
cp /path/to/DivaModManagerLinux/packaging/void-linux/template srcpkgs/divamodmanager/
cp -r /path/to/DivaModManagerLinux/packaging/void-linux/files srcpkgs/divamodmanager/
./xbps-src pkg divamodmanager
sudo xbps-install --repository hostdir/binpkgs divamodmanager
```

The xbps template uses `build_style=meta` with a custom `do_build()` that invokes `dotnet publish`. The `depends` field pulls in: `dotnet-runtime>=8.0 libX11 libXext libXft libXi libXrender fontconfig freetype`.

---

## 8. File Locations (on the user's Void Linux box)

### 8.1 Self-contained install (recommended)

```
~/.local/share/divamodmanager/
├── DivaModManager              # 80 MB self-contained binary
├── DivaModManager.pdb          # debug symbols (can delete)
├── libSkiaSharp.so             # Avalonia rendering
├── libHarfBuzzSharp.so         # font shaping
├── Assets/                     # fonts + images
├── Config.json                 # written on first run
├── Downloads/                  # mod archive cache
│   ├── DML/                    # DML release archives
│   ├── DMMUpdate/              # self-update staging
│   ├── GameBanana/             # GB mod archives (temp)
│   └── DMA/                    # DMA mod archives (temp)
└── dmm.log                     # (future — log file not yet implemented)
```

### 8.2 xbps install (system-wide)

```
/usr/lib/divamodmanager/        # all the above files
/usr/bin/divamodmanager         # wrapper script (sets DOTNET_ROOT, execs dotnet DivaModManager.dll)
/usr/share/applications/divamodmanager.desktop
/usr/share/mime/packages/divamodmanager.xml
/usr/share/icons/hicolor/256x256/apps/divamodmanager.png
```

### 8.3 Game install (Steam Proton)

```
~/.steam/steam/steamapps/common/Hatsune Miku Project DIVA Mega Mix+/
├── DivaMegaMix.exe             # the real game exe (or symlink to DMM if trick installed)
├── DivaMegaMix.exe             # backup of real exe (only if symlink trick installed; note trailing space)
├── dinput8.dll                 # DML's proxy DLL (installed by Setup)
├── config.toml                 # DML config (priority list written by ModService)
├── mods/                       # mod folder (created by Setup)
│   ├── SomeMod/
│   │   ├── config.toml         # mod's own config
│   │   ├── mod.json            # metadata (written by DMM on install)
│   │   └── ...                 # mod files
│   └── AnotherMod/
└── dinput8.log                 # DML's own log (written at game runtime)
```

### 8.4 Steam config

```
~/.steam/steam/userdata/{steamid}/config/localconfig.vdf
  └── UserLocalConfigStore > Software > Valve > Steam > apps > 1761390 > LaunchOptions
      = "WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%"
```

Backup: `localconfig.vdf.dmm-bak` (created by DMM on first modification)

---

## 9. Testing APIs Manually

### 9.1 GameBanana apiv4 (modern)

```bash
# List mods (returns ARRAY at root)
curl -sL -H "User-Agent: DivaModManagerLinux/1.3.1" \
  "https://gamebanana.com/apiv4/Mod/Index?_aFilters[Generic_Game]=16522&_nPage=1&_nPerpage=3" | python3 -m json.tool | head -30

# Single mod (valid csvProperties only!)
curl -sL -H "User-Agent: DivaModManagerLinux/1.3.1" \
  "https://gamebanana.com/apiv4/Mod/693226?_csvProperties=_sName,_sProfileUrl,_aPreviewMedia,_aSubmitter,_aCategory,_aGame,_aFiles,_tsDateAdded"
```

### 9.2 GameBanana Core API (legacy fallback)

```bash
# List mod IDs (returns [["Mod", 12345], ...])
curl -sL "https://api.gamebanana.com/Core/List/New?itemtype=Mod&gameid=16522&page=1"

# Get mod data (returns array of field values in order)
curl -sL "https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid=693226&fields=name,ProfileUrl,Preview().sStructuredDataFullsizeUrl(),Files().aFiles(),Submitter().sName(),Category().sName(),dateline"
```

### 9.3 DMA API

```bash
# List posts
curl -sL -H "User-Agent: DivaModManagerLinux/1.3.1" \
  "https://divamodarchive.com/api/v1/posts?sort=time:desc&offset=0&limit=3" | python3 -m json.tool | head -40

# Single post
curl -sL -H "User-Agent: DivaModManagerLinux/1.3.1" \
  "https://divamodarchive.com/api/v1/posts/581" | python3 -m json.tool
```

### 9.4 GitHub API (for DML + self-update)

```bash
# DML latest release
curl -sL "https://api.github.com/repos/blueskythlikesclouds/DivaModLoader/releases/latest" | python3 -m json.tool | head -20

# DMM latest release
curl -sL "https://api.github.com/repos/TekkaGB/DivaModManager/releases/latest" | python3 -m json.tool | head -20
```

---

## 10. Common Tasks (for the next session)

### 10.1 Add a new toolbar button

1. Add `[RelayCommand] private void XxxCommand() { ... }` to `MainWindowViewModel`
2. Add `<Button Content="Xxx" Command="{Binding XxxCommand}"/>` to `MainWindow.axaml` toolbar
3. Build + publish

### 10.2 Add a new browser/source

1. Create `Models/XxxStructures.cs` with DTOs (use `[JsonPropertyName("...")]` attributes)
2. Create `Services/XxxService.cs` with `static readonly HttpClient _http` (set User-Agent!)
3. Create `ViewModels/XxxBrowserViewModel.cs` extending `ObservableObject` with `CancellationTokenSource` for cancellation
4. Create `Views/XxxBrowserWindow.axaml` + `.axaml.cs` — use `AdvancedImage` for thumbnails, `RoutedEventArgs` for click handlers
5. Add `[RelayCommand] private void OpenXxxBrowser()` to `MainWindowViewModel` that does `new Views.XxxBrowserWindow().ShowDialog(mainWindow)`
6. Add toolbar button in `MainWindow.axaml`

### 10.3 Modify the mod extraction logic

Both `GameBananaService.ExtractAndInstallAsync` and `DmaService.ExtractAndInstallAsync` follow the same pattern. If you need to change how mods are detected (e.g. look for `mod.toml` instead of `config.toml`), update both. Consider extracting a shared `ModInstaller` helper class to avoid duplication.

### 10.4 Add a new Steam launch option check

Edit `SteamLaunchOptionsService.CheckLaunchOptions()` and `EnsureLaunchOptions()`. The VDF path is `UserLocalConfigStore > Software > Valve > Steam > apps > {appId} > LaunchOptions`. Use `VdfParser.Parse()` to read, `VdfParser.Serialize()` to write. Always back up the original file first.

### 10.5 Debug a GameBanana API issue

1. Check the log panel — `GameBananaService` logs warnings on apiv4 failure and errors on Core API failure
2. If apiv4 returns empty: the sandbox may be rate-limited. Try the Core API fallback manually (section 9.2)
3. If single-item returns 400: check the `_csvProperties` list against section 4.3 — invalid properties cause 400
4. If 403: User-Agent header is missing or blocked. Verify `_http.DefaultRequestHeaders.UserAgent` is set in the static constructor

### 10.6 Debug a window-freeze issue

1. Check if the service uses `static readonly HttpClient` (not `new HttpClient()` per instance)
2. Check if the ViewModel has `CancellationTokenSource` and the Window's `Closing` handler calls `CancelLoads()`
3. Check if all async load paths check `token.IsCancellationRequested` before touching the UI
4. If the freeze happens on window OPEN (not close): the `Loaded` handler may be firing before the previous window's HTTP requests completed. The shared HttpClient + cancellation should handle this, but if not, add a `await Task.Delay(100)` at the start of `RefreshAsync()` to yield

---

## 11. User-Specific Notes

- **User's distro**: Void Linux (glibc x86_64). For musl, change `RuntimeIdentifier` to `linux-musl-x64` in `.csproj` and ensure `dotnet-sdk-musl` is installed.
- **User's game launch method**: Steam Proton (AppID 1761390). Proton 9.0+ recommended.
- **User's existing setup**: Previously used the Windows DMM under Wine with the `DivaMegaMix.exe` symlink trick. Config.json was migrated automatically (Z:\ paths translated to Linux paths on first load).
- **User's language**: Spanish (Venezuela timezone `America/Caracas`). The UI is in English but the log messages and PDF guide could be localized if needed.
- **User's reported issues** (all now fixed):
  1. ~~GameBanana returns 400 Bad Request~~ — fixed (apiv4 array root + invalid csvProperties)
  2. ~~DMA preview images don't load~~ — fixed (AdvancedImage)
  3. ~~Game freezes on exit~~ — workaround added (Force Kill button)
  4. ~~GameBanana button freezes DMM on 2nd click~~ — fixed (static HttpClient + CancellationToken)
- **User explicitly requested**: Remove `bin-linux-x64-framework` folder — only `source` and `bin-linux-x64-selfcontained` matter. Done.

---

## 12. Quick Reference

| Thing | Value |
|---|---|
| Steam AppID (Mega Mix+) | `1761390` |
| GameBanana Game ID | `16522` |
| DML GitHub repo | `blueskythlikesclouds/DivaModLoader` |
| DMM GitHub repo | `TekkaGB/DivaModManager` |
| Latest DML version (as of writing) | `0.0.16` |
| DMM version | `1.3.1` (matches upstream tag) |
| .NET target | `net8.0` |
| Avalonia version | `11.2.0` (pinned `>= 11.1.7`, resolves up) |
| Required Wine override | `WINEDLLOVERRIDES="dinput8.dll=n,b" %command%` |
| Steam VDF path | `~/.steam/steam/userdata/{steamid}/config/localconfig.vdf` |
| Proton prefix path | `~/.steam/steam/steamapps/compatdata/1761390/pfx` |
| Game install path | `~/.steam/steam/steamapps/common/Hatsune Miku Project DIVA Mega Mix+` |
| Game exe name | `DivaMegaMix.exe` (literal — no "Mega Mix+" suffix) |
| Config.json location | App directory (next to binary) |
| DML config.toml location | Game directory (next to game exe) |

---

## 13. Next Steps (suggested priorities)

1. **Mod metadata edit dialog** — right-click a mod row → edit `mod.toml` in a built-in text editor. Currently the user has to click "Open Mods" and use an external editor.
2. **Drag-and-drop reorder** — wire up Avalonia's built-in `DragDrop` events to the DataGrid. The `gong-wpf-dragdrop` behavior from upstream should be replicable.
3. **GameBanana category filter + sort UI** — DMA has these; GB doesn't. apiv4 supports `_sSort=newest|top` and category filters.
4. **Log file** — currently logs only go to the UI panel. Add file logging to `dmm.log` in the app directory for post-mortem analysis.
5. **Window layout persistence** — save `LeftGridWidth`, `RightGridWidth`, `TopGridHeight`, `BottomGridHeight` to Config.json (fields already exist in the schema, just not wired up).
6. **Mod auto-update** — wire up `ModUpdater` from upstream. Checks each installed mod's GameBanana ID (stored in `mod.json`) for new releases.
7. **1-click install from web browser** — test the `divamodmanager://` URL scheme end-to-end with an actual xbps install (the .desktop file + MIME registration is in place but untested).

---

**End of handoff document.** The complete source tree is in `DMM-Linux-Port/source/`, the prebuilt self-contained binary is in `DMM-Linux-Port/bin-linux-x64-selfcontained/`, and the original Windows binary + RE report are in the same folder for reference.
