using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Felix.Models;

namespace Felix.Services;

public sealed class FelixApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IPluginLog pluginLog;

    public FelixApiClient(IPluginLog pluginLog)
    {
        this.pluginLog = pluginLog;
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public async Task<FelixPairResponseEnvelope> PairAsync(string dashboardBaseUrl, FelixPairRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/pair"))
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<FelixPairResponseEnvelope>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || payload is null || !payload.Ok)
        {
            throw new InvalidOperationException($"Felix pairing failed ({(int)response.StatusCode}).");
        }

        return payload;
    }

    public async Task UploadSnapshotAsync(string dashboardBaseUrl, string deviceToken, FelixSnapshotPayload payload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/snapshot"))
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            this.pluginLog.Warning($"Felix snapshot upload failed: {(int)response.StatusCode} {body}");
            throw new InvalidOperationException($"Felix snapshot upload failed ({(int)response.StatusCode}).");
        }
    }

    public async Task<FelixMirrorRenderJobPayload?> GetMirrorRenderJobAsync(string dashboardBaseUrl, string deviceToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/mirror/render-job"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<FelixMirrorRenderJobEnvelope>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || payload is null || !payload.Ok)
        {
            throw new InvalidOperationException($"Felix mirror render job fetch failed ({(int)response.StatusCode}).");
        }

        return payload.Job;
    }

    public async Task SubmitMirrorRenderResultAsync(string dashboardBaseUrl, string deviceToken, FelixMirrorRenderResultRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/mirror/render-result"))
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            this.pluginLog.Warning($"Felix mirror render result upload failed: {(int)response.StatusCode} {body}");
            throw new InvalidOperationException($"Felix mirror render result upload failed ({(int)response.StatusCode}).");
        }
    }

    public async Task<FelixRpSceneJoinResponse> JoinRpSceneAsync(
        string dashboardBaseUrl,
        string deviceToken,
        FelixRpSceneJoinRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/rp-scene/join"))
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<FelixRpSceneJoinResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return new FelixRpSceneJoinResponse
            {
                Ok = false,
                Error = $"Invalid response ({(int)response.StatusCode}).",
            };
        }

        if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(payload.Error))
        {
            payload.Ok = false;
            payload.Error = $"HTTP {(int)response.StatusCode}";
        }

        return payload;
    }

    public async Task<(bool Ok, int StatusCode)> PostRpSceneLinesAsync(
        string dashboardBaseUrl,
        string deviceToken,
        FelixRpSceneLinesRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(dashboardBaseUrl, "/api/ffxiv/felix/plugin/rp-scene/lines"))
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

        using var response = await this.httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<FelixRpSceneLinesResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var ok = response.IsSuccessStatusCode && payload is { Ok: true };
        return (ok, (int)response.StatusCode);
    }

    private static Uri BuildUrl(string dashboardBaseUrl, string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(dashboardBaseUrl) ? "https://felixthebot.com" : dashboardBaseUrl.Trim();
        baseUrl = baseUrl.TrimEnd('/');
        relativePath = relativePath.StartsWith("/", StringComparison.Ordinal) ? relativePath : $"/{relativePath}";
        return new Uri($"{baseUrl}{relativePath}", UriKind.Absolute);
    }
}
