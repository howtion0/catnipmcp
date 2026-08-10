using Catnip.Shared.Management;

namespace Catnip.Core.Modules;

public sealed record ModuleReadiness(
    bool ConfigurationComplete,
    bool RequiredConnectorsHealthy)
{
    public static ModuleReadiness Unavailable { get; } = new(false, false);

    public bool CanRun => ConfigurationComplete && RequiredConnectorsHealthy;
}

public sealed record ModuleState(
    ModuleDefinition Definition,
    bool Enabled,
    ModuleReadiness Readiness);

public sealed class ModuleManager
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ModuleDefinition> _definitions;
    private readonly IReadOnlyList<string> _orderedIds;
    private readonly Dictionary<string, bool> _customEnabled;
    private readonly Dictionary<string, bool> _fullEnabled;
    private readonly Dictionary<string, ModuleReadiness> _readiness;
    private GatewayMode _mode = GatewayMode.Custom;

    public ModuleManager()
        : this(ModuleCatalog.Defaults)
    {
    }

    public ModuleManager(IEnumerable<ModuleDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        ModuleDefinition[] definitionArray = definitions.ToArray();
        if (definitionArray.Length == 0)
        {
            throw new ArgumentException("At least one module definition is required.", nameof(definitions));
        }

        _definitions = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
        foreach (ModuleDefinition definition in definitionArray)
        {
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new ArgumentException("Module IDs must not be empty.", nameof(definitions));
            }

            if (!_definitions.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate module ID '{definition.Id}'.", nameof(definitions));
            }
        }

        _orderedIds = Array.AsReadOnly(definitionArray.Select(static definition => definition.Id).ToArray());
        _customEnabled = definitionArray.ToDictionary(
            static definition => definition.Id,
            static definition => definition.DefaultEnabled,
            StringComparer.Ordinal);
        _fullEnabled = definitionArray.ToDictionary(
            static definition => definition.Id,
            static _ => false,
            StringComparer.Ordinal);
        _readiness = definitionArray.ToDictionary(
            static definition => definition.Id,
            static _ => ModuleReadiness.Unavailable,
            StringComparer.Ordinal);
    }

    public GatewayMode Mode
    {
        get
        {
            lock (_sync)
            {
                return _mode;
            }
        }
    }

    public void SetMode(GatewayMode mode)
    {
        lock (_sync)
        {
            _mode = mode;
            if (mode == GatewayMode.Full)
            {
                RecalculateFullState();
            }
        }
    }

    public void SetCustomEnabled(string moduleId, bool enabled)
    {
        lock (_sync)
        {
            EnsureKnownModule(moduleId);
            _customEnabled[moduleId] = enabled;
        }
    }

    public void SetReadiness(string moduleId, ModuleReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        lock (_sync)
        {
            EnsureKnownModule(moduleId);
            _readiness[moduleId] = readiness;
            if (_mode == GatewayMode.Full)
            {
                _fullEnabled[moduleId] = readiness.CanRun;
            }
        }
    }

    public bool IsEnabled(string moduleId)
    {
        lock (_sync)
        {
            EnsureKnownModule(moduleId);
            return CurrentEnabled[moduleId];
        }
    }

    public ModuleDefinition GetDefinition(string moduleId)
    {
        lock (_sync)
        {
            EnsureKnownModule(moduleId);
            return _definitions[moduleId];
        }
    }

    public IReadOnlyList<ModuleState> GetSnapshot()
    {
        lock (_sync)
        {
            Dictionary<string, bool> enabled = CurrentEnabled;
            return Array.AsReadOnly(
                _orderedIds
                    .Select(id => new ModuleState(_definitions[id], enabled[id], _readiness[id]))
                    .ToArray());
        }
    }

    private Dictionary<string, bool> CurrentEnabled =>
        _mode == GatewayMode.Full ? _fullEnabled : _customEnabled;

    private void RecalculateFullState()
    {
        foreach (string moduleId in _orderedIds)
        {
            _fullEnabled[moduleId] = _readiness[moduleId].CanRun;
        }
    }

    private void EnsureKnownModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || !_definitions.ContainsKey(moduleId))
        {
            throw new ArgumentException($"Unknown module ID '{moduleId}'.", nameof(moduleId));
        }
    }
}
