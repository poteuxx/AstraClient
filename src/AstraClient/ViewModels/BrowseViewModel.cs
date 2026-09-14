using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using AstraClient.Models;
using AstraClient.Mvvm;
using AstraClient.Services.Content;
using AstraClient.Services.Data;
using AstraClient.Services.Imaging;
using AstraClient.Services.Modrinth;

namespace AstraClient.ViewModels;

/// <summary>
/// Browse page: live Modrinth discovery for mods, modpacks, plugins, resource packs, shaders and datapacks.
/// Supports search, filters (version, loader, category, environment), sort, install and modpack → instance.
/// </summary>
public class BrowseViewModel : ViewModelBase
{
    private readonly ModrinthService    _modrinth;
    private readonly InstanceService    _instances;
    private readonly ContentInstaller  _installer;
    private readonly ModpackInstaller  _modpacks;
    private readonly ImageCacheService _imageCache;

    // ---- Tab selection -------------------------------------------------------

    private string _selectedType = "mod";
    public string SelectedType
    {
        get => _selectedType;
        set
        {
            if (SetProperty(ref _selectedType, value))
            {
                RaisePropertyChanged(nameof(ShowLoaderFilter), nameof(IsModpackType),
                    nameof(InstallActionLabel), nameof(InstallFooterLabel));
                _ = LoadCategoriesAsync();
                _ = SearchAsync(reset: true);
            }
        }
    }

    public bool ShowLoaderFilter => _selectedType is "mod" or "modpack" or "plugin";
    public bool IsModpackType => _selectedType == "modpack";
    public string InstallActionLabel => IsModpackType ? "Create instance" : "Install";
    public string InstallFooterLabel => IsModpackType
        ? "Creates a new instance from this modpack"
        : "Installing to active instance";

    // ---- Search / filter state ----------------------------------------------

    private string _query = "";
    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }

    private string? _selectedGameVersion;
    public string? SelectedGameVersion
    {
        get => _selectedGameVersion;
        set { if (SetProperty(ref _selectedGameVersion, value)) _ = SearchAsync(reset: true); }
    }

    private string? _selectedLoader;
    public string? SelectedLoader
    {
        get => _selectedLoader;
        set { if (SetProperty(ref _selectedLoader, value)) _ = SearchAsync(reset: true); }
    }

    /// <summary>Recent release versions from Modrinth; powers the version filter ComboBox.</summary>
    public ObservableCollection<string> GameVersions { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    private string? _selectedCategory;
    public string? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) _ = SearchAsync(reset: true); }
    }

    private string? _selectedEnvironment;
    public string? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set { if (SetProperty(ref _selectedEnvironment, value)) _ = SearchAsync(reset: true); }
    }

    private string _sort = "relevance";
    public string Sort
    {
        get => _sort;
        set { if (SetProperty(ref _sort, value)) _ = SearchAsync(reset: true); }
    }

    // ---- Results -------------------------------------------------------------

    public ObservableCollection<ModrinthHitViewModel> Results { get; } = [];

    private int _totalHits;
    public int TotalHits
    {
        get => _totalHits;
        set
        {
            SetProperty(ref _totalHits, value);
            OnPropertyChanged(nameof(HasMore));
        }
    }

    public bool HasMore => Results.Count < _totalHits;

    // ---- Detail flyout -------------------------------------------------------

    private ModrinthHitViewModel? _selectedHit;
    public ModrinthHitViewModel? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                RaisePropertyChanged(nameof(IsDetailOpen), nameof(ShareLinkPreview), nameof(ShareCodePreview));
                OpenOnModrinthCommand.RaiseCanExecuteChanged();
                CopyShareLinkCommand.RaiseCanExecuteChanged();
                CopyShareCodeCommand.RaiseCanExecuteChanged();
                if (value is not null) _ = LoadDetailAsync(value);
            }
        }
    }

    public bool IsDetailOpen => _selectedHit is not null;

    private string _detailBody = "";
    public string DetailBody
    {
        get => _detailBody;
        set => SetProperty(ref _detailBody, value);
    }

    private List<ModrinthVersion> _detailVersions = [];
    public List<ModrinthVersion> DetailVersions
    {
        get => _detailVersions;
        set => SetProperty(ref _detailVersions, value);
    }

    private List<string> _detailGallery = [];
    public List<string> DetailGallery
    {
        get => _detailGallery;
        set => SetProperty(ref _detailGallery, value);
    }

    private ModrinthVersion? _selectedDetailVersion;
    public ModrinthVersion? SelectedDetailVersion
    {
        get => _selectedDetailVersion;
        set
        {
            if (SetProperty(ref _selectedDetailVersion, value))
                RaisePropertyChanged(nameof(ShareLinkPreview), nameof(ShareCodePreview));
        }
    }

    private string _modpackImportLink = "";
    public string ModpackImportLink
    {
        get => _modpackImportLink;
        set
        {
            if (SetProperty(ref _modpackImportLink, value))
                ImportModpackLinkCommand.RaiseCanExecuteChanged();
        }
    }

    public string ShareLinkPreview => BuildShareLink();
    public string ShareCodePreview => BuildShareCode();

    // ---- Status text ---------------------------------------------------------

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ---- Commands ------------------------------------------------------------

    public AsyncRelayCommand SearchCommand    { get; }
    public AsyncRelayCommand LoadMoreCommand  { get; }
    public RelayCommand      SelectHitCommand { get; }
    public RelayCommand      CloseDetailCommand { get; }
    public AsyncRelayCommand InstallCommand            { get; }
    public RelayCommand      OpenOnModrinthCommand     { get; }
    public AsyncRelayCommand ImportModpackFileCommand  { get; }
    public AsyncRelayCommand ImportModpackLinkCommand  { get; }
    public RelayCommand      CopyShareLinkCommand      { get; }
    public RelayCommand      CopyShareCodeCommand      { get; }

    // ---- Pagination ---------------------------------------------------------

    private int _offset;
    private const int PageSize = 20;

    // -------------------------------------------------------------------------

    public BrowseViewModel(
        ModrinthService    modrinth,
        InstanceService    instances,
        ContentInstaller  installer,
        ModpackInstaller  modpacks,
        ImageCacheService imageCache)
    {
        _modrinth   = modrinth;
        _instances  = instances;
        _installer  = installer;
        _modpacks   = modpacks;
        _imageCache = imageCache;

        Title = "Browse";

        SearchCommand    = new AsyncRelayCommand(_ => SearchAsync(reset: true));
        LoadMoreCommand  = new AsyncRelayCommand(_ => SearchAsync(reset: false), _ => HasMore && !IsBusy);
        SelectHitCommand = new RelayCommand(p =>
        {
            if (p is ModrinthHitViewModel vm) SelectedHit = vm;
        });
        CloseDetailCommand = new RelayCommand(_ => SelectedHit = null);
        InstallCommand            = new AsyncRelayCommand(_ => InstallSelectedAsync());
        OpenOnModrinthCommand     = new RelayCommand(_ => OpenOnModrinth(), _ => _selectedHit is not null);
        ImportModpackFileCommand  = new AsyncRelayCommand(_ => ImportModpackFromFileAsync());
        ImportModpackLinkCommand  = new AsyncRelayCommand(_ => ImportModpackFromLinkAsync(), _ => !string.IsNullOrWhiteSpace(_modpackImportLink));
        CopyShareLinkCommand      = new RelayCommand(_ => CopyShareLink(), _ => _selectedHit is not null);
        CopyShareCodeCommand      = new RelayCommand(_ => CopyShareCode(), _ => _selectedHit is not null);
    }

    public override void OnNavigatedTo()
    {
        if (GameVersions.Count == 0 || GameVersions.All(string.IsNullOrWhiteSpace))
            _ = LoadGameVersionsAsync();
        if (Categories.Count == 0)
            _ = LoadCategoriesAsync();
        if (Results.Count == 0)
            _ = SearchAsync(reset: true);
    }

    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var versions = await _modrinth.GetGameVersionTagsAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                GameVersions.Clear();
                GameVersions.Add("");
                foreach (var version in versions)
                    GameVersions.Add(version);
            });
        }
        catch
        {
            // Filter still works when typed manually; dropdown just stays empty.
        }
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var categories = await _modrinth.GetCategoryTagsAsync(_selectedType).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Categories.Clear();
                Categories.Add("");
                foreach (var category in categories)
                    Categories.Add(category);
                if (_selectedCategory is not null && !Categories.Contains(_selectedCategory))
                    SelectedCategory = null;
            });
        }
        catch
        {
            // Category filter optional.
        }
    }

    // ---- Search -------------------------------------------------------------

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;

    private async Task SearchAsync(bool reset)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (reset)
        {
            _offset = 0;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                TotalHits = 0;
            });
        }

        IsBusy = true;
        StatusText = "Searching…";
        try
        {
            var result = await _modrinth.SearchAsync(
                query:       _query,
                projectType: _selectedType,
                gameVersion: _selectedGameVersion,
                loader:      ShowLoaderFilter ? _selectedLoader : null,
                category:    _selectedCategory,
                environment: _selectedEnvironment,
                sort:        _sort,
                limit:       PageSize,
                offset:      _offset,
                ct:          ct).ConfigureAwait(false);

            if (result is null)
            {
                StatusText = "No results.";
                return;
            }

            ct.ThrowIfCancellationRequested();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TotalHits = result.TotalHits;
                foreach (var hit in result.Hits)
                    Results.Add(new ModrinthHitViewModel(hit));
                _offset += result.Hits.Count;
                StatusText = TotalHits == 0
                    ? "No results found."
                    : $"{TotalHits:N0} results";
                LoadMoreCommand.RaiseCanExecuteChanged();
            });

            // Kick off icon loading (fire-and-forget per item).
            foreach (var vm in Results.Skip(_offset - result.Hits.Count))
            {
                _ = LoadIconAsync(vm, ct);
            }
        }
        catch (OperationCanceledException) { /* silently swallow search cancel */ }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                StatusText = $"Search failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task LoadIconAsync(ModrinthHitViewModel vm, CancellationToken ct)
    {
        try
        {
            var bitmap = await _imageCache.GetBitmapAsync(vm.Hit.IconUrl, ct).ConfigureAwait(false);
            if (!ct.IsCancellationRequested)
                await Application.Current.Dispatcher.InvokeAsync(() => vm.Icon = bitmap);
        }
        catch (OperationCanceledException) { }
    }

    // ---- Detail -------------------------------------------------------------

    private async Task LoadDetailAsync(ModrinthHitViewModel vm)
    {
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        var ct = _detailCts.Token;

        DetailBody      = "Loading…";
        DetailVersions  = [];
        DetailGallery   = [];

        try
        {
            var project  = await _modrinth.GetProjectAsync(vm.Hit.ProjectId, ct).ConfigureAwait(false);
            var versions = await _modrinth.GetVersionsAsync(
                vm.Hit.ProjectId,
                _selectedGameVersion,
                _selectedLoader,
                ct).ConfigureAwait(false);

            if (vm.Icon is null && !string.IsNullOrWhiteSpace(project?.IconUrl))
            {
                var fallbackIcon = await _imageCache.GetBitmapAsync(project.IconUrl, ct).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() => vm.Icon = fallbackIcon);
            }

            ct.ThrowIfCancellationRequested();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(SelectedHit, vm)) return;
                DetailBody     = project?.Body ?? vm.Hit.Description;
                DetailVersions = versions;
                DetailGallery  = project?.Gallery
                    .Select(image => image.Url)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? [];
                SelectedDetailVersion = versions.FirstOrDefault();
                RaisePropertyChanged(nameof(ShareLinkPreview), nameof(ShareCodePreview));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(SelectedHit, vm))
                    DetailBody = $"Failed to load details: {ex.Message}";
            });
        }
    }

    // ---- Install ------------------------------------------------------------

    private void OpenOnModrinth()
    {
        if (_selectedHit is null) return;
        Process.Start(new ProcessStartInfo(BuildShareLink()) { UseShellExecute = true });
    }

    private string BuildShareLink()
    {
        if (_selectedHit is null) return "";
        return ModrinthLinkParser.ToShareUrl(
            _selectedHit.Hit.ProjectType,
            _selectedHit.Hit.Slug,
            _selectedDetailVersion?.Id);
    }

    private string BuildShareCode()
    {
        if (_selectedHit is null) return "";
        return ModrinthLinkParser.ToShareCode(
            _selectedHit.Hit.ProjectType,
            _selectedHit.Hit.Slug,
            _selectedDetailVersion?.Id);
    }

    private void CopyShareLink()
    {
        if (_selectedHit is null) return;
        Clipboard.SetText(BuildShareLink());
        StatusText = "Share link copied to clipboard.";
    }

    private void CopyShareCode()
    {
        if (_selectedHit is null) return;
        Clipboard.SetText(BuildShareCode());
        StatusText = "Share code copied (paste to import on another device).";
    }

    private async Task ImportModpackFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Modpack",
            Filter = "Modrinth Pack (*.mrpack)|*.mrpack|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg =>
                Application.Current.Dispatcher.InvokeAsync(() => StatusText = msg));
            await _modpacks.InstallFromFileAsync(dialog.FileName, progress).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportModpackFromLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(_modpackImportLink)) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg =>
                Application.Current.Dispatcher.InvokeAsync(() => StatusText = msg));
            await _modpacks.InstallFromShareLinkAsync(_modpackImportLink.Trim(), progress).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (_selectedHit is null || _selectedDetailVersion is null) return;

        StatusText = IsModpackType
            ? $"Creating instance from {_selectedHit.Hit.Title}…"
            : $"Installing {_selectedHit.Hit.Title}…";
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg =>
                Application.Current.Dispatcher.InvokeAsync(() => StatusText = msg));

            if (IsModpackType)
            {
                var instance = await _modpacks.InstallAsync(
                    _selectedHit.Hit,
                    _selectedDetailVersion,
                    progress).ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = instance is not null
                        ? $"Modpack ready — {instance.Name} is active ✓"
                        : "Modpack install failed — check the status message.";
                });
                return;
            }

            var active = _instances.Active;
            if (active is null)
            {
                StatusText = "No active instance — create one in Manage first.";
                return;
            }

            var installed = await _installer.InstallAsync(
                _selectedHit.Hit,
                _selectedDetailVersion,
                active,
                progress).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (installed is not null)
                {
                    _selectedHit.IsInstalled = true;
                    StatusText = $"Installed {_selectedHit.Hit.Title} ✓";
                }
                else
                {
                    StatusText = "Install failed — check the log.";
                }
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                StatusText = $"Install error: {ex.Message}");
        }
        finally
        {
            await Task.Delay(1000);
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }
}

/// <summary>Wraps a ModrinthHit with UI-bindable state (icon, installed flag).</summary>
public class ModrinthHitViewModel : ObservableObject
{
    public ModrinthHit Hit { get; }

    private System.Windows.Media.Imaging.BitmapImage? _icon;
    public System.Windows.Media.Imaging.BitmapImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetProperty(ref _isInstalling, value);
    }

    public string DownloadsLabel => Hit.Downloads >= 1_000_000
        ? $"{Hit.Downloads / 1_000_000.0:0.#}M"
        : Hit.Downloads >= 1000 ? $"{Hit.Downloads / 1000.0:0.#}K" : Hit.Downloads.ToString();

    public string CategoriesLabel => string.Join(" · ", Hit.Categories.Take(3));

    public ModrinthHitViewModel(ModrinthHit hit) => Hit = hit;
}
