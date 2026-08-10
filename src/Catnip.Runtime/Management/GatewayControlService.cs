using Catnip.Core.Modules;
using Catnip.Runtime.Hosting;
using Catnip.Shared.Management;

namespace Catnip.Runtime.Management;

public interface IGatewayControlPersistence
{
    ValueTask SaveMasterEnabledAsync(bool enabled, CancellationToken cancellationToken);

    ValueTask SaveGatewayModeAsync(GatewayMode mode, CancellationToken cancellationToken);

    ValueTask SaveModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken);
}

public sealed class InMemoryGatewayControlPersistence : IGatewayControlPersistence
{
    private int _masterEnabled;
    private int _mode = (int)GatewayMode.Custom;
    private readonly Dictionary<string, bool> _modules = new(StringComparer.Ordinal);

    public bool MasterEnabled => Volatile.Read(ref _masterEnabled) == 1;

    public GatewayMode Mode => (GatewayMode)Volatile.Read(ref _mode);

    public ValueTask SaveMasterEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _masterEnabled, enabled ? 1 : 0);
        return ValueTask.CompletedTask;
    }

    public ValueTask SaveGatewayModeAsync(GatewayMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _mode, (int)mode);
        return ValueTask.CompletedTask;
    }

    public ValueTask SaveModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_modules)
        {
            _modules[moduleId] = enabled;
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class GatewayControlService(
    GatewayStateService gatewayState,
    ModuleManager moduleManager,
    IGatewayControlPersistence persistence)
{
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public async ValueTask<RuntimeSnapshot> SetMasterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await persistence.SaveMasterEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
            gatewayState.SetMasterEnabled(enabled);
            return gatewayState.GetSnapshot();
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async ValueTask<RuntimeSnapshot> SetGatewayModeAsync(
        GatewayMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Gateway mode is invalid.");
        }

        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await persistence.SaveGatewayModeAsync(mode, cancellationToken).ConfigureAwait(false);
            moduleManager.SetMode(mode);
            return gatewayState.GetSnapshot();
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async ValueTask<RuntimeSnapshot> SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        moduleManager.GetDefinition(moduleId);
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await persistence.SaveModuleEnabledAsync(moduleId, enabled, cancellationToken)
                .ConfigureAwait(false);
            moduleManager.SetCustomEnabled(moduleId, enabled);
            return gatewayState.GetSnapshot();
        }
        finally
        {
            _updateLock.Release();
        }
    }
}
