using AstraClient.Models;

namespace AstraClient.Services.Data;

public sealed class ProfileService
{
    private readonly string _path;
    private UserProfile? _current;

    public ProfileService(string dataRoot)
        => _path = Path.Combine(dataRoot, "profile.json");

    public UserProfile? Current => _current;

    public async Task LoadAsync()
        => _current = await JsonStore.LoadAsync<UserProfile>(_path);

    public async Task SaveAsync(UserProfile profile)
    {
        _current = profile;
        await JsonStore.SaveAsync(_path, profile);
    }

    public void ClearCurrent() => _current = null;

    public async Task ClearAsync()
    {
        _current = null;
        if (File.Exists(_path))
            File.Delete(_path);
        await Task.CompletedTask;
    }
}
