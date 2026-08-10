using System.Text.Json;

namespace Catnip.Shared.Management;

public sealed record ManagementRequest(
    int ProtocolVersion,
    Guid RequestId,
    string Command,
    DateTimeOffset SentAtUtc,
    JsonElement Payload);

public sealed record ManagementResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement? Payload);

public sealed record RuntimeEvent(
    int ProtocolVersion,
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload);
