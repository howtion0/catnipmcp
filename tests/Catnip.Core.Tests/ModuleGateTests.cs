using Catnip.Core.Modules;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Core.Tests;

public sealed class ModuleGateTests
{
    [Fact]
    public void RuntimeState_HasHighestPriority()
    {
        ModuleGateResult result = CreateGate().Evaluate(
            ModuleIds.Weather,
            CreateContext(RuntimeProcessState.Stopping, false, ConnectorStatus.Disabled, false));

        AssertDenied(ErrorCodes.RuntimeStopping, result);
    }

    [Fact]
    public void MasterSwitch_PrecedesModuleState()
    {
        ModuleGateResult result = CreateGate().Evaluate(
            ModuleIds.Weather,
            CreateContext(RuntimeProcessState.Running, false, ConnectorStatus.Disabled, false));

        AssertDenied(ErrorCodes.GatewayDisabled, result);
    }

    [Fact]
    public void ModuleSwitch_PrecedesConnectorState()
    {
        ModuleGateResult result = CreateGate().Evaluate(
            ModuleIds.Weather,
            CreateContext(RuntimeProcessState.Running, true, ConnectorStatus.Disabled, false));

        AssertDenied(ErrorCodes.ModuleDisabled, result);
    }

    [Fact]
    public void DisabledConnector_PrecedesConfiguration()
    {
        ModuleGateResult result = CreateGateWithTodayTodosEnabled().Evaluate(
            ModuleIds.TodayTodos,
            CreateContext(RuntimeProcessState.Running, true, ConnectorStatus.Disabled, false));

        AssertDenied(ErrorCodes.ConnectorDisabled, result);
    }

    [Theory]
    [InlineData(ConnectorStatus.NotConfigured)]
    [InlineData(ConnectorStatus.Connecting)]
    [InlineData(ConnectorStatus.Degraded)]
    [InlineData(ConnectorStatus.AuthenticationFailed)]
    [InlineData(ConnectorStatus.Timeout)]
    [InlineData(ConnectorStatus.Unavailable)]
    [InlineData(ConnectorStatus.Faulted)]
    public void NonHealthyConnector_IsUnavailable(ConnectorStatus status)
    {
        ModuleGateResult result = CreateGateWithTodayTodosEnabled().Evaluate(
            ModuleIds.TodayTodos,
            CreateContext(RuntimeProcessState.Running, true, status, false));

        AssertDenied(ErrorCodes.ConnectorUnavailable, result);
    }

    [Fact]
    public void MissingConnector_IsUnavailable()
    {
        var context = new ModuleGateContext(
            RuntimeProcessState.Running,
            true,
            new Dictionary<string, ConnectorStatus>(),
            false);

        ModuleGateResult result = CreateGateWithTodayTodosEnabled().Evaluate(ModuleIds.TodayTodos, context);

        AssertDenied(ErrorCodes.ConnectorUnavailable, result);
    }

    [Fact]
    public void Configuration_IsCheckedAfterHealthyConnectors()
    {
        ModuleGateResult result = CreateGateWithTodayTodosEnabled().Evaluate(
            ModuleIds.TodayTodos,
            CreateContext(RuntimeProcessState.Running, true, ConnectorStatus.Healthy, false));

        AssertDenied(ErrorCodes.ConfigurationInvalid, result);
    }

    [Fact]
    public void HealthyEnabledModule_IsAllowed()
    {
        ModuleGateResult result = CreateGateWithTodayTodosEnabled().Evaluate(
            ModuleIds.TodayTodos,
            CreateContext(RuntimeProcessState.Running, true, ConnectorStatus.Healthy, true));

        Assert.True(result.Allowed);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Context_CopiesConnectorStatuses()
    {
        var statuses = new Dictionary<string, ConnectorStatus>
        {
            ["feishu"] = ConnectorStatus.Healthy,
        };
        var context = new ModuleGateContext(RuntimeProcessState.Running, true, statuses, true);

        statuses["feishu"] = ConnectorStatus.Disabled;

        Assert.Equal(ConnectorStatus.Healthy, context.ConnectorStatuses["feishu"]);
    }

    private static ModuleGate CreateGate() => new(new ModuleManager());

    private static ModuleGate CreateGateWithTodayTodosEnabled() => new(new ModuleManager());

    private static ModuleGateContext CreateContext(
        RuntimeProcessState runtimeState,
        bool masterEnabled,
        ConnectorStatus connectorStatus,
        bool configurationComplete) =>
        new(
            runtimeState,
            masterEnabled,
            new Dictionary<string, ConnectorStatus>
            {
                ["feishu"] = connectorStatus,
                ["weather"] = connectorStatus,
            },
            configurationComplete);

    private static void AssertDenied(string errorCode, ModuleGateResult result)
    {
        Assert.False(result.Allowed);
        Assert.Equal(errorCode, result.ErrorCode);
    }
}
