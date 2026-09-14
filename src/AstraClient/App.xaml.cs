using System.IO;
using System.Windows;
using AstraClient.Mvvm;
using AstraClient.Services.Auth;
using AstraClient.Services.Data;
using AstraClient.Services.Http;
using AstraClient.Services.Imaging;
using AstraClient.Services.Mojang;
using AstraClient.Services.Modrinth;
using AstraClient.Services.Content;
using AstraClient.Services.Java;
using AstraClient.Services.Launch;
using AstraClient.Services.Loaders;
using AstraClient.Shell;
using AstraClient.ViewModels;

namespace AstraClient;

public partial class App : Application
{
    // ---- App-wide data root ----
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".astraclient");

    public static ServiceContainer Services { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure data directories exist.
        EnsureDataDirectories();

        // Register infrastructure.
        var http = new HttpClientProvider();
        Services.RegisterInstance(http);
        Services.RegisterInstance(new JsonHelper());

        // Persistence.
        var settings  = new SettingsService(DataRoot);
        var profiles  = new ProfileService(DataRoot);
        var instances = new InstanceService(DataRoot);
        var library   = new LibraryService(DataRoot);
        
        await settings.LoadAsync();
        await profiles.LoadAsync();
        await instances.LoadAsync();
        
        Services.RegisterInstance(settings);
        Services.RegisterInstance(profiles);
        Services.RegisterInstance(instances);
        Services.RegisterInstance(library);

        // Imaging.
        var imageCache = new ImageCacheService(Path.Combine(DataRoot, "cache", "images"), http.Client);
        Services.RegisterInstance(imageCache);

        // API services.
        var mojang   = new MojangService(http);
        var modrinth = new ModrinthService(http);
        Services.RegisterInstance(mojang);
        Services.RegisterInstance(modrinth);

        // Loader registry.
        var loaderRegistry = new LoaderRegistry(http);
        Services.RegisterInstance(loaderRegistry);

        // Auth providers.
        var offlineAuth = new OfflineAuthProvider();
        var msAuth      = new MicrosoftAuthProvider(http, settings);
        Services.RegisterInstance<IAuthProvider>(offlineAuth);
        Services.RegisterInstance(offlineAuth);
        Services.RegisterInstance(msAuth);

        // Launch services.
        var javaRuntime  = new JavaRuntimeService(http, DataRoot);
        var launchService = new LaunchService(http, mojang, javaRuntime, library, instances, loaderRegistry, DataRoot);
        Services.RegisterInstance(javaRuntime);
        Services.RegisterInstance(launchService);
        // Content installer + modpack import/export.
        var installer = new ContentInstaller(http, instances, library);
        var modpackImport = new ModpackInstaller(http, instances, modrinth);
        var modpackExport = new ModpackExporter();
        Services.RegisterInstance(installer);
        Services.RegisterInstance(modpackImport);
        Services.RegisterInstance(modpackExport);

        // Navigation + shell.
        var nav  = new NavigationService(Services);
        Services.RegisterInstance<INavigationService>(nav);
        Services.RegisterInstance(nav);

        // Page ViewModels (lazy factories so they're singletons on first access).
        Services.RegisterFactory(() => new HomeViewModel(mojang, instances, launchService, profiles, settings));
        Services.RegisterFactory(() => new BrowseViewModel(modrinth, instances, installer, modpackImport, imageCache));
        Services.RegisterFactory(() => new ManageViewModel(instances, library, loaderRegistry, javaRuntime, launchService, modpackExport, modpackImport, settings));
        Services.RegisterFactory(() => new SettingsViewModel(settings, profiles, msAuth, offlineAuth, instances, Services.Get<ShellViewModel>()));
        Services.RegisterFactory(() => new AternosViewModel());
        Services.RegisterFactory(() => new OutputConsoleViewModel(instances, launchService, DataRoot));

        var shell = new ShellViewModel(nav);
        shell.CurrentUser = profiles.Current;
        Services.RegisterInstance(shell);
        // Subscribe before the first launch so crash output is captured live.
        _ = Services.Get<OutputConsoleViewModel>();

        var window = new MainWindow(shell, profiles, settings);
        window.Show();

        // Unhandled exception safety net — log to console without crashing.
        DispatcherUnhandledException += (_, ex) =>
        {
            Console.Error.WriteLine($"[UNHANDLED UI] {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Console.Error.WriteLine($"[UNHANDLED CLR] {ex.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Console.Error.WriteLine($"[UNOBSERVED TASK] {ex.Exception}");
            ex.SetObserved();
        };
    }

    private static void EnsureDataDirectories()
    {
        foreach (var dir in new[]
        {
            DataRoot,
            Path.Combine(DataRoot, "versions"),
            Path.Combine(DataRoot, "libraries"),
            Path.Combine(DataRoot, "assets", "indexes"),
            Path.Combine(DataRoot, "assets", "objects"),
            Path.Combine(DataRoot, "instances"),
            Path.Combine(DataRoot, "servers"),
            Path.Combine(DataRoot, "cache", "images"),
            Path.Combine(DataRoot, "runtimes"),
        })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
