using System.Collections.ObjectModel;
using System.Windows;
using AstraClient.Models;
using AstraClient.Mvvm;
using AstraClient.Services.Auth;
using AstraClient.Services.Data;
using AstraClient.Services.Launch;
using AstraClient.Services.Mojang;

namespace AstraClient.ViewModels;

/// <summary>
/// Home page: version framework (grouped, filtered, searchable), active instance card and Play.
/// </summary>
public class HomeViewModel : ViewModelBase
{
    private readonly MojangService   _mojang;
    private readonly InstanceService _instances;
    private readonly LaunchService   _launch;
    private readonly ProfileService  _profiles;
    private readonly SettingsService _settings;

    public event Action<string>? LaunchFailed;

    // ---- Collections ---------------------------------------------------------

    public ObservableCollection<VersionGroupViewModel> VersionGroups { get; } = [];
    public ObservableCollection<Instance>              Instances     { get; } = [];

    // ---- Filter / search -----------------------------------------------------

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilters();
        }
    }

    private bool _showSnapshots;
    public bool ShowSnapshots
    {
        get => _showSnapshots;
        set { if (SetProperty(ref _showSnapshots, value)) ApplyFilters(); }
    }

    private bool _showOldVersions;
    public bool ShowOldVersions
    {
        get => _showOldVersions;
        set { if (SetProperty(ref _showOldVersions, value)) ApplyFilters(); }
    }

    // ---- Active instance / version -------------------------------------------

    private Instance? _activeInstance;
    public Instance? ActiveInstance
    {
        get => _activeInstance;
        set
        {
            if (SetProperty(ref _activeInstance, value))
            {
                OnPropertyChanged(nameof(HasActiveInstance));
                OnPropertyChanged(nameof(ActiveLoaderVisual));
                OnPropertyChanged(nameof(ActiveLoaderVersion));
                OnPropertyChanged(nameof(ActiveInstanceLabel));
                OnPropertyChanged(nameof(PlayLabel));
            }
        }
    }

    private VersionEntry? _selectedVersion;
    public VersionEntry? SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    public bool   HasActiveInstance => _activeInstance is not null;
    public LoaderVisual ActiveLoaderVisual => LoaderVisual.For(_activeInstance?.Loader);
    public string ActiveLoaderVersion => string.IsNullOrWhiteSpace(_activeInstance?.LoaderVersion)
        ? "Official runtime"
        : _activeInstance.LoaderVersion;
    public string ActiveInstanceLabel => _activeInstance is null
        ? "No instance — create one in Manage"
        : $"{_activeInstance.Name}  ·  {_activeInstance.GameVersion}  ·  {_activeInstance.Loader}";
    public string PlayLabel => IsLaunching ? "Launching…" : "▶  Play";

    // ---- Launch state --------------------------------------------------------

    private bool   _isLaunching;
    private double _launchProgress;
    private string _launchStatus = "";

    public bool IsLaunching
    {
        get => _isLaunching;
        set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                OnPropertyChanged(nameof(PlayLabel));
                PlayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double LaunchProgress
    {
        get => _launchProgress;
        set => SetProperty(ref _launchProgress, value);
    }

    public string LaunchStatus
    {
        get => _launchStatus;
        set => SetProperty(ref _launchStatus, value);
    }

    // ---- Commands ------------------------------------------------------------

    public AsyncRelayCommand PlayCommand { get; }
    public RelayCommand      RefreshCommand { get; }
    public RelayCommand      SelectVersionCommand { get; }

    // ---- Raw data (for filtering) -------------------------------------------
    private List<VersionEntry> _allVersions = [];

    // -------------------------------------------------------------------------

    public HomeViewModel(
        MojangService   mojang,
        InstanceService instances,
        LaunchService   launch,
        ProfileService  profiles,
        SettingsService settings)
    {
        _mojang    = mojang;
        _instances = instances;
        _launch    = launch;
        _profiles  = profiles;
        _settings  = settings;

        Title = "Home";

        PlayCommand          = new AsyncRelayCommand(PlayAsync, () => HasActiveInstance && !IsLaunching);
        RefreshCommand       = new RelayCommand(_ => _ = LoadVersionsAsync());
        SelectVersionCommand = new RelayCommand(p =>
        {
            if (p is VersionEntry ve) SelectedVersion = ve;
        });
    }

    public override void OnNavigatedTo()
    {
        ShowSnapshots = _settings.Current.ShowSnapshots;
        ShowOldVersions = _settings.Current.ShowOldVersions;

        // Refresh instance list on every visit.
        SyncInstances();

        // Load versions if not yet loaded.
        if (_allVersions.Count == 0)
            _ = LoadVersionsAsync();
    }

    // ---- Data loading -------------------------------------------------------

    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        LaunchStatus = "Fetching Mojang version manifest…";
        try
        {
            var manifest = await _mojang.GetManifestAsync(false).ConfigureAwait(false);
            _allVersions = manifest.Versions;
            await Application.Current.Dispatcher.InvokeAsync(ApplyFilters);
        }
        catch (Exception ex)
        {
            LaunchStatus = $"Failed to load versions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilters()
    {
        var filtered = _allVersions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
            filtered = filtered.Where(v => v.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        if (!_showSnapshots)
            filtered = filtered.Where(v => v.Type == "release" || v.Type == "old_alpha" || v.Type == "old_beta");

        if (!_showOldVersions)
            filtered = filtered.Where(v => v.Type != "old_alpha" && v.Type != "old_beta");

        // Group by major version train (1.21, 1.20, … 1.7, 26.x).
        var grouped = filtered
            .GroupBy(v => GetVersionGroup(v.Id))
            .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase);

        VersionGroups.Clear();
        foreach (var g in grouped)
            VersionGroups.Add(new VersionGroupViewModel(g.Key, g.ToList()));

        LaunchStatus = _allVersions.Count == 0 ? "Loading…" :
            $"{_allVersions.Count} versions available (1.7 → latest)";
    }

    private static string GetVersionGroup(string versionId)
    {
        // "1.21.4" → "1.21",  "26w09a" → "26.x",  "1.7.10" → "1.7"
        if (versionId.Length > 2 && char.IsDigit(versionId[0]) && !versionId.StartsWith("1."))
        {
            int wIdx = versionId.IndexOf('w', StringComparison.Ordinal);
            int sliceEnd = wIdx > 0 ? wIdx : 2;
            return versionId[..sliceEnd] + ".x";
        }

        var parts = versionId.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : versionId;
    }

    private void SyncInstances()
    {
        Instances.Clear();
        foreach (var inst in _instances.All)
            Instances.Add(inst);
        ActiveInstance = _instances.Active;
    }

    // ---- Play ---------------------------------------------------------------

    private CancellationTokenSource? _launchCts;

    private async Task PlayAsync()
    {
        if (_activeInstance is null) return;

        var profile = _profiles.Current;
        if (profile is null)
        {
            LaunchStatus = "No account — using offline mode (Steve).";
            profile = new UserProfile
            {
                Username = "Steve",
                Uuid = OfflineAuthProvider.GenerateOfflineUuid("Steve")
            };
        }

        _launchCts  = new CancellationTokenSource();
        IsLaunching = true;
        LaunchProgress = 0;
        LaunchStatus   = "Starting…";

        var progressReporter = new Progress<OperationProgress>(p =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LaunchStatus   = p.Message;
                LaunchProgress = p.Percent;
            });
        });

        try
        {
            await _launch.LaunchAsync(_activeInstance, profile, progressReporter, _launchCts.Token)
                         .ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LaunchStatus   = "Minecraft launched!";
                LaunchProgress = 100;
                if (_settings.Current.CloseAfterLaunch && Application.Current.MainWindow is { } window)
                    window.WindowState = WindowState.Minimized;
            });
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => LaunchStatus = "Launch cancelled.");
        }
        catch (Exception ex)
        {
            LaunchFailed?.Invoke(ex.Message);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LaunchStatus   = $"Launch failed: {ex.Message}";
                LaunchProgress = 0;
            });
        }
        finally
        {
            await Task.Delay(1000);
            await Application.Current.Dispatcher.InvokeAsync(() => IsLaunching = false);
        }
    }
}

/// <summary>Version group (e.g. "1.21") shown as a collapsible section in the version grid.</summary>
public class VersionGroupViewModel : ObservableObject
{
    public string             GroupKey { get; }
    public List<VersionEntry> Versions { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public VersionGroupViewModel(string key, List<VersionEntry> versions)
    {
        GroupKey = key;
        Versions = versions;
    }
}
