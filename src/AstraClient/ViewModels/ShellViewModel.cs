using AstraClient.Models;
using AstraClient.Mvvm;

namespace AstraClient.ViewModels;

public enum NavItem
{
    Home,
    Browse,
    Manage,
    Aternos,
    Settings,
    OutputConsole
}

/// <summary>
/// Root view model for the window shell: owns navigation between pages,
/// the sidebar selection state and the account (user) area.
/// </summary>
public class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly Stack<NavItem> _history = new();
    private bool _isGoingBack;
    private HomeViewModel? _homeFailureSource;

    public ShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;

        NavigateCommand = new RelayCommand(p => Select(ParseNav(p)));
        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);

        _navigation.CurrentChanged += OnCurrentChanged;

        // Land on Home.
        Select(NavItem.Home);
        AttachHomeFailureHandler();
    }

    public ViewModelBase? CurrentViewModel => _navigation.Current;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand GoBackCommand { get; }
    public bool CanGoBack => _history.Count > 0;

    // ---- Account area ----
    private UserProfile? _currentUser;
    public UserProfile? CurrentUser
    {
        get => _currentUser;
        set
        {
            if (SetProperty(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(AccountLabel));
                OnPropertyChanged(nameof(AccountInitials));
                OnPropertyChanged(nameof(AccountKind));
                OnPropertyChanged(nameof(AccountAvatarUrl));
                OnPropertyChanged(nameof(AccountSkinUrl));
            }
        }
    }

    public bool IsLoggedIn => _currentUser != null;
    public string AccountLabel => _currentUser?.DisplayName ?? "Sign in";
    public string AccountInitials => _currentUser?.Initials ?? "AS";
    public string AccountKind => _currentUser?.Type switch
    {
        AuthType.Microsoft => "Microsoft",
        AuthType.Offline   => "Offline",
        _                  => "Offline"
    };

    public string AccountAvatarUrl => BuildSkinUrl("avatar", 64);
    public string AccountSkinUrl => BuildSkinUrl("body", 128);

    private string BuildSkinUrl(string view, int size)
    {
        if (_currentUser is null) return string.Empty;

        var skinKey = _currentUser.Type == AuthType.Offline
            ? _currentUser.Username
            : _currentUser.Uuid;

        return string.IsNullOrWhiteSpace(skinKey)
            ? string.Empty
            : $"https://mc-heads.net/{view}/{Uri.EscapeDataString(skinKey)}/{size}.png?overlay=true";
    }

    // ---- Sidebar selection ----
    private NavItem _selected = NavItem.Home;
    public NavItem SelectedNav
    {
        get => _selected;
        set => Select(value);
    }

    public bool IsHomeActive { get => _selected == NavItem.Home; set { if (value) Select(NavItem.Home); } }
    public bool IsBrowseActive { get => _selected == NavItem.Browse; set { if (value) Select(NavItem.Browse); } }
    public bool IsManageActive { get => _selected == NavItem.Manage; set { if (value) Select(NavItem.Manage); } }
    public bool IsAternosActive { get => _selected == NavItem.Aternos; set { if (value) Select(NavItem.Aternos); } }
    public bool IsSettingsActive { get => _selected == NavItem.Settings; set { if (value) Select(NavItem.Settings); } }
    public bool IsOutputConsoleActive { get => _selected == NavItem.OutputConsole; set { if (value) Select(NavItem.OutputConsole); } }

    private void Select(NavItem item)
    {
        if (item == _selected && _navigation.Current is not null)
            return;

        if (!_isGoingBack && _navigation.Current is not null)
            _history.Push(_selected);

        _selected = item;
        RaisePropertyChanged(nameof(SelectedNav), nameof(IsHomeActive), nameof(IsBrowseActive),
            nameof(IsManageActive), nameof(IsAternosActive), nameof(IsSettingsActive), nameof(IsOutputConsoleActive));
        GoBackCommand.RaiseCanExecuteChanged();

        try
        {
            _navigation.NavigateTo(item switch
            {
                NavItem.Home => _navigation.Resolve<HomeViewModel>(),
                NavItem.Browse => _navigation.Resolve<BrowseViewModel>(),
                NavItem.Manage => _navigation.Resolve<ManageViewModel>(),
                NavItem.Aternos => _navigation.Resolve<AternosViewModel>(),
                NavItem.Settings => _navigation.Resolve<SettingsViewModel>(),
                NavItem.OutputConsole => _navigation.Resolve<OutputConsoleViewModel>(),
                _ => _navigation.Resolve<HomeViewModel>()
            });
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"Navigation failed: {ex}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>Navigates and switches the sidebar highlight (used by deep links, e.g. Browse sub-items).</summary>
    public void GoTo(NavItem item) => Select(item);

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentViewModel));
        if (_homeFailureSource is not null)
            _homeFailureSource.LaunchFailed -= HandleLaunchFailed;

        AttachHomeFailureHandler();
    }

    private void HandleLaunchFailed(string message)
    {
        ShowOutputConsole();
    }

    public void ShowOutputConsole()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        void RevealAndNavigate()
        {
            if (System.Windows.Application.Current.MainWindow is AstraClient.Shell.MainWindow window)
                window.ShowAndActivate();
            GoTo(NavItem.OutputConsole);
        }

        if (dispatcher.CheckAccess())
            RevealAndNavigate();
        else
            dispatcher.BeginInvoke(RevealAndNavigate);
    }

    private void AttachHomeFailureHandler()
    {
        var home = _navigation.Resolve<HomeViewModel>();
        if (ReferenceEquals(_homeFailureSource, home)) return;

        if (_homeFailureSource is not null)
            _homeFailureSource.LaunchFailed -= HandleLaunchFailed;

        _homeFailureSource = home;
        _homeFailureSource.LaunchFailed += HandleLaunchFailed;
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;

        _isGoingBack = true;
        try { Select(_history.Pop()); }
        finally
        {
            _isGoingBack = false;
            GoBackCommand.RaiseCanExecuteChanged();
        }
    }

    private static NavItem ParseNav(object? parameter) => parameter switch
    {
        NavItem n => n,
        string s when Enum.TryParse<NavItem>(s, true, out var parsed) => parsed,
        _ => NavItem.Home
    };
}
