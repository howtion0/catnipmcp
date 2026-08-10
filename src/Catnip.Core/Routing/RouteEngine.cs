using System.Text.Json;
using Catnip.Core.Connectors;
using Catnip.Shared.Business;
using Catnip.Shared.Errors;

namespace Catnip.Core.Routing;

public sealed class RouteEngine : IRouteEngine
{
    private const string RouteTraceId = "route";
    private readonly IConnectorRegistry _connectorRegistry;
    private readonly IReadOnlyDictionary<string, RouteDefinition> _routes;

    public RouteEngine(IConnectorRegistry connectorRegistry, IEnumerable<RouteDefinition> routes)
    {
        _connectorRegistry = connectorRegistry ?? throw new ArgumentNullException(nameof(connectorRegistry));
        ArgumentNullException.ThrowIfNull(routes);

        var routeMap = new Dictionary<string, RouteDefinition>(StringComparer.Ordinal);
        foreach (RouteDefinition route in routes)
        {
            Validate(route);
            if (!routeMap.TryAdd(route.Operation, route))
            {
                throw new ArgumentException($"Duplicate route operation '{route.Operation}'.", nameof(routes));
            }
        }

        _routes = routeMap;
    }

    public async ValueTask<RouteExecutionResult> ExecuteAsync(
        string operation,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation) || !_routes.TryGetValue(operation, out RouteDefinition? route))
        {
            return Failure(ErrorCodes.ValidationError, $"Unknown route operation '{operation}'.");
        }

        if (route.Mode == RouteMode.Direct)
        {
            RouteTargetResult target = await ExecuteTargetAsync(route.Targets[0], input, cancellationToken);
            return target.Result.Success
                ? new RouteExecutionResult(true, false, [target], null, null)
                : new RouteExecutionResult(
                    false,
                    false,
                    [target],
                    target.Result.ErrorCode,
                    target.Result.Message);
        }

        Task<RouteTargetResult>[] tasks = route.Targets
            .Select(target => ExecuteTargetAsync(target, input, cancellationToken).AsTask())
            .ToArray();
        RouteTargetResult[] results = await Task.WhenAll(tasks);
        int successCount = results.Count(static result => result.Result.Success);

        if (successCount == results.Length)
        {
            return new RouteExecutionResult(true, false, results, null, null);
        }

        if (successCount > 0)
        {
            return new RouteExecutionResult(true, true, results, null, null);
        }

        RouteTargetResult firstFailure = results[0];
        return new RouteExecutionResult(
            false,
            false,
            results,
            firstFailure.Result.ErrorCode ?? ErrorCodes.UpstreamError,
            firstFailure.Result.Message ?? "All route targets failed.");
    }

    private async ValueTask<RouteTargetResult> ExecuteTargetAsync(
        RouteTarget target,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        if (!_connectorRegistry.TryGet(target.ConnectorId, out IConnector? registered))
        {
            return FailedTarget(target, ErrorCodes.ConnectorUnavailable, "Connector is not registered.");
        }

        if (!_connectorRegistry.TryGetRoutable(target.ConnectorId, out IConnector? connector))
        {
            return FailedTarget(target, ErrorCodes.ConnectorDisabled, "Connector is disabled.");
        }

        if (!registered.SupportedOperations.Contains(target.Operation))
        {
            return FailedTarget(target, ErrorCodes.ValidationError, "Connector operation is not supported.");
        }

        try
        {
            OperationResult<JsonElement> result = await connector.ExecuteAsync(
                target.Operation,
                input,
                cancellationToken);
            return new RouteTargetResult(target.ConnectorId, target.Operation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FailedTarget(target, ErrorCodes.UpstreamError, "Connector execution failed.");
        }
    }

    private static RouteTargetResult FailedTarget(RouteTarget target, string errorCode, string message) =>
        new(
            target.ConnectorId,
            target.Operation,
            OperationResult<JsonElement>.Fail(errorCode, message, RouteTraceId));

    private static RouteExecutionResult Failure(string errorCode, string message) =>
        new(false, false, [], errorCode, message);

    private static void Validate(RouteDefinition route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (string.IsNullOrWhiteSpace(route.Operation))
        {
            throw new ArgumentException("Route operations must not be empty.", nameof(route));
        }

        if (route.Targets.Count == 0)
        {
            throw new ArgumentException("Routes require at least one target.", nameof(route));
        }

        if (route.Targets.Any(static target =>
                string.IsNullOrWhiteSpace(target.ConnectorId)
                || string.IsNullOrWhiteSpace(target.Operation)))
        {
            throw new ArgumentException("Route targets require connector and operation IDs.", nameof(route));
        }

        if (route.Mode == RouteMode.Direct && route.Targets.Count != 1)
        {
            throw new ArgumentException("Direct routes require exactly one target.", nameof(route));
        }

        if (route.IsWrite && route.Mode != RouteMode.Direct)
        {
            throw new ArgumentException("Write routes must use Direct mode.", nameof(route));
        }
    }
}
