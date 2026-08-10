using System.Diagnostics.CodeAnalysis;

namespace Catnip.Core.Connectors;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
    private readonly List<string> _orderedIds = [];

    public void Register(IConnector connector, bool enabled = true, ConnectorHealth? initialHealth = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        if (string.IsNullOrWhiteSpace(connector.Id))
        {
            throw new ArgumentException("Connector IDs must not be empty.", nameof(connector));
        }

        lock (_sync)
        {
            var registration = new Registration(
                connector,
                enabled,
                initialHealth ?? ConnectorHealth.NotConfigured(DateTimeOffset.UtcNow));
            if (!_registrations.TryAdd(connector.Id, registration))
            {
                throw new ArgumentException($"Duplicate connector ID '{connector.Id}'.", nameof(connector));
            }

            _orderedIds.Add(connector.Id);
        }
    }

    public bool TryGet(string connectorId, [NotNullWhen(true)] out IConnector? connector)
    {
        lock (_sync)
        {
            if (_registrations.TryGetValue(connectorId, out Registration? registration))
            {
                connector = registration.Connector;
                return true;
            }

            connector = null;
            return false;
        }
    }

    public bool TryGetRoutable(string connectorId, [NotNullWhen(true)] out IConnector? connector)
    {
        lock (_sync)
        {
            if (_registrations.TryGetValue(connectorId, out Registration? registration)
                && registration.Enabled)
            {
                connector = registration.Connector;
                return true;
            }

            connector = null;
            return false;
        }
    }

    public void SetEnabled(string connectorId, bool enabled)
    {
        lock (_sync)
        {
            Registration registration = GetRequired(connectorId);
            registration.Enabled = enabled;
        }
    }

    public void UpdateHealth(string connectorId, ConnectorHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        lock (_sync)
        {
            Registration registration = GetRequired(connectorId);
            registration.Health = health;
        }
    }

    public IReadOnlyList<ConnectorRegistrationSnapshot> GetSnapshot()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(
                _orderedIds
                    .Select(CreateSnapshot)
                    .ToArray());
        }
    }

    private ConnectorRegistrationSnapshot CreateSnapshot(string connectorId)
    {
        Registration registration = _registrations[connectorId];
        return new ConnectorRegistrationSnapshot(
            registration.Connector.Id,
            registration.Connector.DisplayName,
            registration.Connector.Kind,
            registration.Enabled,
            registration.Health,
            registration.Connector.SupportedOperations.Order(StringComparer.Ordinal).ToArray());
    }

    private Registration GetRequired(string connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId)
            || !_registrations.TryGetValue(connectorId, out Registration? registration))
        {
            throw new ArgumentException($"Unknown connector ID '{connectorId}'.", nameof(connectorId));
        }

        return registration;
    }

    private sealed class Registration(
        IConnector connector,
        bool enabled,
        ConnectorHealth health)
    {
        public IConnector Connector { get; } = connector;

        public bool Enabled { get; set; } = enabled;

        public ConnectorHealth Health { get; set; } = health;
    }
}
