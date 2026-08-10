using System.Text.Json;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class RuntimeStatusContractsTests
{
    [Fact]
    public void Enums_MatchFrozenContractExactly()
    {
        Assert.Equal(
            ["Stopped", "Starting", "Running", "Stopping", "Faulted"],
            Enum.GetNames<RuntimeProcessState>());
        Assert.Equal(["Full", "Custom"], Enum.GetNames<GatewayMode>());
        Assert.Equal(
            ["Disabled", "Enabled", "NotConfigured", "Degraded", "Faulted"],
            Enum.GetNames<ModuleStatus>());
        Assert.Equal(
            [
                "NotConfigured",
                "Disabled",
                "Connecting",
                "Healthy",
                "Degraded",
                "AuthenticationFailed",
                "Timeout",
                "Unavailable",
                "Faulted",
            ],
            Enum.GetNames<ConnectorStatus>());
    }

    [Fact]
    public void Serialize_RuntimeSnapshotMatchesFrozenJsonShape()
    {
        var snapshot = new RuntimeSnapshot(
            RuntimeProcessState.Running,
            true,
            GatewayMode.Custom,
            "http://127.0.0.1:5137/mcp",
            "0.3.0",
            null,
            new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero),
            2,
            1024,
            2048,
            4096,
            [],
            [],
            null,
            null,
            null);

        string json = JsonSerializer.Serialize(snapshot, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"processState\":\"running\",\"masterEnabled\":true,\"mode\":\"custom\",\"mcpAddress\":\"http://127.0.0.1:5137/mcp\",\"version\":\"0.3.0\",\"startedAt\":null,\"updatedAt\":\"2026-08-07T01:02:03+00:00\",\"activeCalls\":2,\"uploadedBytes\":1024,\"downloadedBytes\":2048,\"workingSetBytes\":4096,\"modules\":[],\"connectors\":[],\"lastSuccessfulInvocationAt\":null,\"faultCode\":null,\"faultMessage\":null}",
            json);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesNestedStatusValues()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 7, 2, 3, 4, TimeSpan.Zero);
        var snapshot = new RuntimeSnapshot(
            RuntimeProcessState.Faulted,
            false,
            GatewayMode.Full,
            "http://127.0.0.1:5137/mcp",
            "0.3.0",
            checkedAt.AddMinutes(-5),
            checkedAt,
            0,
            10,
            20,
            30,
            [
                new ModuleInfoDto(
                    "today-todos",
                    "今日待办",
                    "Aggregated todos",
                    true,
                    ModuleStatus.Degraded,
                    ["calendar"],
                    checkedAt.AddMinutes(-1),
                    "Calendar degraded"),
            ],
            [
                new ConnectorInfoDto(
                    "calendar",
                    "Calendar",
                    "fake",
                    true,
                    ConnectorStatus.Healthy,
                    12,
                    checkedAt,
                    checkedAt,
                    null,
                    null,
                    [new ConnectorCapabilityDto("read", "Read", ConnectorStatus.Healthy, null)]),
            ],
            checkedAt.AddMinutes(-1),
            "RUNTIME_FAULT",
            "Runtime is faulted");

        string json = JsonSerializer.Serialize(snapshot, SharedJsonSerializerOptions.Create());
        RuntimeSnapshot? result = JsonSerializer.Deserialize<RuntimeSnapshot>(
            json,
            SharedJsonSerializerOptions.Create());

        Assert.NotNull(result);
        Assert.Equal(RuntimeProcessState.Faulted, result.ProcessState);
        Assert.Equal(checkedAt, result.UpdatedAt);
        Assert.Equal(ModuleStatus.Degraded, Assert.Single(result.Modules).Status);
        ConnectorInfoDto connector = Assert.Single(result.Connectors);
        Assert.Equal(ConnectorStatus.Healthy, connector.Status);
        Assert.Equal("read", Assert.Single(connector.Capabilities).Id);
        Assert.Equal("RUNTIME_FAULT", result.FaultCode);
    }

    [Fact]
    public void Constructors_NormalizeNullCollectionsToEmpty()
    {
        var module = new ModuleInfoDto(
            "module",
            "Module",
            "Description",
            true,
            ModuleStatus.Enabled,
            null!,
            null,
            null);
        var connector = new ConnectorInfoDto(
            "connector",
            "Connector",
            "fake",
            true,
            ConnectorStatus.Healthy,
            null,
            null,
            null,
            null,
            null,
            null!);
        var snapshot = new RuntimeSnapshot(
            RuntimeProcessState.Stopped,
            false,
            GatewayMode.Full,
            "http://127.0.0.1:5137/mcp",
            "0.3.0",
            null,
            DateTimeOffset.UnixEpoch,
            0,
            0,
            0,
            0,
            null!,
            null!,
            null,
            null,
            null);

        Assert.Empty(module.RequiredConnectorIds);
        Assert.Empty(connector.Capabilities);
        Assert.Empty(snapshot.Modules);
        Assert.Empty(snapshot.Connectors);
    }

    [Fact]
    public void Deserialize_MissingCollectionFieldsProducesEmptyCollections()
    {
        const string snapshotJson =
            "{\"processState\":\"stopped\",\"masterEnabled\":false,\"mode\":\"full\",\"mcpAddress\":\"http://127.0.0.1:5137/mcp\",\"version\":\"0.3.0\",\"updatedAt\":\"1970-01-01T00:00:00+00:00\",\"activeCalls\":0,\"uploadedBytes\":0,\"downloadedBytes\":0,\"workingSetBytes\":0}";
        const string moduleJson =
            "{\"id\":\"module\",\"displayName\":\"Module\",\"description\":\"Description\",\"enabled\":true,\"status\":\"enabled\"}";
        const string connectorJson =
            "{\"id\":\"connector\",\"displayName\":\"Connector\",\"kind\":\"fake\",\"enabled\":true,\"status\":\"healthy\"}";
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        RuntimeSnapshot? snapshot = JsonSerializer.Deserialize<RuntimeSnapshot>(snapshotJson, options);
        ModuleInfoDto? module = JsonSerializer.Deserialize<ModuleInfoDto>(moduleJson, options);
        ConnectorInfoDto? connector = JsonSerializer.Deserialize<ConnectorInfoDto>(connectorJson, options);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Modules);
        Assert.Empty(snapshot.Connectors);
        Assert.NotNull(module);
        Assert.Empty(module.RequiredConnectorIds);
        Assert.NotNull(connector);
        Assert.Empty(connector.Capabilities);
    }
}
