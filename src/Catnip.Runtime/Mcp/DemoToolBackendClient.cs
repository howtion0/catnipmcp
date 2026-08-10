using System.Net.Http.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Errors;
using Catnip.Shared.Serialization;

namespace Catnip.Runtime.Mcp;

public interface IDemoToolBackendClient
{
    Task<OperationResult<TodayTodoData>> GetTodayTodosAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<WeatherData>> GetWeatherAsync(
        string city,
        CancellationToken cancellationToken = default);
}

public sealed class DemoToolBackendClient(HttpClient httpClient) : IDemoToolBackendClient
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        SharedJsonSerializerOptions.Create();

    public async Task<OperationResult<TodayTodoData>> GetTodayTodosAsync(
        CancellationToken cancellationToken = default)
    {
        string traceId = Guid.NewGuid().ToString("N");
        try
        {
            DemoTodoResponse response = await GetRequiredAsync<DemoTodoResponse>(
                "/api/demo/todos",
                cancellationToken).ConfigureAwait(false);
            TodayTodoItemDto[] items = response.Items.Select(item => new TodayTodoItemDto(
                item.Id,
                item.Source,
                item.Id,
                item.Type,
                item.Title,
                item.Description,
                null,
                item.DueTime,
                item.Priority,
                item.Status,
                null,
                null,
                null)).ToArray();
            return OperationResult<TodayTodoData>.Ok(
                new TodayTodoData(response.Date, response.Count, items),
                response.TraceId);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            return OperationResult<TodayTodoData>.Fail(
                ErrorCodes.ConnectorUnavailable,
                "Demo API 暂不可用。",
                traceId);
        }
    }

    public async Task<OperationResult<WeatherData>> GetWeatherAsync(
        string city,
        CancellationToken cancellationToken = default)
    {
        string traceId = Guid.NewGuid().ToString("N");
        try
        {
            string path = "/api/weather?city=" + Uri.EscapeDataString(city);
            return await GetRequiredAsync<OperationResult<WeatherData>>(path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            return OperationResult<WeatherData>.Fail(
                ErrorCodes.ConnectorUnavailable,
                "Demo API 暂不可用。",
                traceId);
        }
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(path, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Demo API returned an empty {typeof(T).Name} response.");
    }

    private sealed record DemoTodoItem(
        string Id,
        string Source,
        string Type,
        string Title,
        string Description,
        DateTimeOffset? DueTime,
        string Priority,
        string Status);

    private sealed record DemoTodoResponse(
        string Date,
        int Count,
        IReadOnlyList<DemoTodoItem> Items,
        string TraceId);
}
