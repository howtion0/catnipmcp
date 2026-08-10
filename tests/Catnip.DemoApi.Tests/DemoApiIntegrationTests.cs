using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catnip.DemoApi.Hosting;
using Catnip.DemoApi.Models;
using Catnip.DemoApi.Runtime;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Catnip.DemoApi.Tests;

public sealed class DemoApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = SharedJsonSerializerOptions.Create();

    [Fact]
    public async Task IndependentApi_ExposesHealthTodosStatusAndBoundedLogs()
    {
        using var directory = new TemporaryDirectory();
        await using WebApplication app = DemoApiApplication.Build([], CreateOptions(directory.Path));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient client = CreateClient(app);

        using HttpResponseMessage health = await client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);
        DemoTodoResponse? todos = await client.GetFromJsonAsync<DemoTodoResponse>(
            "/api/demo/todos",
            JsonOptions,
            TestContext.Current.CancellationToken);
        DemoRuntimeSnapshot? status = await client.GetFromJsonAsync<DemoRuntimeSnapshot>(
            "/api/runtime/status",
            JsonOptions,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage invalidLogRequest = await client.GetAsync(
            "/api/logs?take=501",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(3, todos?.Count);
        Assert.Equal(RuntimeProcessState.Stopped, status?.ProcessState);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLogRequest.StatusCode);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IndependentApi_StartsControlsAndGracefullyStopsOneRuntimeProcess()
    {
        using var directory = new TemporaryDirectory();
        await using WebApplication app = DemoApiApplication.Build([], CreateOptions(directory.Path));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using HttpClient client = CreateClient(app);

        Task<DemoControlResult?> firstStart = StartRuntimeAsync();
        Task<DemoControlResult?> secondStart = StartRuntimeAsync();
        DemoControlResult?[] starts = await Task.WhenAll(firstStart, secondStart);

        Assert.All(starts, result => Assert.True(result?.Success));
        Assert.Single(starts.Select(result => result?.Snapshot.ProcessId).Distinct());
        Assert.All(starts, result => Assert.Equal(RuntimeProcessState.Running, result?.Snapshot.ProcessState));

        using HttpResponseMessage masterResponse = await client.PutAsJsonAsync(
            "/api/runtime/master",
            new SetEnabledRequest(false),
            JsonOptions,
            TestContext.Current.CancellationToken);
        DemoControlResult? master = await masterResponse.Content.ReadFromJsonAsync<DemoControlResult>(
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.True(master?.Success);
        Assert.False(master?.Snapshot.MasterEnabled);

        using HttpResponseMessage modeResponse = await client.PutAsJsonAsync(
            "/api/runtime/mode",
            new SetModeRequest(GatewayMode.Custom),
            JsonOptions,
            TestContext.Current.CancellationToken);
        DemoControlResult? mode = await modeResponse.Content.ReadFromJsonAsync<DemoControlResult>(
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.True(mode?.Success);
        Assert.Equal(GatewayMode.Custom, mode?.Snapshot.Mode);

        using HttpResponseMessage moduleResponse = await client.PutAsJsonAsync(
            "/api/runtime/modules/today-todos",
            new SetEnabledRequest(false),
            JsonOptions,
            TestContext.Current.CancellationToken);
        DemoControlResult? module = await moduleResponse.Content.ReadFromJsonAsync<DemoControlResult>(
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.True(module?.Success);
        Assert.False(module?.Snapshot.Modules.Single(item => item.Id == "today-todos").Enabled);

        using HttpResponseMessage stopResponse = await client.PostAsync(
            "/api/runtime/stop",
            content: null,
            TestContext.Current.CancellationToken);
        DemoControlResult? stop = await stopResponse.Content.ReadFromJsonAsync<DemoControlResult>(
            JsonOptions,
            TestContext.Current.CancellationToken);
        RuntimeLogResponse? logs = await client.GetFromJsonAsync<RuntimeLogResponse>(
            "/api/logs?take=200",
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.True(stop?.Success);
        Assert.Equal(RuntimeProcessState.Stopped, stop?.Snapshot.ProcessState);
        Assert.NotEmpty(logs?.Lines ?? []);
        Assert.Contains(
            logs?.Lines ?? [],
            line => line.Message.Contains("Application is shutting down", StringComparison.Ordinal));
        await app.StopAsync(TestContext.Current.CancellationToken);

        async Task<DemoControlResult?> StartRuntimeAsync()
        {
            using HttpResponseMessage response = await client.PostAsync(
                "/api/runtime/start",
                content: null,
                TestContext.Current.CancellationToken);
            return await response.Content.ReadFromJsonAsync<DemoControlResult>(
                JsonOptions,
                TestContext.Current.CancellationToken);
        }
    }

    private static DemoApiOptions CreateOptions(string dataRoot)
    {
        int runtimePort = TestPorts.Reserve();
        return new DemoApiOptions(
            "http://127.0.0.1:0",
            $"http://127.0.0.1:{runtimePort}",
            Path.Combine(AppContext.BaseDirectory, "Catnip.Runtime.dll"),
            dataRoot);
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }
}
