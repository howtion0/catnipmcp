using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Catnip.DemoApi.Configuration;
using Catnip.DemoApi.Models;
using Catnip.Shared.Business;

namespace Catnip.DemoApi.Weather;

public sealed class QWeatherService(WeatherCredentialStore credentialStore, IHttpClientFactory httpClientFactory)
{
    public async Task<OperationResult<WeatherData>> GetCurrentAsync(
        string city,
        CancellationToken cancellationToken = default)
    {
        string traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        string normalizedCity = city.Trim();
        if (normalizedCity.Length is < 1 or > 50)
        {
            return OperationResult<WeatherData>.Fail(
                "VALIDATION_ERROR",
                "城市名称长度必须为 1-50。",
                traceId);
        }

        WeatherCredential? credential = await credentialStore.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        if (credential is null || string.IsNullOrWhiteSpace(credential.ApiHost))
        {
            return OperationResult<WeatherData>.Fail(
                "CONFIGURATION_INVALID",
                "请先在 API 密钥配置页保存和风天气 API Host、凭据 ID 和 API KEY。",
                traceId);
        }

        try
        {
            HttpClient client = httpClientFactory.CreateClient("qweather");
            QWeatherLocation location = await LookupCityAsync(
                client,
                credential,
                normalizedCity,
                cancellationToken).ConfigureAwait(false);
            QWeatherNowResponse current = await GetNowAsync(
                client,
                credential,
                location.Id,
                cancellationToken).ConfigureAwait(false);
            if (current.Now is null
                || !decimal.TryParse(current.Now.Temp, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal temperature))
            {
                return OperationResult<WeatherData>.Fail(
                    "UPSTREAM_ERROR",
                    "和风天气返回的数据格式不完整。",
                    traceId);
            }

            DateTimeOffset observedAt = DateTimeOffset.TryParse(
                current.Now.ObsTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
            return OperationResult<WeatherData>.Ok(
                new WeatherData(location.Name, current.Now.Text, temperature, "QWeather", observedAt),
                traceId);
        }
        catch (QWeatherException exception)
        {
            return OperationResult<WeatherData>.Fail(exception.ErrorCode, exception.Message, traceId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult<WeatherData>.Fail("UPSTREAM_TIMEOUT", "和风天气请求超时。", traceId);
        }
        catch (HttpRequestException)
        {
            return OperationResult<WeatherData>.Fail("UPSTREAM_ERROR", "暂时无法连接和风天气。", traceId);
        }
        catch (System.Text.Json.JsonException)
        {
            return OperationResult<WeatherData>.Fail("UPSTREAM_ERROR", "和风天气响应无法解析。", traceId);
        }
    }

    public async Task<WeatherConnectionTestResult> TestAsync(
        string? city,
        CancellationToken cancellationToken = default)
    {
        WeatherCredential? credential = await credentialStore.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        string selectedCity = string.IsNullOrWhiteSpace(city) ? credential?.DefaultCity ?? string.Empty : city;
        var stopwatch = Stopwatch.StartNew();
        OperationResult<WeatherData> result = await GetCurrentAsync(selectedCity, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();
        return new WeatherConnectionTestResult(result, stopwatch.ElapsedMilliseconds);
    }

    private static async Task<QWeatherLocation> LookupCityAsync(
        HttpClient client,
        WeatherCredential credential,
        string city,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildUri(
            credential.ApiHost,
            "/geo/v2/city/lookup",
            $"location={Uri.EscapeDataString(city)}&number=1&lang=zh");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-QW-Api-Key", credential.ApiKey);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        QWeatherLocationResponse payload = await ReadPayloadAsync<QWeatherLocationResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        if (payload.Code is "401" or "403")
        {
            throw new QWeatherException("AUTH_FAILED", "和风天气鉴权失败，请检查 API KEY 和专属 API Host。");
        }

        if (payload.Code != "200")
        {
            throw new QWeatherException("UPSTREAM_ERROR", $"和风天气城市查询失败（代码 {payload.Code}）。");
        }

        return payload.Location?.FirstOrDefault()
            ?? throw new QWeatherException("NOT_FOUND", "未找到该城市，请补充省份或使用 LocationID。");
    }

    private static async Task<QWeatherNowResponse> GetNowAsync(
        HttpClient client,
        WeatherCredential credential,
        string locationId,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildUri(
            credential.ApiHost,
            "/v7/weather/now",
            $"location={Uri.EscapeDataString(locationId)}&lang=zh");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-QW-Api-Key", credential.ApiKey);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        QWeatherNowResponse payload = await ReadPayloadAsync<QWeatherNowResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        if (payload.Code is "401" or "403")
        {
            throw new QWeatherException("AUTH_FAILED", "和风天气鉴权失败，请检查 API KEY 和专属 API Host。");
        }

        if (payload.Code != "200")
        {
            throw new QWeatherException("UPSTREAM_ERROR", $"和风天气实时天气查询失败（代码 {payload.Code}）。");
        }

        return payload;
    }

    private static async Task<T> ReadPayloadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 401 or 403)
            {
                throw new QWeatherException("AUTH_FAILED", "和风天气鉴权失败，请检查 API KEY 和专属 API Host。");
            }

            throw new QWeatherException("UPSTREAM_ERROR", $"和风天气 HTTP 请求失败（{(int)response.StatusCode}）。");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new QWeatherException("UPSTREAM_ERROR", "和风天气返回空响应。");
    }

    private static Uri BuildUri(string host, string path, string query)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, host)
        {
            Path = path,
            Query = query,
        };
        return builder.Uri;
    }

    private sealed record QWeatherLocationResponse(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("location")] IReadOnlyList<QWeatherLocation>? Location);

    private sealed record QWeatherLocation(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("id")] string Id);

    private sealed record QWeatherNowResponse(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("now")] QWeatherNow? Now);

    private sealed record QWeatherNow(
        [property: JsonPropertyName("obsTime")] string ObsTime,
        [property: JsonPropertyName("temp")] string Temp,
        [property: JsonPropertyName("text")] string Text);

    private sealed class QWeatherException(string errorCode, string message) : Exception(message)
    {
        public string ErrorCode { get; } = errorCode;
    }
}
