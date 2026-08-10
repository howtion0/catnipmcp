using System.Text.Json;
using Catnip.Core.Modules;
using Catnip.DemoApi.Hosting;
using Catnip.DemoApi.Runtime;
using Catnip.Shared.Errors;
using Catnip.WorkBuddyBridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Catnip.DemoApi.Tests;

public sealed class WorkBuddyBridgeTests
{
    [Fact]
    public void Address_RejectsNonLoopbackOrPath()
    {
        Assert.Throws<ArgumentException>(() => DemoApiBridgeAddress.Resolve("https://example.com"));
        Assert.Throws<ArgumentException>(() => DemoApiBridgeAddress.Resolve("http://127.0.0.1:5220/api"));
        Assert.Equal("http://127.0.0.1:5220/", DemoApiBridgeAddress.Resolve().AbsoluteUri);
    }

    [Fact]
    public async Task OfficialStdioClient_RequiresRunningRuntimeAndHonorsGatewayGates()
    {
        using var directory = new TemporaryDirectory();
        await using WebApplication app = DemoApiApplication.Build([], CreateOptions(directory.Path));
        await app.StartAsync(TestContext.Current.CancellationToken);
        string apiAddress = GetAddress(app);
        RuntimeProcessSupervisor supervisor = app.Services.GetRequiredService<RuntimeProcessSupervisor>();
        Dictionary<string, string?> environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment[DemoApiBridgeAddress.EnvironmentVariable] = apiAddress;
        environment[DemoApiOptions.DemoDataRootEnvironmentVariable] = directory.Path;
        var options = new StdioClientTransportOptions
        {
            Name = "catnip-test",
            Command = "dotnet",
            Arguments = [Path.Combine(ResolveBridgeDirectory(), "Catnip.WorkBuddyBridge.dll")],
            WorkingDirectory = ResolveBridgeDirectory(),
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        };
        await using McpClient client = await McpClient.CreateAsync(
            new StdioClientTransport(options),
            cancellationToken: TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var names = tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
        var stoppedStatus = await client.CallToolAsync(
            "catnip_get_gateway_status",
            cancellationToken: TestContext.Current.CancellationToken);
        var stoppedTodo = await client.CallToolAsync(
            "catnip_get_today_todos",
            cancellationToken: TestContext.Current.CancellationToken);
        var stoppedWeather = await client.CallToolAsync(
            "catnip_get_weather",
            new Dictionary<string, object?> { ["city"] = "北京" },
            cancellationToken: TestContext.Current.CancellationToken);
        var started = await supervisor.StartAsync(TestContext.Current.CancellationToken);
        var masterOffTodo = await client.CallToolAsync(
            "catnip_get_today_todos",
            cancellationToken: TestContext.Current.CancellationToken);
        var masterOn = await supervisor.SetMasterEnabledAsync(
            enabled: true,
            TestContext.Current.CancellationToken);
        var todoResult = await client.CallToolAsync(
            "catnip_get_today_todos",
            cancellationToken: TestContext.Current.CancellationToken);
        var moduleOffWeather = await client.CallToolAsync(
            "catnip_get_weather",
            new Dictionary<string, object?> { ["city"] = "北京" },
            cancellationToken: TestContext.Current.CancellationToken);
        var weatherOn = await supervisor.SetModuleEnabledAsync(
            ModuleIds.Weather,
            enabled: true,
            TestContext.Current.CancellationToken);
        var weatherResult = await client.CallToolAsync(
            "catnip_get_weather",
            new Dictionary<string, object?> { ["city"] = "北京" },
            cancellationToken: TestContext.Current.CancellationToken);
        var stopped = await supervisor.StopAsync(TestContext.Current.CancellationToken);
        var afterStopTodo = await client.CallToolAsync(
            "catnip_get_today_todos",
            cancellationToken: TestContext.Current.CancellationToken);

        string stoppedStatusJson = JsonSerializer.Serialize(stoppedStatus.StructuredContent);
        string stoppedTodoJson = JsonSerializer.Serialize(stoppedTodo.StructuredContent);
        string stoppedWeatherJson = JsonSerializer.Serialize(stoppedWeather.StructuredContent);
        string masterOffTodoJson = JsonSerializer.Serialize(masterOffTodo.StructuredContent);
        string todoJson = JsonSerializer.Serialize(todoResult.StructuredContent);
        string moduleOffWeatherJson = JsonSerializer.Serialize(moduleOffWeather.StructuredContent);
        string weatherJson = JsonSerializer.Serialize(weatherResult.StructuredContent);
        string afterStopTodoJson = JsonSerializer.Serialize(afterStopTodo.StructuredContent);
        using JsonDocument weatherDocument = JsonDocument.Parse(weatherJson);
        string weatherTraceId = weatherDocument.RootElement.GetProperty("traceId").GetString()!;

        Assert.Equal(
            ["catnip_get_gateway_status", "catnip_get_today_todos", "catnip_get_weather"],
            names);
        Assert.Contains("Stopped", stoppedStatusJson, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.RuntimeStopping, stoppedTodoJson, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.RuntimeStopping, stoppedWeatherJson, StringComparison.Ordinal);
        Assert.True(started.Success);
        Assert.Contains(ErrorCodes.GatewayDisabled, masterOffTodoJson, StringComparison.Ordinal);
        Assert.True(masterOn.Success);
        Assert.Contains("\"count\":3", todoJson, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", todoJson, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.ModuleDisabled, moduleOffWeatherJson, StringComparison.Ordinal);
        Assert.True(weatherOn.Success);
        Assert.Contains("CONFIGURATION_INVALID", weatherJson, StringComparison.Ordinal);
        Assert.True(stopped.Success);
        Assert.Contains(ErrorCodes.RuntimeStopping, afterStopTodoJson, StringComparison.Ordinal);
        string bridgeLog = Path.Combine(
            directory.Path,
            "logs",
            $"workbuddy-bridge-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
        Assert.True(File.Exists(bridgeLog));
        string logText = await File.ReadAllTextAsync(bridgeLog, TestContext.Current.CancellationToken);
        Assert.Contains("catnip_get_today_todos", logText, StringComparison.Ordinal);
        Assert.Contains("catnip_get_weather", logText, StringComparison.Ordinal);
        string runtimeLog = Path.Combine(
            directory.Path,
            "logs",
            $"runtime-demo-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
        string runtimeLogText = await File.ReadAllTextAsync(
            runtimeLog,
            TestContext.Current.CancellationToken);
        Assert.Contains(weatherTraceId, runtimeLogText, StringComparison.Ordinal);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private static DemoApiOptions CreateOptions(string dataRoot) => new(
        $"http://127.0.0.1:{TestPorts.Reserve()}",
        $"http://127.0.0.1:{TestPorts.Reserve()}",
        Path.Combine(AppContext.BaseDirectory, "Catnip.Runtime.dll"),
        dataRoot);

    private static string GetAddress(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        return server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    private static string ResolveBridgeDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Catnip.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Unable to locate Catnip.sln for bridge test.");
        }

        return Path.Combine(
            directory.FullName,
            "src",
            "Catnip.WorkBuddyBridge",
            "bin",
            "Release",
            "net10.0");
    }
}
