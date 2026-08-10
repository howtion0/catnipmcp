using System.Text.Json.Serialization;
using Catnip.Shared.Management;

namespace Catnip.Shared.Business;

public sealed record GatewayStatusData(
    string RuntimeState,
    bool MasterEnabled,
    string GatewayMode,
    string McpAddress,
    string Version,
    DateTimeOffset? StartedAt,
    IReadOnlyList<ModuleInfoDto> Modules,
    IReadOnlyList<ConnectorInfoDto> Connectors)
{
    [JsonPropertyOrder(1)]
    public IReadOnlyList<ModuleInfoDto> Modules { get; init; } = Modules ?? [];

    [JsonPropertyOrder(2)]
    public IReadOnlyList<ConnectorInfoDto> Connectors { get; init; } = Connectors ?? [];
}
