using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;

namespace AstraClient.Services.Imaging;

/// <summary>
/// Two-level image cache (memory LRU + disk) for Modrinth icons and gallery images.
/// All public methods are thread-safe; the returned BitmapImage is always frozen.
/// </summary>
public sealed class ImageCacheService
{
    private readonly string _cacheDir;
    private readonly HttpClient _http;
    private readonly Dictionary<string, BitmapImage?> _mem = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, Task<BitmapImage?>> _inflight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadGate = new(initialCount: 4, maxCount: 4);
    private const int MaxMem = 100;

    public ImageCacheService(string cacheDir, HttpClient http)
    {
        _cacheDir = cacheDir;
        _http = http;
        Directory.CreateDirectory(cacheDir);
    }

    /// <summary>
    /// Returns a frozen BitmapImage for the given URL, downloading + caching it on first call.
    /// Always resolves on a background thread; the caller must marshal to the UI thread.
    /// </summary>
    public async Task<BitmapImage?> GetBitmapAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Memory check (under lock for thread safety).
        lock (_mem)
        {
            if (_mem.TryGetValue(url, out var cached))
            {
                Promote(url);
                return cached;
            }
        }

        Task<BitmapImage?> loadTask;
        lock (_mem)
        {
            if (!_inflight.TryGetValue(url, out loadTask!))
            {
                loadTask = LoadAndCacheAsync(url);
                _inflight[url] = loadTask;
            }
        }

        try
        {
            return await loadTask.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                lock (_mem)
                    _inflight.Remove(url);
            }
        }
    }

    private async Task<BitmapImage?> LoadAndCacheAsync(string url)
    {
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var diskPath = GetDiskPath(url);
            BitmapImage? bitmap = null;

            if (File.Exists(diskPath))
            {
                bitmap = await LoadFromDiskAsync(diskPath, CancellationToken.None).ConfigureAwait(false);
            }

            // A partial or corrupt disk entry should not permanently hide a project's artwork.
            if (bitmap is null)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                    using var response = await _http.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length == 0)
                        throw new InvalidDataException("The image response was empty.");

                    bytes = await NormalizeForWpfAsync(bytes).ConfigureAwait(false);

                    var temporaryPath = diskPath + ".tmp";
                    await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(false);
                    File.Move(temporaryPath, diskPath, overwrite: true);
                    bitmap = await LoadFromDiskAsync(diskPath, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    bitmap = null;
                    if (File.Exists(diskPath))
                        File.Delete(diskPath);
                }
            }

            if (bitmap is not null)
            {
                lock (_mem)
                {
                    if (!_mem.ContainsKey(url))
                    {
                        _mem[url] = bitmap;
                        _lru.AddFirst(url);
                        while (_lru.Count > MaxMem && _lru.Last is not null)
                        {
                            _mem.Remove(_lru.Last.Value);
                            _lru.RemoveLast();
                        }
                    }
                }
            }

            return bitmap;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static async Task<BitmapImage?> LoadFromDiskAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            bytes = await NormalizeForWpfAsync(bytes).ConfigureAwait(false);
            // BitmapImage must be created on the UI thread or an STA thread.
            BitmapImage? result = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    using var ms = new MemoryStream(bytes);
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption  = BitmapCacheOption.OnLoad;
                    // 256 px is crisp for cards/gallery thumbnails while keeping memory bounded.
                    bmp.DecodePixelWidth = 256;
                    bmp.EndInit();
                    bmp.Freeze();
                    result = bmp;
                }
                catch { result = null; }
            });
            return result;
        }
        catch { return null; }
    }

    private static async Task<byte[]> NormalizeForWpfAsync(byte[] bytes)
    {
        if (bytes.Length < 12
            || bytes[0] != (byte)'R' || bytes[1] != (byte)'I'
            || bytes[2] != (byte)'F' || bytes[3] != (byte)'F'
            || bytes[8] != (byte)'W' || bytes[9] != (byte)'E'
            || bytes[10] != (byte)'B' || bytes[11] != (byte)'P')
            return bytes;

        using var image = Image.Load(bytes);
        using var png = new MemoryStream();
        await image.SaveAsPngAsync(png).ConfigureAwait(false);
        return png.ToArray();
    }

    private string GetDiskPath(string url)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_cacheDir, hash);
    }

    private void Promote(string key)
    {
        var node = _lru.Find(key);
        if (node is not null) { _lru.Remove(node); _lru.AddFirst(key); }
    }
}
