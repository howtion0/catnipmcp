using System.Net;
using System.Text;
using Catnip.DemoApi.Configuration;
using Catnip.DemoApi.Models;
using Catnip.DemoApi.Weather;
using Catnip.Shared.Business;

namespace Catnip.DemoApi.Tests;

public sealed class QWeatherServiceTests
{
    [Fact]
    public async Task GetCurrent_UsesGeoThenWeatherWithHeaderAndMapsFrozenContract()
    {
        using var directory = new TemporaryDirectory();
        WeatherCredentialStore store = await CreateConfiguredStoreAsync(directory.Path);
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/geo/v2/city/lookup" => Json("""
                {"code":"200","location":[{"name":"北京","id":"101010100"}]}
                """),
            "/v7/weather/now" => Json("""
                {"code":"200","now":{"obsTime":"2026-08-07T12:00+08:00","temp":"31","text":"晴"}}
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var service = new QWeatherService(
            store,
            new SingleClientFactory(new HttpClient(handler)));

        OperationResult<WeatherData> result = await service.GetCurrentAsync(
            "北京",
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("北京", result.Data?.City);
        Assert.Equal("晴", result.Data?.Condition);
        Assert.Equal(31, result.Data?.TemperatureC);
        Assert.Equal("QWeather", result.Data?.Source);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("unit.qweatherapi.com", request.Uri.Host);
            Assert.Equal("unit-test-key", request.ApiKey);
        });
        Assert.Contains("location=%E5%8C%97%E4%BA%AC", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("location=101010100", handler.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrent_MapsAuthenticationFailureWithoutReturningSecret()
    {
        using var directory = new TemporaryDirectory();
        WeatherCredentialStore store = await CreateConfiguredStoreAsync(directory.Path);
        var service = new QWeatherService(
            store,
            new SingleClientFactory(new HttpClient(new RecordingHandler(_ => Json("{" + "\"code\":\"401\"}")))));

        OperationResult<WeatherData> result = await service.GetCurrentAsync(
            "北京",
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("AUTH_FAILED", result.ErrorCode);
        Assert.DoesNotContain("unit-test-key", result.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrent_WhenNotConfigured_ReturnsStableErrorWithoutNetwork()
    {
        using var directory = new TemporaryDirectory();
        var store = new WeatherCredentialStore(CreateOptions(directory.Path));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not call"));
        var service = new QWeatherService(store, new SingleClientFactory(new HttpClient(handler)));

        OperationResult<WeatherData> result = await service.GetCurrentAsync(
            "北京",
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("CONFIGURATION_INVALID", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    private static async Task<WeatherCredentialStore> CreateConfiguredStoreAsync(string dataRoot)
    {
        var store = new WeatherCredentialStore(CreateOptions(dataRoot));
        await store.SaveAsync(
            new WeatherCredentialSaveRequest(
                "unit.qweatherapi.com",
                "unit-project",
                "project-test",
                "unit-credential",
                "credential-test",
                "unit-test-key",
                "北京"),
            TestContext.Current.CancellationToken);
        return store;
    }

    private static Catnip.DemoApi.Runtime.DemoApiOptions CreateOptions(string dataRoot) => new(
        "http://127.0.0.1:0",
        "http://127.0.0.1:5210",
        Path.Combine(AppContext.BaseDirectory, "Catnip.Runtime.dll"),
        dataRoot);

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? key = request.Headers.TryGetValues("X-QW-Api-Key", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            Requests.Add(new RecordedRequest(request.RequestUri!, key));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record RecordedRequest(Uri Uri, string? ApiKey);
}
