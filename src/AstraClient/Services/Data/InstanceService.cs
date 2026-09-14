using AstraClient.Models;

namespace AstraClient.Services.Data;

public sealed class InstanceService
{
    private readonly string _dataRoot;
    private readonly string _path;
    private List<Instance> _instances = [];
    private string? _activeId;

    public InstanceService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _path = Path.Combine(dataRoot, "instances.json");
    }

    public IReadOnlyList<Instance> All => _instances;

    public Instance? Active
        => _activeId is null ? _instances.FirstOrDefault() : _instances.FirstOrDefault(i => i.Id == _activeId);

    public string? ActiveId
    {
        get => _activeId;
        set { _activeId = value; _ = SaveAsync(); }
    }

    public async Task LoadAsync()
    {
        var data = await JsonStore.LoadAsync<InstancesData>(_path);
        _instances = data?.Instances ?? [];
        _activeId  = data?.ActiveId;

        // Ensure every instance directory exists.
        foreach (var inst in _instances)
            EnsureInstanceDirs(inst);
    }

    public async Task<Instance> CreateAsync(string name, string gameVersion, string loader = "vanilla")
    {
        var inst = new Instance
        {
            Name        = name,
            GameVersion = gameVersion,
            Loader      = loader,
        };
        EnsureInstanceDirs(inst);
        _instances.Add(inst);
        if (_instances.Count == 1) _activeId = inst.Id;
        await SaveAsync();
        return inst;
    }

    public async Task UpdateAsync(Instance inst)
    {
        var idx = _instances.FindIndex(i => i.Id == inst.Id);
        if (idx >= 0) _instances[idx] = inst;
        await SaveAsync();
    }

    public async Task DeleteAsync(string id)
    {
        _instances.RemoveAll(i => i.Id == id);
        if (_activeId == id) _activeId = _instances.FirstOrDefault()?.Id;
        await SaveAsync();
    }

    public Instance? GetById(string id) => _instances.FirstOrDefault(i => i.Id == id);

    private void EnsureInstanceDirs(Instance inst)
    {
        foreach (var sub in new[] { "mods", "plugins", "resourcepacks", "shaderpacks", "datapacks", "modpacks", "saves", "config" })
            Directory.CreateDirectory(Path.Combine(inst.GameDir, sub));
    }

    private async Task SaveAsync()
        => await JsonStore.SaveAsync(_path, new InstancesData { Instances = _instances, ActiveId = _activeId });

    private class InstancesData
    {
        public List<Instance> Instances { get; set; } = [];
        public string? ActiveId { get; set; }
    }
}
