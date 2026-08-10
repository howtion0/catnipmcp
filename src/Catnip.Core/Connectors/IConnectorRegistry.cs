using System.Diagnostics.CodeAnalysis;

namespace Catnip.Core.Connectors;

public interface IConnectorRegistry
{
    void Register(IConnector connector, bool enabled = true, ConnectorHealth? initialHealth = null);

    bool TryGet(string connectorId, [NotNullWhen(true)] out IConnector? connector);

    bool TryGetRoutable(string connectorId, [NotNullWhen(true)] out IConnector? connector);

    void SetEnabled(string connectorId, bool enabled);

    void UpdateHealth(string connectorId, ConnectorHealth health);

    IReadOnlyList<ConnectorRegistrationSnapshot> GetSnapshot();
}
