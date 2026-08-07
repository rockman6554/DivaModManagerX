# Changelog — DivaModManager X

All notable changes to this project are documented in this file.

The format is loosely based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
dating follows the commit/merge timestamps in the repository.

---

## Project context — for future GLM sessions

This block is intentionally preserved. Read it before touching the codebase.

### What this is

**DivaModManager X** (DMM) is a mod manager GUI for the Steam game
*Hatsune Miku: Project DIVA Mega Mix+* (Steam **AppID 1761390**, launched via
`steam://rungameid/1761390` under **Proton**). It targets **Void Linux** (glibc
x86_64; a musl variant is documented in the Void packaging template).

DMM is an **orchestrator**, not a runtime injector. It depends on
**DivaModLoader (DML)** — a `dinput8.dll` proxy that hooks the game at startup
to apply mods. DMM's job is to:

- download DML from GitHub releases of `blueskythlikesclouds/DivaModLoader`;
- extract `dinput8.dll` + `config.toml` into the game directory;
- manage a `mods/` folder of installed mods (each mod is a subfolder with a
  `mod.toml` parsed with **Tomlyn**);
- write the priority list of enabled mods to `{gameDir}/config.toml`;
- configure Steam's launch options so Proton loads DML's `dinput8.dll`
  instead of its builtin (`WINEDLLOVERRIDES="dinput8.dll=n,b"`, written into
  Steam's `localconfig.vdf` via the custom `VdfParser`);
- launch the game via `steam://rungameid/1761390` (Denuvo DRM requires Steam's
  runtime — Wine direct-launch was removed in 2022 round 2 and is not coming
  back).

### Stack

- **Avalonia 11.1+** (wildcard pin in `.csproj` is intentional — NuGet will
  resolve to 11.2.0; don't "fix" the version warning by pinning). UI is
  AXAML, themed dark with a Miku-teal accent palette in `App.axaml`.
- **.NET 8.0** (`net8.0`), `OutputType=WinExe` (intentional on Linux —
  avoids a terminal window popping alongside the GUI; do not change to
  `Exe`).
- **CommunityToolkit.Mvvm 8.3.2** — source generators
  (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyCanExecuteChangedFor]`).
  Architecture is MVVM: business logic in `Services/`, thin orchestration in
  `ViewModels/`, no UI types leak into VMs.
- **Octokit 0.51.0** (GitHub API for DML and DMM self-update),
  **SharpCompress 0.32.0** (ZIP/TAR/7z — **pinned, do not bump**: matches
  upstream DMM v1.3.1 for `Config.json` binary compatibility; the known
  GHSA-6c8g-7p36-r338 vuln only affects untrusted-archive extraction and we
  only extract from GameBanana/DMA),
  **Tomlyn 0.14.3** (mod.toml + config.toml),
  **AsyncImageLoader.Avalonia 3.3.0** (`AdvancedImage` — plain
  `Avalonia.Image.Source` bound to a URL does *not* async-load; this was a
  round-3 bug and `AdvancedImage` is the fix).
- `InvariantGlobalization=false` — locale-aware string formatting is required
  (maintainer's locale is Spanish).

### Architecture notes that surprise people

- **`Global` static state is intentional.** It mirrors upstream DMM's design
  (static `config`/`logger`/`ModList`). Don't refactor into DI without
  explicitly coordinating — it's a deliberate upstream-fidelity choice.
- **Business logic is preserved near-verbatim from upstream TekkaGB/WPF** —
  same NuGet versions, same DTO schemas, same `Config.json` format. Only the
  UI layer was rewritten (WPF → Avalonia) plus the components the RE report
  flagged as Linux-incompatible (`SevenZipExtractor`/`7z.dll` → SharpCompress
  + system `unrar`/`7z` fallback; `Onova.Updater.exe` → custom
  `SelfUpdateService` emitting a bash `apply-update.sh`; `OpenFileDialog` →
  Avalonia `StorageProvider`; `Registry` probes → `ProtonPrefixLocator`).
- Two browser windows (`GameBananaBrowserWindow` + `DmaBrowserWindow`) share
  ~85% of their AXAML/code-behind. This is a known duplication, not a bug —
  refactor into a shared `BrowserViewModel<T>` is a candidate improvement,
  not urgent.

### Why Avalonia (the one-paragraph version)

`DMM_ReverseEngineering_Report.md` (root, Spanish, 375 lines) is the forensic
analysis of the original Windows binary that preceded this repo. Verdict:
the entire UI was WPF (`PresentationFramework`/`PresentationCore`/
`WindowsBase`/`System.Xaml`), which has no Linux implementation and never
will — Microsoft has no plans to port it. The business logic (Octokit,
SharpCompress, Tomlyn, `System.Net.Http`, `System.IO`,
`System.Diagnostics.Process`) is portable C#. Strategia A from the RE
report's §7 was chosen: rewrite the UI layer to Avalonia, preserve business
logic verbatim. Read the report if you need P/Invoke forensic context or the
upstream WPF class map.

### Build / reproducibility — known mismatches

- **Self-contained mismatch originates at the initial commit** and has never
  been resolved:
  - `source/DivaModManagerLinux.csproj` sets `SelfContained=false`
    (framework-dependent default).
  - `compile.txt` (root) publishes with `--self-contained true
    -p:PublishSingleFile=true -p:PublishTrimmed=false`.
  - It works because the `compile.txt` switch overrides at publish time, but
    the csproj is misleading. The README's "88 MB no system deps prebuilt"
    matches `compile.txt`, not the csproj. If you see the build behaving
    inconsistently, this is the first place to look.
- `PublishReadyToRun` is unset; `PublishTrimmed=false` is explicit because
  Avalonia + reflection-heavy SharpCompress/Tomlyn do not trim cleanly.
- `RuntimeIdentifiers=linux-x64;linux-musl-x64` (musl variant requires
  `dotnet-sdk-musl` on Void and `RuntimeIdentifier=linux-musl-x64`).
- **The 80MB `bin-linux-x64-selfcontained/DivaModManager` binary is committed
  to git intentionally.** It is the current test target — the maintainer and
  collaborator run this binary in-tree. It will be untracked at packaging
  time (AppImage / tarball release), *not* before. Do not "clean it up" in a
  QA-cycle commit.

### AppImage status (read this before proposing packaging changes)

**AppImage is a *future* goal, not the current sprint.** It will be proposed
*only* when the normal binary is bug-free and no further features are
pending. The current cycle is: friend-PRs → first-hand QA → GLM-assisted
improvements → repeat. Once the maintainer declares the binary ready, the
packaging work lands. Until then:

- Do not wire up `linuxdeploy`/`AppRun`/`appimagetool`/CI workflows.
- Do not bundle `unrar`/`7z`/`wl-clipboard` into an AppDir.
- Do not rewrite `ZipExtractor.ResolveToolPath` to add `$APPDIR`-relative
  fallbacks (the `/usr/bin /usr/sbin /usr/local/bin /bin` hardcoding is
  correct for the current Void-binary delivery model).
- Do not add `rsync` to the xbps `depends` list for the self-update flow.

The known AppImage blockers are cataloged in §"Future blockers" below for
reference, *not* for action.

### Reference documents

| Path | Content |
|---|---|
| `DMM_ReverseEngineering_Report.md` (root, Spanish) | Forensic analysis of the upstream Windows binary; the pre-git GLM RE session deliverable. |
| `source/README.md` | User-facing v1.3.1 release notes. |
| `source/DivaModManagerLinux.csproj` | Project file, NuGet refs, runtime identifiers. |
| `compile.txt` (root) | The publish command actually used to build the committed binary. |
| `source/packaging/void-linux/template` | xbps-src template (placeholders for `maintainer`/`checksum` are known and intentional-for-now). |
| `git show 983a8bb:DMM_Linux_Port_OpenCode_Handoff.md` | 838-line English handoff doc committed at the initial port, deleted at `207c117`. Recover via this git command. Contains the Round 1/2/3 iteration history that is summarized below. |

---

## [Unreleased]

Placeholder. Friend-PRs and GLM-assisted improvements land here during the
QA cycle. Move items under the appropriate subsection and stamp a version
header when cutting a release.

### Added

- _(nothing yet)_

### Changed

- _(nothing yet)_

### Fixed

- _(nothing yet)_

### Removed

- _(nothing yet)_

---

## [1.3.1] — 2026-08-02 — PR #2: Fix search/install + modern UI redesign with feedback dialogs

**Contributor:** CrowRei (CrowRei34) — commit `a941144`, merged by
rockman6554 at `e1ba1f1`. Single-commit PR, 23 files (4 new), +1291/−439
lines. Did not touch `.csproj` or packaging.

### Added

- **`Helpers/DialogHelper.cs`** (241 lines) — modal dialogs built from raw
  Avalonia controls (Avalonia 11 has no `ContentDialog`). Five public
  methods: `ShowConfirmAsync`, `ShowConfirmDestructiveAsync`,
  `ShowErrorAsync`, `ShowInfoAsync`, `ShowInputAsync` (TextBox with
  Enter/Esc).
- **`Helpers/MainWindowProvider.cs`** (19 lines) — resolves the active
  `MainWindow` from `IClassicDesktopStyleApplicationLifetime` so ViewModels
  can hand the owner window to `DialogHelper` without holding a direct UI
  reference.
- **`ViewModels/Converters.cs`** (26 lines) —
  `CountToVisibilityConverter` (`count == 0 → true`), with an `Instance`
  singleton. Used by the empty-state overlays in `MainWindow.axaml`.
- **RAR5 archive support** in `ZipExtractor` — SharpCompress can't read
  RAR5. Adds `TryExtractWithSystemToolAsync` (prefers `unrar x -y -o+`,
  falls back to `7z x -y -o`). General fallback: any SharpCompress failure
  retries via `7z` (`forceAny: true`). `ResolveToolPath` scans `PATH` then
  `/usr/bin /usr/sbin /usr/local/bin /bin`. Uses `proc.WaitForExitAsync(ct)`
  (cancellation-aware).
- **`Mod` model implements `INotifyPropertyChanged`** — `name`/`enabled`
  switched from auto-props to backing fields firing `PropertyChanged`. This
  enables instant two-way checkbox-toggle persistence without a separate
  `ToggleMod` command. Lowercase property names preserved (they serialize
  to `Config.json`).

### Changed

- **Unified Miku teal palette** centralized in `App.axaml`: retints
  Fluent's `SystemAccentColor`/`Dark1-3`/`Light1-3` to `#39C5BB` family;
  introduces named `SurfaceBg`/`SurfaceBgAlt`/`SurfaceBgElevated`/
  `TextPrimary`/`TextSecondary`/`TextMuted` and semantic
  `Success`/`Warning`/`Error` brushes. `LogInfo`/`LogWarning`/`LogError`
  kept as back-compat aliases.
- **`Styles/AppStyles.axaml`** expanded 59 → ~178 lines: reusable
  `TextBlock.header`/`.muted`; `Border.panel`/`.card`/`.vsep`;
  `Panel.surface`; full Button state coverage for `.primary`/`.danger`/
  `.subtle`/`.icon` (`:pointerover`/`:pressed`/`:disabled` template
  overrides); `Border.card:pointerover` accent border; `ListBox.cards` with
  transparent `ListBoxItem` so the card carries hover/selection;
  `TextBox.CornerRadius=6`; `ProgressBar` Miku fill;
  `GridSplitter:pointerover` accent stripe; `BrushTransition` 0.12 s
  animations.
- **`MainWindow.axaml` keyboard shortcuts** (`<Window.KeyBindings>`): `F5`
  Refresh, `Ctrl+L` Launch, `Ctrl+G` GameBanana, `Ctrl+D` DMA, `Delete`
  DeleteMod, `Alt+↑`/`Alt+↓` MoveUp/MoveDown.
- **Main window UI**: toolbar uses `subtle` class + `Border.vsep`
  separators; `DataGridCheckBoxColumn` → `DataGridTemplateColumn` with a
  `CheckBox IsChecked="{Binding enabled, Mode=TwoWay}"` (comment notes
  `DataGridCheckBoxColumn` is neither single-click-toggle-able nor
  clickable under `IsReadOnly`); the toggle button in the Mod Info panel →
  `ToggleSwitch`; empty-state overlay bound via
  `CountToVisibilityConverter.Instance` on `ModList.Count`; standalone
  progress `Border` row folded into the log header; log row gets a
  `GridSplitter` resize handle and `Copy`/`Clear` icon buttons;
  `MinWidth 900 → 980`.
- **Both browser windows**: hardcoded `#131313`/`#1F1F1F`/`#0F0F0F`/`#888`
  swapped for `{DynamicResource SurfaceBg}` family; rowdefs reorganized to
  `Auto,Auto,*,Auto,Auto`; card templates via `Border.card` +
  `ListBox.cards`; new `<Border>` overlays for empty state (emoji "🔍" +
  `EmptyMessage`/`EmptyHint`) and `IsLoading` indeterminate `ProgressBar`;
  new install-status bar
  `IsVisible="{Binding InstallStatus, Converter={x:Static
  StringConverters.IsNotNullOrEmpty}}"`; `MinWidth 800 → 820`.
- **`ModService` toggle persistence** — `MainWindowViewModel` ctor
  subscribes `ModList.CollectionChanged` (single-subscribe via `-= / +=`)
  which routes `Mod.enabled` → `OnModPropertyChanged` → `PersistModState()`
  (calls `Global.UpdateConfig()` + `_mods.ApplyLoadoutToDml(gameDir)` only
  if `config.toml` exists). This replaces the old `ToggleMod` command.
- **`MainWindowViewModel.LogEntry.Color`** retinted to `#4ADE80` /
  `#FBBF24` / `#F87171`.
- **`DeleteMod`** → `async Task DeleteModAsync()` with a
  `DialogHelper.ShowConfirmDestructiveAsync` confirmation before deleting.
- **`AddLoadout`** → `async Task AddLoadoutAsync()` via
  `DialogHelper.ShowInputAsync`.
- **`DeleteLoadout`** → `async Task DeleteLoadoutAsync()` with destructive
  confirmation.
- **`LaunchAsync` / `ConfigureSteamAsync`** now surface
  `DialogHelper.ShowInfoAsync`/`ShowErrorAsync` after their clipboard
  actions.
- **`InstallFromUrlAsync`** — hand-rolled inline dialog (`new Window` +
  `installBtn.Click += …`) replaced with `DialogHelper.ShowInputAsync`.
- **`SortedAlphabetically`** now preserves `SelectedMod` across the
  clear/re-add.
- **`MoveUp`/`MoveDown`/`DeleteMod`** get
  `[RelayCommand(CanExecute = nameof(HasSelectedMod))]`; `_selectedMod`
  decorated `[NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]` etc., so
  the commands disable when nothing is selected (was always-enabled silent
  no-op).
- Browser VMs: install/empty-state observables (`_isInstalling`,
  `_installStatus`, `_installStatusColor`, `_showEmpty`, `_emptyMessage`,
  `_emptyHint`); `CanInstall => !IsInstalling`; `CanGoPrev/CanGoNext` now
  also `&& !IsLoading`. `OnIsInstallingChanged`/`OnIsLoadingChanged`
  partials raise the dependent `OnPropertyChanged`.
- Browser code-behind: `OnKeyDown` Esc closes, `Ctrl+F` focuses SearchBox;
  `Install_Click` resolves the row post from
  `(sender as Button)?.DataContext as DmaPostViewModel` (DMA) /
  `GameBananaRecordViewModel` (GB); `Loaded` focuses `SearchBox`.
- `MainWindow.axaml.cs`: `HookLogAutoScroll()` subscribes
  `LogEntries.CollectionChanged` and posts
  `LogList.ScrollIntoView(LogList.ItemCount - 1)` at
  `DispatcherPriority.Background` on `Add`.

### Fixed

- **`Install_Click` silent no-op** — the install button was reading
  `_vm.SelectedPost`, which was null when the user clicked the button on
  the row itself (SelectionChanged hadn't fired for the click row). Now
  resolves from `(sender as Button)?.DataContext as <RowViewModel>`.
- **`Directory.Move` phantom-success** in both `GameBananaService` and
  `DmaService` install tails — the `Directory.Move(tempDir, dest)` call was
  inside a try/catch that swallowed the exception and returned `true`
  anyway. Now logs `"Failed to move mod folder to {dest}: {ex.Message}"` and
  **returns false**. The multi-folder loop tracks `var anyMoved = false`
  and `return anyMoved;` — phantom-success bug eliminated.
- **SharpCompress failure path** — `ZipExtractor` no longer swallows the
  error; throws `IOException` with a consolidated message including
  "For RAR5 archives, install 'unrar' or '7z' on your system."
- **Command-can-execute** — `MoveUp`/`MoveDown`/`DeleteMod` were
  always-enabled and silently no-op'd on no selection (see Changed).

### Removed

- **`ToggleMod` command** — replaced by two-way `enabled` binding +
  `INotifyPropertyChanged` on the `Mod` model.
- **Hand-rolled inline `InstallFromUrlAsync` dialog** — replaced by
  `DialogHelper.ShowInputAsync`.
- **Committed test artifacts** `bin-linux-x64-selfcontained/Config.json.orig-damper`
  and `bin-linux-x64-selfcontained/DivaModManager.orig-backup` — deleted.
  `.gitignore` updated to ignore `Config.json`, `*.orig-*`, `*-bak`,
  `Downloads/`, `source/bin/`, `source/obj/`, `*.user`.

---

## [1.3.1] — 2026-07-22 — PR #1: Browser fix

**Contributor:** CrowRei (CrowRei34) — commit `75c3486`, merged by
rockman6554 at `cedcc06`. Single-commit PR (commit subject had a stray `}`
typo: "Browser fix}"), 13 files (1 new), +572/−356 lines. Did not touch
`.csproj` or packaging.

### Added

- **`Helpers/ClipboardHelper.cs`** (112 lines) — cross-frontend clipboard
  helper. `public static async Task<bool> CopyAsync(string text)` tries
  Avalonia's `IClipboard.SetTextAsync` first (walks
  `IClassicDesktopStyleApplicationLifetime.MainWindow.Clipboard`), then
  falls back to shelling out to `wl-copy` / `xclip -selection clipboard` /
  `xsel --clipboard --input` via `TryRunClipboardTool` with stdin piping
  and `proc.WaitForExit(2000)`. Exposes
  `public static bool IsWaylandSession()` checking `WAYLAND_DISPLAY` /
  `XDG_SESSION_TYPE`.
- **15-minute LRU feed cache** on both `GameBananaService` and
  `DmaService` — `private static readonly Dictionary<…> _feedCache` with
  `_cacheLock`, 15-entry cap ("LRU-ish" — evicts oldest by `TimeFetched`).
  Cache-miss fetch happens outside the lock.
- **DMA count-endpoint pagination** — new
  `private async Task<int> FetchTotalCountAsync(...)` hits
  `/api/v1/posts/count` (plain-text number) with `double.TryParse`. New
  `DmaFeedResult { Posts, TotalRecords, TotalPages }` container. New
  `private static string BuildFeedUrl(...)` URL-encodes + trims.
- **`FeedResult`** container on `GameBananaService` mirroring the DMA shape
  (`Records`, `TotalRecords`, `TotalPages`).

### Changed

- **GameBanana service rewritten** (~298 lines churned, the heart of this
  PR): deletes the entire N+1 fan-out strategy — removed
  `FetchRecordViaApiv6Async` (per-mod), `FetchRecordsLegacyAsync`, and
  `FetchRecordViaCoreApiAsync` (which walked `/Core/List/New` then
  `/Core/Item/Data` per mod — the Cloudflare 429 trigger). Replaced with a
  single HTTP GET to `apiv6/Mod/ByGame?_aGameRowIds[]=16522&…` (browse) or
  `apiv6/Mod/ByName?_sName=*{query}*&_idGameRow=16522&…` (search).
  Pagination total read from response header `X-GbApi-Metadata_nRecordCount`
  via `resp.Headers.TryGetValues(...)`. `FetchItemAsync` endpoint switched
  apiv4 → `apiv6/Mod/{id}`. File ID `16522` hardcoded as
  `MegaMixGameId` (magic constant carried over from upstream).
- **Steam flow rethink** — `MainWindowViewModel.LaunchAsync`'s failure
  branch no longer calls `_launch.AutoConfigureSteam()` (which wrote
  `localconfig.vdf` and was silently lost when Steam was running).
  Instead copies `"{SteamLaunchOptionsService.RequiredWineOverride}
  %command%"` to the clipboard via the new `ClipboardHelper.CopyAsync` and
  updates `SteamStatus = "Copied — paste in Steam Launch Options"`.
- **`ConfigureSteam`** (sync `void`) → `private async Task
  ConfigureSteamAsync()` decorated `[RelayCommand]`, body now async
  clipboard copy with Wayland/X11 logging.
- **`MainWindow.axaml`** — the "Configure Steam" button relabeled "Copy
  Steam Launch Option" with an updated `ToolTip.Tip` describing the
  clipboard flow.
- Browser VMs: `CanGoNext` now `_page < _totalPages` (was
  `_page`/`_perPage` count-based — wrong on partial last pages). Added
  `TotalPages` property with `OnPropertyChanged(nameof(CanGoNext))`.
  `LoadAsync` consumes the new feed-result containers and sets
  `ResultCount = $"{feed.TotalRecords} mods total — page {_page} of
  {TotalPages}"`.

### Fixed

- **GameBanana Cloudflare 429** — the Core API fan-out issued hundreds of
  per-mod HTTP requests per page; Cloudflare rate-limited the entire
  upstream IP. Replaced by single-request apiv6 (see Changed).
- **DMA `dependencies` field `JsonException`** — the model declared
  `List<int>?` but DMA returns nested post objects. Changed to
  `List<JsonElement>?` with `using System.Text.Json`. Comment notes "we
  don't use this field" — pragmatic fix, not a model-completeness win.
- **Pagination math** — `CanGoNext` was comparing `Posts.Count >= _perPage`
  which disabled Next on partial last pages. Now compares against
  `_totalPages` from the count endpoint.

---

## [Pre-release] Reverse Engineering

**When:** Before the 2026-07-10 initial git commit.
**Contributor:** GLM 5.2 session (OpenCode handoff).
**Deliverable:** `DMM_ReverseEngineering_Report.md` (Spanish, 375 lines,
committed at `983a8bb`, root of this repo — read it for the full forensic
detail).

### Analyzed — the upstream Windows binary

- File: `DivaMegaMix.exe` (renamed upstream DMM; 7,938,651 bytes)
- MD5: `106319482accf8ff1501b9c9f46d263c`
- Type: PE32+ (x86-64), .NET 6.0 single-file self-extract app
- Assembly: `DivaModManager, Version=1.3.1.0, Culture=neutral,
  PublicKeyToken=null`
- TFM: `net6.0-windows-win10-x64`
- PDB path: `D:\Modding\Miku\DivaModManager\DivaModManager\obj\Release\
  net6.0-windows\win10-x64\DivaModManager.pdb`
- Compression: single-file self-extract (`PublishSingleFile=true`,
  `IsTrimmable`)

### Mapped — upstream architecture

- Class map: `App` (`DispatcherUnhandledException`), `MainWindow` (grid
  of mods, loadouts, drag-drop, launch), `Setup` (wizard), `ModDownloader`,
  `ModUpdater`, `AutoUpdater` (Onova), `ZipExtractor` (SharpCompress +
  SevenZipExtractor), `DownloadWindow`, `FetchWindow`, `ProgressBox`,
  `ConfigureModWindow`, `DMAFeedGenerator`/`FeedGenerator`,
  `HttpClientExtensions`/`StreamExtensions`.
- `Config.json` schema captured verbatim (see RE report §2.3).
- DML runtime model documented: proxy `dinput8.dll`, `mods/` folder +
  per-mod `mod.toml`, `config.toml` priority list, AppID 1761390,
  symlink-trick launch option (used pre-Proton; now superseded by
  `steam://rungameid/1761390`).
- Game's `Launcher` `exe` size is compared to an expected size to detect
  if the binary has been "converted" (steamless / 4GB patch) —
  `LauncherOptionConverted` + `ConvertedFileSize` in the schema.

### Identified — Linux blockers

| Blocker | Severity | Solubility |
|---|---|---|
| **WPF** (`PresentationFramework` + `PresentationCore` + `WindowsBase` + `System.Xaml`) | Critical | No Linux port exists; never will. Requires UI-layer rewrite (→ Avalonia). |
| `SevenZipExtractor` + native `7z.dll` (PE32+ Windows DLL) | Hard | SharpCompress already handles 7z in C#; drop `SevenZipExtractor`. |
| `Onova.Updater.exe` (net46, Windows) | Hard | Custom `SelfUpdateService` emitting a bash `apply-update.sh`. |
| `Microsoft.Win32.OpenFileDialog` | Hard | Avalonia `StorageProvider`. |
| Registry (`ADVAPI32.dll`: `RegGetValueW`/`RegOpenKeyExW`/…) | Hard | `ProtonPrefixLocator` reads `libraryfolders.vdf` + `localconfig.vdf` instead. |
| Wine `Z:\` paths in `Config.json` | Medium | `WinePathTranslator` one-way migration on `ConfigService.Load`. |
| `FontAwesome5` (WPF icon font) | Medium | Avalonia icon font / unicode. |
| `gong-wpf-dragdrop` | Medium | Avalonia built-in drag-drop. |
| `WpfAnimatedGif` | Medium | `load.gif` kept as asset; rendering via Avalonia. |

### Identified — portable (kept verbatim)

- Octokit (GitHub API), SharpCompress (ZIP/TAR), Tomlyn (TOML),
  `System.Net.Http`, `System.IO`, `System.Diagnostics.Process` (with path
  adjustments), `Onova.dll` core (without `Updater.exe`).

### Decision — Strategia A: Avalonia UI port

The RE report's §7 evaluated four strategies: (A) Avalonia port if upstream
source is obtainable — recommended; (B) CLI/TUI rewrite without UI; (C)
headless scripts; (D) keep Wine with workarounds. The verdict (§9): native
port is impossible without a source rewrite because WPF is the hard
blocker. Strategia A was chosen: rewrite the UI layer to Avalonia, preserve
business logic verbatim, switch TFM `net6.0-windows` → `net8.0` +
`linux-x64`.

---

## Initial port — Round 1

**When:** 2026-07-10.
**Contributors:** rockman6554 + GLM 5.2 (OpenCode handoff).
**Commits:** `3f3f981` (Initial commit), `983a8bb` (Start reverse engineering
to original DMM), `c4f8e2b` (Hide exe to keep public clean).

### 3f3f981 — Initial commit (2026-07-10)

- `LICENSE` (GPL-3.0-or-later, inherited from upstream TekkaGB).

### 983a8bb — Start reverse engineering (2026-07-10)

Added in one commit:

- **`DMM_ReverseEngineering_Report.md`** (Spanish, 375 lines) — the RE
  deliverable (see [Pre-release] above).
- **`DMM_Linux_Port_OpenCode_Handoff.md`** (English, 838 lines) — the GLM
  → next-GLM handoff document. **Deleted at commit `207c117`** but
  recoverable via `git show 983a8bb:DMM_Linux_Port_OpenCode_Handoff.md`.
  Contains the Round 1/2/3 iteration history summarized in this
  section and the next.
- **`DivaModManager.exe.windows-original`** (7.9 MB) — the original
  Windows binary, kept for reference. Removed at `038fceb` once the port
  was self-sustaining.
- **First Avalonia scaffold** — all of:
  - `Program.cs` (`[STAThread] Main`, `-download <url>` arg parsing,
    `BuildAvaloniaApp()` platform detect + InterFont + bumped Skia GPU
    memory limit).
  - `App.axaml`/`App.axaml.cs` (bootstrap: `ConfigService.Load()`,
    `Global` statics, `MainWindowViewModel`, window-size persistence,
    save-on-exit).
  - `Models/`: `ModStructures` (`Mod`, `Metadata`, `Config`, `GameConfig`,
    `Choice`), `GameBananaStructures` (`GameBananaItem`-legacy +
    `GameBananaAPIV4`/`GameBananaRecord`/`GameBananaImage`/etc.), `DivaModArchiveStructures`
    (`DivaModArchivePost`/`User`/`ModList`, `DmaFeedSort`/`DmaFeedFilter`),
    `DownloadProgress` (immutable record).
  - `Services/`: `Global` (static state + `Logger` event fan-in),
    `ConfigService` (load/save, Z:\ → Linux path migration),
    `SetupService` (first-run wizard),
    `DmlUpdateService` (Octokit → `blueskythlikesclouds/DivaModLoader`),
    `LaunchService` (`VerifyLaunch`/`AutoConfigureSteam`/
    `LaunchViaSteam`/`OpenModsFolder`/`ForceKillGame`/symlink trick),
    `SteamLaunchOptionsService` (read/write `WINEDLLOVERRIDES` in
    `localconfig.vdf` for AppID 1761390),
    `ModService` (refresh/reorder/apply-loadout/delete/create,
    `mod.toml` reader),
    `ZipExtractor` (SharpCompress 7z/zip/tar + system unrar/7z fallback),
    `GameBananaService` (apiv6 client + LRU cache + 1-click install),
    `DmaService` (`/api/v1` client + count-based pagination),
    `SelfUpdateService` (GitHub-release self-update for DMM, emits
    `apply-update.sh`).
  - `Helpers/`: `HttpClientExtensions` (download w/ progress),
    `WinePathTranslator` (Linux ↔ Wine Z:\ translation),
    `StringConverters` (FormatSize/Number/TimeAgo/Singular, NaturalSort),
    `VdfParser` (minimal recursive-descent VDF parser/writer for Steam's
    `localconfig.vdf`).
  - `ViewModels/`: `MainWindowViewModel` (698 lines wiring all services),
    `DmaBrowserViewModel`, `GameBananaBrowserViewModel`.
  - `Views/`: `MainWindow` (+ auto-scroll code-behind), `DmaBrowserWindow`,
    `GameBananaBrowserWindow`.
  - `Styles/AppStyles.axaml` (initial reusable classes).
  - `Assets/` (12 binary resources: `miku.ico`, fonts, logos, `load.gif`).
  - `DivaModManagerLinux.csproj` (net8.0, Avalonia 11.1+, all NuGet refs,
    `SelfContained=false`, `RuntimeIdentifiers=linux-x64;linux-musl-x64`,
    `OutputType=WinExe`).
  - `compile.txt` (publish command, three lines).
  - Void Linux packaging: `packaging/void-linux/template` + `files/`
    (`divamodmanager.sh`, `.desktop`, `.xml`, `install-steam-symlink-trick.sh`).
  - **`bin-linux-x64-selfcontained/DivaModManager`** (80 MB self-contained,
    committed — the test target, intentional).

### Known gaps carried by Round 1

- No GameBanana/DMA browser UI (added in Round 2).
- No Steam `localconfig.vdf` auto-config (added in Round 2).
- Wine direct-launch offered as an option — doesn't work (game has
  Denuvo — requires Steam runtime; removed in Round 2).

### Round 2 — Steam + browsers + From-URL

User feedback: *"Steam loads vanilla, Wine errors, need mod download"*.

- Removed Wine direct-launch option (Denuvo requires Steam runtime).
- Added `SteamLaunchOptionsService` + `VdfParser` — DMM auto-writes
  `WINEDLLOVERRIDES="dinput8.dll=n,b"` into Steam's `localconfig.vdf`.
- Added pre-launch verification: (1) `dinput8.dll` exists, (2) `config.toml`
  exists with the priority list, (3) Steam launch options contain the
  `WINEDLLOVERRIDES`. On failure, DMM explains and offers to fix.
- Added `GameBanana` + `DMA` browser windows.
- Added "From URL" install dialog (paste any GB/DMA URL or
  `divamodmanager://` 1-click URL).
- Improved archive extraction: scans for subfolders containing
  `config.toml` (the actual mod folders per DML convention); writes
  `mod.json` metadata alongside each installed mod (author, description,
  preview URL, …).

### Round 3 — Cloudflare + images + force-kill + 2nd-open freeze

User feedback: *"GB 400, DMA images don't load, game freezes on exit, GB
button freezes DMM on 2nd click"*.

- **GameBanana 400 fix** — apiv4 `/Mod/Index` returns an **array at the
  root** (not an object with `_aRecords`). Removed invalid `_csvProperties`
  on the single-item endpoint (`_aRootCategory`,
  `_aAlternateFileSources`, `_bHasUpdates`, `_aLatestUpdates` don't exist
  on apiv4 and cause 400). Added Core API (`api.gamebanana.com/Core/Item/Data`)
  fallback using legacy `fields=name,Files().aFiles(),…` syntax.
- **Preview images fix** — `Avalonia.Image.Source` bound to a string URL
  does *not* async-load. Switched to `AdvancedImage` from
  `AsyncImageLoader.Avalonia` 3.3.0.
- **Game freeze on exit** — DML's `dinput8.dll` doesn't shut down cleanly
  under Proton when the user clicks Exit Game. Added `ForceKillGame()`
  running `pkill -f DivaMegaMix.exe` then `pkill -9 -f DivaMegaMix.exe`
  fallback; wired to a red "Force Kill" toolbar button.
- **GB browser freeze on 2nd open** — each browser window was creating its
  own `GameBananaService`/`DmaService` with its own `HttpClient`, and
  in-flight requests kept sockets open after the window closed. Fixed by
  making `_http` a **static shared HttpClient** on both services and
  adding `CancellationTokenSource` to the ViewModels cancelled on
  `Window.Closing`.

### c4f8e2b — Hide exe (2026-07-10)

- `.gitignore` patch to keep the Windows original out of the public repo
  (the binary itself was removed at `038fceb`; this commit just stopped
  tracking the path).

---

## [1.3.1] — 2026-07-17 — 207c117: Rebrand to "DivaModManager X"

**Contributor:** rockman6554.

### Added

- `bin-linux-x64-selfcontained/Assets/miku.ico` — app icon (also copied
  to output via `<None Include="Assets\miku.ico" CopyToOutputDirectory=
  PreserveNewest>`).
- `bin-linux-x64-selfcontained/Config.json` — committed default config for
  the bundled self-contained binary (later removed from tracking in PR #2).
- Custom font assets in `source/Assets/`:
  `AnekLatin-Regular.ttf`/`-Medium.ttf`/`-SemiBold.ttf`,
  `RobotoMono-Regular.ttf`, plus logos `DMA_BLACK.png`, `GameBanana.png`,
  `KoFi.png`, `dml.png`, `load.gif`, `preview.png`, `Icons/mmplus.png`.

### Changed

- Project name → **"DivaModManager X"** (branding + `MainWindow` title).
- GameBanana thumbnails switched to apiv6 `_aPreviewMedia` metadata
  (was previously relying on broken thumbnail paths).

### Removed

- **`DMM_Linux_Port_OpenCode_Handoff.md`** (838 lines) — superseded by
  this `CHANGELOG.md` and `source/README.md`. Recoverable via
  `git show 983a8bb:DMM_Linux_Port_OpenCode_Handoff.md`.
- Two transitional working artifacts: `rename-DivaModManager-X-with-cyan-X-accent.patch`
  and `fetch-full-mod-data-via-apiv6-thumbnails-meta.patch` — their
  changes had been absorbed into the tree. Their lifespans were one round
  of work each.

---

## [1.3.1] — 2026-07-18 — 038fceb: Cleanup

**Contributor:** rockman6554.

### Changed

- `Models/GameBananaStructures.cs` expanded with apiv6 DTO shapes
  (`GameBananaAPIV4`, `GameBananaRecord`, `GameBananaImage`,
  `GameBananaMember`, `GameBananaCategory`, `GameBananaUpdates`,
  `AlternateFileSource`, `HTML→Text` converter on records).
- `GameBananaService` adapted to the new apiv6 models.
- `GameBananaBrowserViewModel` updated to consume the new record shape.
- `MainWindow.axaml` minor tweaks.

### Removed

- The two `.patch` artifacts (already absorbed at `207c117` — removed from
  disk).
- `DivaModManager.exe.windows-original` (7.9 MB) — the Windows binary
  no longer needs to ship in the repo. The RE report stays as the
  forensic record.

---

## Future blockers — catalog, do not action

These are known issues that would block an AppImage release (or are quality
smells worth fixing in the normal QA cycle). They are listed here so future
GLM sessions don't have to re-derive them. **AppImage is a future goal,
not the current sprint** — see the project-context header. Do not work on
these unless the maintainer pulls them into the sprint.

### Build / packaging

1. **`SelfContained` mismatch** — `SelfContained=false` in
   `source/DivaModManagerLinux.csproj` vs `--self-contained true` in
   `compile.txt`. Resolve to one source of truth (likely via
   `Directory.Build.props` or by making the csproj the authority and
   deleting `compile.txt`).
2. **xbps template placeholders** — `packaging/void-linux/template` has
   `maintainer="Your Name <you@example.com>"` and
   `checksum="<sha256-of-upstream-tarball>"` unfilled; the `do_build`
   step references a `linux-port.patch` and a `divamodmanager.png` that
   are not in `files/`. Not buildable as-is.
3. **Self-update `rsync` dep** — `SelfUpdateService`'s `apply-update.sh`
   depends on `rsync`, which is not in the xbps template's `depends`.
   Either add `rsync` or switch to `cp -a`.
4. **`chmod +x apply-update.sh` failure silently swallowed** — log at
   minimum a warning that the user may need to chmod manually.

### Code-quality smells (QA candidates, not blockers)

5. **Hardcoded color literals duplicate the `App.axaml` palette** —
   `Helpers/DialogHelper.cs` hardcodes `#39C5BB`/`#161618`/`#E8E8EC`/
   `#F87171`; `MainWindowViewModel.LogEntry.Color` hardcodes
   `#4ADE80`/`#FBBF24`/`#F87171`; browser VMs' `InstallStatusColor`
   hardcode `#9A9AA4`/`#39C5BB`/`#4ADE80`/`#F87171`. All should use
   `DynamicResource` against `App.axaml` so a future theme change
   propagates.
6. **`DialogHelper.BuildDialog` dead cancel-handler** — first
   `cancelBtn.Click += (s, e) => { /* close via window */ };` is a no-op
   with a misleading comment `// Wire close: store on the window's Tag`.
   The real wiring (`win.Close()`) is added below the
   `if (buttons.Children.Count > 1)` guard. Delete the dead handler and
   the false-trail comment.
7. **`DialogHelper.ShowAsync` inverted null-guard** — the `else` branch
   does `await dialog.ShowDialog(owner!)` with `owner!` (null-forgiving)
   when `owner` is null → `ShowDialog(null)` throws. The branches are
   inverted: if owner is null, you want `dialog.Show()` (non-modal), not
   `ShowDialog(owner!)`. Currently masked because the desktop lifetime
   always has a `MainWindow`.
8. **Browser windows ~85% duplicated** — `DmaBrowserWindow.axaml`/
   `.axaml.cs` and `GameBananaBrowserWindow.axaml`/`.axaml.cs` are
   near-identical. Same for `DmaService`+`GameBananaService` install tails
   (`Directory.Move` + `anyMoved`). Refactor candidate: shared
   `BrowserViewModel<T>` + shared browser control; every tweak currently
   must be made twice.
9. **`LaunchService.OpenModsFolder` extra trailing quote** at
   `source/Services/LaunchService.cs:129`:
   `Arguments = $"\"{modsFolder}\"\""` — likely a typo; other
   `ProcessStartInfo` calls in the file use proper single-trailing-quote
   quoting. Verify against `xdg-open` arg parsing.
10. **Unused `using`s** added in `source/Views/MainWindow.axaml.cs` at
    PR #2: `Avalonia.Automation.Peers`, `Avalonia.Controls.Primitives`,
    `Avalonia.LogicalTree` — compiler warnings, harmless but cleanup-able.
11. **Dead models** — `GameBananaItem` and `GameBananaInstallerIntegration`
    in `Models/GameBananaStructures.cs` are unreferenced (legacy apiv4
    shapes superseded by `GameBananaAPIV4`/`GameBananaRecord`). The
    `GameConfig.LauncherOption`/`LauncherOptionIndex`/
    `LauncherOptionConverted` fields are dead (the Wine direct-launch was
    removed in Round 2 — these are read from old `Config.json`s for
    back-compat but never written). Mark `[Obsolete]` or remove after
    confirming no user still has a `Config.json` carrying them.
12. **`ProtonPrefixLocator.FindGameInstall` fragile regex** — reads
    `libraryfolders.vdf` line-by-line with `IndexOf('"', 6)` heuristic.
    Should use the existing `VdfParser` instead.
13. **No `DispatcherUnhandledException`** — `Program.cs`/`App` don't set
    one (the WPF upstream had it; this port doesn't). The many
    fire-and-forget `Dispatcher.UIThread.InvokeAsync` calls in
    `MainWindowViewModel` could crash silently if one throws.
14. **Static cache + lock anti-pattern** in `GameBananaService`/`DmaService`
    — `_cacheLock` guards only the dictionary mutation, not the "compute
    cache miss + fetch over HTTP" path. Concurrent calls with the same URL
    both hit the network and both write (last-writer-wins). Acceptable for
    a single-user mod manager; worth a note.
15. **PR #2 install-status auto-clear off the UI thread** —
    `_ = Task.Delay(6000).ContinueWith(_ => { if
    (InstallStatus.StartsWith("✓")) InstallStatus = string.Empty; })`
    mutates an `ObservableProperty` setter off the UI thread. Wrap in
    `Dispatcher.UIThread.Post`.
16. **`ClipboardHelper.TryRunClipboardTool`** uses
    `proc.WaitForExit(2000)` (sync) inside an `async Task<bool>`. Low
    impact (2 s ceiling) but inconsistent with the async style elsewhere
    (PR #2's `ZipExtractor` uses `WaitForExitAsync(ct)` correctly).
17. **No unit tests** on the rewritten `GameBananaService`/`DmaService`
    HTTP + parse logic. The service rewrites are pure HTTP/parse and
    would benefit from at least deserialization tests.
18. **`ZipExtractor` argument quoting** uses
    `ProcessStartInfo.Arguments = $"x -y -o\"{destDir}\" \"{archivePath}\""`
    (string concat) rather than `ArgumentList` (the safer route used in
    PR #1's `ClipboardHelper`). Consistency, not a real injection vector
    (paths come from trusted sources).

### AppImage blockers — future, do not action now

19. **`ZipExtractor.ResolveToolPath`** hardcoded fallback list
    (`/usr/bin /usr/sbin /usr/local/bin /bin`) skips the AppDir. Inside an
    AppImage the bundled `unrar`/`7z` would live at `$APPDIR/usr/bin`.
    When AppImage work begins: add `$APPDIR`-relative fallback.
20. **`ClipboardHelper`** fallbacks (`wl-copy`/`xclip`/`xsel`) won't be
    found inside an AppImage unless bundled. Avalonia's in-process
    `IClipboard` is the primary path and works; the fallbacks are
    belt-and-suspenders.
21. **`SelfUpdateService.apply-update.sh`** assumes the binary lives in a
    writable install dir; an AppImage is a single file mounted via FUSE.
    The self-update flow needs rethinking for AppImage (replace the
    `.AppImage` file, inform the user to re-launch).

---

## Format conventions

- Each release entry is one commit (or a small group of related commits).
- Subsections follow Keep-a-Changelog: **Added / Changed / Fixed / Removed**
  (with **Analyzed / Mapped / Identified / Decision** reserved for the
  Reverse-Engineering phase).
- Dates are ISO-8601 taken from the commit/merge timestamp.
- Commit SHAs are abbreviated to 7 chars and linked implicitly (this repo
  is small enough that `git show <sha>` is unambiguous).
- When a future release is cut, stamp a new `[1.3.2]` / `[1.4.0]` /
  `[2.0.0]` header above `[Unreleased]` and move the staged items down.
