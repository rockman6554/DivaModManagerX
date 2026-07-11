# Reporte de Ingeniería Inversa: DivaModManager v1.3.1

## Ejecución nativa en Linux — Análisis de viabilidad

---

## 1. Identidad del binario

| Atributo | Valor |
|---|---|
| Archivo | `DivaMegaMix.exe` (renombrado; 7,938,651 bytes) |
| MD5 | `106319482accf8ff1501b9c9f46d263c` |
| Tipo | PE32+ (x86-64), .NET 6.0 single-file app |
| Assembly | `DivaModManager, Version=1.3.1.0, Culture=neutral, PublicKeyToken=null` |
| TFM | `net6.0-windows-win10-x64` |
| PDB path | `D:\Modding\Miku\DivaModManager\DivaModManager\obj\Release\net6.0-windows\win10-x64\DivaModManager.pdb` |
| Compresión | Single-file self-extract (`PublishSingleFile=true`, `IsTrimmable`) |

---

## 2. Arquitectura de la aplicación

### 2.1 Clases principales (del namespace `DivaModManager.*`)

| Clase | Responsabilidad |
|---|---|
| `App` | Entry point WPF, `DispatcherUnhandledException` |
| `MainWindow` | UI principal: grid de mods, loadouts, drag-drop, launch |
| `Setup` | Wizard de primera ejecución: elegir GamePath, Launcher, descargar DML |
| `ModDownloader` | Descarga de mods (GameBanana + GitHub) |
| `ModUpdater` | Chequeo de updates de mods |
| `AutoUpdater` | Auto-update de DMM (Onova) |
| `ZipExtractor` | Extracción de archivos de mods (SharpCompress) |
| `DownloadWindow` | UI de progreso de descarga |
| `FetchWindow` | UI de fetch de metadata |
| `ProgressBox` | UI de progreso genérico |
| `ConfigureModWindow` (`UI`) | Configuración de mod individual |
| `DMAFeedGenerator` / `FeedGenerator` | Generación de feed de mods |
| `HttpClientExtensions` / `StreamExtensions` | Helpers HTTP/stream |

### 2.2 Flujo de gestión (diagrama)

```
┌─────────────────────────────────────────────────────────────┐
│  SETUP (wizard, FirstOpen=true)                             │
│  ├─ Browse_Click → OpenFileDialog (Windows-only picker)     │
│  ├─ Browse_Click → FolderBrowserDialog                      │
│  ├─ CheckForDMLUpdate → Octokit GET /repos/.../releases/latest│
│  ├─ DownloadDML → HttpClient.DownloadAsync                  │
│  └─ ExtractFile → SharpCompress / SevenZipExtractor          │
│        → extrae DML al GamePath                              │
│  → escribe Config.json: GamePath, Launcher, ModsFolder,     │
│    ModLoaderVersion, FirstOpen=false                         │
└─────────────────────────────────────────────────────────────┘
           │
           ▼
┌─────────────────────────────────────────────────────────────┐
│  RUNTIME (MainWindow)                                       │
│  ├─ InitializeBrowser → GameBanana API v4 (HTTP REST)       │
│  ├─ DMAModBrowser / GameBananaModList → browser in-app      │
│  ├─ ModDownloader.BrowserDownload → descarga .zip/.7z        │
│  ├─ ZipExtractor.ExtractPackageAsync → ModsFolder/<mod>/     │
│  ├─ mod.toml parseado con Tomlyn (name, author, files, etc.)│
│  ├─ DataGrid: enable/disable mods, drag-drop reorder        │
│  ├─ Loadouts: presets de mods activos (LoadoutsBox)         │
│  ├─ SaveButton_Click → escribe Config.json + mod order      │
│  └─ Launch_Click → Process.Start(Launcher, args)             │
│        → arranca el exe del juego con DML ya inyectado       │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 Config.json (esquema confirmado)

```json
{
  "CurrentGame": "Project DIVA Mega Mix+",
  "Configs": {
    "Project DIVA Mega Mix+": {
      "Launcher": "Z:\\...\\DivaMegaMix.exe",
      "GamePath": "Z:\\...\\Hatsune Miku Project DIVA Mega Mix Plus",
      "LauncherOption": false,
      "LauncherOptionIndex": 0,
      "LauncherOptionConverted": true,
      "FirstOpen": false,
      "ModsFolder": "Z:\\...\\Mods",
      "ModLoaderVersion": "x.y.z",
      "CurrentLoadout": "Default",
      "Loadouts": { "Default": [] }
    }
  },
  "LeftGridWidth": 1.8, "RightGridWidth": 1,
  "TopGridHeight": 1.6, "BottomGridHeight": 1,
  "Height": 750, "Width": 1280, "Maximized": false
}
```

### 2.4 Cómo gestiona DMM cada componente

DMM es una app **.NET 6.0 WPF** (single-file, `win10-x64`). No parchea el juego directamente — **delega en un loader externo (DML = DivaModLoader)**. Su rol es de **orquestador/configurador**, no de inyector runtime.

#### Configuración central: `Config.json`
DMM lee/escribe un único `Config.json` en **su propia carpeta** (donde está el exe). Campos confirmados del binario:
- `CurrentGame` — juego activo (`"Project DIVA Mega Mix+"`)
- `Configs[game]`:
  - `GamePath` — ruta raíz del juego (formato Win32 `Z:\...`)
  - `Launcher` — exe que arrancar al hacer Launch
  - `LauncherOption` / `LauncherOptionIndex` — dropdown de modo de lanzamiento
  - `LauncherOptionConverted` + `ConvertedFileSize` — detecta si el exe fue "convertido" (ej. steamless/4GB patch) **comparando el tamaño** del exe contra un esperado
  - `ModsFolder` — carpeta de mods
  - `ModLoaderVersion` — versión instalada de DML (para update-checks)
  - `FirstOpen` — flag del wizard de setup
  - `CurrentLoadout` + `Loadouts{Default:[]}` — presets de mods activos
- UI: `LeftGridWidth`, `Height`, `Maximized`, etc.

#### Setup (wizard, clase `DivaModManager.Setup`)
Flow: `Setup_Click` → pide `GamePath` (folder picker) + `Launcher` (file picker) → `CheckForDMLUpdate` (GitHub API vía Octokit, `GET /repos/{owner}/{repo}/releases/latest`) → `DownloadDML` → `ExtractFile` (SharpCompress/7z). **Acá es donde rompen los symlinks**: el file picker de Wine no los lista.

#### DivaModLoader (DML) — el runtime real
DMM **descarga DML desde GitHub** (releases) y lo extrae dentro del `GamePath`. DML es el que hookea el juego (proxy DLL / ASI-style). DMM solo anota `ModLoaderVersion` y ofrece actualizarlo (`CheckForDMLUpdate`). No toca los `.cpk` del juego.

#### Gestión de mods
- **Almacenamiento**: cada mod va a una subcarpeta bajo `ModsFolder`. Cada mod tiene un **`mod.toml`** (parseado con **Tomlyn**) con campos `name`, `description`, `author`, `category`, `files`, `priority`, `dependencies`.
- **Descarga**: `ModDownloader` con dos fuentes:
  - **GameBanana** (`GameBananaAPIV4`, `GameBananaItem`, `GameBananaItemFile`, `GameBananaAlternateFileSource`) — browser in-app (`DMAModBrowser`, `GameBananaModList`)
  - **GitHub** (Octokit) — para mods hosteados como releases
- **Extracción**: `ZipExtractor.ExtractPackageAsync` (SharpCompress) o `SevenZipExtractor`
- **Loadouts**: agrupaciones de mods enabled/disabled (`LoadoutsBox`, `EditLoadouts`, `CreateLoadoutName`). Toggle per-mod, drag-drop reorden (`gong-wpf-dragdrop`), orden alfabético agrupado (`SortAlphabeticallyAndGroupEnabled_Click`).
- **Apply**: DMM no "patchea" los cpks; **DML en runtime** lee `ModsFolder` y el loadout y carga los archivos del mod encima de los del juego. DMM solo organiza archivos en disco + escribe la config del loadout.

#### Launch (arrancar el juego)
`Launch_Click` → `Process.Start(Launcher)` con `LauncherOption`/`LauncherOptionIndex` (decide cómo invocarlo). El `Launcher` apunta al exe del juego. DML ya está instalado en el `GamePath`, así que al arrancar el juego DML se carga solo y aplica los mods del loadout activo.

#### Auto-update
- `AutoUpdater.CheckForDMMUpdate` — DMM se auto-actualiza con **Onova** (`Onova.Updater.exe`, `ZipPackageExtractor`)
- `ModUpdater` / `CheckForUpdates` — chequea updates de mods individuales

---

## 3. Dependencias (deps.json embebido)

### 3.1 NuGet packages

| Package | Versión | Linux-compatible? | Notas |
|---|---|---|---|
| **Octokit** | 0.51.0 | **SI** | `netstandard2.0` — API GitHub pura C# |
| **SharpCompress** | 0.32.0 | **SI** | `net6.0` — ZIP/TAR/GZIP/LZMA puro C# |
| **Tomlyn** | 0.14.3 | **SI** | `net6.0` — parser TOML puro C# |
| **Onova** | 2.6.2 | **SI** | `netcoreapp3.0` — auto-update; `Onova.Updater.exe` es .NET 4.6 (Windows-only) |
| **SevenZipExtractor** | 1.0.17 | **PARCIAL** | `netstandard2.0` wrapper, pero carga **`7z.dll` nativa** (Windows x64) |
| **FontAwesome5** | 2.1.11 | **NO** | `net6.0-windows7.0` — WPF-specific (iconos en XAML) |
| **gong-wpf-dragdrop** | 3.1.1 | **NO** | `net6.0-windows7.0` — drag-drop WPF |
| **WpfAnimatedGif** | 2.0.2 | **NO** | `netcoreapp3.0` — decoder GIF WPF-specific |
| Microsoft.NET.ILLink | 7.0.100 | N/A | Linker (build-time only) |

### 3.2 Framework references (Windows-only)

| Assembly | Versión | Linux? |
|---|---|---|
| **PresentationFramework** | 6.0.2.0 | **NO** — WPF |
| **PresentationCore** | 6.0.2.0 | **NO** — WPF |
| **WindowsBase** | 6.0.2.0 | **NO** — WPF |
| **System.Xaml** | 6.0.2.0 | **NO** — XAML WPF |

---

## 4. APIs Windows-specific detectadas

### 4.1 P/Invoke — DLLs nativas importadas

```
KERNEL32.dll     → GetModuleHandleW, LoadLibraryExW, GetProcAddress,
                   FindFirstFileExW, FindNextFileW, GetFileAttributesExW,
                   GetFullPathNameW, GetEnvironmentVariableW, QueryPerformanceCounter,
                   GetCurrentProcess, TerminateProcess, TlsAlloc/Free/Get/Set
USER32.dll       → MessageBoxW
SHELL32.dll      → ShellExecuteW
ADVAPI32.dll     → RegGetValueW, RegOpenKeyExW, RegCloseKey,
                   RegisterEventSourceW, ReportEventW, DeregisterEventSource
ole32.dll        → COM (WPF rendering)
ntdll.dll        → RtlCaptureContext, RtlLookupFunctionEntry, RtlUnwindEx, RtlVirtualUnwind
api-ms-win-crt-* → UCRT (C runtime)
```

### 4.2 WPF (bloqueador critico)

DMM usa extensivamente el stack WPF:

- **Controles**: `DataGrid`, `DataGridCheckBoxColumn`, `DataGridTextColumn`, `DataGridTemplateColumn`, `DataGridRow`, `DataGridCell`, `Image`, `Primitives.DataGridColumnHeader`
- **Shell**: `System.Windows.Shell.TaskbarItemInfo`
- **Media**: `System.Windows.Media.Animation`, `System.Windows.Media.Effects.BlurEffect`, `System.Windows.Media.Imaging`
- **Drag-drop**: `gong-wpf-dragdrop` (drag reorder de mods)
- **XAML/BAML**: recursos compilados `DivaModManager.g.resources` (BAML embebido)
- **Icons**: `FontAwesome5` (WPF icon font)

### 4.3 OpenFileDialog (Windows-only)

```
Microsoft.Win32.OpenFileDialog
```

Usado en `Setup.Browse_Click` para elegir el exe del juego. **No hay alternativa cross-platform** en el binario.

### 4.4 Registry (Windows-only)

`Microsoft.Win32.Registry` — `RegGetValueW`, `RegOpenKeyExW`. Probablemente para detectar instalacion de Steam/ruta del juego.

### 4.5 Process.Start

`System.Diagnostics.Process` con `ProcessStartInfo`:
- `set_UseShellExecute` — ejecuta vía shell
- `set_CreateNoWindow`
- `set_WorkingDirectory`
- `set_Verb`
- `set_Arguments`

Esto es cross-platform en .NET, pero los paths pasados son formato Windows (`Z:\...`).

### 4.6 SevenZipExtractor → `7z.dll` nativa

El archivo `x64/7z.dll` es un PE32+ Windows DLL (confirmado con `file`). SevenZipExtractor lo carga vía P/Invoke. **No funcionará en Linux** sin reemplazo.

### 4.7 Onova.Updater.exe

`Onova.Updater.exe` está embebido y es compilado para `net46` (Windows). Se extrae y ejecuta para auto-updates. **No funcionará en Linux**.

---

## 5. Componentes cross-platform (ya compatibles)

| Componente | Estado |
|---|---|
| Octokit (GitHub API) | Puro C#, HTTP — funciona en Linux |
| SharpCompress (ZIP/TAR) | Puro C# — funciona en Linux |
| Tomlyn (TOML parser) | Puro C# — funciona en Linux |
| `System.Net.Http` (HttpClient) | Cross-platform |
| `System.IO` (File/Directory) | Cross-platform |
| `System.Diagnostics.Process` | Cross-platform (con ajustes de paths) |
| Onova.dll (core) | Puro C# — funciona (pero `Updater.exe` no) |

---

## 6. Evaluacion: Se puede correr nativo en Linux?

### 6.1 Veredicto: NO directamente. Requiere port o reescritura del UI.

### 6.2 Bloqueador critico: **WPF**

WPF (`PresentationFramework` + `PresentationCore` + `WindowsBase`) es **exclusivo de Windows**. No existe implementacion para Linux. Microsoft no tiene planes de portarlo. No hay shim/compat layer.

Esto afecta:
- Toda la UI (MainWindow, Setup, DownloadWindow, etc.)
- Todos los XAML/BAML compilados
- `FontAwesome5` (WPF-specific)
- `gong-wpf-dragdrop` (WPF-specific)
- `WpfAnimatedGif` (WPF-specific)
- `OpenFileDialog` (`Microsoft.Win32`)

### 6.3 Bloqueadores secundarios

| Problema | Impacto | Solubilidad |
|---|---|---|
| `7z.dll` nativa (Windows) | Extraccion .7z de mods | SharpCompress ya soporta 7z en C# — reemplazar SevenZipExtractor |
| `Onova.Updater.exe` (net46) | Auto-update de DMM | Deshabilitar o reimplementar |
| Registry access | Deteccion de Steam | Eliminar / hardcodear paths |
| Paths `Z:\` (Wine) | GamePath/Launcher en Config.json | Usar paths Linux nativos |
| `OpenFileDialog` | Picker de setup | Reimplementar con dialog CLI o GTK |

---

## 7. Estrategias para Linux nativo

### Estrategia A: Port a Avalonia UI (recomendada si hay fuente)

**Requiere el codigo fuente de DMM** (repo GitHub del autor en `D:\Modding\Miku\`).

- Reemplazar WPF → **Avalonia UI** (cross-platform .NET UI, similar a WPF)
- Migrar XAML → AXAML (sintaxis casi identica)
- Reemplazar `OpenFileDialog` → `Avalonia.Platform.Storage.IStorageProvider`
- Reemplazar `gong-wpf-dragdrop` → Avalonia built-in drag-drop
- Reemplazar `FontAwesome5` → Avalonia icon font
- Reemplazar `WpfAnimatedGif` → Avalonia animation
- Reemplazar `SevenZipExtractor` → SharpCompress directo
- Eliminar Registry → hardcodear/config paths
- Eliminar `Onova.Updater.exe` → usar `Onova` core con extractor cross-platform
- Cambiar TFM: `net6.0-windows` → `net6.0` + `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>`

**Esfuerzo estimado**: Medio-alto. La logica de negocio (Octokit, SharpCompress, Tomlyn, mod management) es portable sin cambios. El UI requiere reescritura completa de XAML + bindings.

### Estrategia B: CLI/TUI (sin UI grafico)

Reescribir solo la logica de gestion como herramienta CLI:
- Setup: aceptar GamePath/Launcher/ModsFolder como argumentos
- Listar mods: `dmm list`
- Instalar: `dmm install <url>`
- Enable/disable: `dmm enable <mod>` / `dmm disable <mod>`
- Loadouts: `dmm loadout save/load`
- Launch: `dmm launch`
- Descargar DML: `dmm setup-dml`

**Ventaja**: No depende de ningun framework UI. Reutiliza Octokit/SharpCompress/Tomlyn directamente.
**Desventaja**: Pierde el browser in-app de GameBanana.

### Estrategia C: Headless + script (minimo viable)

Sin recompilar nada. Un script que:
1. Pre-llena `Config.json` con paths Linux nativos
2. Descarga DML manualmente del repo de GitHub
3. Gestiona mods como archivos en `ModsFolder/`
4. Lanza el juego con DML ya instalado

**Limitacion**: Sin browser de mods, sin auto-update, sin loadout management visual. Funcional pero primitivo.

### Estrategia D: Wine/Proton con workarounds (lo que ya tenemos)

Mantener DMM en Wine/Proton pero arreglar los puntos de friccion:
- Pre-llenar `Config.json` (saltar el wizard)
- Hardlinks en vez de symlinks (Wine no los muestra en el picker)
- Paths `Z:\` en config

**Ventaja**: Cero cambios de codigo, funciona hoy.
**Desventaja**: Sigue dependiendo de Wine.

---

## 8. Resumen de compatibilidad

```
┌──────────────────────────────┬──────────┬────────────────────────────────┐
│ Componente                   │ Linux?   │ Notas                          │
├──────────────────────────────┼──────────┼────────────────────────────────┤
│ Logica de negocio (C#)       │  SI      │ Octokit, SharpCompress, Tomlyn │
│ Config.json I/O              │  SI      │ System.IO cross-platform       │
│ GitHub API (descarga DML)    │  SI      │ HttpClient + Octokit           │
│ GameBanana API               │  SI      │ HttpClient REST                │
│ mod.toml parsing             │  SI      │ Tomlyn puro C#                 │
│ ZIP extraction               │  SI      │ SharpCompress puro C#          │
│ 7z extraction                │  PARCIAL │ Reemplazar 7z.dll→SharpCompress│
│ Process.Start (launch game)  │  SI*     │ *Paths Windows→Linux           │
├──────────────────────────────┼──────────┼────────────────────────────────┤
│ WPF (toda la UI)             │  NO      │ Bloqueador critico             │
│ FontAwesome5 (WPF icons)     │  NO      │ WPF-specific                   │
│ gong-wpf-dragdrop            │  NO      │ WPF-specific                   │
│ WpfAnimatedGif               │  NO      │ WPF-specific                   │
│ OpenFileDialog               │  NO      │ Microsoft.Win32 (Windows)      │
│ Registry access              │  NO      │ ADVAPI32.dll                   │
│ Onova.Updater.exe            │  NO      │ net46 Windows-only             │
│ ShellExecuteW                │  NO      │ SHELL32.dll                    │
│ EventLog (ReportEventW)      │  NO      │ ADVAPI32.dll                   │
└──────────────────────────────┴──────────┴────────────────────────────────┘
```

---

## 9. Conclusion

**DMM no puede ejecutarse nativo en Linux sin recompilacion.** El 100% del UI esta construido sobre WPF, que es Windows-exclusivo. No existe implementacion de WPF para Linux y Microsoft no tiene planes de portarlo.

Sin embargo, **toda la logica de negocio** (gestion de mods, descarga, parsing TOML, API de GitHub/GameBanana, extraccion ZIP) es C# puro cross-platform y seria reutilizable tal cual en un port.

La ruta mas realista es la **Estrategia A (port a Avalonia)** si se consigue el codigo fuente, o **Estrategia B (CLI)** como alternativa sin fuente. Mientras tanto, **Estrategia D (Wine + workarounds)** es la unica opcion inmediata.

---

## 10. Apéndice — Datos del entorno analizado

| Item | Valor |
|---|---|
| AppID Steam | 1761390 |
| Juego | Hatsune Miku: Project DIVA Mega Mix+ |
| Prefix Proton | `compatdata/1761390/pfx` (Proton 9.0-203) |
| Drive mapping | `z: → /`, `c: → drive_c` |
| Exe DMM | `DivaMegaMix.exe` (7.9 MB, renombrado de DivaModManager.exe) |
| Exe juego real | `DivaMegaMix.exe ` (402 MB, con espacio al final) |
| Config.json | En carpeta del juego (donde reside el exe de DMM) |
| Filesystem | `/dev/sda2` (mismo FS para game root, dmm/, exe real) |
