using Catnip.Core.Modules;
using Catnip.Shared.Management;

namespace Catnip.Runtime.Hosting;

public sealed class GatewayStateService
{
    private readonly string _mcpAddress;
    private readonly ModuleManager _moduleManager;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _version;
    private int _processState = (int)RuntimeProcessState.Starting;
    private int _masterEnabled;
    private int _ready;

    public GatewayStateService(string mcpAddress, string version)
        : this(mcpAddress, version, new ModuleManager())
    {
    }

    public GatewayStateService(
        string mcpAddress,
        string version,
        ModuleManager moduleManager)
    {
        _mcpAddress = mcpAddress;
        _version = version;
        _moduleManager = moduleManager;
    }

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public bool MasterEnabled => Volatile.Read(ref _masterEnabled) == 1;

    public void MarkRunning() =>
        Interlocked.Exchange(ref _processState, (int)RuntimeProcessState.Running);

    public void MarkStopping() =>
        Interlocked.Exchange(ref _processState, (int)RuntimeProcessState.Stopping);

    public void SetReady(bool ready) =>
        Interlocked.Exchange(ref _ready, ready ? 1 : 0);

    public void SetMasterEnabled(bool enabled) =>
        Interlocked.Exchange(ref _masterEnabled, enabled ? 1 : 0);

    public RuntimeSnapshot GetSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<ModuleInfoDto> modules = _moduleManager.GetSnapshot()
            .Select(ToModuleInfo)
            .ToArray();

        return new RuntimeSnapshot(
            (RuntimeProcessState)Volatile.Read(ref _processState),
            MasterEnabled,
            _moduleManager.Mode,
            _mcpAddress,
            _version,
            _startedAt,
            now,
            ActiveCalls: 0,
            UploadedBytes: 0,
            DownloadedBytes: 0,
            Environment.WorkingSet,
            modules,
            Connectors: [],
            LastSuccessfulInvocationAt: null,
            FaultCode: null,
            FaultMessage: null);
    }

    private static ModuleInfoDto ToModuleInfo(ModuleState state)
    {
        ModuleStatus status = state.Enabled switch
        {
            false => ModuleStatus.Disabled,
            true when !state.Readiness.ConfigurationComplete => ModuleStatus.NotConfigured,
            true when !state.Readiness.RequiredConnectorsHealthy => ModuleStatus.Degraded,
            _ => ModuleStatus.Enabled,
        };
        string? statusMessage = status switch
        {
            ModuleStatus.NotConfigured => "Module configuration is incomplete.",
            ModuleStatus.Degraded => "A required connector is unavailable.",
            _ => null,
        };

        return new ModuleInfoDto(
            state.Definition.Id,
            state.Definition.DisplayName,
            state.Definition.Description,
            state.Enabled,
            status,
            state.Definition.RequiredConnectorIds,
            LastInvokedAt: null,
            statusMessage);
    }
}
