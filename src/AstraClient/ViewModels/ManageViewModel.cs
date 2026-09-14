using System.Collections.ObjectModel;
using System.Windows;
using AstraClient.Models;
using AstraClient.Mvvm;
using AstraClient.Services.Content;
using AstraClient.Services.Data;
using AstraClient.Services.Java;
using AstraClient.Services.Launch;
using AstraClient.Services.Loaders;
using Microsoft.Win32;

namespace AstraClient.ViewModels;

/// <summary>
/// Manage page: instances CRUD, mod-loader chooser (dynamic per version),
/// JVM/game argument editor, library view, and Java runtime panel.
/// </summary>
public class ManageViewModel : ViewModelBase
{
    private readonly InstanceService _instances;
    private readonly LibraryService  _library;
    private readonly LoaderRegistry  _loaderRegistry;
    private readonly JavaRuntimeService _java;
    private readonly LaunchService   _launch;
    private readonly ModpackExporter _modpackExport;
    private readonly ModpackInstaller _modpackImport;
    private readonly SettingsService _settings;

    // ---- Instances list ------------------------------------------------------

    public ObservableCollection<Instance>  Instances { get; } = [];
    public ObservableCollection<InstalledContent> Library { get; } = [];
    public ObservableCollection<LoaderOptionViewModel> LoaderOptions { get; } = [];

    // ---- Selected instance ---------------------------------------------------

    private Instance? _selectedInstance;
    public Instance? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(InstanceName));
                OnPropertyChanged(nameof(InstanceVersion));
                OnPropertyChanged(nameof(InstanceMemory));
                OnPropertyChanged(nameof(InstanceJvmArgs));
                OnPropertyChanged(nameof(InstanceGameArgs));
                OnPropertyChanged(nameof(InstanceJavaOverride));
                if (value is not null)
                {
                    _ = LoadLoadersAsync(value.GameVersion);
                    _ = LoadLibraryAsync(value.Id);
                }
                else
                {
                    Library.Clear();
                    LoaderOptions.Clear();
                }
            }
        }
    }

    public bool HasSelection => _selectedInstance is not null;

    // ---- Active instance tracking -------------------------------------------

    private string? _activeId;
    public string? ActiveInstanceId
    {
        get => _activeId;
        private set => SetProperty(ref _activeId, value);
    }

    public bool IsActiveInstance(string id) => id == _activeId;

    // ---- Instance field proxies (two-way bound to edit fields) ---------------

    public string InstanceName
    {
        get => _selectedInstance?.Name ?? "";
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.Name = value;
            OnPropertyChanged();
        }
    }

    public string InstanceVersion
    {
        get => _selectedInstance?.GameVersion ?? "";
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.GameVersion = value;
            OnPropertyChanged();
            _ = LoadLoadersAsync(value);
        }
    }

    public int InstanceMemory
    {
        get => _selectedInstance?.MemoryMb ?? 2048;
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.MemoryMb = value;
            OnPropertyChanged();
        }
    }

    public string InstanceJvmArgs
    {
        get => _selectedInstance?.JvmArgs ?? "";
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.JvmArgs = value;
            OnPropertyChanged();
        }
    }

    public string InstanceGameArgs
    {
        get => _selectedInstance?.GameArgs ?? "";
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.GameArgs = value;
            OnPropertyChanged();
        }
    }

    public string InstanceJavaOverride
    {
        get => _selectedInstance?.JavaOverride ?? "";
        set
        {
            if (_selectedInstance is null) return;
            _selectedInstance.JavaOverride = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    // ---- Loader selection ---------------------------------------------------

    private LoaderOptionViewModel? _selectedLoader;
    public LoaderOptionViewModel? SelectedLoader
    {
        get => _selectedLoader;
        set
        {
            if (SetProperty(ref _selectedLoader, value) && value is not null && _selectedInstance is not null)
            {
                foreach (var opt in LoaderOptions)
                    opt.IsSelected = (opt == value);

                var avail = value.Availability;
                _selectedInstance.Loader = avail.LoaderName;
                if (value.SelectedVersion != null)
                {
                    _selectedInstance.LoaderVersion = value.SelectedVersion.Version;
                }
                else
                {
                    var lo = avail.Latest ?? avail.Recommended ?? avail.AllVersions.FirstOrDefault();
                    _selectedInstance.LoaderVersion = lo?.Version ?? "";
                }

                // Auto-fill recommended JVM args.
                if (string.IsNullOrWhiteSpace(_selectedInstance.JvmArgs))
                {
                    _selectedInstance.JvmArgs = avail.Provider.RecommendedJvmArgs(_selectedInstance.MemoryMb);
                    OnPropertyChanged(nameof(InstanceJvmArgs));
                }
            }
        }
    }

    // ---- Status --------------------------------------------------------------

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    // ---- New instance fields -------------------------------------------------

    private string _newName    = "My Instance";
    private string _newVersion = "1.21.1";
    private bool _useSavedMemory = true;

    public string NewName    { get => _newName;    set => SetProperty(ref _newName, value); }
    public string NewVersion { get => _newVersion; set => SetProperty(ref _newVersion, value); }
    public bool UseSavedMemory { get => _useSavedMemory; set => SetProperty(ref _useSavedMemory, value); }

    // ---- Commands ------------------------------------------------------------

    public AsyncRelayCommand CreateInstanceCommand  { get; }
    public AsyncRelayCommand DeleteInstanceCommand  { get; }
    public AsyncRelayCommand SaveInstanceCommand    { get; }
    public AsyncRelayCommand SetActiveCommand       { get; }
    public AsyncRelayCommand RemoveContentCommand   { get; }
    public RelayCommand      SelectLoaderCommand    { get; }
    public RelayCommand      OpenInstanceFolderCommand { get; }
    public AsyncRelayCommand FillRecommendedArgsCommand { get; }
    public AsyncRelayCommand ExportModpackCommand { get; }
    public AsyncRelayCommand ImportModpackFileCommand { get; }

    // -------------------------------------------------------------------------

    public ManageViewModel(
        InstanceService     instances,
        LibraryService      library,
        LoaderRegistry      loaderRegistry,
        JavaRuntimeService  java,
        LaunchService       launch,
        ModpackExporter     modpackExport,
        ModpackInstaller    modpackImport,
        SettingsService     settings)
    {
        _instances      = instances;
        _library        = library;
        _loaderRegistry = loaderRegistry;
        _java           = java;
        _launch         = launch;
        _modpackExport  = modpackExport;
        _modpackImport  = modpackImport;
        _settings       = settings;

        Title = "Manage";

        CreateInstanceCommand = new AsyncRelayCommand(_ => CreateInstanceAsync());
        DeleteInstanceCommand = new AsyncRelayCommand(_ => DeleteInstanceAsync(),
            _ => _selectedInstance is not null);
        SaveInstanceCommand   = new AsyncRelayCommand(_ => SaveInstanceAsync(),
            _ => _selectedInstance is not null);
        SetActiveCommand      = new AsyncRelayCommand(_ => SetActiveAsync(),
            _ => _selectedInstance is not null);
        RemoveContentCommand  = new AsyncRelayCommand(p => RemoveContentAsync(p as InstalledContent));
        SelectLoaderCommand   = new RelayCommand(p => SelectedLoader = p as LoaderOptionViewModel);
        OpenInstanceFolderCommand = new RelayCommand(_ =>
        {
            if (_selectedInstance is null) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", _selectedInstance.GameDir)
                { UseShellExecute = true });
            }
            catch { }
        });
        FillRecommendedArgsCommand = new AsyncRelayCommand(_ => FillRecommendedArgsAsync(),
            _ => _selectedInstance is not null);
        ExportModpackCommand = new AsyncRelayCommand(_ => ExportModpackAsync(), _ => _selectedInstance is not null);
        ImportModpackFileCommand = new AsyncRelayCommand(_ => ImportModpackFromFileAsync());
    }

    private async Task ExportModpackAsync()
    {
        if (_selectedInstance is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export Modpack",
            Filter = "Modrinth Pack (*.mrpack)|*.mrpack",
            FileName = $"{_selectedInstance.Name}.mrpack",
            DefaultExt = ".mrpack"
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => Application.Current.Dispatcher.InvokeAsync(() => Status = msg));
            await _modpackExport.ExportAsync(_selectedInstance, dialog.FileName, status: progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
            var progress = new Progress<string>(msg => Application.Current.Dispatcher.InvokeAsync(() => Status = msg));
            var instance = await _modpackImport.InstallFromFileAsync(dialog.FileName, progress).ConfigureAwait(false);
            if (instance is not null)
            {
                SyncInstances();
                SelectedInstance = Instances.FirstOrDefault(i => i.Id == instance.Id) ?? instance;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override void OnNavigatedTo()
    {
        SyncInstances();
    }

    // ---- Data loading -------------------------------------------------------

    private void SyncInstances()
    {
        Instances.Clear();
        foreach (var inst in _instances.All)
            Instances.Add(inst);
        ActiveInstanceId = _instances.ActiveId;
        SelectedInstance ??= _instances.Active;
    }

    private async Task LoadLoadersAsync(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return;
        IsBusy = true;
        Status = $"Checking loader availability for {mcVersion}…";
        try
        {
            var avail = await _loaderRegistry.GetAvailabilityAsync(mcVersion).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LoaderOptions.Clear();
                foreach (var a in avail)
                {
                    var currentVer = _selectedInstance?.Loader == a.LoaderName ? _selectedInstance?.LoaderVersion : null;
                    var vm = new LoaderOptionViewModel(a, currentVer);
                    vm.OnVersionChanged += (s, e) =>
                    {
                        if (vm.IsSelected && _selectedInstance is not null && vm.SelectedVersion is not null)
                        {
                            _selectedInstance.LoaderVersion = vm.SelectedVersion.Version;
                        }
                    };
                    LoaderOptions.Add(vm);
                }

                // Pre-select the loader that matches the current instance.
                SelectedLoader = _selectedInstance is null
                    ? null
                    : LoaderOptions.FirstOrDefault(lo =>
                        lo.LoaderName.Equals(_selectedInstance.Loader, StringComparison.OrdinalIgnoreCase));
                OnPropertyChanged(nameof(SelectedLoader));

                Status = $"Loaders loaded for {mcVersion}.";
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Loader check failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task LoadLibraryAsync(string instanceId)
    {
        var items = await _library.GetForInstanceAsync(instanceId).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Library.Clear();
            foreach (var item in items)
                Library.Add(item);
        });
    }

    // ---- CRUD ---------------------------------------------------------------

    private async Task CreateInstanceAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName) || string.IsNullOrWhiteSpace(_newVersion))
        {
            Status = "Enter a name and version first.";
            return;
        }
        IsBusy = true;
        try
        {
            var inst = await _instances.CreateAsync(_newName, _newVersion).ConfigureAwait(false);
            if (UseSavedMemory)
            {
                inst.MemoryMb = _settings.Current.DefaultMemoryMb;
                await _instances.UpdateAsync(inst).ConfigureAwait(false);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Instances.Add(inst);
                SelectedInstance = inst;
                Status = $"Created instance \"{inst.Name}\".";
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Create failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task DeleteInstanceAsync()
    {
        if (_selectedInstance is null) return;
        if (_settings.Current.ConfirmDestructiveActions)
        {
            var answer = MessageBox.Show(
                $"Delete \"{_selectedInstance.Name}\" and all of its files? This cannot be undone.",
                "Delete instance", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        var id = _selectedInstance.Id;
        await _instances.DeleteAsync(id).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var vm = Instances.FirstOrDefault(i => i.Id == id);
            if (vm is not null) Instances.Remove(vm);
            SelectedInstance = Instances.FirstOrDefault();
            Status = "Instance deleted.";
        });
    }

    private async Task SaveInstanceAsync()
    {
        if (_selectedInstance is null) return;
        await _instances.UpdateAsync(_selectedInstance).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() => Status = "Instance saved.");
    }

    private async Task SetActiveAsync()
    {
        if (_selectedInstance is null) return;
        _instances.ActiveId = _selectedInstance.Id;
        ActiveInstanceId = _selectedInstance.Id;
        await Application.Current.Dispatcher.InvokeAsync(() => Status = $"\"{_selectedInstance.Name}\" is now active.");
    }

    private async Task RemoveContentAsync(InstalledContent? content)
    {
        if (content is null || _selectedInstance is null) return;
        await _library.RemoveAsync(content.InstanceId, content.Id).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Library.Remove(content);
            Status = $"Removed {content.Name}.";
        });
    }

    private async Task FillRecommendedArgsAsync()
    {
        if (_selectedInstance is null) return;
        var loader = _loaderRegistry.Get(_selectedInstance.Loader) ??
                     _loaderRegistry.All.First();
        InstanceJvmArgs  = loader.RecommendedJvmArgs(_selectedInstance.MemoryMb);
        InstanceGameArgs = loader.RecommendedGameArgs();
        await SaveInstanceAsync();
        Status = "Recommended args applied.";
    }
}

public class LoaderOptionViewModel : ObservableObject
{
    public LoaderAvailability Availability { get; }
    public string LoaderName => Availability.LoaderName;
    public LoaderVisual Visual => LoaderVisual.For(LoaderName);
    public LoaderVersion? Recommended => Availability.Recommended;
    public bool IsAvailable => Availability.IsSupported;
    public IReadOnlyList<LoaderVersion> AllVersions => Availability.AllVersions;
    
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    private LoaderVersion? _selectedVersion;
    public LoaderVersion? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnVersionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    
    public event EventHandler? OnVersionChanged;

    public LoaderOptionViewModel(LoaderAvailability availability, string? currentVersion)
    {
        Availability = availability;
        _selectedVersion = availability.AllVersions.FirstOrDefault(v => v.Version == currentVersion)
                           ?? availability.Recommended
                           ?? availability.Latest
                           ?? availability.AllVersions.FirstOrDefault();
    }
}
