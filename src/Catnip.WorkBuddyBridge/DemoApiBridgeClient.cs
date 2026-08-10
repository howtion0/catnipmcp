using System.Diagnostics;
using System.Net.Http.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.WorkBuddyBridge;

public sealed class DemoApiBridgeClient(HttpClient httpClient, BridgeLogStore logStore)
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        SharedJsonSerializerOptions.Create();

    public async Task<OperationResult<GatewayStatusData>> GetGatewayStatusAsync(
        CancellationToken cancellationToken = default)
    {
        const string tool = "catnip_get_gateway_status";
        string traceId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            DemoRuntimeSnapshot snapshot = await GetRequiredAsync<DemoRuntimeSnapshot>(
                "/api/runtime/status",
                cancellationToken).ConfigureAwait(false);
            var data = new GatewayStatusData(
                snapshot.ProcessState.ToString(),
                snapshot.MasterEnabled,
                snapshot.Mode.ToString(),
                snapshot.McpAddress,
                snapshot.Version,
                snapshot.StartedAt,
                snapshot.Modules,
                []);
            OperationResult<GatewayStatusData> result = OperationResult<GatewayStatusData>.Ok(data, traceId);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            OperationResult<GatewayStatusData> result = FailUnavailable<GatewayStatusData>(traceId);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<OperationResult<TodayTodoData>> GetTodayTodosAsync(
        CancellationToken cancellationToken = default)
    {
        const string tool = "catnip_get_today_todos";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            OperationResult<TodayTodoData> result = await GetRequiredAsync<OperationResult<TodayTodoData>>(
                "/api/mcp/todos",
                cancellationToken).ConfigureAwait(false);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            string traceId = Guid.NewGuid().ToString("N");
            OperationResult<TodayTodoData> result = FailUnavailable<TodayTodoData>(traceId);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<OperationResult<WeatherData>> GetWeatherAsync(
        string city,
        CancellationToken cancellationToken = default)
    {
        const string tool = "catnip_get_weather";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            string path = "/api/mcp/weather?city=" + Uri.EscapeDataString(city);
            OperationResult<WeatherData> result = await GetRequiredAsync<OperationResult<WeatherData>>(
                path,
                cancellationToken).ConfigureAwait(false);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            string traceId = Guid.NewGuid().ToString("N");
            OperationResult<WeatherData> result = FailUnavailable<WeatherData>(traceId);
            await WriteLogAsync(tool, result, stopwatch, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Demo API returned an empty {typeof(T).Name} response.");
    }

    private async Task WriteLogAsync<T>(
        string tool,
        OperationResult<T> result,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        await logStore.WriteAsync(
            tool,
            result.TraceId,
            result.Success,
            result.ErrorCode,
            stopwatch.ElapsedMilliseconds,
            cancellationToken).ConfigureAwait(false);
    }

    private static OperationResult<T> FailUnavailable<T>(string traceId) =>
        OperationResult<T>.Fail(
            "CONNECTOR_UNAVAILABLE",
            "本地 Demo API 未运行。请先打开Catnip 应用。",
            traceId);
}
