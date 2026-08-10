namespace Catnip.Core.Modules;

public sealed record ModuleDefinition(
    string Id,
    string DisplayName,
    string Description,
    bool DefaultEnabled,
    IReadOnlyList<string> RequiredConnectorIds)
{
    public IReadOnlyList<string> RequiredConnectorIds { get; init; } =
        Array.AsReadOnly((RequiredConnectorIds ?? []).ToArray());
}

public static class ModuleCatalog
{
    private static readonly IReadOnlyList<ModuleDefinition> DefaultDefinitions =
        Array.AsReadOnly<ModuleDefinition>(
        [
            new(
                ModuleIds.TodayTodos,
                "今日待办",
                "聚合任务、日历、审批、客户跟进和回款计划。",
                true,
                ["feishu"]),
            new(
                ModuleIds.CustomerInteractions,
                "客户沟通读取",
                "读取测试来源中的客户沟通。",
                true,
                ["feishu"]),
            new(
                ModuleIds.CustomerWriteback,
                "客户资料写回",
                "写回已确认的客户画像和跟进结果。",
                false,
                ["feishu"]),
            new(
                ModuleIds.Weather,
                "天气测试",
                "调用配置的天气测试连接器。",
                false,
                ["weather"]),
        ]);

    public static IReadOnlyList<ModuleDefinition> Defaults => DefaultDefinitions;
}
