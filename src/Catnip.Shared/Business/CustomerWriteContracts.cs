using System.Text.Json.Serialization;

namespace Catnip.Shared.Business;

public sealed record WriteCustomerInsightInput(
    string CustomerId,
    string Summary,
    string? Classification,
    IReadOnlyList<string> Tags,
    [property: JsonPropertyOrder(2)] string? RiskLevel,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> CoreNeeds,
    IReadOnlyList<string> NextActions,
    [property: JsonPropertyOrder(6)] bool CreateFollowUpRecord,
    [property: JsonPropertyOrder(7)] bool CreateTask,
    [property: JsonPropertyOrder(8)] DateTimeOffset? TaskDueTime,
    [property: JsonPropertyOrder(9)] string IdempotencyKey,
    [property: JsonPropertyOrder(10)] bool Confirmed)
{
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];

    [JsonPropertyOrder(3)]
    public IReadOnlyList<string> Risks { get; init; } = Risks ?? [];

    [JsonPropertyOrder(4)]
    public IReadOnlyList<string> CoreNeeds { get; init; } = CoreNeeds ?? [];

    [JsonPropertyOrder(5)]
    public IReadOnlyList<string> NextActions { get; init; } = NextActions ?? [];
}

public sealed record WriteCustomerInsightData(
    string CustomerId,
    bool ReplayedFromIdempotency,
    bool PartialSuccess,
    IReadOnlyList<WriteStepResult> Steps)
{
    public IReadOnlyList<WriteStepResult> Steps { get; init; } = Steps ?? [];
}

public sealed record WriteStepResult(
    string Step,
    bool Success,
    string? RecordId,
    string? ErrorCode,
    string? Message);
