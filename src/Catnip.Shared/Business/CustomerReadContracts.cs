using System.Text.Json.Serialization;

namespace Catnip.Shared.Business;

public sealed record GetCustomerInteractionsInput(
    string SourceType,
    string SourceId,
    string? CustomerKeyword,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int MaxItems = 100);

public sealed record CustomerInteractionsData(
    string SourceType,
    string SourceId,
    int Count,
    bool Truncated,
    IReadOnlyList<CustomerInteractionDto> Items)
{
    public IReadOnlyList<CustomerInteractionDto> Items { get; init; } = Items ?? [];
}

public sealed record CustomerInteractionDto(
    string InteractionId,
    string SourceType,
    DateTimeOffset OccurredAt,
    string SpeakerId,
    string SpeakerName,
    string SpeakerRole,
    string Content,
    string? CustomerId,
    string? CustomerName);

public sealed record SearchCustomersInput(
    string Keyword,
    int MaxItems = 20);

public sealed record SearchCustomersData(
    int Count,
    IReadOnlyList<CustomerDto> Customers)
{
    public IReadOnlyList<CustomerDto> Customers { get; init; } = Customers ?? [];
}

public sealed record CustomerDto(
    string CustomerId,
    string Name,
    string? ContactName,
    string? MobileMasked,
    string? OwnerId,
    string? Classification,
    IReadOnlyList<string> Tags,
    [property: JsonPropertyOrder(2)] string? RiskLevel,
    [property: JsonPropertyOrder(3)] string? LastInsightSummary)
{
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];
}
