using System.Text.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class TodayTodoContractsTests
{
    [Fact]
    public void Input_DefaultsMatchFrozenContract()
    {
        var input = new GetTodayTodosInput("2026-08-07");

        Assert.False(input.IncludeCompleted);
        Assert.Equal(50, input.MaxItems);
    }

    [Fact]
    public void Input_SerializesFrozenShape()
    {
        var input = new GetTodayTodosInput("2026-08-07", true, 100);

        string json = JsonSerializer.Serialize(input, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"date\":\"2026-08-07\",\"includeCompleted\":true,\"maxItems\":100}",
            json);
    }

    [Fact]
    public void Data_SerializesFrozenShapeAndStableStrings()
    {
        var data = new TodayTodoData(
            "2026-08-07",
            1,
            [
                new TodayTodoItemDto(
                    "todo-1",
                    "fake-calendar",
                    "record-1",
                    "customer_followup",
                    "Follow up with customer",
                    null,
                    new DateTimeOffset(2026, 8, 7, 9, 30, 0, TimeSpan.FromHours(8)),
                    null,
                    "high",
                    "in_progress",
                    "customer-1",
                    "Demo Customer",
                    null),
            ]);

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"date\":\"2026-08-07\",\"count\":1,\"items\":[{\"id\":\"todo-1\",\"source\":\"fake-calendar\",\"sourceRecordId\":\"record-1\",\"type\":\"customer_followup\",\"title\":\"Follow up with customer\",\"description\":null,\"startTime\":\"2026-08-07T09:30:00+08:00\",\"dueTime\":null,\"priority\":\"high\",\"status\":\"in_progress\",\"customerId\":\"customer-1\",\"customerName\":\"Demo Customer\",\"sourceUrl\":null}]}",
            json);
    }

    [Fact]
    public void Data_RoundTripPreservesOffsetAndNullableFields()
    {
        var startTime = new DateTimeOffset(2026, 8, 7, 9, 30, 0, TimeSpan.FromHours(8));
        var data = new TodayTodoData(
            "2026-08-07",
            1,
            [
                new TodayTodoItemDto(
                    "todo-1",
                    "fake",
                    "record-1",
                    "task",
                    "Task",
                    null,
                    startTime,
                    null,
                    "normal",
                    "pending",
                    null,
                    null,
                    null),
            ]);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        string json = JsonSerializer.Serialize(data, options);
        TodayTodoData? result = JsonSerializer.Deserialize<TodayTodoData>(json, options);

        Assert.NotNull(result);
        TodayTodoItemDto item = Assert.Single(result.Items);
        Assert.Equal(startTime, item.StartTime);
        Assert.Null(item.DueTime);
        Assert.Null(item.CustomerId);
    }

    [Fact]
    public void Data_NormalizesNullItemsToEmpty()
    {
        var data = new TodayTodoData("2026-08-07", 0, null!);

        Assert.Empty(data.Items);
        Assert.Equal(
            "{\"date\":\"2026-08-07\",\"count\":0,\"items\":[]}",
            JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create()));
    }

    [Fact]
    public void Data_DeserializesMissingItemsAsEmpty()
    {
        TodayTodoData? data = JsonSerializer.Deserialize<TodayTodoData>(
            "{\"date\":\"2026-08-07\",\"count\":0}",
            SharedJsonSerializerOptions.Create());

        Assert.NotNull(data);
        Assert.Empty(data.Items);
    }
}
