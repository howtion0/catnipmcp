using System.ComponentModel;
using Catnip.Shared.Business;
using ModelContextProtocol.Server;

namespace Catnip.WorkBuddyBridge.Mcp;

[McpServerToolType]
public sealed class BridgeTools(DemoApiBridgeClient client)
{
    [McpServerTool(
        Name = "catnip_get_gateway_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "检查Catnip 的本地运行状态、总开关、模式和模块状态。"
        + "当用户询问服务是否启动、MCP 是否可用或模块为什么不可用时调用。"
        + "这是只读操作，不会修改任何配置或业务数据。")]
    public Task<OperationResult<GatewayStatusData>> GetGatewayStatusAsync(
        CancellationToken cancellationToken) =>
        client.GetGatewayStatusAsync(cancellationToken);

    [McpServerTool(
        Name = "catnip_get_today_todos",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "读取Catnip 本地 Demo API 的今日待办。"
        + "当用户询问今天要做什么、今日计划或待办清单时调用。"
        + "这是只读测试操作，当前固定返回三条可核对的 Demo 数据。")]
    public Task<OperationResult<TodayTodoData>> GetTodayTodosAsync(
        CancellationToken cancellationToken) =>
        client.GetTodayTodosAsync(cancellationToken);

    [McpServerTool(
        Name = "catnip_get_weather",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "通过Catnip 的本地 API 查询和风天气实时天气。"
        + "当用户询问某个城市当前天气或温度时调用，city 必须是 1-50 字符的城市名或 LocationID。"
        + "调用链为 WorkBuddy 到 MCP Server、再到本地 Demo API 和和风 GeoAPI；这是只读操作。")]
    public Task<OperationResult<WeatherData>> GetWeatherAsync(
        [Description("城市名称，例如北京、上海，或和风 LocationID。")]
        string city,
        CancellationToken cancellationToken) =>
        client.GetWeatherAsync(city, cancellationToken);
}
