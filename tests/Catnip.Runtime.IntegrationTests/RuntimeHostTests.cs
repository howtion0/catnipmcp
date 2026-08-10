using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Catnip.Core.Modules;
using Catnip.Ipc.Management;
using Catnip.Runtime.Hosting;
using Catnip.Runtime.Security;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Catnip.Runtime.IntegrationTests;

public sealed class RuntimeHostTests
{
    [Fact]
    public async Task Live_Returns200WithoutConfigurationDetails()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync();

        HttpResponseMessage response = await runtime.Client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"live\"}", body);
    }

    [Fact]
    public async Task Ready_DefaultsTo503UntilDependenciesAreReady()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync();

        HttpResponseMessage response = await runtime.Client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            new HealthStatus("notReady"),
            await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_Returns200WhenMcpApiKeyIsConfigured()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(CreateApiKey());

        HttpResponseMessage response = await runtime.Client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new HealthStatus("ready"),
            await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mcp_RejectsMissingAndIncorrectApiKeys()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(CreateApiKey());
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        HttpResponseMessage missing = await runtime.Client.PostAsync(
            "/mcp",
            content,
            TestContext.Current.CancellationToken);
        using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        wrongRequest.Headers.Add(McpApiKeyMiddleware.HeaderName, CreateApiKey());
        HttpResponseMessage wrong = await runtime.Client.SendAsync(
            wrongRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Mcp_OfficialClientDiscoversAndCallsGatewayStatus()
    {
        string apiKey = CreateApiKey();
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(apiKey);
        await using var managementClient = new NamedPipeManagementClient(runtime.ManagementPipeName);
        await managementClient.ConnectAsync(TestContext.Current.CancellationToken);
        ManagementResponse setMaster = await managementClient.SetMasterEnabledAsync(
            enabled: true,
            TestContext.Current.CancellationToken);
        ManagementResponse setMode = await managementClient.SetGatewayModeAsync(
            GatewayMode.Custom,
            TestContext.Current.CancellationToken);
        ManagementResponse setModule = await managementClient.SetModuleEnabledAsync(
            ModuleIds.TodayTodos,
            enabled: false,
            TestContext.Current.CancellationToken);
        JsonElement httpStatus = await runtime.Client.GetFromJsonAsync<JsonElement>(
            "/status",
            TestContext.Current.CancellationToken);
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(runtime.Client.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>
            {
                [McpApiKeyMiddleware.HeaderName] = apiKey,
            },
        };
        await using McpClient client = await McpClient.CreateAsync(
            new HttpClientTransport(transportOptions),
            cancellationToken: TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        string[] toolNames = tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
        var result = await client.CallToolAsync(
            "catnip_get_gateway_status",
            cancellationToken: TestContext.Current.CancellationToken);
        var todoResult = await client.CallToolAsync(
            "catnip_get_today_todos",
            cancellationToken: TestContext.Current.CancellationToken);
        var weatherResult = await client.CallToolAsync(
            "catnip_get_weather",
            new Dictionary<string, object?> { ["city"] = "上海" },
            cancellationToken: TestContext.Current.CancellationToken);
        string structuredContent = JsonSerializer.Serialize(result.StructuredContent);
        string todoContent = JsonSerializer.Serialize(todoResult.StructuredContent);
        string weatherContent = JsonSerializer.Serialize(weatherResult.StructuredContent);

        Assert.Equal(
            ["catnip_get_gateway_status", "catnip_get_today_todos", "catnip_get_weather"],
            toolNames);
        Assert.True(setMaster.Success);
        Assert.True(setMode.Success);
        Assert.True(setModule.Success);
        Assert.True(httpStatus.GetProperty("masterEnabled").GetBoolean());
        Assert.Equal("custom", httpStatus.GetProperty("mode").GetString());
        JsonElement httpTodos = httpStatus.GetProperty("modules")
            .EnumerateArray()
            .Single(static module => module.GetProperty("id").GetString() == ModuleIds.TodayTodos);
        Assert.False(httpTodos.GetProperty("enabled").GetBoolean());
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Contains("traceId", structuredContent, StringComparison.Ordinal);
        Assert.Contains("Running", structuredContent, StringComparison.Ordinal);
        Assert.Contains("\"masterEnabled\":true", structuredContent, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"today-todos\"", structuredContent, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.ModuleDisabled, todoContent, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.ModuleDisabled, weatherContent, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, structuredContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagementPipe_PingAndSnapshotReturnRuntimeState()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(CreateApiKey());
        await using var client = new NamedPipeManagementClient(runtime.ManagementPipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        ManagementResponse ping = await client.PingAsync(TestContext.Current.CancellationToken);
        ManagementResponse snapshotResponse = await client.GetRuntimeSnapshotAsync(
            TestContext.Current.CancellationToken);
        RuntimeSnapshot? snapshot = snapshotResponse.Payload?.Deserialize<RuntimeSnapshot>(
            SharedJsonSerializerOptions.Create());

        Assert.True(ping.Success);
        Assert.True(snapshotResponse.Success);
        Assert.NotNull(snapshot);
        Assert.Equal(RuntimeProcessState.Running, snapshot.ProcessState);
    }

    [Fact]
    public async Task ManagementPipe_RejectsInvalidProtocolUnknownCommandAndForcedShutdown()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(CreateApiKey());
        await using var client = new NamedPipeManagementClient(runtime.ManagementPipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        JsonElement emptyPayload = JsonSerializer.SerializeToElement(new EmptyCommand());

        ManagementResponse invalidProtocol = await client.SendAsync(
            new ManagementRequest(99, Guid.NewGuid(), "Ping", DateTimeOffset.UtcNow, emptyPayload),
            TestContext.Current.CancellationToken);
        ManagementResponse unknown = await client.SendAsync(
            new ManagementRequest(1, Guid.NewGuid(), "Unknown", DateTimeOffset.UtcNow, emptyPayload),
            TestContext.Current.CancellationToken);
        ManagementResponse forced = await client.ShutdownRuntimeAsync(
            graceful: false,
            TestContext.Current.CancellationToken);

        Assert.False(invalidProtocol.Success);
        Assert.Equal(ErrorCodes.IpcError, invalidProtocol.ErrorCode);
        Assert.False(unknown.Success);
        Assert.Equal(ErrorCodes.ValidationError, unknown.ErrorCode);
        Assert.False(forced.Success);
        Assert.Equal(ErrorCodes.ValidationError, forced.ErrorCode);
        Assert.False(runtime.App.Lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task ManagementPipe_GracefulShutdownRespondsBeforeStoppingHost()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync(CreateApiKey());
        await using var client = new NamedPipeManagementClient(runtime.ManagementPipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.App.Lifetime.ApplicationStopping.Register(stopping.SetResult);

        ManagementResponse response = await client.ShutdownRuntimeAsync(
            graceful: true,
            TestContext.Current.CancellationToken);
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Equal(RuntimeProcessState.Stopping, runtime.State.GetSnapshot().ProcessState);
        Assert.False(runtime.State.IsReady);
    }

    [Fact]
    public async Task Version_ReturnsProductAndProtocolMetadataWithoutSecrets()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync();

        JsonElement version = await runtime.Client.GetFromJsonAsync<JsonElement>(
            "/version",
            TestContext.Current.CancellationToken);
        string json = version.GetRawText();

        Assert.Equal(1, version.GetProperty("ipcProtocolVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("productVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("mcpSdkVersion").GetString()));
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_ReturnsRunningSnapshotWithNonNullCollections()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync();

        JsonElement status = await runtime.Client.GetFromJsonAsync<JsonElement>(
            "/status",
            TestContext.Current.CancellationToken);

        Assert.Equal("running", status.GetProperty("processState").GetString());
        Assert.False(status.GetProperty("masterEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, status.GetProperty("modules").ValueKind);
        Assert.Equal(JsonValueKind.Array, status.GetProperty("connectors").ValueKind);
    }

    [Fact]
    public async Task ControlState_PersistsAcrossRuntimeRestartWithoutPersistingApiKey()
    {
        using var dataRoot = new TemporaryDirectory();
        string apiKey = CreateApiKey();

        await using (RunningRuntime first = await RunningRuntime.StartAsync(apiKey, dataRoot.Path))
        {
            Assert.False(first.State.GetSnapshot().MasterEnabled);

            await using var client = new NamedPipeManagementClient(first.ManagementPipeName);
            await client.ConnectAsync(TestContext.Current.CancellationToken);

            Assert.True((await client.SetGatewayModeAsync(
                GatewayMode.Custom,
                TestContext.Current.CancellationToken)).Success);
            Assert.True((await client.SetModuleEnabledAsync(
                ModuleIds.Weather,
                enabled: true,
                TestContext.Current.CancellationToken)).Success);
            Assert.True((await client.SetModuleEnabledAsync(
                ModuleIds.TodayTodos,
                enabled: false,
                TestContext.Current.CancellationToken)).Success);
            Assert.True((await client.SetMasterEnabledAsync(
                enabled: true,
                TestContext.Current.CancellationToken)).Success);
        }

        await using RunningRuntime restarted = await RunningRuntime.StartAsync(apiKey, dataRoot.Path);
        RuntimeSnapshot snapshot = restarted.State.GetSnapshot();
        ModuleInfoDto weather = Assert.Single(
            snapshot.Modules,
            static module => module.Id == ModuleIds.Weather);
        ModuleInfoDto todos = Assert.Single(
            snapshot.Modules,
            static module => module.Id == ModuleIds.TodayTodos);
        string persisted = await File.ReadAllTextAsync(
            Path.Combine(dataRoot.Path, "settings.json"),
            TestContext.Current.CancellationToken);

        Assert.True(snapshot.MasterEnabled);
        Assert.Equal(GatewayMode.Custom, snapshot.Mode);
        Assert.True(weather.Enabled);
        Assert.False(todos.Enabled);
        Assert.DoesNotContain(apiKey, persisted, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(dataRoot.Path, "settings.json.bak")));
    }

    [Fact]
    public async Task StopAsync_CompletesGracefullyAndMarksStopping()
    {
        await using RunningRuntime runtime = await RunningRuntime.StartAsync();

        await runtime.App.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            "Stopping",
            runtime.State.GetSnapshot().ProcessState.ToString());
    }

    private sealed class RunningRuntime : IAsyncDisposable
    {
        private RunningRuntime(
            WebApplication app,
            HttpClient client,
            GatewayStateService state,
            string managementPipeName)
        {
            App = app;
            Client = client;
            State = state;
            ManagementPipeName = managementPipeName;
        }

        public WebApplication App { get; }

        public HttpClient Client { get; }

        public GatewayStateService State { get; }

        public string ManagementPipeName { get; }

        public static async Task<RunningRuntime> StartAsync(
            string? inboundApiKey = null,
            string? dataRoot = null)
        {
            string managementPipeName = PipeNames.Management($"t-{Guid.NewGuid():N}"[..10]);
            WebApplication app = RuntimeApplication.Build(
                ["--urls", "http://127.0.0.1:0"],
                new RuntimeHostOptions(
                    inboundApiKey,
                    managementPipeName,
                    DataRoot: dataRoot));
            await app.StartAsync(TestContext.Current.CancellationToken);
            IServer server = app.Services.GetRequiredService<IServer>();
            string address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            var client = new HttpClient { BaseAddress = new Uri(address) };
            GatewayStateService state = app.Services.GetRequiredService<GatewayStateService>();
            return new RunningRuntime(app, client, state, managementPipeName);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private static string CreateApiKey() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Catnip.Runtime.IntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
