using Catnip.Core.Modules;
using Catnip.Runtime.Hosting;
using Catnip.Runtime.Management;
using Catnip.Shared.Management;

namespace Catnip.Runtime.IntegrationTests;

public sealed class GatewayControlServiceTests
{
    [Fact]
    public void State_DefaultsDisabledAndPublishesAtomicMasterChanges()
    {
        var state = CreateState();

        Assert.False(state.MasterEnabled);
        Assert.False(state.GetSnapshot().MasterEnabled);

        state.SetMasterEnabled(true);

        Assert.True(state.MasterEnabled);
        Assert.True(state.GetSnapshot().MasterEnabled);
    }

    [Fact]
    public async Task Control_PersistsBeforePublishingState()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state);
        var control = new GatewayControlService(state, moduleManager, persistence);

        var snapshot = await control.SetMasterEnabledAsync(
            enabled: true,
            TestContext.Current.CancellationToken);

        Assert.False(persistence.ObservedStateDuringSave);
        Assert.True(persistence.MasterEnabled);
        Assert.True(snapshot.MasterEnabled);

        var disabledSnapshot = await control.SetMasterEnabledAsync(
            enabled: false,
            TestContext.Current.CancellationToken);

        Assert.True(persistence.ObservedStateDuringSave);
        Assert.False(persistence.MasterEnabled);
        Assert.False(disabledSnapshot.MasterEnabled);
    }

    [Fact]
    public async Task Control_SaveFailureLeavesPublishedStateUnchanged()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state) { FailSave = true };
        var control = new GatewayControlService(state, moduleManager, persistence);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await control.SetMasterEnabledAsync(
                enabled: true,
                TestContext.Current.CancellationToken));

        Assert.False(state.MasterEnabled);
        Assert.False(state.GetSnapshot().MasterEnabled);
    }

    [Fact]
    public async Task Control_ConcurrentUpdatesRemainSerializedAndConsistent()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state) { YieldDuringSave = true };
        var control = new GatewayControlService(state, moduleManager, persistence);

        await Task.WhenAll(
            Enumerable.Range(0, 100)
                .Select(index => control.SetMasterEnabledAsync(
                    index % 2 == 0,
                    TestContext.Current.CancellationToken).AsTask()));

        Assert.Equal(1, persistence.MaximumConcurrentSaves);
        Assert.Equal(persistence.MasterEnabled, state.MasterEnabled);
        Assert.Equal(state.MasterEnabled, state.GetSnapshot().MasterEnabled);
    }

    [Fact]
    public async Task Control_ModeAndModuleChangesAreVisibleInSnapshot()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state);
        var control = new GatewayControlService(state, moduleManager, persistence);

        await control.SetModuleEnabledAsync(
            ModuleIds.TodayTodos,
            enabled: false,
            TestContext.Current.CancellationToken);
        var snapshot = await control.SetGatewayModeAsync(
            GatewayMode.Custom,
            TestContext.Current.CancellationToken);

        ModuleInfoDto todos = Assert.Single(
            snapshot.Modules,
            static module => module.Id == ModuleIds.TodayTodos);
        Assert.Equal(GatewayMode.Custom, snapshot.Mode);
        Assert.False(todos.Enabled);
        Assert.Equal(ModuleStatus.Disabled, todos.Status);
    }

    [Fact]
    public async Task Control_FullModeUsesCoreReadinessWithoutLosingCustomSelection()
    {
        var moduleManager = new ModuleManager();
        moduleManager.SetCustomEnabled(ModuleIds.TodayTodos, false);
        moduleManager.SetReadiness(ModuleIds.TodayTodos, new ModuleReadiness(true, true));
        var state = CreateState(moduleManager);
        var control = new GatewayControlService(state, moduleManager, new RecordingPersistence(state));

        var full = await control.SetGatewayModeAsync(
            GatewayMode.Full,
            TestContext.Current.CancellationToken);
        var custom = await control.SetGatewayModeAsync(
            GatewayMode.Custom,
            TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(full.Modules, static x => x.Id == ModuleIds.TodayTodos).Enabled);
        Assert.False(Assert.Single(custom.Modules, static x => x.Id == ModuleIds.TodayTodos).Enabled);
    }

    [Fact]
    public async Task Control_RejectsUnknownModuleBeforePersistence()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state);
        var control = new GatewayControlService(state, moduleManager, persistence);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await control.SetModuleEnabledAsync(
                "unknown",
                enabled: true,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, persistence.ModuleSaveCount);
    }

    [Fact]
    public async Task Control_RejectsUndefinedModeBeforePersistence()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state);
        var control = new GatewayControlService(state, moduleManager, persistence);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await control.SetGatewayModeAsync(
                (GatewayMode)99,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, persistence.ModeSaveCount);
    }

    [Fact]
    public async Task Control_ModuleSaveFailureLeavesCustomSelectionUnchanged()
    {
        var moduleManager = new ModuleManager();
        var state = CreateState(moduleManager);
        var persistence = new RecordingPersistence(state) { FailModuleSave = true };
        var control = new GatewayControlService(state, moduleManager, persistence);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await control.SetModuleEnabledAsync(
                ModuleIds.TodayTodos,
                enabled: false,
                TestContext.Current.CancellationToken));

        Assert.True(moduleManager.IsEnabled(ModuleIds.TodayTodos));
        Assert.True(Assert.Single(
            state.GetSnapshot().Modules,
            static module => module.Id == ModuleIds.TodayTodos).Enabled);
    }

    private static GatewayStateService CreateState() =>
        new("http://127.0.0.1:5210/mcp", "test");

    private static GatewayStateService CreateState(ModuleManager moduleManager) =>
        new("http://127.0.0.1:5210/mcp", "test", moduleManager);

    private sealed class RecordingPersistence(GatewayStateService state) : IGatewayControlPersistence
    {
        private int _activeSaves;
        private int _masterEnabled;
        private int _maximumConcurrentSaves;
        private int _modeSaveCount;
        private int _moduleSaveCount;

        public bool FailSave { get; init; }

        public bool FailModuleSave { get; init; }

        public bool YieldDuringSave { get; init; }

        public bool MasterEnabled => Volatile.Read(ref _masterEnabled) == 1;

        public bool ObservedStateDuringSave { get; private set; }

        public int MaximumConcurrentSaves => Volatile.Read(ref _maximumConcurrentSaves);

        public int ModeSaveCount => Volatile.Read(ref _modeSaveCount);

        public int ModuleSaveCount => Volatile.Read(ref _moduleSaveCount);

        public async ValueTask SaveMasterEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int active = Interlocked.Increment(ref _activeSaves);
            UpdateMaximum(ref _maximumConcurrentSaves, active);

            try
            {
                ObservedStateDuringSave = state.MasterEnabled;
                if (FailSave)
                {
                    throw new InvalidOperationException("Simulated persistence failure.");
                }

                if (YieldDuringSave)
                {
                    await Task.Yield();
                }

                Interlocked.Exchange(ref _masterEnabled, enabled ? 1 : 0);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSaves);
            }
        }

        public ValueTask SaveGatewayModeAsync(
            GatewayMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _modeSaveCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveModuleEnabledAsync(
            string moduleId,
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _moduleSaveCount);

            if (FailModuleSave)
            {
                throw new InvalidOperationException("Simulated module persistence failure.");
            }

            return ValueTask.CompletedTask;
        }

        private static void UpdateMaximum(ref int maximum, int value)
        {
            int current = Volatile.Read(ref maximum);

            while (current < value)
            {
                int observed = Interlocked.CompareExchange(ref maximum, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
