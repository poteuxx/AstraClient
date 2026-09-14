using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AstraClient.Services.Http;

/// <summary>Shared HttpClient with User-Agent and retry logic for the whole app.</summary>
public sealed class HttpClientProvider
{
    public HttpClient Client { get; }

    public HttpClientProvider()
    {
        Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("AstraClient/1.0 (github.com/astraclient)");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>GET + JSON deserialize with 3-attempt retry (429/5xx).</summary>
    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var response = await Client.GetAsync(url, ct).ConfigureAwait(false);

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    int delay = (int)Math.Pow(2, attempt) * 600;
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(content, JsonHelper.Options);
            }
            catch (OperationCanceledException) { throw; }
            catch when (attempt < 2)
            {
                await Task.Delay(800 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
        return default;
    }

    /// <summary>Download raw bytes with progress reporting.</summary>
    public async Task DownloadFileAsync(string url, string destPath,
        IProgress<(long bytes, long total)>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                                         .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? -1;

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = destPath + ".tmp";
        try
        {
            await using var src  = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dest = File.Create(tmp);

            var buf = new byte[81920];
            long received = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                progress?.Report((received, total));
            }
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }

        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(tmp, destPath);
    }
}
