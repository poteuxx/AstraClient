using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Web.WebView2.Core;

namespace AstraClient.Views;

public partial class AternosView : UserControl
{
    private bool _isInitializing;
    private bool _isInitialized;
    private bool _hasNavigated;
    private const string AternosUrl = "https://aternos.org/servers/";

    public AternosView()
    {
        InitializeComponent();

        // Set the user data folder before any initialization
        AternosWebView.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
        {
            UserDataFolder = System.IO.Path.Combine(App.DataRoot, "webview2")
        };

        // Delay initialization until the panel is visible. WebView2 is an HWND-based control and
        // may remain blank when it is initialized while its parent is collapsed.
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsVisible)
            _ = InitializeAndNavigateAsync();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            _ = InitializeAndNavigateAsync();
    }

    private async Task InitializeAndNavigateAsync(bool forceNavigation = false)
    {
        if (_isInitializing)
            return;

        if (_isInitialized && !forceNavigation)
        {
            return;
        }

        _isInitializing = true;
        ShowLoading("Starting Aternos…");
        ErrorOverlay.Visibility = Visibility.Collapsed;

        try
        {
            await AternosWebView.EnsureCoreWebView2Async();
            var core = AternosWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 did not provide a browser instance.");

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed -= OnProcessFailed;
            core.ProcessFailed += OnProcessFailed;

            _isInitialized = true;
            ReloadButton.IsEnabled = true;

            // CoreWebView2 starts at about:blank, so checking Source for null would
            // incorrectly skip the first real navigation and leave a black surface.
            if (forceNavigation || !_hasNavigated)
            {
                _hasNavigated = true;
                core.Navigate(AternosUrl);
            }
            else
                HideLoading();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("Microsoft Edge WebView2 Runtime is required to display Aternos. Install or repair the runtime, then try again.");
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[WebView2] Initialization failed: {ex}");
            ShowError("The embedded Aternos browser could not start. Check your WebView2 Runtime installation and try again.");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        => ShowLoading("Loading Aternos…");

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
            HideLoading();
        else
            ShowError($"Aternos could not be reached ({e.WebErrorStatus}). Check your connection and try again.");
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        => ShowError("The embedded Aternos browser stopped unexpectedly. Try reloading the panel.");

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (AternosWebView.CoreWebView2 is { } core)
            core.Reload();
        else
            _ = InitializeAndNavigateAsync(forceNavigation: true);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
        => _ = InitializeAndNavigateAsync(forceNavigation: true);

    private void ShowLoading(string message)
    {
        LoadingText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

    private void ShowError(string message)
    {
        HideLoading();
        ErrorText.Text = message;
        ErrorOverlay.Visibility = Visibility.Visible;
    }
}
