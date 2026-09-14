# Astra Client — Minecraft Custom Client / Launcher (WPF, C#, MSBuild)

## Context

Greenfield build (empty directory `D:\reposit\Minecraft Client Application`). The goal is a
**full-featured custom Minecraft client application** — a real launcher — delivered as a
**C#/.NET WPF app built with MSBuild** (`dotnet build`), with a **Windows 11 frosted-glass
aesthetic** (rounded window + rounded controls, borderless, custom menu strip + top bar) and
**no neon** — refined dark glass with subtle purple/blue gradients and blur.

All content is **live from free public APIs** (no paid keys):
- **Mojang/piston-meta** for the version framework (912 versions, 1.7.x → latest `1.21.11` / `26.x`).
- **Modrinth** for Browse (mods 75k, resource packs 34k, datapacks 14k, shaders) — free, key-less,
  supports search, version/category facets, sort, pagination.
- **Loader meta APIs** — Fabric (`meta.fabricmc.net`), Quilt (`meta.quiltmc.org`), NeoForge
  (`maven.neoforged.net`), Forge (`files.minecraftforge.net` promotions + maven).

It must **actually launch the game** (download client jar + libraries + assets + natives, resolve
Java, assemble legacy & modern args, spawn the process), support **every mod loader per version**,
**auto-fill recommended game/JVM args when a loader is chosen**, and **install browsed content
directly into the selected instance's folder** (`mods/`, `resourcepacks/`, `shaderpacks/`,
`datapacks/`) based on the version selected in Home.

### Verified API facts (grounding)
- Manifest: `GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` →
  `{ latest:{release,snapshot}, versions:[{id,type,url,time,releaseTime,sha1,complianceLevel}] }`.
- 1.7.10 version json: `mainClass=net.minecraft.client.main.Main`, **legacy `minecraftArguments`**,
  `assetIndex.id=1.7.10`, `downloads.client.url` present, `javaVersion={component:jre-legacy,majorVersion:8}`.
- 1.21.1 version json: **modern `arguments.{game,jvm}`** (28 game entries), `assetIndex.id=17`,
  `javaVersion={component:java-runtime-delta,majorVersion:21}`.
- Modrinth search: `GET https://api.modrinth.com/v2/search?limit&offset&query&index&facets`
  (facets = JSON 2D array, e.g. `[["project_type:mod"],["versions:1.7.10"],["categories:fabric"]]`).
  Hit fields: `project_id, slug, title, description, author, project_type, categories, versions,
  downloads, follows, icon_url, gallery, color, date_created, date_modified, client_side,
  server_side, license, latest_version`. Requires a `User-Agent` header.
- Loader availability: Fabric/Quilt return `[]` for 1.7.10 (1.14+ only); NeoForge starts 20.x
  (MC 1.20.2+); Forge promotions cover 1.7.10 → latest.

### Hard prerequisites / honest limitations
- **Java**: only an Oracle `java8path` stub exists (`JAVA_HOME` empty). The launcher will **detect
  system Java** and otherwise **auto-download the correct Mojang JRE** by `javaVersion.component`.
- **Microsoft auth** requires an **Azure Application (client) ID** that cannot be embedded. Offline
  auth works out of the box; Microsoft device-code flow is fully implemented but needs a user-supplied
  Client ID (Settings). Documented clearly.
- **Modern Forge** install (installer "processors" pipeline) is the highest-risk loader; implemented
  best-effort behind the same `ILoaderProvider` interface. Vanilla + Fabric + Quilt + NeoForge are
  clean JSON-merge loaders and fully implemented.
- **Agent cannot visually verify the GUI** or complete a real multi-GB launch here (no display
  assertion, no MS account). Build success, process start, live-API fetch, download pipeline, and
  assembled launch command are verifiable; final in-game launch is user-verified.

## Architecture

**MVVM, zero external NuGet dependencies** (only WPF + BCL: `System.Net.Http`, `System.Text.Json`,
`System.IO.Compression`). Guarantees offline `dotnet build`. Hand-rolled MVVM primitives.

- Target: `net8.0-windows`, `<UseWPF>true</UseWPF>`, `<Nullable>enable</Nullable>`,
  `<ApplicationManifest>` for DPI/long-path awareness. Single project + solution.
- App data root: `%APPDATA%\.astraclient\` — shared `libraries/`, `assets/`, `versions/{id}/`,
  `cache/`, `instances/{name}/` (each with `mods/ resourcepacks/ shaderpacks/ datapacks/ saves/ config/`),
  plus `library.json`, `settings.json`, `profiles.json`.

### Project layout
```
AstraClient.sln
src/AstraClient/
  AstraClient.csproj
  App.xaml(.cs)                     startup + lightweight service container
  Themes/  Glass.xaml, Controls.xaml, Icons.xaml   (brushes, rounded styles, vector icons)
  Interop/ DwmInterop.cs (blur-behind + Win11 corner pref), WindowBehavior.cs (drag/resize/maximize)
  Shell/   MainWindow.xaml(.cs), TitleBar.xaml (logo+title left · account center/right · window btns far-right)
  Mvvm/    ObservableObject, RelayCommand, AsyncRelayCommand, ViewModelBase, INavigator, NavigationService
  Models/  VersionManifest, MinecraftVersion, VersionJson(+Arguments/Library/AssetIndex/JavaVersion),
           ModrinthHit, ModrinthProject, ModrinthVersionFile, LoaderInfo, Instance, InstalledContent,
           UserProfile, LaunchProfile(args), Settings
  Services/
    Http/    HttpClientProvider (shared client, User-Agent), Json (camelCase, lenient)
    Mojang/  MojangService (manifest, version json, asset index)
    Modrinth/ModrinthService (search, project, versions, tag/categories)
    Loaders/ ILoaderProvider + Vanilla, Fabric, Quilt, NeoForge, Forge + LoaderRegistry (availability per version)
    Launch/  LaunchService (download deps → natives → classpath → args → spawn + log), ArgumentBuilder,
             RulesEvaluator (OS/side rules), LibraryResolver (maven path → local path)
    Java/    JavaRuntimeService (detect system java by majorVersion; download Mojang runtime by component)
    Auth/    IAuthProvider + OfflineAuthProvider (works now), MicrosoftAuthProvider (device-code, needs Client ID)
    Content/ ContentInstaller (download Modrinth file → instance folder by type), InstanceService (CRUD, active instance)
    Data/    LibraryService, SettingsService, ProfileService, FavoritesService  (JSON persistence)
    Imaging/ ImageCacheService (disk+mem cache) + AsyncImage attached property (binds icon/gallery URLs)
  ViewModels/ ShellViewModel, HomeViewModel, BrowseViewModel, ProjectDetailViewModel,
              ManageViewModel, LaunchViewModel, SettingsViewModel
  Views/      HomeView, BrowseView, ProjectDetailView(flyout), ManageView, LaunchView, SettingsView
  Converters/ BoolToVisibility, DownloadsToCompact(1.2M), RelativeDate, NullToPlaceholder, EnumToLabel
  Assets/     astra-logo (vector/xaml), fonts
```

## Feature breakdown

### 1. Glass shell / window chrome (Win11 style, no neon)
- Borderless window: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`,
  root `Border CornerRadius=12` with layered translucent dark gradients + 1px light border →
  frosted glass + rounded corners. Real blur via `DwmEnableBlurBehindWindow` (guarded try/catch);
  attempt Win11 `DWMWA_SYSTEMBACKDROP_TYPE`/`DWMWA_WINDOW_CORNER_PREFERENCE` where supported, else
  degrade gracefully. Corner radius → 0 when maximized. Manual move (title bar DragMove) + 8 resize grips.
- **Top bar** (single row): far-left **logo + "Astra Client" title**; center/left **menu strip**
  (File · Edit · View · Help, glass-styled WPF `Menu`); right **user/account area** (avatar, name,
  Login/Logout, Offline vs Microsoft); far-right **window buttons** (minimize · maximize/restore ·
  close, rounded hover states).
- **Left sidebar**: nav (Home · Browse · Manage · Settings) with vector icons + active pill;
  Browse expands to type shortcuts (Mods / Resource Packs / Shaders / Datapacks). Content host =
  `ContentControl` with `DataTemplate` per ViewModel via `NavigationService`.

### 2. Home panel — version framework + launch
- **Hero**: active instance card — selected version, chosen loader, account, big **Play** button,
  progress bar (download/launch stages), status text.
- **Version framework**: every version from **1.7 → latest** (live from Mojang), grouped by release
  train (1.7, 1.8, … 1.21, 26.x). Filters: Releases / Snapshots / (Beta·Alpha). Search box.
  Selecting a version sets the **active version/instance** (drives Browse install routing + Play).
  Offline fallback seed of major versions so the grid always renders if the network is down.
- **Featured / news** strip + quick stats (library size, instances count).

### 3. Browse — live Modrinth (mods / textures / shaders / datapacks)
- Type tabs: **Mods · Resource Packs · Shaders · Datapacks** (→ `project_type` facet).
- **Search bar** (debounced) + **filters**: game version, loader/category (chips from
  `/v2/tag`), sort (Relevance · Downloads · Follows · Newest · Updated), pagination / load-more.
- Results as **glass cards** (async icon, title, author, downloads, follows, category tags, color accent).
- **Context interactions**: hover actions, right-click menu (Install · Open on Modrinth · Favorite ·
  Copy link), click → **detail flyout** (`/v2/project/{id}` + `/v2/project/{id}/version`): gallery,
  description, versions/files, license, client/server side, supported versions.
- **Install** → `ContentInstaller` downloads the correct file for the active version/loader into the
  **selected instance's folder by type** (`mods/` etc.) and records it in the library (per user request).

### 4. Manage — instances, loaders, args, library
- **Instances**: create/rename/delete; each bound to a version + loader + Java + memory + game dir.
- **Mod loader chooser** per instance: Vanilla/Forge/Fabric/Quilt/NeoForge — only loaders the meta
  API returns for that version are enabled (badge "recommended"); selecting one **auto-fills
  recommended JVM/game args**.
- **Game/JVM arguments** editor: auto-populated recommended presets (memory `-Xmx`, G1GC flags,
  loader-specific), fully editable; shows the resolved final command (dry-run/log).
- **Library**: installed content (mods/packs/shaders/datapacks) with enable/disable, remove, open folder.
- **Java runtime** panel: detected runtimes, required major version per game version, download button.

### 5. Auth / user area
- **Offline** (default, works now): username → offline UUID + token; launches immediately.
- **Microsoft** (device-code OAuth → Xbox Live → XSTS → Minecraft token → profile/UUID): fully
  implemented; requires user-supplied **Azure Client ID** in Settings (cannot embed one). Clear UI
  guidance + status. Account shown in top-bar user area.

### 6. Launch pipeline (LaunchService) — the core
1. Resolve version json (+ loader patch, `inheritsFrom` merge for Fabric/Quilt/NeoForge/Forge).
2. Download **client jar** → `versions/{id}/{id}.jar` (skip if sha1 matches).
3. Download **libraries** (apply Windows `rules`, resolve `downloads.artifact`/`classifiers`,
  handle `natives.windows`) → `libraries/` by maven path; verify sha1.
4. Download **asset index** → `assets/indexes/{id}.json`, then all **asset objects** →
  `assets/objects/{xx}/{hash}` (parallel, resumable, sha1-verified).
5. Extract **natives** to a temp dir.
6. Resolve **Java** via `JavaRuntimeService` (system java matching `majorVersion`, else download
   Mojang runtime by `component`).
7. Build **classpath** (libraries + client jar, natives excluded, correct order).
8. Build **arguments**: modern `arguments.{game,jvm}` with `RulesEvaluator` + placeholder
   substitution, **or** legacy `minecraftArguments` path for ≤1.12 (both supported).
9. **Auth** token/profile injection.
10. **Spawn** `java [jvmArgs] -cp <classpath> <mainClass> [gameArgs]`, working dir = instance game
    dir; stream stdout/stderr into an in-app **launch console**; report exit code.
Progress + cancellable; errors surfaced in UI with actionable messages.

### 7. Progress & status UX (soft rounded bar + logical messaging)
- **Reusable `SoftProgressBar`** style (`Themes/Controls.xaml`): rounded pill track (translucent glass),
  rounded animated fill with a subtle top-highlight gradient (no neon), smooth eased width transitions,
  optional indeterminate state (soft sliding sheen), and a compact percentage readout. Used in Home hero,
  Browse install buttons, Manage downloads, and the launch console.
- **Logical, accurate status messaging**: every long-running op reports granular progress via a shared
  `IProgress<OperationProgress>` (`Stage`, `Message`, `Current`, `Total`, `Percent`, `BytesPerSec`).
  Stages map to human-readable text bound next to the bar, e.g.:
  "Preparing 1.21.1 (Fabric)…" → "Downloading client jar…" → "Fetching libraries 45/97…" →
  "Downloading assets 1,204/3,915…" → "Extracting natives…" → "Resolving Java 21…" →
  "Building classpath & arguments…" → "Launching Minecraft…". Install shows "Installing Fabric API → mods/…".
  Counts/percent come from real download progress, so the bar and text always reflect actual state;
  errors replace the message with the cause + a Retry action. Marshalled to the UI thread.

## Key technical approaches
- **Glass/rounded/blur**: custom borderless WPF window + guarded DWM P/Invoke (see §1). All controls
  rounded via shared styles in `Themes/Controls.xaml`.
- **Async images**: `AsyncImage.Source` attached property → `ImageCacheService` downloads to
  `%APPDATA%\.astraclient\cache` + memory cache, sets `BitmapImage` on UI thread (frozen).
- **DI/navigation**: tiny hand-rolled container in `App.xaml.cs`; `NavigationService` swaps the
  content-host ViewModel; `DataTemplate` maps VM → View.
- **HTTP**: one `HttpClient` with `User-Agent: AstraClient/1.0`, timeouts, retry/backoff for
  Modrinth rate limits; all calls async, marshalled to UI via `IProgress`/dispatcher.
- **Persistence**: `JsonStore` helper (atomic write) for settings/library/profiles.
- **Rules evaluation**: shared evaluator for Mojang library/argument `rules` (os name/arch, features).

## Implementation order (phased, each compiles)
1. **Skeleton + build**: sln/csproj, App, hand-rolled MVVM, Themes (glass/controls/icons), DwmInterop,
   borderless rounded glass MainWindow with top bar (logo/title · menu strip · user area · window
   buttons) + sidebar + navigation host. **`dotnet build` green.**
2. **Services**: Http/Json, MojangService, ModrinthService, persistence, ImageCache/AsyncImage.
3. **Home**: live version framework (grouped, filtered, searchable) + active instance + hero/Play shell.
4. **Browse**: tabs, search, filters, sort, cards, detail flyout, context menu, Install → instance folder.
5. **Manage**: instances, loader chooser (dynamic availability), args editor + recommended presets,
   library, Java panel.
6. **Launch pipeline**: downloads (client/libraries/assets/natives), JavaRuntimeService (detect +
   Mojang JRE download), classpath/args (legacy+modern), Offline auth, process spawn + console.
   Loader providers: Vanilla → Fabric → Quilt → NeoForge → Forge(best-effort).
7. **Auth**: Microsoft device-code flow + Settings (Client ID, memory, java path, data dir, theme toggle).
8. **Polish**: converters, empty/loading/error states, favorites, offline fallback, accessibility.

## Critical files (to be created)
- `src/AstraClient/AstraClient.csproj`, `App.xaml(.cs)`
- `Interop/DwmInterop.cs`, `Interop/WindowBehavior.cs`, `Shell/MainWindow.xaml(.cs)`, `Shell/TitleBar.xaml`
- `Themes/Glass.xaml`, `Themes/Controls.xaml`, `Themes/Icons.xaml`
- `Services/Mojang/MojangService.cs`, `Services/Modrinth/ModrinthService.cs`
- `Services/Launch/LaunchService.cs`, `ArgumentBuilder.cs`, `RulesEvaluator.cs`, `LibraryResolver.cs`
- `Services/Loaders/*Loader.cs` + `LoaderRegistry.cs`, `Services/Java/JavaRuntimeService.cs`
- `Services/Auth/{Offline,Microsoft}AuthProvider.cs`, `Services/Content/ContentInstaller.cs`, `InstanceService.cs`
- `ViewModels/*`, `Views/*`, `Converters/*`

## Verification (end-to-end)
- **Build**: `dotnet build src/AstraClient/AstraClient.csproj -c Debug` succeeds, no warnings-as-errors.
- **Run**: `dotnet run` starts, window stays alive (no startup exception); logged to console.
- **Live data**: Home lists 1.7.x→latest from Mojang; Browse returns real Modrinth results per type,
  search/filter/sort/pagination work; detail flyout shows gallery/versions.
- **Install**: installing a mod for the active version places the real file in that instance's
  `mods/` (or `resourcepacks/shaderpacks/datapacks/`) and it appears in Manage → Library.
- **Launch dry-run**: with a version+loader selected, LaunchService downloads client/libraries/assets,
  resolves Java (or downloads Mojang JRE), and prints the exact `java ... <mainClass> ...` command;
  for Offline auth + a valid Java, the process spawns (agent verifies command assembly + downloads;
  full in-game launch is user-verified since it needs a display + MS account for online play).
- **Persistence**: instances/library/settings survive restart (`%APPDATA%\.astraclient\`).
