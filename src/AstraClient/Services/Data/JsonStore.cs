using System.IO;
using System.Text.Json;
using AstraClient.Services.Http;

namespace AstraClient.Services.Data;

/// <summary>Atomic JSON read/write helper (temp-file swap on write).</summary>
public static class JsonStore
{
    public static async Task<T?> LoadAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, JsonHelper.Options).ConfigureAwait(false);
        }
        catch { return default; }
    }

    public static async Task SaveAsync<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, value, JsonHelper.Options).ConfigureAwait(false);

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }
}
