using System.Collections.ObjectModel;
using System.Windows;
using AstraClient.Mvvm;
using AstraClient.Services.Data;
using AstraClient.Services.Launch;

namespace AstraClient.ViewModels;

public sealed class OutputConsoleViewModel : ViewModelBase
{
    private const int MaxLines = 2000;
    private readonly InstanceService _instances;
    private readonly LaunchService _launch;
    private readonly string _dataRoot;
    private readonly ObservableCollection<string> _lines = [];
    private string _logText = string.Empty;
    private string _status = "Waiting for game output";

    public OutputConsoleViewModel(InstanceService instances, LaunchService launch, string dataRoot)
    {
        _instances = instances;
        _launch = launch;
        _dataRoot = dataRoot;
        ClearCommand = new RelayCommand(Clear);
        CopyCommand = new RelayCommand(Copy, () => _lines.Count > 0);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        StopGameCommand = new RelayCommand(StopGame, () => IsGameRunning);
        _launch.LogReceived += OnLogReceived;
        _launch.ProcessStateChanged += OnProcessStateChanged;
        Title = "Output Console";
    }

    public string ActiveInstanceName => _instances.Active?.Name ?? "No active instance";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public bool HasOutput => _lines.Count > 0;
    public bool IsGameRunning => _instances.Active is { } instance && _launch.IsRunning(instance.Id);
    public RelayCommand ClearCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand StopGameCommand { get; }

    public override void OnNavigatedTo()
    {
        OnPropertyChanged(nameof(ActiveInstanceName));
        OnPropertyChanged(nameof(IsGameRunning));
        StopGameCommand.RaiseCanExecuteChanged();
        LoadLog();
    }

    private void OnProcessStateChanged(string instanceId, bool running)
    {
        if (_instances.Active?.Id != instanceId) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(IsGameRunning));
            Status = running ? "Game process running" : "Game process stopped";
            StopGameCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnLogReceived(string instanceId, string line)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_instances.GetById(instanceId) is null) return;
            _lines.Add(line);
            while (_lines.Count > MaxLines) _lines.RemoveAt(0);
            LogText = string.Join(Environment.NewLine, _lines);
            OnPropertyChanged(nameof(HasOutput));
            Status = "Live game output";
            CopyCommand.RaiseCanExecuteChanged();
        });
    }

    private void LoadLog()
    {
        _lines.Clear();
        var path = GetLogPath();
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path).TakeLast(MaxLines))
                _lines.Add(line);
        }

        LogText = string.Join(Environment.NewLine, _lines);
        OnPropertyChanged(nameof(HasOutput));
        Status = _lines.Count == 0 ? "Waiting for game output" : "Loaded saved launch output";
        CopyCommand.RaiseCanExecuteChanged();
    }

    private void Clear()
    {
        _lines.Clear();
        LogText = string.Empty;
        Status = "Console cleared";
        OnPropertyChanged(nameof(HasOutput));
        CopyCommand.RaiseCanExecuteChanged();
    }

    private void Copy()
    {
        if (_lines.Count > 0)
            Clipboard.SetText(string.Join(Environment.NewLine, _lines));
    }

    private void OpenLogFolder()
    {
        var directory = Path.Combine(_instances.Active?.GameDir ?? _dataRoot, "logs");
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", directory)
        { UseShellExecute = true });
    }

    private void StopGame()
    {
        var instance = _instances.Active;
        if (instance is not null && _launch.Stop(instance.Id))
            Status = "Stopping game process…";
    }

    private string GetLogPath()
        => Path.Combine(_instances.Active?.GameDir ?? _dataRoot, "logs", "astra-launch.log");
}
