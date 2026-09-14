using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AstraClient.Models;
using AstraClient.Mvvm;
using AstraClient.Services.Data;

namespace AstraClient.ViewModels;

/// <summary>Presentation layer for independently managed local Minecraft servers.</summary>
public sealed class ServersViewModel : ViewModelBase
{
    private readonly LocalServerService _servers;
    public ObservableCollection<LocalServer> Servers { get; } = [];
    public IReadOnlyList<ServerTypeOption> ServerTypeOptions { get; } = ServerTypeOption.All;

    private LocalServer? _selectedServer;
    public LocalServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                RaisePropertyChanged(nameof(HasSelection), nameof(IsRunning));
                StartCommand?.RaiseCanExecuteChanged();
                BackupCommand?.RaiseCanExecuteChanged();
                SaveCommand?.RaiseCanExecuteChanged();
            }
        }
    }
    public bool HasSelection => SelectedServer is not null;
    public bool IsRunning => SelectedServer is not null && _servers.IsRunning(SelectedServer);

    private string _newName = "My Server";
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }
    private string _newVersion = "1.21.1";
    public string NewVersion { get => _newVersion; set => SetProperty(ref _newVersion, value); }
    private LocalServerType _newType = LocalServerType.Vanilla;
    public LocalServerType NewType { get => _newType; set => SetProperty(ref _newType, value); }
    private string _status = "Create a server profile to begin.";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    private string _consoleInput = "";
    public string ConsoleInput { get => _consoleInput; set => SetProperty(ref _consoleInput, value); }
    private string _console = "";
    public string Console { get => _console; private set => SetProperty(ref _console, value); }

    public AsyncRelayCommand CreateAndProvisionCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public RelayCommand SendConsoleCommand { get; }
    public AsyncRelayCommand BackupCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public ServersViewModel(LocalServerService servers)
    {
        _servers = servers;
        Title = "Local Servers";
        CreateAndProvisionCommand = new AsyncRelayCommand(_ => CreateAndProvisionAsync(), _ => !IsBusy);
        StartCommand = new AsyncRelayCommand(_ => StartAsync(), _ => SelectedServer is not null && !IsRunning && !IsBusy);
        StopCommand = new RelayCommand(_ =>
        {
            if (SelectedServer is not null)
            {
                _servers.Stop(SelectedServer);
                Status = "Stopping server…";
                RaisePropertyChanged(nameof(IsRunning));
                StartCommand.RaiseCanExecuteChanged();
            }
        });
        RestartCommand = new AsyncRelayCommand(_ => RestartAsync(), _ => SelectedServer is not null && !IsBusy);
        SendConsoleCommand = new RelayCommand(_ => SendConsole());
        BackupCommand = new AsyncRelayCommand(_ => BackupAsync(), _ => SelectedServer is not null && !IsBusy);
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => SelectedServer is not null && !IsBusy);
        _servers.OutputReceived += (_, message) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (SelectedServer?.Id == message.ServerId)
                Console = (Console + message.Line + Environment.NewLine)[^Math.Min(Console.Length + message.Line.Length + Environment.NewLine.Length, 24_000)..];
        });
        _servers.ServerStopped += (_, id) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (SelectedServer?.Id == id) { Status = "Server stopped."; RaisePropertyChanged(nameof(IsRunning)); StartCommand.RaiseCanExecuteChanged(); }
        });
    }

    public override void OnNavigatedTo() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var saved = await _servers.LoadAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Servers.Clear();
                foreach (var server in saved)
                    Servers.Add(server);
                SelectedServer ??= Servers.FirstOrDefault();
                if (Servers.Count == 0)
                    Status = "No servers yet — use the form on the left to create one.";
                else
                    Status = $"{Servers.Count} server(s) loaded.";
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                Status = $"Could not load servers: {ex.Message}");
        }
    }

    private async Task CreateAndProvisionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewVersion)) { Status = "Enter a server name and Minecraft version."; return; }
        IsBusy = true;
        CreateAndProvisionCommand.RaiseCanExecuteChanged();
        var server = new LocalServer { Name = NewName.Trim(), GameVersion = NewVersion.Trim(), Type = NewType, Motd = $"{NewName.Trim()} · powered by Astra" };
        await Application.Current.Dispatcher.InvokeAsync(() => { Servers.Add(server); SelectedServer = server; });
        Status = "Creating folders and downloading server files…";
        try
        {
            await _servers.ProvisionAsync(server).ConfigureAwait(false);
            await SaveAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                Status = "Server is ready. Review settings, accept the EULA, then start it.");
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Provisioning failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsBusy = false;
                CreateAndProvisionCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task StartAsync()
    {
        if (SelectedServer is null) return;
        IsBusy = true;
        StartCommand.RaiseCanExecuteChanged();
        try
        {
            _servers.WriteProperties(SelectedServer);
            await _servers.StartAsync(SelectedServer).ConfigureAwait(false);
            await SaveAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Status = "Server started — live output appears below.";
                RaisePropertyChanged(nameof(IsRunning));
                RestartCommand.RaiseCanExecuteChanged();
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Start failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsBusy = false;
                StartCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task RestartAsync()
    {
        if (SelectedServer is null) return;
        IsBusy = true;
        Status = "Restarting server…";
        try
        {
            _servers.WriteProperties(SelectedServer);
            await _servers.RestartAsync(SelectedServer).ConfigureAwait(false);
            await SaveAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Status = "Server restarted.";
                RaisePropertyChanged(nameof(IsRunning));
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Status = $"Restart failed: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsBusy = false;
                RestartCommand.RaiseCanExecuteChanged();
                StartCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task BackupAsync()
    {
        if (SelectedServer is null) return;
        try
        {
            Status = "Creating compressed world backup…";
            var backup = await _servers.BackupAsync(SelectedServer).ConfigureAwait(false);
            Status = $"Backup created: {Path.GetFileName(backup)}";
        }
        catch (Exception ex) { Status = $"Backup failed: {ex.Message}"; }
    }

    private void SendConsole()
    {
        if (SelectedServer is null || string.IsNullOrWhiteSpace(ConsoleInput)) return;
        _servers.SendCommand(SelectedServer, ConsoleInput); ConsoleInput = "";
    }
    private void OpenFolder()
    {
        if (SelectedServer is null) return;
        Directory.CreateDirectory(_servers.GetDirectory(SelectedServer));
        Process.Start(new ProcessStartInfo("explorer.exe", _servers.GetDirectory(SelectedServer)) { UseShellExecute = true });
    }
    private Task SaveAsync() => _servers.SaveAsync(Servers);
}
