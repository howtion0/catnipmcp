using System.Text.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class CustomerReadContractsTests
{
    [Fact]
    public void InteractionInput_DefaultMaxItemsIsFrozenValue()
    {
        var input = new GetCustomerInteractionsInput(
            "demo",
            "source-1",
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Equal(100, input.MaxItems);
    }

    [Fact]
    public void SearchInput_DefaultMaxItemsIsFrozenValue()
    {
        Assert.Equal(20, new SearchCustomersInput("demo").MaxItems);
    }

    [Fact]
    public void InteractionInput_SerializesFrozenShape()
    {
        var input = new GetCustomerInteractionsInput(
            "demo",
            "source-1",
            "Demo",
            new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 8, 7, 18, 0, 0, TimeSpan.FromHours(8)),
            200);

        string json = JsonSerializer.Serialize(input, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"sourceType\":\"demo\",\"sourceId\":\"source-1\",\"customerKeyword\":\"Demo\",\"startTime\":\"2026-08-01T08:00:00+08:00\",\"endTime\":\"2026-08-07T18:00:00+08:00\",\"maxItems\":200}",
            json);
    }

    [Fact]
    public void InteractionData_SerializesFrozenShapeAndSpeakerRole()
    {
        var data = new CustomerInteractionsData(
            "demo",
            "source-1",
            1,
            false,
            [
                new CustomerInteractionDto(
                    "interaction-1",
                    "demo",
                    new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.FromHours(8)),
                    "speaker-1",
                    "Demo Speaker",
                    "customer",
                    "Demo message",
                    "customer-1",
                    "Demo Customer"),
            ]);

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"sourceType\":\"demo\",\"sourceId\":\"source-1\",\"count\":1,\"truncated\":false,\"items\":[{\"interactionId\":\"interaction-1\",\"sourceType\":\"demo\",\"occurredAt\":\"2026-08-07T09:00:00+08:00\",\"speakerId\":\"speaker-1\",\"speakerName\":\"Demo Speaker\",\"speakerRole\":\"customer\",\"content\":\"Demo message\",\"customerId\":\"customer-1\",\"customerName\":\"Demo Customer\"}]}",
            json);
    }

    [Fact]
    public void InteractionData_RoundTripPreservesOffsetAndNullableFields()
    {
        var occurredAt = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.FromHours(8));
        var data = new CustomerInteractionsData(
            "demo",
            "source-1",
            1,
            true,
            [new CustomerInteractionDto("i", "demo", occurredAt, "s", "Speaker", "other", "Text", null, null)]);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        CustomerInteractionsData? result = JsonSerializer.Deserialize<CustomerInteractionsData>(
            JsonSerializer.Serialize(data, options),
            options);

        Assert.NotNull(result);
        CustomerInteractionDto item = Assert.Single(result.Items);
        Assert.Equal(occurredAt, item.OccurredAt);
        Assert.Null(item.CustomerId);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void SearchData_SerializesFrozenShapeAndTagOrder()
    {
        var data = new SearchCustomersData(
            1,
            [
                new CustomerDto(
                    "customer-1",
                    "Demo Customer",
                    "Demo Contact",
                    "***",
                    "owner-1",
                    "A",
                    ["priority", "demo"],
                    "low",
                    "Demo summary"),
            ]);

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"count\":1,\"customers\":[{\"customerId\":\"customer-1\",\"name\":\"Demo Customer\",\"contactName\":\"Demo Contact\",\"mobileMasked\":\"***\",\"ownerId\":\"owner-1\",\"classification\":\"A\",\"tags\":[\"priority\",\"demo\"],\"riskLevel\":\"low\",\"lastInsightSummary\":\"Demo summary\"}]}",
            json);
    }

    [Fact]
    public void Constructors_NormalizeNullCollections()
    {
        var interactions = new CustomerInteractionsData("demo", "source", 0, false, null!);
        var search = new SearchCustomersData(0, null!);
        var customer = new CustomerDto("id", "name", null, null, null, null, null!, null, null);

        Assert.Empty(interactions.Items);
        Assert.Empty(search.Customers);
        Assert.Empty(customer.Tags);
    }

    [Fact]
    public void Deserialize_MissingCollectionsProducesEmptyCollections()
    {
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();
        CustomerInteractionsData? interactions = JsonSerializer.Deserialize<CustomerInteractionsData>(
            "{\"sourceType\":\"demo\",\"sourceId\":\"source\",\"count\":0,\"truncated\":false}",
            options);
        SearchCustomersData? search = JsonSerializer.Deserialize<SearchCustomersData>("{\"count\":0}", options);
        CustomerDto? customer = JsonSerializer.Deserialize<CustomerDto>(
            "{\"customerId\":\"id\",\"name\":\"name\"}",
            options);

        Assert.NotNull(interactions);
        Assert.Empty(interactions.Items);
        Assert.NotNull(search);
        Assert.Empty(search.Customers);
        Assert.NotNull(customer);
        Assert.Empty(customer.Tags);
    }
}
