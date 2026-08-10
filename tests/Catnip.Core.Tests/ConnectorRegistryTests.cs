using System.Text.Json;
using Catnip.Core.Connectors;
using Catnip.Shared.Business;
using Catnip.Shared.Management;

namespace Catnip.Core.Tests;

public sealed class ConnectorRegistryTests
{
    [Fact]
    public void Register_ExposesConnectorById()
    {
        var registry = new ConnectorRegistry();
        var connector = new StubConnector("demo");

        registry.Register(connector);

        Assert.True(registry.TryGet("demo", out IConnector? result));
        Assert.Same(connector, result);
    }

    [Fact]
    public void Register_RejectsDuplicateId()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"));

        Assert.Throws<ArgumentException>(() => registry.Register(new StubConnector("demo")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Register_RejectsEmptyId(string connectorId)
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new StubConnector(connectorId)));
    }

    [Fact]
    public void TryGet_UnknownIdReturnsFalse()
    {
        var registry = new ConnectorRegistry();

        Assert.False(registry.TryGet("unknown", out IConnector? connector));
        Assert.Null(connector);
    }

    [Fact]
    public void EnabledConnector_IsRoutable()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"), enabled: true);

        Assert.True(registry.TryGetRoutable("demo", out IConnector? connector));
        Assert.NotNull(connector);
    }

    [Fact]
    public void DisabledConnector_IsNotRoutableButRemainsRegistered()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"), enabled: false);

        Assert.False(registry.TryGetRoutable("demo", out IConnector? routable));
        Assert.Null(routable);
        Assert.True(registry.TryGet("demo", out _));
    }

    [Fact]
    public void SetEnabled_ChangesOnlyRouteVisibility()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"), enabled: true, Healthy());

        registry.SetEnabled("demo", false);

        ConnectorRegistrationSnapshot snapshot = Assert.Single(registry.GetSnapshot());
        Assert.False(snapshot.Enabled);
        Assert.Equal(ConnectorStatus.Healthy, snapshot.Health.Status);
    }

    [Fact]
    public void SetEnabled_RejectsUnknownId()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(() => registry.SetEnabled("unknown", true));
    }

    [Fact]
    public void UpdateHealth_ReplacesHealthSnapshot()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"));
        ConnectorHealth health = Healthy();

        registry.UpdateHealth("demo", health);

        Assert.Same(health, Assert.Single(registry.GetSnapshot()).Health);
    }

    [Fact]
    public void UpdateHealth_RejectsUnknownId()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(() => registry.UpdateHealth("unknown", Healthy()));
    }

    [Fact]
    public void DefaultHealth_IsNotConfigured()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"));

        ConnectorHealth health = Assert.Single(registry.GetSnapshot()).Health;

        Assert.Equal(ConnectorStatus.NotConfigured, health.Status);
        Assert.Null(health.LatencyMs);
        Assert.Null(health.LastSuccessfulAt);
    }

    [Fact]
    public void Snapshot_PreservesRegistrationOrderAndMetadata()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("second", "Second", "fake"));
        registry.Register(new StubConnector("first", "First", "http"));

        IReadOnlyList<ConnectorRegistrationSnapshot> snapshot = registry.GetSnapshot();

        Assert.Equal(["second", "first"], snapshot.Select(static item => item.Id));
        Assert.Equal(["Second", "First"], snapshot.Select(static item => item.DisplayName));
        Assert.Equal(["fake", "http"], snapshot.Select(static item => item.Kind));
    }

    [Fact]
    public void Snapshot_OperationsAreSortedAndCopied()
    {
        var operations = new HashSet<string>(StringComparer.Ordinal) { "Tasks.List", "Auth.Test" };
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo", operations: operations));

        ConnectorRegistrationSnapshot snapshot = Assert.Single(registry.GetSnapshot());
        operations.Clear();

        Assert.Equal(["Auth.Test", "Tasks.List"], snapshot.SupportedOperations);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.SupportedOperations).Clear());
    }

    [Fact]
    public void Snapshot_DoesNotChangeAfterRegistryMutation()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo"), enabled: true);
        IReadOnlyList<ConnectorRegistrationSnapshot> before = registry.GetSnapshot();

        registry.SetEnabled("demo", false);

        Assert.True(before[0].Enabled);
        Assert.False(registry.GetSnapshot()[0].Enabled);
    }

    [Fact]
    public async Task ConnectorInterface_ExecutesStructuredResult()
    {
        var connector = new StubConnector("demo");
        JsonElement input = JsonSerializer.SerializeToElement(new { value = 1 });

        OperationResult<JsonElement> result = await connector.ExecuteAsync(
            "Auth.Test",
            input,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task ConnectorInterface_ReturnsHealth()
    {
        var connector = new StubConnector("demo");

        ConnectorHealth health = await connector.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectorStatus.Healthy, health.Status);
    }

    [Fact]
    public void ConcurrentDistinctRegistrationsAndUpdates_AreConsistent()
    {
        var registry = new ConnectorRegistry();

        Parallel.For(0, 100, index => registry.Register(new StubConnector($"demo-{index:D3}")));
        Parallel.For(
            0,
            100,
            index =>
            {
                string id = $"demo-{index:D3}";
                registry.SetEnabled(id, index % 2 == 0);
                registry.UpdateHealth(id, Healthy());
            });

        IReadOnlyList<ConnectorRegistrationSnapshot> snapshot = registry.GetSnapshot();
        Assert.Equal(100, snapshot.Count);
        Assert.Equal(50, snapshot.Count(static connector => connector.Enabled));
        Assert.All(snapshot, static connector => Assert.Equal(ConnectorStatus.Healthy, connector.Health.Status));
    }

    private static ConnectorHealth Healthy() =>
        new(
            ConnectorStatus.Healthy,
            5,
            new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
            null,
            null);

    private sealed class StubConnector : IConnector
    {
        public StubConnector(
            string id,
            string displayName = "Demo",
            string kind = "fake",
            IReadOnlySet<string>? operations = null)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            SupportedOperations = operations ?? new HashSet<string>(StringComparer.Ordinal) { "Auth.Test" };
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Kind { get; }

        public IReadOnlySet<string> SupportedOperations { get; }

        public ValueTask<OperationResult<JsonElement>> ExecuteAsync(
            string operation,
            JsonElement input,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(OperationResult<JsonElement>.Ok(input, "demo-trace"));

        public ValueTask<ConnectorHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Healthy());
    }
}
