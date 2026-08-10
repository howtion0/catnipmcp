using Catnip.Shared.Management;

namespace Catnip.Core.Connectors;

public sealed record ConnectorHealth(
    ConnectorStatus Status,
    long? LatencyMs,
    DateTimeOffset CheckedAt,
    DateTimeOffset? LastSuccessfulAt,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ConnectorHealth NotConfigured(DateTimeOffset checkedAt) =>
        new(ConnectorStatus.NotConfigured, null, checkedAt, null, null, null);
}

public sealed record ConnectorRegistrationSnapshot(
    string Id,
    string DisplayName,
    string Kind,
    bool Enabled,
    ConnectorHealth Health,
    IReadOnlyList<string> SupportedOperations)
{
    public IReadOnlyList<string> SupportedOperations { get; init; } =
        Array.AsReadOnly((SupportedOperations ?? []).ToArray());
}
