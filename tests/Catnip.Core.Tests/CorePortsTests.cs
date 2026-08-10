using System.Text.Json;
using Catnip.Core.Configuration;
using Catnip.Core.Idempotency;
using Catnip.Core.Logging;
using Catnip.Core.Security;

namespace Catnip.Core.Tests;

public sealed class CorePortsTests
{
    [Fact]
    public void SecretStore_HasGetSaveDeleteBoundary() =>
        AssertMethods<ISecretStore>("GetAsync", "SaveAsync", "DeleteAsync");

    [Fact]
    public void SettingsStore_UsesJsonDocumentBoundary() =>
        AssertMethods<ISettingsStore>("LoadAsync", "SaveAsync");

    [Fact]
    public void InvocationStore_HasAppendAndPagedReadBoundary() =>
        AssertMethods<IInvocationLogStore>("AppendAsync", "GetRecentAsync");

    [Fact]
    public void IdempotencyStore_HasGetSaveCleanupBoundary() =>
        AssertMethods<IIdempotencyStore>("GetAsync", "SaveAsync", "DeleteExpiredAsync");

    [Fact]
    public void InvocationEntry_CopiesConnectorIds()
    {
        string[] connectors = ["demo"];
        InvocationLogEntry entry = CreateEntry(connectors);
        connectors[0] = "changed";

        Assert.Equal("demo", Assert.Single(entry.ConnectorIds));
    }

    [Fact]
    public void InvocationEntry_NormalizesNullConnectorIds()
    {
        InvocationLogEntry entry = CreateEntry(null!);

        Assert.Empty(entry.ConnectorIds);
    }

    [Fact]
    public void InvocationEntry_PreservesFrozenNullableFields()
    {
        InvocationLogEntry entry = CreateEntry([]);

        Assert.Null(entry.CompletedAtUtc);
        Assert.Null(entry.ModuleId);
        Assert.Null(entry.ErrorCode);
        Assert.Null(entry.DurationMs);
    }

    [Fact]
    public void IdempotencyRecord_PreservesFrozenFieldsAndOffsets()
    {
        var created = new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);
        var record = new IdempotencyRecord("demo-key", "demo-tool", "hash", "{}", created, created.AddDays(1));

        Assert.Equal("demo-key", record.IdempotencyKey);
        Assert.Equal(created.AddDays(1), record.ExpiresAtUtc);
    }

    [Fact]
    public void SettingsBoundary_AcceptsStructuredJsonElement()
    {
        JsonElement document = JsonSerializer.SerializeToElement(new { schemaVersion = 1 });

        Assert.Equal(1, document.GetProperty("schemaVersion").GetInt32());
    }

    private static InvocationLogEntry CreateEntry(IReadOnlyList<string> connectorIds) =>
        new("id", "trace", DateTimeOffset.UnixEpoch, null, "tool", null, connectorIds, true, null, null, null, null, null, 0, 0);

    private static void AssertMethods<T>(params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), typeof(T).GetMethods().Select(static method => method.Name).Order(StringComparer.Ordinal));
}
