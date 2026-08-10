using System.Text.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class CustomerWriteContractsTests
{
    [Fact]
    public void Input_SerializesFrozenShapeAndOrder()
    {
        var input = CreateInput();

        string json = JsonSerializer.Serialize(input, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"customerId\":\"customer-demo\",\"summary\":\"Demo summary\",\"classification\":\"A\",\"tags\":[\"demo\"],\"riskLevel\":\"medium\",\"risks\":[\"demo-risk\"],\"coreNeeds\":[\"demo-need\"],\"nextActions\":[\"demo-action\"],\"createFollowUpRecord\":true,\"createTask\":true,\"taskDueTime\":\"2026-08-08T10:00:00+08:00\",\"idempotencyKey\":\"demo-key-001\",\"confirmed\":true}",
            json);
    }

    [Fact]
    public void Data_SerializesStableStepValues()
    {
        var data = new WriteCustomerInsightData(
            "customer-demo",
            false,
            true,
            [
                new WriteStepResult("update_customer_profile", true, "record-1", null, null),
                new WriteStepResult("create_followup_record", false, null, "UPSTREAM_ERROR", "Demo error"),
                new WriteStepResult("create_followup_task", true, "record-2", null, null),
            ]);

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Contains("\"step\":\"update_customer_profile\"", json, StringComparison.Ordinal);
        Assert.Contains("\"step\":\"create_followup_record\"", json, StringComparison.Ordinal);
        Assert.Contains("\"step\":\"create_followup_task\"", json, StringComparison.Ordinal);
        Assert.Contains("\"partialSuccess\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Input_RoundTripPreservesConfirmationIdempotencyAndOffset()
    {
        WriteCustomerInsightInput input = CreateInput();
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        WriteCustomerInsightInput? result = JsonSerializer.Deserialize<WriteCustomerInsightInput>(
            JsonSerializer.Serialize(input, options),
            options);

        Assert.NotNull(result);
        Assert.True(result.Confirmed);
        Assert.Equal("medium", result.RiskLevel);
        Assert.Equal("demo-key-001", result.IdempotencyKey);
        Assert.Equal(input.TaskDueTime, result.TaskDueTime);
    }

    [Fact]
    public void Constructors_NormalizeNullCollections()
    {
        var input = new WriteCustomerInsightInput(
            "customer-demo",
            "Demo summary",
            null,
            null!,
            null,
            null!,
            null!,
            null!,
            false,
            false,
            null,
            "demo-key-001",
            true);
        var data = new WriteCustomerInsightData("customer-demo", false, false, null!);

        Assert.Empty(input.Tags);
        Assert.Empty(input.Risks);
        Assert.Empty(input.CoreNeeds);
        Assert.Empty(input.NextActions);
        Assert.Empty(data.Steps);
    }

    [Fact]
    public void Deserialize_MissingCollectionsProducesEmptyCollections()
    {
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();
        WriteCustomerInsightInput? input = JsonSerializer.Deserialize<WriteCustomerInsightInput>(
            "{\"customerId\":\"customer-demo\",\"summary\":\"Demo\",\"createFollowUpRecord\":false,\"createTask\":false,\"idempotencyKey\":\"demo-key-001\",\"confirmed\":true}",
            options);
        WriteCustomerInsightData? data = JsonSerializer.Deserialize<WriteCustomerInsightData>(
            "{\"customerId\":\"customer-demo\",\"replayedFromIdempotency\":false,\"partialSuccess\":false}",
            options);

        Assert.NotNull(input);
        Assert.Empty(input.Tags);
        Assert.Empty(input.Risks);
        Assert.Empty(input.CoreNeeds);
        Assert.Empty(input.NextActions);
        Assert.NotNull(data);
        Assert.Empty(data.Steps);
    }

    [Fact]
    public void StepResult_PreservesNullableFailureFields()
    {
        var step = new WriteStepResult("update_customer_profile", false, null, "UPSTREAM_ERROR", null);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        WriteStepResult? result = JsonSerializer.Deserialize<WriteStepResult>(
            JsonSerializer.Serialize(step, options),
            options);

        Assert.NotNull(result);
        Assert.Null(result.RecordId);
        Assert.Equal("UPSTREAM_ERROR", result.ErrorCode);
        Assert.Null(result.Message);
    }

    private static WriteCustomerInsightInput CreateInput() =>
        new(
            "customer-demo",
            "Demo summary",
            "A",
            ["demo"],
            "medium",
            ["demo-risk"],
            ["demo-need"],
            ["demo-action"],
            true,
            true,
            new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.FromHours(8)),
            "demo-key-001",
            true);
}
