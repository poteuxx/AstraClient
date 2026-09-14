using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace AstraClient.Shell;

public partial class ReleaseHistoryWindow : Window
{
    public ObservableCollection<ReleaseEntry> Releases { get; } = [];

    public ReleaseHistoryWindow()
    {
        InitializeComponent();
        DataContext = this;
        Add("v1.31", "About and release history", "Project documentation and version visibility.", "Updated About labels to v1.31.", "Added this complete release history window.", "Kept the existing About window as the primary project information surface.");
        Add("v1.30", "Browse artwork", "Reliable Modrinth project artwork.", "Added WebP normalization for WPF.", "Repaired stale image cache entries.", "Improved icon fallback and gallery loading.");
        Add("v1.29", "Browse discovery", "Live Modrinth discovery across content types.", "Added project tabs and filters.", "Added project detail galleries and versions.", "Added install actions and pagination.");
        Add("v1.28", "Modpack workflow", "Import, export and install Modrinth packs.", "Added .mrpack import and export.", "Added share links and compact codes.", "Added instance creation from packs.");
        Add("v1.27", "Instance management", "Per-instance Minecraft configuration.", "Added instance CRUD and active selection.", "Added memory, JVM, game and Java overrides.", "Added installed content management.");
        Add("v1.26", "Loader support", "Dynamic Minecraft loader management.", "Added Fabric, Quilt, Forge and NeoForge support.", "Added loader version discovery.", "Added recommended arguments.");
        Add("v1.25", "Launch pipeline", "A complete Minecraft preparation flow.", "Added version and loader patch resolution.", "Added client, library, asset and native downloads.", "Added progress and launch error handling.");
        Add("v1.24", "Java runtime", "Compatible Java setup and detection.", "Added Java runtime detection.", "Added Mojang runtime downloads.", "Added global and per-instance overrides.");
        Add("v1.23", "Authentication", "Offline and Microsoft account support.", "Added offline profiles.", "Added Microsoft device-code sign-in.", "Added account state in the shell.");
        Add("v1.22", "Settings", "Persistent launcher preferences.", "Added memory and Java settings.", "Added snapshot and legacy filters.", "Added launch and safety preferences.");
        Add("v1.21", "Home framework", "Grouped Minecraft version discovery.", "Added version search and grouping.", "Added snapshot and legacy filters.", "Added active instance launch card.");
        Add("v1.20", "Glass shell", "A cohesive Windows desktop shell.", "Added custom chrome and controls.", "Added sidebar and account areas.", "Added reusable glass controls.");
        Add("v1.19", "Aternos", "Stable embedded Aternos experience.", "Added persistent WebView2 hosting.", "Added reload and browser state preservation.", "Added dedicated Aternos navigation.");
        Add("v1.18", "Server foundations", "Initial multiplayer and server data foundations.", "Added server-oriented navigation concepts.", "Added local data service groundwork.", "Added status surfaces.");
        Add("v1.17", "Content services", "Shared installed-content operations.", "Added content installation services.", "Added library persistence.", "Added cleanup on failed downloads.");
        Add("v1.16", "Networking", "Shared HTTP infrastructure.", "Added common HttpClient configuration.", "Added JSON helpers and retries.", "Added streamed downloads with progress.");
        Add("v1.15", "Persistence", "Reliable launcher state storage.", "Added settings, profile and instance JSON stores.", "Added atomic saves.", "Added application data root management.");
        Add("v1.14", "MVVM foundation", "An extensible application structure.", "Added observable view models.", "Added relay and async commands.", "Added navigation and service containers.");
        Add("v1.13", "Modrinth API", "Live project discovery services.", "Added search models and service calls.", "Added project and version details.", "Added category and tag discovery.");
        Add("v1.12", "Mojang metadata", "Official Minecraft metadata integration.", "Added version manifest loading.", "Added version JSON parsing.", "Added asset and download models.");
        Add("v1.11", "Progress feedback", "Clearer long-running operations.", "Added operation progress models.", "Added status surfaces.", "Added cancellation-aware asynchronous flows.");
        Add("v1.10", "Error handling", "Safer failure reporting.", "Added user-facing status messages.", "Added transient HTTP retries.", "Added startup exception diagnostics.");
        Add("v1.9", "Desktop integration", "Windows application behavior.", "Added tray support.", "Added Explorer data-folder actions.", "Added manifest and application identity.");
        Add("v1.8", "UI controls", "A reusable visual language.", "Added glass buttons and inputs.", "Added tabs, chips, scrollbars and progress bars.", "Added shared icons and converters.");
        Add("v1.7", "Navigation", "Core launcher sections in one shell.", "Added Home, Browse, Manage, Aternos and Settings.", "Added active navigation states.", "Added reusable page templates.");
        Add("v1.6", "Profiles", "Consistent player identity.", "Added UserProfile.", "Added offline profile persistence.", "Added shell account summary.");
        Add("v1.5", "Instance storage", "Isolated Minecraft instance data.", "Added instance directories.", "Added active instance tracking.", "Added per-instance persistence.");
        Add("v1.4", "Launcher services", "Initial service boundaries.", "Added Mojang, Java and launch services.", "Added shared application data.", "Added startup registration.");
        Add("v1.3", "WPF shell", "The first desktop application shell.", "Added WPF startup.", "Added main window and resources.", "Added MVVM page composition.");
        Add("v1.2", "Minecraft models", "Initial launcher domain models.", "Added versions, libraries and assets.", "Added loader and instance concepts.", "Added launcher configuration structures.");
        Add("v1.1", "Project foundation", "The solution and application identity.", "Added AstraClient solution and WPF project.", "Added Windows metadata.", "Added initial model and service organization.");
        Add("v1.0", "Astra Client", "The first usable project milestone.", "Established the Astra Client identity.", "Created the first launchable Windows client.", "Set the foundation for Minecraft management features.");
    }

    private void Add(string version, string title, string summary, params string[] features)
        => Releases.Add(new ReleaseEntry(version, title, summary, features));

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record ReleaseEntry(string Version, string Title, string Summary, IReadOnlyList<string> Features);
