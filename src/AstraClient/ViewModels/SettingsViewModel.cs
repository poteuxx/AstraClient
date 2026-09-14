using System.Windows;
using AstraClient.Models;
using AstraClient.Mvvm;
using AstraClient.Services.Auth;
using AstraClient.Services.Data;

namespace AstraClient.ViewModels;

/// <summary>
/// Settings page: account / auth, memory, Java path, data directory, appearance.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService       _settings;
    private readonly ProfileService        _profiles;
    private readonly MicrosoftAuthProvider _msAuth;
    private readonly OfflineAuthProvider   _offlineAuth;
    private readonly InstanceService       _instances;
    private readonly ShellViewModel        _shell;

    // ---- Azure Client ID (Microsoft auth) -----------------------------------

    public string AzureClientId
    {
        get => _settings.Current.AzureClientId;
        set
        {
            _settings.Current.AzureClientId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MsAuthAvailable));
        }
    }

    public bool MsAuthAvailable => !string.IsNullOrWhiteSpace(_settings.Current.AzureClientId);

    // ---- Memory default -----------------------------------------------------

    public int DefaultMemoryMb
    {
        get => _settings.Current.DefaultMemoryMb;
        set
        {
            _settings.Current.DefaultMemoryMb = value;
            OnPropertyChanged();
        }
    }

    // ---- Java override path -------------------------------------------------

    public string JavaOverridePath
    {
        get => _settings.Current.JavaOverridePath;
        set
        {
            _settings.Current.JavaOverridePath = value;
            OnPropertyChanged();
        }
    }

    // ---- Theme --------------------------------------------------------------

    public string Theme
    {
        get => _settings.Current.Theme;
        set
        {
            _settings.Current.Theme = value;
            OnPropertyChanged();
        }
    }

    // ---- Misc toggles -------------------------------------------------------

    public bool ShowSnapshots
    {
        get => _settings.Current.ShowSnapshots;
        set { _settings.Current.ShowSnapshots = value; OnPropertyChanged(); }
    }

    public bool ShowOldVersions
    {
        get => _settings.Current.ShowOldVersions;
        set { _settings.Current.ShowOldVersions = value; OnPropertyChanged(); }
    }

    public bool CloseAfterLaunch
    {
        get => _settings.Current.CloseAfterLaunch;
        set { _settings.Current.CloseAfterLaunch = value; OnPropertyChanged(); }
    }

    public bool ConfirmDestructiveActions
    {
        get => _settings.Current.ConfirmDestructiveActions;
        set { _settings.Current.ConfirmDestructiveActions = value; OnPropertyChanged(); }
    }

    // ---- Offline account ----------------------------------------------------

    private string _offlineUsername = "";
    public string OfflineUsername
    {
        get => _offlineUsername;
        set
        {
            if (SetProperty(ref _offlineUsername, value))
                LoginOfflineCommand?.RaiseCanExecuteChanged();
        }
    }

    // ---- Microsoft auth state -----------------------------------------------

    private string _msAuthStatus = "";
    public string MsAuthStatus
    {
        get => _msAuthStatus;
        set => SetProperty(ref _msAuthStatus, value);
    }

    private string _msVerificationUrl = "";
    public string MsVerificationUrl
    {
        get => _msVerificationUrl;
        set => SetProperty(ref _msVerificationUrl, value);
    }

    private string _msDeviceCode = "";
    public string MsDeviceCode
    {
        get => _msDeviceCode;
        set => SetProperty(ref _msDeviceCode, value);
    }

    // ---- Data root ----------------------------------------------------------

    public string DataRoot => App.DataRoot;

    // ---- Status --------------------------------------------------------------

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    // ---- Commands -----------------------------------------------------------

    public AsyncRelayCommand SaveCommand          { get; }
    public AsyncRelayCommand LoginOfflineCommand  { get; }
    public AsyncRelayCommand LoginMicrosoftCommand { get; }
    public RelayCommand      OpenDataFolderCommand { get; }
    public RelayCommand      OpenMsAzureHelpCommand { get; }
    public RelayCommand      CopyMsVerificationUrlCommand { get; }
    public RelayCommand      CopyMsDeviceCodeCommand { get; }
    public RelayCommand      OpenMsVerificationUrlCommand { get; }

    // -------------------------------------------------------------------------

    public SettingsViewModel(
        SettingsService       settings,
        ProfileService        profiles,
        MicrosoftAuthProvider msAuth,
        OfflineAuthProvider   offlineAuth,
        InstanceService       instances,
        ShellViewModel        shell)
    {
        _settings    = settings;
        _profiles    = profiles;
        _msAuth      = msAuth;
        _offlineAuth = offlineAuth;
        _instances   = instances;
        _shell       = shell;

        Title = "Settings";

        SaveCommand   = new AsyncRelayCommand(_ => SaveAsync());
        LoginOfflineCommand   = new AsyncRelayCommand(_ => LoginOfflineAsync(),
            _ => !string.IsNullOrWhiteSpace(_offlineUsername));
        LoginMicrosoftCommand = new AsyncRelayCommand(_ => LoginMicrosoftAsync(),
            _ => MsAuthAvailable && !IsBusy);
        OpenDataFolderCommand = new RelayCommand(_ =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", App.DataRoot)
                { UseShellExecute = true });
            }
            catch { }
        });
        OpenMsAzureHelpCommand = new RelayCommand(_ =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://aka.ms/msa-application-registration")
                { UseShellExecute = true });
            }
            catch { }
        });
        CopyMsVerificationUrlCommand = new RelayCommand(_ => CopyToClipboard(MsVerificationUrl),
            _ => !string.IsNullOrWhiteSpace(MsVerificationUrl));
        CopyMsDeviceCodeCommand = new RelayCommand(_ => CopyToClipboard(MsDeviceCode),
            _ => !string.IsNullOrWhiteSpace(MsDeviceCode));
        OpenMsVerificationUrlCommand = new RelayCommand(_ =>
        {
            if (Uri.TryCreate(MsVerificationUrl, UriKind.Absolute, out var uri))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString())
                    { UseShellExecute = true });
                }
                catch { }
            }
        }, _ => IsValidVerificationUrl(MsVerificationUrl));
    }

    public override void OnNavigatedTo()
    {
        // Sync current username from profile.
        _offlineUsername = _profiles.Current?.Username ?? "";
        OnPropertyChanged(nameof(OfflineUsername));
        MsAuthStatus = _profiles.Current?.Type == AuthType.Microsoft
            ? $"Signed in as {_profiles.Current.Username}"
            : "";
    }

    // ---- Save ---------------------------------------------------------------

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Status = "Settings saved.";
                OnPropertyChanged(nameof(MsAuthAvailable));
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Save failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    // ---- Auth ---------------------------------------------------------------

    private async Task LoginOfflineAsync()
    {
        if (string.IsNullOrWhiteSpace(_offlineUsername)) return;
        _offlineAuth.Username = _offlineUsername;
        var profile = await _offlineAuth.AuthenticateAsync().ConfigureAwait(false);
        if (profile is not null)
        {
            await _profiles.SaveAsync(profile).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _shell.CurrentUser = profile;
                MsAuthStatus = "";
                Status = $"Signed in as {profile.Username} (offline).";
            });
        }
    }

    private async Task LoginMicrosoftAsync()
    {
        if (!MsAuthAvailable) return;

        IsBusy = true;
        MsAuthStatus = "Starting Microsoft sign-in (device-code)…";
        MsVerificationUrl = "";
        MsDeviceCode = "";
        Status = "";

        // Hook device-code event so UI can show it.
        void OnDeviceCode(string code, string url)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MsVerificationUrl = url;
                MsDeviceCode = code;
                MsAuthStatus = "Open the verification page and enter this code:";
                CopyMsVerificationUrlCommand.RaiseCanExecuteChanged();
                CopyMsDeviceCodeCommand.RaiseCanExecuteChanged();
                OpenMsVerificationUrlCommand.RaiseCanExecuteChanged();
            });
        }

        _msAuth.DeviceCodeReceived += OnDeviceCode;
        void OnAuthStatus(string message)
        {
            Application.Current.Dispatcher.InvokeAsync(() => MsAuthStatus = message);
        }

        _msAuth.StatusChanged += OnAuthStatus;
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
            var profile = await _msAuth.AuthenticateAsync().ConfigureAwait(false);
            if (profile is not null)
            {
                await _profiles.SaveAsync(profile).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _shell.CurrentUser = profile;
                    MsAuthStatus = $"Signed in as {profile.Username}";
                    Status = "Microsoft login successful.";
                });
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MsAuthStatus = "Sign-in cancelled or timed out.";
                });
            }
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MsAuthStatus = $"Microsoft sign-in failed: {ex.Message}";
                Status = $"Microsoft sign-in failed: {ex.Message}";
            });
        }
        finally
        {
            _msAuth.DeviceCodeReceived -= OnDeviceCode;
            _msAuth.StatusChanged -= OnAuthStatus;
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private static void CopyToClipboard(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Clipboard.SetText(value);
    }

    private static bool IsValidVerificationUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? _);
}
