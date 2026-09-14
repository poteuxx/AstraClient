namespace AstraClient.Services.Data;

public class AppSettings
{
    public const string DefaultAzureClientId = "3f5e420b-ca37-4db9-aac0-b55074247555";
    public string AzureClientId   { get; set; } = DefaultAzureClientId;
    public string JavaOverridePath { get; set; } = "";
    public int    DefaultMemoryMb  { get; set; } = 2048;
    public bool   SidebarCollapsed { get; set; } = false;
    public string Theme            { get; set; } = "dark";
    public bool   CloseAfterLaunch { get; set; } = false;
    public bool   ShowSnapshots    { get; set; } = false;
    public bool   ShowOldVersions  { get; set; } = false;
    public bool   ConfirmDestructiveActions { get; set; } = true;
}

public sealed class SettingsService
{
    private readonly string _path;
    private AppSettings _current = new();

    public SettingsService(string dataRoot)
        => _path = Path.Combine(dataRoot, "settings.json");

    public AppSettings Current => _current;

    public async Task LoadAsync()
    {
        _current = await JsonStore.LoadAsync<AppSettings>(_path) ?? new AppSettings();
        if (string.IsNullOrWhiteSpace(_current.AzureClientId))
            _current.AzureClientId = AppSettings.DefaultAzureClientId;
    }

    public async Task SaveAsync()
        => await JsonStore.SaveAsync(_path, _current);
}
