namespace Catnip.Core.Logging;

public sealed record InvocationLogEntry(
    string Id,
    string TraceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ToolName,
    string? ModuleId,
    IReadOnlyList<string> ConnectorIds,
    bool Success,
    string? ErrorCode,
    long? DurationMs,
    string? InputSummary,
    string? OutputSummary,
    string? CallerSummary,
    long UploadedBytes,
    long DownloadedBytes)
{
    public IReadOnlyList<string> ConnectorIds { get; init; } =
        Array.AsReadOnly((ConnectorIds ?? []).ToArray());
}

public interface IInvocationLogStore
{
    ValueTask AppendAsync(InvocationLogEntry entry, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<InvocationLogEntry>> GetRecentAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
