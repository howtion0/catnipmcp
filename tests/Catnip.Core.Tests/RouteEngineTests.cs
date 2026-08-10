using System.Collections.Concurrent;
using System.Text.Json;
using Catnip.Core.Connectors;
using Catnip.Core.Routing;
using Catnip.Shared.Business;
using Catnip.Shared.Errors;

namespace Catnip.Core.Tests;

public sealed class RouteEngineTests
{
    [Fact]
    public async Task Direct_SuccessReturnsSingleTarget()
    {
        RouteEngine engine = CreateEngine(
            new StubConnector("demo", Success),
            DirectRoute());

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.True(result.Success);
        Assert.False(result.PartialSuccess);
        Assert.True(Assert.Single(result.Targets).Result.Success);
    }

    [Fact]
    public async Task Direct_BusinessFailureIsPreserved()
    {
        RouteEngine engine = CreateEngine(
            new StubConnector("demo", Failure),
            DirectRoute());

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsValidationError()
    {
        RouteEngine engine = CreateEngine(new StubConnector("demo", Success), DirectRoute());

        RouteExecutionResult result = await engine.ExecuteAsync(
            "Unknown",
            JsonSerializer.SerializeToElement(new { }),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public async Task MissingConnector_ReturnsUnavailable()
    {
        var registry = new ConnectorRegistry();
        var engine = new RouteEngine(registry, [DirectRoute()]);

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.Equal(ErrorCodes.ConnectorUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task DisabledConnector_ReturnsDisabled()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("demo", Success), enabled: false);
        var engine = new RouteEngine(registry, [DirectRoute()]);

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.Equal(ErrorCodes.ConnectorDisabled, result.ErrorCode);
    }

    [Fact]
    public async Task UnsupportedConnectorOperation_ReturnsValidationError()
    {
        RouteEngine engine = CreateEngine(
            new StubConnector(
                "demo",
                Success,
                new HashSet<string>(StringComparer.Ordinal) { "Other.Operation" }),
            DirectRoute());

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
    }

    [Fact]
    public async Task ConnectorException_ReturnsUpstreamError()
    {
        RouteEngine engine = CreateEngine(
            new StubConnector("demo", static (_, _) => throw new InvalidOperationException()),
            DirectRoute());

        RouteExecutionResult result = await ExecuteAsync(engine);

        Assert.Equal(ErrorCodes.UpstreamError, result.ErrorCode);
    }

    [Fact]
    public async Task Aggregate_AllSuccessReturnsOrderedTargets()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("first", Success));
        registry.Register(new StubConnector("second", Success));
        var engine = new RouteEngine(registry, [AggregateRoute()]);

        RouteExecutionResult result = await ExecuteAsync(engine, "Aggregate");

        Assert.True(result.Success);
        Assert.False(result.PartialSuccess);
        Assert.Equal(["first", "second"], result.Targets.Select(static target => target.ConnectorId));
    }

    [Fact]
    public async Task Aggregate_PartialFailureReturnsSuccessWithPartialFlag()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("first", Success));
        registry.Register(new StubConnector("second", Failure));
        var engine = new RouteEngine(registry, [AggregateRoute()]);

        RouteExecutionResult result = await ExecuteAsync(engine, "Aggregate");

        Assert.True(result.Success);
        Assert.True(result.PartialSuccess);
        Assert.Equal([true, false], result.Targets.Select(static target => target.Result.Success));
    }

    [Fact]
    public async Task Aggregate_AllFailureReturnsStableFailure()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("first", Failure));
        registry.Register(new StubConnector("second", Failure));
        var engine = new RouteEngine(registry, [AggregateRoute()]);

        RouteExecutionResult result = await ExecuteAsync(engine, "Aggregate");

        Assert.False(result.Success);
        Assert.False(result.PartialSuccess);
        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(2, result.Targets.Count);
    }

    [Fact]
    public async Task Aggregate_StartsTargetsConcurrently()
    {
        var started = new ConcurrentBag<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<OperationResult<JsonElement>> Wait(string id, CancellationToken cancellationToken)
        {
            started.Add(id);
            await release.Task.WaitAsync(cancellationToken);
            return OperationResult<JsonElement>.Ok(default, "demo-trace");
        }

        var registry = new ConnectorRegistry();
        registry.Register(new StubConnector("first", (input, token) => Wait("first", token)));
        registry.Register(new StubConnector("second", (input, token) => Wait("second", token)));
        var engine = new RouteEngine(registry, [AggregateRoute()]);

        ValueTask<RouteExecutionResult> execution = engine.ExecuteAsync(
            "Aggregate",
            JsonSerializer.SerializeToElement(new { }),
            TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => started.Count == 2);
        release.SetResult();

        Assert.True((await execution).Success);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connector = new StubConnector(
            "demo",
            static async (input, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return OperationResult<JsonElement>.Ok(input, "demo-trace");
            });
        RouteEngine engine = CreateEngine(connector, DirectRoute());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await engine.ExecuteAsync(
                "Direct",
                JsonSerializer.SerializeToElement(new { }),
                cancellation.Token));
    }

    [Fact]
    public void DuplicateRoute_IsRejected()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(() => new RouteEngine(registry, [DirectRoute(), DirectRoute()]));
    }

    [Fact]
    public void EmptyTargets_AreRejected()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(
            () => new RouteEngine(registry, [new RouteDefinition("Empty", RouteMode.Aggregate, [])]));
    }

    [Fact]
    public void DirectWithMultipleTargets_IsRejected()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(
            () => new RouteEngine(
                registry,
                [new RouteDefinition("Direct", RouteMode.Direct, [new("one", "Read"), new("two", "Read")])]));
    }

    [Fact]
    public void AggregateWriteRoute_IsRejected()
    {
        var registry = new ConnectorRegistry();

        Assert.Throws<ArgumentException>(
            () => new RouteEngine(
                registry,
                [new RouteDefinition("Write", RouteMode.Aggregate, [new("demo", "Write")], IsWrite: true)]));
    }

    [Fact]
    public void Definition_CopiesTargets()
    {
        RouteTarget[] targets = [new("demo", "Read")];
        var definition = new RouteDefinition("Direct", RouteMode.Direct, targets);

        targets[0] = new RouteTarget("changed", "Read");

        Assert.Equal("demo", Assert.Single(definition.Targets).ConnectorId);
    }

    private static RouteEngine CreateEngine(IConnector connector, RouteDefinition definition)
    {
        var registry = new ConnectorRegistry();
        registry.Register(connector);
        return new RouteEngine(registry, [definition]);
    }

    private static RouteDefinition DirectRoute() =>
        new("Direct", RouteMode.Direct, [new RouteTarget("demo", "Read")]);

    private static RouteDefinition AggregateRoute() =>
        new(
            "Aggregate",
            RouteMode.Aggregate,
            [new RouteTarget("first", "Read"), new RouteTarget("second", "Read")]);

    private static ValueTask<RouteExecutionResult> ExecuteAsync(RouteEngine engine, string operation = "Direct") =>
        engine.ExecuteAsync(
            operation,
            JsonSerializer.SerializeToElement(new { value = 1 }),
            TestContext.Current.CancellationToken);

    private static ValueTask<OperationResult<JsonElement>> Success(
        JsonElement input,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(OperationResult<JsonElement>.Ok(input, "demo-trace"));

    private static ValueTask<OperationResult<JsonElement>> Failure(
        JsonElement input,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            OperationResult<JsonElement>.Fail(ErrorCodes.NotFound, "Demo not found", "demo-trace"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubConnector(
        string id,
        Func<JsonElement, CancellationToken, ValueTask<OperationResult<JsonElement>>> execute,
        IReadOnlySet<string>? operations = null) : IConnector
    {
        public string Id { get; } = id;

        public string DisplayName => Id;

        public string Kind => "fake";

        public IReadOnlySet<string> SupportedOperations { get; } =
            operations ?? new HashSet<string>(StringComparer.Ordinal) { "Read" };

        public ValueTask<OperationResult<JsonElement>> ExecuteAsync(
            string operation,
            JsonElement input,
            CancellationToken cancellationToken) =>
            execute(input, cancellationToken);

        public ValueTask<ConnectorHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(ConnectorHealth.NotConfigured(DateTimeOffset.UnixEpoch));
    }
}
