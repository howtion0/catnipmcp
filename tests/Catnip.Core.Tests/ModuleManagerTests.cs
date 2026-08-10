using Catnip.Core.Modules;
using Catnip.Shared.Management;

namespace Catnip.Core.Tests;

public sealed class ModuleManagerTests
{
    [Fact]
    public void ModuleIds_AndCatalogMatchFrozenDefaults()
    {
        Assert.Equal(
            ["today-todos", "customer-interactions", "customer-writeback", "weather"],
            ModuleIds.All);
        Assert.Equal(ModuleIds.All, ModuleCatalog.Defaults.Select(static definition => definition.Id));
        Assert.Equal([true, true, false, false], ModuleCatalog.Defaults.Select(static definition => definition.DefaultEnabled));
        Assert.Equal(["feishu", "feishu", "feishu", "weather"], ModuleCatalog.Defaults.SelectMany(static definition => definition.RequiredConnectorIds));
    }

    [Fact]
    public void Definition_CopiesRequiredConnectorIds()
    {
        string[] connectors = ["demo"];
        var definition = new ModuleDefinition("module", "Module", "Demo", true, connectors);

        connectors[0] = "changed";

        Assert.Equal("demo", Assert.Single(definition.RequiredConnectorIds));
    }

    [Fact]
    public void Manager_StartsInCustomModeWithFrozenDefaults()
    {
        var manager = new ModuleManager();

        Assert.Equal(GatewayMode.Custom, manager.Mode);
        Assert.Equal([true, true, false, false], manager.GetSnapshot().Select(static state => state.Enabled));
    }

    [Fact]
    public void Manager_UpdatesCustomSelection()
    {
        var manager = new ModuleManager();

        manager.SetCustomEnabled(ModuleIds.CustomerWriteback, true);

        Assert.True(manager.IsEnabled(ModuleIds.CustomerWriteback));
    }

    [Fact]
    public void FullMode_EnablesOnlyReadyModules()
    {
        var manager = new ModuleManager();
        manager.SetReadiness(ModuleIds.TodayTodos, new ModuleReadiness(true, true));
        manager.SetReadiness(ModuleIds.CustomerInteractions, new ModuleReadiness(true, false));
        manager.SetReadiness(ModuleIds.CustomerWriteback, new ModuleReadiness(false, true));
        manager.SetReadiness(ModuleIds.Weather, new ModuleReadiness(true, true));

        manager.SetMode(GatewayMode.Full);

        Assert.Equal([true, false, false, true], manager.GetSnapshot().Select(static state => state.Enabled));
    }

    [Fact]
    public void ReturningToCustom_RestoresSavedSelection()
    {
        var manager = new ModuleManager();
        manager.SetCustomEnabled(ModuleIds.CustomerWriteback, true);
        manager.SetReadiness(ModuleIds.TodayTodos, new ModuleReadiness(false, false));
        manager.SetMode(GatewayMode.Full);

        manager.SetMode(GatewayMode.Custom);

        Assert.Equal([true, true, true, false], manager.GetSnapshot().Select(static state => state.Enabled));
    }

    [Fact]
    public void CustomChangesDuringFullMode_DoNotChangeFullState()
    {
        var manager = new ModuleManager();
        manager.SetReadiness(ModuleIds.Weather, new ModuleReadiness(true, true));
        manager.SetMode(GatewayMode.Full);

        manager.SetCustomEnabled(ModuleIds.Weather, false);

        Assert.True(manager.IsEnabled(ModuleIds.Weather));
        manager.SetMode(GatewayMode.Custom);
        Assert.False(manager.IsEnabled(ModuleIds.Weather));
    }

    [Fact]
    public void ReadinessChange_RecalculatesCurrentFullState()
    {
        var manager = new ModuleManager();
        manager.SetMode(GatewayMode.Full);

        manager.SetReadiness(ModuleIds.TodayTodos, new ModuleReadiness(true, true));

        Assert.True(manager.IsEnabled(ModuleIds.TodayTodos));
    }

    [Fact]
    public void Snapshot_IsReadOnlyAndDoesNotExposeManagerState()
    {
        var manager = new ModuleManager();
        IReadOnlyList<ModuleState> snapshot = manager.GetSnapshot();

        Assert.Throws<NotSupportedException>(() => ((IList<ModuleState>)snapshot).Clear());
        manager.SetCustomEnabled(ModuleIds.TodayTodos, false);

        Assert.True(snapshot[0].Enabled);
        Assert.False(manager.GetSnapshot()[0].Enabled);
    }

    [Fact]
    public void UnknownModule_IsRejected()
    {
        var manager = new ModuleManager();

        Assert.Throws<ArgumentException>(() => manager.IsEnabled("unknown"));
        Assert.Throws<ArgumentException>(() => manager.SetCustomEnabled("unknown", true));
        Assert.Throws<ArgumentException>(() => manager.SetReadiness("unknown", new ModuleReadiness(true, true)));
    }

    [Fact]
    public void DuplicateDefinitions_AreRejected()
    {
        var definition = new ModuleDefinition("module", "Module", "Demo", true, []);

        Assert.Throws<ArgumentException>(() => new ModuleManager([definition, definition]));
    }

    [Fact]
    public void ConcurrentReadsAndWrites_KeepCompleteSnapshots()
    {
        var manager = new ModuleManager();

        Parallel.For(
            0,
            1_000,
            index =>
            {
                manager.SetCustomEnabled(ModuleIds.TodayTodos, index % 2 == 0);
                manager.SetReadiness(
                    ModuleIds.TodayTodos,
                    new ModuleReadiness(index % 2 == 0, index % 3 == 0));
                Assert.Equal(4, manager.GetSnapshot().Count);
            });

        Assert.Equal(4, manager.GetSnapshot().Count);
    }
}
