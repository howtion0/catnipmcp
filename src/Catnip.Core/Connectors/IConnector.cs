using System.Text.Json;
using Catnip.Shared.Business;

namespace Catnip.Core.Connectors;

public interface IConnector
{
    string Id { get; }

    string DisplayName { get; }

    string Kind { get; }

    IReadOnlySet<string> SupportedOperations { get; }

    ValueTask<OperationResult<JsonElement>> ExecuteAsync(
        string operation,
        JsonElement input,
        CancellationToken cancellationToken);

    ValueTask<ConnectorHealth> CheckHealthAsync(CancellationToken cancellationToken);
}
