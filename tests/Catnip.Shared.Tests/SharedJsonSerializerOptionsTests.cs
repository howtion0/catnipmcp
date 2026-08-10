using System.Text.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class SharedJsonSerializerOptionsTests
{
    [Fact]
    public void Serialize_UsesFrozenPropertyNamesStringEnumsAndEmptyArrays()
    {
        var result = OperationResult<JsonTestPayload>.Ok(
            new JsonTestPayload(42, JsonTestState.InProgress),
            "0123456789abcdef0123456789abcdef");

        string json = JsonSerializer.Serialize(result, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"success\":true,\"errorCode\":null,\"message\":null,\"data\":{\"itemCount\":42,\"state\":\"inProgress\"},\"traceId\":\"0123456789abcdef0123456789abcdef\",\"warnings\":[]}",
            json);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownFields()
    {
        const string json =
            "{\"itemCount\":42,\"state\":\"inProgress\",\"futureField\":\"ignored\"}";

        JsonTestPayload? result = JsonSerializer.Deserialize<JsonTestPayload>(
            json,
            SharedJsonSerializerOptions.Create());

        Assert.Equal(new JsonTestPayload(42, JsonTestState.InProgress), result);
    }

    [Fact]
    public void Deserialize_RejectsNumbersEncodedAsStrings()
    {
        const string json = "{\"itemCount\":\"42\",\"state\":\"inProgress\"}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonTestPayload>(json, SharedJsonSerializerOptions.Create()));
    }

    [Fact]
    public void Deserialize_RejectsIntegerEnums()
    {
        const string json = "{\"itemCount\":42,\"state\":1}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonTestPayload>(json, SharedJsonSerializerOptions.Create()));
    }

    [Fact]
    public void Create_ReturnsIndependentOptions()
    {
        JsonSerializerOptions first = SharedJsonSerializerOptions.Create();
        JsonSerializerOptions second = SharedJsonSerializerOptions.Create();

        Assert.NotSame(first, second);
        Assert.NotSame(first.Converters, second.Converters);
    }
}

public enum JsonTestState
{
    NotStarted,
    InProgress,
}

public sealed record JsonTestPayload(int ItemCount, JsonTestState State);
