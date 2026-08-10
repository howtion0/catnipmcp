using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Catnip.Desktop.Mac.Models;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Desktop.Mac.Services;

public sealed class DemoApiClient : IDemoApiClient
{
    public const string DefaultAddress = DemoApiClientAddress.Value;
    private static readonly JsonSerializerOptions JsonOptions = SharedJsonSerializerOptions.Create();
    private readonly HttpClient _httpClient;

    public DemoApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (httpClient.BaseAddress is null
            || !httpClient.BaseAddress.IsLoopback
            || httpClient.BaseAddress.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Demo API client must use an HTTP loopback base address.", nameof(httpClient));
        }

        _httpClient = httpClient;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                "/health/live",
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public Task<DemoRuntimeSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<DemoRuntimeSnapshot>("/api/runtime/status", cancellationToken);
    }

    public Task<DemoControlResult> StartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<DemoControlResult>(HttpMethod.Post, "/api/runtime/start", null, cancellationToken);
    }

    public Task<DemoControlResult> StopRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<DemoControlResult>(HttpMethod.Post, "/api/runtime/stop", null, cancellationToken);
    }

    public Task<DemoControlResult> SetMasterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<DemoControlResult>(
            HttpMethod.Put,
            "/api/runtime/master",
            JsonContent.Create(new SetEnabledRequest(enabled), options: JsonOptions),
            cancellationToken);
    }

    public Task<DemoControlResult> SetModeAsync(
        GatewayMode mode,
        CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<DemoControlResult>(
            HttpMethod.Put,
            "/api/runtime/mode",
            JsonContent.Create(new SetModeRequest(mode), options: JsonOptions),
            cancellationToken);
    }

    public Task<DemoControlResult> SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        string escapedModuleId = Uri.EscapeDataString(moduleId);
        return SendRequiredAsync<DemoControlResult>(
            HttpMethod.Put,
            $"/api/runtime/modules/{escapedModuleId}",
            JsonContent.Create(new SetEnabledRequest(enabled), options: JsonOptions),
            cancellationToken);
    }

    public Task<DemoTodoResponse> GetTodayTodosAsync(CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<DemoTodoResponse>("/api/demo/todos", cancellationToken);
    }

    public Task<RuntimeLogResponse> GetLogsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        return GetRequiredAsync<RuntimeLogResponse>($"/api/logs?take={take}", cancellationToken);
    }

    public Task<WeatherCredentialView> GetWeatherCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<WeatherCredentialView>("/api/config/weather", cancellationToken);
    }

    public Task<WeatherCredentialView> SaveWeatherCredentialAsync(
        WeatherCredentialSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendRequiredAsync<WeatherCredentialView>(
            HttpMethod.Put,
            "/api/config/weather",
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);
    }

    public Task<WeatherConnectionTestResult> TestWeatherAsync(
        string? city,
        CancellationToken cancellationToken = default)
    {
        return SendRequiredAsync<WeatherConnectionTestResult>(
            HttpMethod.Post,
            "/api/config/weather/test",
            JsonContent.Create(new WeatherConnectionTestRequest(city), options: JsonOptions),
            cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Demo API returned an empty {typeof(T).Name} response.");
    }

    private async Task<T> SendRequiredAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content,
        };
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Demo API returned an empty {typeof(T).Name} response.");
    }
}
