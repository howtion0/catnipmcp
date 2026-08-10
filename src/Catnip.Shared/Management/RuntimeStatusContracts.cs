using System.Text.Json.Serialization;

namespace Catnip.Shared.Management;

public enum RuntimeProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

public enum GatewayMode
{
    Full,
    Custom,
}

public sealed record RuntimeSnapshot(
    RuntimeProcessState ProcessState,
    bool MasterEnabled,
    GatewayMode Mode,
    string McpAddress,
    string Version,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    int ActiveCalls,
    long UploadedBytes,
    long DownloadedBytes,
    long WorkingSetBytes,
    IReadOnlyList<ModuleInfoDto> Modules,
    IReadOnlyList<ConnectorInfoDto> Connectors,
    [property: JsonPropertyOrder(3)] DateTimeOffset? LastSuccessfulInvocationAt,
    [property: JsonPropertyOrder(4)] string? FaultCode,
    [property: JsonPropertyOrder(5)] string? FaultMessage)
{
    [JsonPropertyOrder(1)]
    public IReadOnlyList<ModuleInfoDto> Modules { get; init; } = Modules ?? [];

    [JsonPropertyOrder(2)]
    public IReadOnlyList<ConnectorInfoDto> Connectors { get; init; } = Connectors ?? [];
}

public enum ModuleStatus
{
    Disabled,
    Enabled,
    NotConfigured,
    Degraded,
    Faulted,
}

public sealed record ModuleInfoDto(
    string Id,
    string DisplayName,
    string Description,
    bool Enabled,
    ModuleStatus Status,
    IReadOnlyList<string> RequiredConnectorIds,
    [property: JsonPropertyOrder(2)] DateTimeOffset? LastInvokedAt,
    [property: JsonPropertyOrder(3)] string? StatusMessage)
{
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> RequiredConnectorIds { get; init; } = RequiredConnectorIds ?? [];
}

public enum ConnectorStatus
{
    NotConfigured,
    Disabled,
    Connecting,
    Healthy,
    Degraded,
    AuthenticationFailed,
    Timeout,
    Unavailable,
    Faulted,
}

public sealed record ConnectorInfoDto(
    string Id,
    string DisplayName,
    string Kind,
    bool Enabled,
    ConnectorStatus Status,
    long? LastLatencyMs,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessfulAt,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<ConnectorCapabilityDto> Capabilities)
{
    public IReadOnlyList<ConnectorCapabilityDto> Capabilities { get; init; } = Capabilities ?? [];
}

public sealed record ConnectorCapabilityDto(
    string Id,
    string DisplayName,
    ConnectorStatus Status,
    string? Message);
