namespace Catnip.Shared.Business;

public sealed record GetTodayTodosInput(
    string Date,
    bool IncludeCompleted = false,
    int MaxItems = 50);

public sealed record TodayTodoData(
    string Date,
    int Count,
    IReadOnlyList<TodayTodoItemDto> Items)
{
    public IReadOnlyList<TodayTodoItemDto> Items { get; init; } = Items ?? [];
}

public sealed record TodayTodoItemDto(
    string Id,
    string Source,
    string SourceRecordId,
    string Type,
    string Title,
    string? Description,
    DateTimeOffset? StartTime,
    DateTimeOffset? DueTime,
    string Priority,
    string Status,
    string? CustomerId,
    string? CustomerName,
    string? SourceUrl);
