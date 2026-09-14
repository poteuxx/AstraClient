using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstraClient.Models;
using AstraClient.Services.Data;
using AstraClient.Services.Http;

namespace AstraClient.Services.Auth;

/// <summary>
/// Microsoft OAuth2 device-code flow → Xbox Live → XSTS → Minecraft token → profile.
/// REQUIRES a user-supplied Azure Application Client ID (Settings → Account).
/// Cannot embed one — Azure policy forbids embedding a shared launcher Client ID.
/// </summary>
public sealed class MicrosoftAuthProvider : IAuthProvider
{
    private readonly HttpClientProvider _http;
    private readonly SettingsService _settings;

    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl      = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblUrl        = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl       = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McAuthUrl     = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl  = "https://api.minecraftservices.com/minecraft/profile";
    private const string Scope         = "XboxLive.signin offline_access";

    // Raised so the UI can display the device code + URL to the user.
    public event Action<string /*deviceCode*/, string /*verificationUri*/>? DeviceCodeReceived;
    public event Action<string>? StatusChanged;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.Current.AzureClientId);

    public MicrosoftAuthProvider(HttpClientProvider http, SettingsService settings)
    {
        _http     = http;
        _settings = settings;
    }

    public async Task<UserProfile?> AuthenticateAsync(CancellationToken ct = default)
    {
        StatusChanged?.Invoke("Requesting a Microsoft sign-in code...");
        var clientId = _settings.Current.AzureClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "Azure Application Client ID is required for Microsoft sign-in.\n" +
                "Add it in Settings → Account → Azure Client ID.");

        // Step 1: Get device code.
        var dcResponse = await PostFormAsync(DeviceCodeUrl, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"]     = Scope
        }, ct).ConfigureAwait(false);

        ThrowIfOAuthError(dcResponse);

        var deviceCode     = dcResponse.GetProperty("device_code").GetString()!;
        var userCode       = dcResponse.GetProperty("user_code").GetString()!;
        var verificationUri = dcResponse.GetProperty("verification_uri").GetString()!;
        int interval       = dcResponse.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;

        DeviceCodeReceived?.Invoke(userCode, verificationUri);
        StatusChanged?.Invoke("Waiting for Microsoft sign-in to finish in your browser...");

        // Step 2: Poll for token.
        JsonElement tokenResponse = default;
        string? lastError = null;
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(interval * 1000, ct).ConfigureAwait(false);
            tokenResponse = await PostFormAsync(TokenUrl, new Dictionary<string, string>
            {
                ["client_id"]   = clientId,
                ["grant_type"]  = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode
            }, ct).ConfigureAwait(false);

            if (tokenResponse.TryGetProperty("access_token", out _)) break;

            lastError = GetOAuthError(tokenResponse);
            if (tokenResponse.TryGetProperty("error", out var error) &&
                error.GetString() == "slow_down")
            {
                interval += 5;
            }
            else if (tokenResponse.TryGetProperty("error", out error) &&
                     error.GetString() != "authorization_pending")
            {
                throw new InvalidOperationException(lastError);
            }
        }

        if (!tokenResponse.TryGetProperty("access_token", out var accessToken))
            throw new InvalidOperationException(lastError ?? "Microsoft sign-in timed out. Complete the device sign-in and try again.");

        var msAccessToken = accessToken.GetString()!;

        // Step 3: Xbox Live.
        StatusChanged?.Invoke("Microsoft approved. Connecting to Xbox Live...");
        var xblToken = await GetXblTokenAsync(msAccessToken, ct).ConfigureAwait(false);

        // Step 4: XSTS.
        StatusChanged?.Invoke("Verifying Xbox account...");
        var (xstsToken, uhs) = await GetXstsTokenAsync(xblToken, ct).ConfigureAwait(false);

        // Step 5: Minecraft token.
        StatusChanged?.Invoke("Connecting to Minecraft services...");
        var mcToken = await GetMinecraftTokenAsync(xstsToken, uhs, ct).ConfigureAwait(false);

        // Step 6: Profile.
        StatusChanged?.Invoke("Loading Minecraft profile...");
        var profile = await GetMinecraftProfileAsync(mcToken, ct).ConfigureAwait(false);

        return profile;
    }

    private async Task<string> GetXblTokenAsync(string msToken, CancellationToken ct)
    {
        var body = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName   = "user.auth.xboxlive.com",
                RpsTicket  = $"d={msToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType    = "JWT"
        };
        var response = await _http.Client.PostAsync(XblUrl,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct)
            .ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, json, "Xbox Live");
        return JsonDocument.Parse(json).RootElement.GetProperty("Token").GetString()!;
    }

    private async Task<(string token, string uhs)> GetXstsTokenAsync(string xblToken, CancellationToken ct)
    {
        var body = new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType    = "JWT"
        };
        var response = await _http.Client.PostAsync(XstsUrl,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct)
            .ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, json, "Xbox security token");
        var doc  = JsonDocument.Parse(json).RootElement;
        var token = doc.GetProperty("Token").GetString()!;
        var uhs   = doc.GetProperty("DisplayClaims")
                       .GetProperty("xui")[0]
                       .GetProperty("uhs").GetString()!;
        return (token, uhs);
    }

    private async Task<string> GetMinecraftTokenAsync(string xstsToken, string uhs, CancellationToken ct)
    {
        var body = new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" };
        var response = await _http.Client.PostAsync(McAuthUrl,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct)
            .ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, json, "Minecraft authentication");
        return JsonDocument.Parse(json).RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<UserProfile> GetMinecraftProfileAsync(string mcToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        var response = await _http.Client.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, json, "Minecraft profile");
        var doc  = JsonDocument.Parse(json).RootElement;

        return new UserProfile
        {
            Username    = doc.GetProperty("name").GetString()!,
            Uuid        = doc.GetProperty("id").GetString()!,
            AccessToken = mcToken,
            Type        = AuthType.Microsoft
        };
    }

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> fields, CancellationToken ct)
    {
        var form     = new FormUrlEncodedContent(fields);
        var response = await _http.Client.PostAsync(url, form, ct).ConfigureAwait(false);
        var json     = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(json).RootElement;
    }

    private static void ThrowIfOAuthError(JsonElement response)
    {
        if (response.TryGetProperty("error", out _))
            throw new InvalidOperationException(GetOAuthError(response));
    }

    private static string GetOAuthError(JsonElement response)
    {
        if (response.TryGetProperty("error_description", out var description))
            return description.GetString() ?? "Microsoft authorization failed.";

        if (response.TryGetProperty("error", out var error))
            return error.GetString() ?? "Microsoft authorization failed.";

        return "Microsoft authorization failed.";
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string stage)
    {
        if (response.IsSuccessStatusCode) return;

        var detail = body;
        if (body.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
        {
            detail = "This Azure app is not registered for Minecraft authentication. " +
                     "Microsoft must approve the launcher application through the MSA application " +
                     "registration process; Azure settings alone are not sufficient.";
        }
        if (ReferenceEquals(detail, body))
        {
            try
            {
                var json = JsonDocument.Parse(body).RootElement;
                if (json.TryGetProperty("XErr", out var xerr))
                {
                    var message = json.TryGetProperty("Message", out var messageProperty)
                        ? messageProperty.GetString()
                        : null;
                    detail = $"Xbox error {xerr}: {message ?? "Xbox account is not eligible for this sign-in."}";
                }
                else if (json.TryGetProperty("Message", out var message))
                    detail = message.GetString() ?? body;
                else if (json.TryGetProperty("errorMessage", out var errorMessage))
                    detail = errorMessage.GetString() ?? body;
                else if (json.TryGetProperty("error", out var error))
                    detail = error.GetString() ?? body;
            }
            catch
            {
                // Keep the raw response when the service does not return JSON.
            }
        }

        throw new HttpRequestException(
            $"{stage} rejected the sign-in ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }
}
