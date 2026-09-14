using AstraClient.Models;

namespace AstraClient.Services.Data;

/// <summary>Persists a list of favorited Modrinth project IDs.</summary>
public sealed class FavoritesService
{
    private readonly string _path;
    private HashSet<string> _ids = [];

    public FavoritesService(string dataRoot)
        => _path = Path.Combine(dataRoot, "favorites.json");

    public async Task LoadAsync()
    {
        var list = await JsonStore.LoadAsync<List<string>>(_path) ?? [];
        _ids = [.. list];
    }

    public bool IsFavorite(string projectId) => _ids.Contains(projectId);

    public async Task ToggleAsync(string projectId)
    {
        if (!_ids.Add(projectId)) _ids.Remove(projectId);
        await JsonStore.SaveAsync(_path, _ids.ToList());
    }
}
