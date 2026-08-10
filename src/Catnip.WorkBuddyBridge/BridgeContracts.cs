using Catnip.Shared.Management;

namespace Catnip.WorkBuddyBridge;

public sealed record DemoRuntimeSnapshot(
    RuntimeProcessState ProcessState,
    int? ProcessId,
    string RuntimeAddress,
    string McpAddress,
    string TestApiAddress,
    bool ApiKeyConfigured,
    string Version,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    long WorkingSetBytes,
    bool MasterEnabled,
    GatewayMode Mode,
    IReadOnlyList<ModuleInfoDto> Modules,
    string LogFileName,
    string? FaultCode,
    string? FaultMessage);
