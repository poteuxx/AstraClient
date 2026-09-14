using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AstraClient.Interop;
using AstraClient.Services.Data;
using AstraClient.ViewModels;

namespace AstraClient.Shell;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly ProfileService _profiles;
    private readonly SettingsService _settings;
    private bool _isSidebarCollapsed;

    public MainWindow(ShellViewModel shell, ProfileService profiles, SettingsService settings)
    {
        InitializeComponent();
        _shell = shell;
        _profiles = profiles;
        _settings = settings;
        DataContext = shell;
        _shell.PropertyChanged += Shell_PropertyChanged;
        UpdatePageHosts();
        SetSidebarCollapsed(settings.Current.SidebarCollapsed, save: false);
        SourceInitialized += (_, _) =>
        {
            // Progressive enhancement: real Win11 acrylic where supported, else blur-behind.
            if (!DwmInterop.TryEnableAcrylicBackdrop(this))
                DwmInterop.TryEnableBlurBehind(this, 12);
            DwmInterop.TryEnableRoundedCorners(this);
        };
    }

    private void Shell_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsAternosActive)
            or nameof(ShellViewModel.CurrentViewModel))
            UpdatePageHosts();
    }

    private void UpdatePageHosts()
    {
        var isAternos = _shell.IsAternosActive;
        if (isAternos)
        {
            GenericPageHost.Visibility = Visibility.Collapsed;
            PersistentAternosView.DataContext = _shell.CurrentViewModel;
            PersistentAternosView.Visibility = Visibility.Visible;
        }
        else
        {
            PersistentAternosView.Visibility = Visibility.Collapsed;
            GenericPageHost.Content = _shell.CurrentViewModel;
            GenericPageHost.Visibility = Visibility.Visible;
        }
    }

    // ---------- Title bar drag / double-click ----------
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == WindowState.Maximized)
            return;
        try { DragMove(); } catch { /* drag not permitted in some states */ }
    }

    // ---------- Window buttons ----------
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---------- Menu: navigation ----------
    private void NavHome_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Home);
    private void NavBrowse_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Browse);
    private void NavManage_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Manage);
    private void NavAternos_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Aternos);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Settings);
    private void NavOutputConsole_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.OutputConsole);

    private void MenuNavHome_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Home);
    private void MenuNavBrowse_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Browse);
    private void MenuNavManage_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Manage);
    private void MenuNavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Settings);
    private void MenuNavOutputConsole_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.OutputConsole);

    private void ContextNavHome_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Home);
    private void ContextNavBrowse_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Browse);
    private void ContextNavManage_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Manage);
    private void ContextNavOutputConsole_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.OutputConsole);
    private void ContextNavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(NavItem.Settings);

    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        TitleBarMenu.IsOpen = !TitleBarMenu.IsOpen;
        e.Handled = true;
    }

    private void MenuStripToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.IsChecked != true)
            return;

        if (!ReferenceEquals(selected, FileMenuToggle)) FileMenuToggle.IsChecked = false;
        if (!ReferenceEquals(selected, ViewMenuToggle)) ViewMenuToggle.IsChecked = false;
        if (!ReferenceEquals(selected, HelpMenuToggle)) HelpMenuToggle.IsChecked = false;
    }

    private void MenuPopup_Closed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, FileMenuPopup)) FileMenuToggle.IsChecked = false;
        else if (ReferenceEquals(sender, ViewMenuPopup)) ViewMenuToggle.IsChecked = false;
        else if (ReferenceEquals(sender, HelpMenuPopup)) HelpMenuToggle.IsChecked = false;
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarCollapsed(!_isSidebarCollapsed, save: true);
        ViewMenuToggle.IsChecked = false;
    }

    private void SetSidebarCollapsed(bool collapsed, bool save)
    {
        _isSidebarCollapsed = collapsed;
        Sidebar.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.Width = collapsed ? new GridLength(0) : new GridLength(236);
        SidebarMenuLabel.Text = collapsed ? "Expand sidebar" : "Collapse sidebar";
        if (save)
        {
            _settings.Current.SidebarCollapsed = collapsed;
            _ = _settings.SaveAsync();
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt
            && _shell.GoBackCommand.CanExecute(null))
        {
            _shell.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleSidebar_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void PopupBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0)
            return;

        border.Clip = new RectangleGeometry(
            new Rect(0, 0, border.ActualWidth, border.ActualHeight), 16, 16);
    }

    private void NavigateTo(NavItem item)
    {
        _shell.GoTo(item);
        var currentView = _shell.CurrentViewModel;

        if (item == NavItem.Aternos)
        {
            GenericPageHost.Visibility = Visibility.Collapsed;
            PersistentAternosView.DataContext = currentView;
            PersistentAternosView.Visibility = Visibility.Visible;
        }
        else
        {
            PersistentAternosView.Visibility = Visibility.Collapsed;
            GenericPageHost.Content = currentView;
            GenericPageHost.Visibility = Visibility.Visible;
        }
        TitleBarMenu.IsOpen = false;
    }

    // ---------- Menu: actions ----------
    private void MenuNewInstance_Click(object sender, RoutedEventArgs e)
    {
        _shell.GoTo(NavItem.Manage);
        // The Manage page owns instance creation; navigate there and let the user create one.
    }

    private void MenuOpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = App.DataRoot;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the data folder:\n{ex.Message}", "Astra Client",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        HelpMenuToggle.IsChecked = false;
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void MenuReleaseHistory_Click(object sender, RoutedEventArgs e)
    {
        HelpMenuToggle.IsChecked = false;
        var history = new ReleaseHistoryWindow { Owner = this };
        history.ShowDialog();
    }

    // ---------- Account (ToggleButton + Popup — reclick closes like ComboBox) ----------
    private void CloseAccountFlyout() => AccountToggle.IsChecked = false;

    private void AccountOffline_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        _shell.GoTo(NavItem.Settings);
    }

    private void AccountMicrosoft_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        MessageBox.Show(this,
            "Microsoft sign-in requires an Azure Application (client) ID.\nAdd it under Settings → Account.",
            "Sign in with Microsoft", MessageBoxButton.OK, MessageBoxImage.Information);
        _shell.GoTo(NavItem.Settings);
    }

    private void AccountViewSkin_Click(object sender, RoutedEventArgs e)
    {
        var url = _shell.AccountSkinUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Sign in or create an offline profile before viewing a skin.",
                "Astra Client", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            CloseAccountFlyout();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the skin viewer:\n{ex.Message}",
                "Astra Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AccountSignOut_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        await _profiles.ClearAsync();
        _shell.CurrentUser = null;
    }

    // ---------- Tray Icon ----------
    private void MenuOpen_Click(object sender, RoutedEventArgs e) => ShowAndActivate();
    
    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowAndActivate();

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // force to front
        Topmost = false; // remove topmost constraint
        Focus();
    }
}
