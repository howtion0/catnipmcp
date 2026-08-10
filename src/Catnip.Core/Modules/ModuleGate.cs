using System.Collections.ObjectModel;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Core.Modules;

public sealed record ModuleGateContext
{
    public ModuleGateContext(
        RuntimeProcessState runtimeState,
        bool masterEnabled,
        IReadOnlyDictionary<string, ConnectorStatus> connectorStatuses,
        bool configurationComplete)
    {
        RuntimeState = runtimeState;
        MasterEnabled = masterEnabled;
        ConnectorStatuses = new ReadOnlyDictionary<string, ConnectorStatus>(
            new Dictionary<string, ConnectorStatus>(
                connectorStatuses ?? new Dictionary<string, ConnectorStatus>(),
                StringComparer.Ordinal));
        ConfigurationComplete = configurationComplete;
    }

    public RuntimeProcessState RuntimeState { get; }

    public bool MasterEnabled { get; }

    public IReadOnlyDictionary<string, ConnectorStatus> ConnectorStatuses { get; }

    public bool ConfigurationComplete { get; }
}

public sealed record ModuleGateResult(bool Allowed, string? ErrorCode)
{
    public static ModuleGateResult Success { get; } = new(true, null);

    public static ModuleGateResult Denied(string errorCode) => new(false, errorCode);
}

public sealed class ModuleGate(ModuleManager moduleManager)
{
    private readonly ModuleManager _moduleManager =
        moduleManager ?? throw new ArgumentNullException(nameof(moduleManager));

    public ModuleGateResult Evaluate(string moduleId, ModuleGateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModuleDefinition definition = _moduleManager.GetDefinition(moduleId);

        if (context.RuntimeState != RuntimeProcessState.Running)
        {
            return ModuleGateResult.Denied(ErrorCodes.RuntimeStopping);
        }

        if (!context.MasterEnabled)
        {
            return ModuleGateResult.Denied(ErrorCodes.GatewayDisabled);
        }

        if (!_moduleManager.IsEnabled(moduleId))
        {
            return ModuleGateResult.Denied(ErrorCodes.ModuleDisabled);
        }

        if (definition.RequiredConnectorIds.Any(
                connectorId => context.ConnectorStatuses.TryGetValue(connectorId, out ConnectorStatus status)
                    && status == ConnectorStatus.Disabled))
        {
            return ModuleGateResult.Denied(ErrorCodes.ConnectorDisabled);
        }

        if (definition.RequiredConnectorIds.Any(
                connectorId => !context.ConnectorStatuses.TryGetValue(connectorId, out ConnectorStatus status)
                    || status != ConnectorStatus.Healthy))
        {
            return ModuleGateResult.Denied(ErrorCodes.ConnectorUnavailable);
        }

        if (!context.ConfigurationComplete)
        {
            return ModuleGateResult.Denied(ErrorCodes.ConfigurationInvalid);
        }

        return ModuleGateResult.Success;
    }
}
