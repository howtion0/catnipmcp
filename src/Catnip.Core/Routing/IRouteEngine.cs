using System.Text.Json;

namespace Catnip.Core.Routing;

public interface IRouteEngine
{
    ValueTask<RouteExecutionResult> ExecuteAsync(
        string operation,
        JsonElement input,
        CancellationToken cancellationToken);
}
