using System.Text.Json;
using Catnip.Shared.Configuration;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class ManagementContractsTests
{
    public static IEnumerable<object[]> CommandJsonCases()
    {
        yield return [new EmptyCommand(), "{}"];
        yield return [new SetMasterEnabledCommand(true), "{\"enabled\":true}"];
        yield return [new SetGatewayModeCommand(GatewayMode.Custom), "{\"mode\":\"custom\"}"];
        yield return
        [
            new SetModuleEnabledCommand("today-todos", false),
            "{\"moduleId\":\"today-todos\",\"enabled\":false}",
        ];
        yield return
        [
            new SetConnectorEnabledCommand("calendar", true),
            "{\"connectorId\":\"calendar\",\"enabled\":true}",
        ];
        yield return
        [
            new TestConnectorCommand("calendar"),
            "{\"connectorId\":\"calendar\"}",
        ];
        yield return
        [
            new SaveSecretCommand("weather.api-key", "REDACTED"),
            "{\"secretId\":\"weather.api-key\",\"secretValue\":\"REDACTED\"}",
        ];
        yield return
        [
            new DeleteSecretCommand("weather.api-key"),
            "{\"secretId\":\"weather.api-key\"}",
        ];
        yield return [new ShutdownRuntimeCommand(true), "{\"graceful\":true}"];
    }

    [Fact]
    public void ManagementRequest_SerializesFrozenShape()
    {
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();
        JsonElement payload = JsonSerializer.SerializeToElement(
            new SetModuleEnabledCommand("today-todos", true),
            options);
        var request = new ManagementRequest(
            1,
            Guid.Parse("d0c95c4d-74b8-4e92-8fbe-a7e0f8d7f136"),
            "SetModuleEnabled",
            new DateTimeOffset(2026, 8, 7, 6, 30, 0, TimeSpan.Zero),
            payload);

        string json = JsonSerializer.Serialize(request, options);

        Assert.Equal(
            "{\"protocolVersion\":1,\"requestId\":\"d0c95c4d-74b8-4e92-8fbe-a7e0f8d7f136\",\"command\":\"SetModuleEnabled\",\"sentAtUtc\":\"2026-08-07T06:30:00+00:00\",\"payload\":{\"moduleId\":\"today-todos\",\"enabled\":true}}",
            json);
    }

    [Fact]
    public void ManagementResponse_RoundTripsNullablePayload()
    {
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();
        var response = new ManagementResponse(
            1,
            Guid.Parse("d0c95c4d-74b8-4e92-8fbe-a7e0f8d7f136"),
            false,
            "IPC_ERROR",
            "Request failed",
            null);

        string json = JsonSerializer.Serialize(response, options);
        ManagementResponse? result = JsonSerializer.Deserialize<ManagementResponse>(json, options);

        Assert.Equal(
            "{\"protocolVersion\":1,\"requestId\":\"d0c95c4d-74b8-4e92-8fbe-a7e0f8d7f136\",\"success\":false,\"errorCode\":\"IPC_ERROR\",\"errorMessage\":\"Request failed\",\"payload\":null}",
            json);
        Assert.NotNull(result);
        Assert.Null(result.Payload);
        Assert.Equal("IPC_ERROR", result.ErrorCode);
    }

    [Fact]
    public void RuntimeEvent_RoundTripsJsonElementPayload()
    {
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();
        JsonElement payload = JsonSerializer.SerializeToElement(
            new { ProcessState = "running" },
            options);
        var runtimeEvent = new RuntimeEvent(
            1,
            Guid.Parse("9d10690c-3264-426a-97a6-7bb624eb3313"),
            "RuntimeStateChanged",
            new DateTimeOffset(2026, 8, 7, 6, 31, 0, TimeSpan.Zero),
            payload);

        string json = JsonSerializer.Serialize(runtimeEvent, options);
        RuntimeEvent? result = JsonSerializer.Deserialize<RuntimeEvent>(json, options);

        Assert.Equal(
            "{\"protocolVersion\":1,\"eventId\":\"9d10690c-3264-426a-97a6-7bb624eb3313\",\"eventType\":\"RuntimeStateChanged\",\"occurredAtUtc\":\"2026-08-07T06:31:00+00:00\",\"payload\":{\"processState\":\"running\"}}",
            json);
        Assert.NotNull(result);
        Assert.Equal("running", result.Payload.GetProperty("processState").GetString());
    }

    [Theory]
    [MemberData(nameof(CommandJsonCases))]
    public void Commands_SerializeFrozenPayloadShape(object command, string expectedJson)
    {
        string json = JsonSerializer.Serialize(
            command,
            command.GetType(),
            SharedJsonSerializerOptions.Create());

        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void SaveSettingsCommand_SerializesFrozenSettingsShape()
    {
        var command = new SaveSettingsCommand(CreateSettings(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["today-todos"] = true,
                ["customer-interactions"] = true,
                ["customer-writeback"] = false,
                ["weather"] = false,
            }));

        string json = JsonSerializer.Serialize(command, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"settings\":{\"schemaVersion\":1,\"gateway\":{\"listenAddress\":\"127.0.0.1\",\"port\":5210,\"mcpPath\":\"/mcp\",\"masterEnabled\":true,\"mode\":\"custom\",\"requestTimeoutSeconds\":15,\"maxResponseBytes\":524288,\"maxConcurrentCalls\":16},\"desktop\":{\"theme\":\"system\",\"closeBehavior\":\"minimizeToTray\",\"autoStartRuntime\":false,\"startWithWindows\":false,\"compactMode\":false},\"identity\":{\"mode\":\"fixedDemoUser\",\"demoUserOpenId\":\"\",\"demoOwnerId\":\"sales-demo-001\",\"demoCalendarId\":\"\"},\"logging\":{\"fileRetentionDays\":14,\"invocationRetentionDays\":30,\"minimumLevel\":\"Information\"},\"modules\":{\"today-todos\":true,\"customer-interactions\":true,\"customer-writeback\":false,\"weather\":false}}}",
            json);
    }

    [Fact]
    public void GatewaySettings_NormalizesAndIsolatesModules()
    {
        var source = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["today-todos"] = true,
        };
        GatewaySettingsDto settings = CreateSettings(source);

        source["weather"] = true;
        GatewaySettingsDto? missingModules = JsonSerializer.Deserialize<GatewaySettingsDto>(
            "{\"schemaVersion\":1,\"gateway\":{\"listenAddress\":\"127.0.0.1\",\"port\":5210,\"mcpPath\":\"/mcp\",\"masterEnabled\":true,\"mode\":\"custom\",\"requestTimeoutSeconds\":15,\"maxResponseBytes\":524288,\"maxConcurrentCalls\":16},\"desktop\":{\"theme\":\"system\",\"closeBehavior\":\"minimizeToTray\",\"autoStartRuntime\":false,\"startWithWindows\":false,\"compactMode\":false},\"identity\":{\"mode\":\"fixedDemoUser\",\"demoUserOpenId\":\"\",\"demoOwnerId\":\"sales-demo-001\",\"demoCalendarId\":\"\"},\"logging\":{\"fileRetentionDays\":14,\"invocationRetentionDays\":30,\"minimumLevel\":\"Information\"}}",
            SharedJsonSerializerOptions.Create());

        Assert.Single(settings.Modules);
        Assert.False(settings.Modules.ContainsKey("weather"));
        Assert.NotNull(missingModules);
        Assert.Empty(missingModules.Modules);
    }

    [Fact]
    public void PipeNames_MatchFrozenValues()
    {
        const string userSidHash = "0123456789abcdef";

        Assert.Equal("Catnip.Management.0123456789abcdef", PipeNames.Management(userSidHash));
        Assert.Equal("Catnip.Events.0123456789abcdef", PipeNames.Events(userSidHash));
    }

    private static GatewaySettingsDto CreateSettings(IReadOnlyDictionary<string, bool> modules) =>
        new(
            1,
            new GatewayServiceSettingsDto(
                "127.0.0.1",
                5210,
                "/mcp",
                true,
                GatewayMode.Custom,
                15,
                524288,
                16),
            new DesktopSettingsDto("system", "minimizeToTray", false, false, false),
            new IdentitySettingsDto("fixedDemoUser", string.Empty, "sales-demo-001", string.Empty),
            new LoggingSettingsDto(14, 30, "Information"),
            modules);
}
