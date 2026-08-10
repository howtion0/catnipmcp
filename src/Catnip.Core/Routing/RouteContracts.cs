using Catnip.Shared.Business;

namespace Catnip.Core.Routing;

public enum RouteMode
{
    Direct,
    Aggregate,
}

public sealed record RouteTarget(string ConnectorId, string Operation);

public sealed record RouteDefinition(
    string Operation,
    RouteMode Mode,
    IReadOnlyList<RouteTarget> Targets,
    bool IsWrite = false)
{
    public IReadOnlyList<RouteTarget> Targets { get; init; } =
        Array.AsReadOnly((Targets ?? []).ToArray());
}

public sealed record RouteTargetResult(
    string ConnectorId,
    string Operation,
    OperationResult<System.Text.Json.JsonElement> Result);

public sealed record RouteExecutionResult(
    bool Success,
    bool PartialSuccess,
    IReadOnlyList<RouteTargetResult> Targets,
    string? ErrorCode,
    string? Message)
{
    public IReadOnlyList<RouteTargetResult> Targets { get; init; } =
        Array.AsReadOnly((Targets ?? []).ToArray());
}
