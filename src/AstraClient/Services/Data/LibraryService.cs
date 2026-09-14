using AstraClient.Models;

namespace AstraClient.Services.Data;

public sealed class LibraryService
{
    private readonly string _dataRoot;
    private readonly Dictionary<string, List<InstalledContent>> _byInstance = new();

    public LibraryService(string dataRoot)
        => _dataRoot = dataRoot;

    private string GetPath(string instanceId)
        => Path.Combine(_dataRoot, "instances", instanceId, "library.json");

    public async Task<List<InstalledContent>> GetForInstanceAsync(string instanceId)
    {
        if (_byInstance.TryGetValue(instanceId, out var cached)) return cached;
        var list = await JsonStore.LoadAsync<List<InstalledContent>>(GetPath(instanceId)) ?? [];
        _byInstance[instanceId] = list;
        return list;
    }

    public async Task AddAsync(InstalledContent content)
    {
        var list = await GetForInstanceAsync(content.InstanceId);
        list.Add(content);
        await JsonStore.SaveAsync(GetPath(content.InstanceId), list);
    }

    public async Task RemoveAsync(string instanceId, string contentId)
    {
        var list = await GetForInstanceAsync(instanceId);
        list.RemoveAll(c => c.Id == contentId);
        await JsonStore.SaveAsync(GetPath(instanceId), list);
    }

    public async Task UpdateAsync(InstalledContent content)
    {
        var list = await GetForInstanceAsync(content.InstanceId);
        var idx = list.FindIndex(c => c.Id == content.Id);
        if (idx >= 0) list[idx] = content;
        await JsonStore.SaveAsync(GetPath(content.InstanceId), list);
    }

    /// <summary>Total library size across all instances (bytes on disk).</summary>
    public async Task<long> GetTotalSizeBytesAsync()
    {
        long total = 0;
        if (!Directory.Exists(Path.Combine(_dataRoot, "instances"))) return 0;
        foreach (var dir in Directory.GetDirectories(Path.Combine(_dataRoot, "instances")))
        {
            var id = Path.GetFileName(dir);
            var list = await GetForInstanceAsync(id);
            foreach (var item in list) total += item.SizeBytes;
        }
        return total;
    }
}
