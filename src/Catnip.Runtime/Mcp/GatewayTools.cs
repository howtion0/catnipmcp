using System.ComponentModel;
using Catnip.Core.Modules;
using Catnip.Runtime.Hosting;
using Catnip.Shared.Business;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;
using ModelContextProtocol.Server;

namespace Catnip.Runtime.Mcp;

[McpServerToolType]
public sealed class GatewayTools
{
    [McpServerTool(
        Name = "catnip_get_gateway_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Returns the current Catnip runtime, gateway, module, and connector status. "
        + "Use this read-only tool when a user asks whether connections are healthy, "
        + "why a tool is unavailable, or to check the Catnip gateway.")]
    public static OperationResult<GatewayStatusData> GetGatewayStatus(
        GatewayStateService gatewayState)
    {
        var snapshot = gatewayState.GetSnapshot();
        var status = new GatewayStatusData(
            snapshot.ProcessState.ToString(),
            snapshot.MasterEnabled,
            snapshot.Mode.ToString(),
            snapshot.McpAddress,
            snapshot.Version,
            snapshot.StartedAt,
            snapshot.Modules,
            snapshot.Connectors);

        return OperationResult<GatewayStatusData>.Ok(
            status,
            Guid.NewGuid().ToString("N"));
    }

    [McpServerTool(
        Name = "catnip_get_today_todos",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns today's demo todo items through the guarded Catnip runtime.")]
    public static async Task<OperationResult<TodayTodoData>> GetTodayTodosAsync(
        GatewayStateService gatewayState,
        ModuleManager moduleManager,
        IDemoToolBackendClient backendClient,
        CancellationToken cancellationToken)
    {
        OperationResult<TodayTodoData>? blocked = CheckGate<TodayTodoData>(
            gatewayState,
            moduleManager,
            ModuleIds.TodayTodos);
        return blocked ?? await backendClient.GetTodayTodosAsync(cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = "catnip_get_weather",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Returns current weather for a city through the guarded Catnip runtime.")]
    public static async Task<OperationResult<WeatherData>> GetWeatherAsync(
        [Description("City name, for example 上海 or Beijing.")] string city,
        GatewayStateService gatewayState,
        ModuleManager moduleManager,
        IDemoToolBackendClient backendClient,
        CancellationToken cancellationToken)
    {
        string traceId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(city) || city.Length > 50)
        {
            return OperationResult<WeatherData>.Fail(
                ErrorCodes.ValidationError,
                "城市名称必须为 1 到 50 个字符。",
                traceId);
        }

        OperationResult<WeatherData>? blocked = CheckGate<WeatherData>(
            gatewayState,
            moduleManager,
            ModuleIds.Weather,
            traceId);
        return blocked ?? await backendClient.GetWeatherAsync(city.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }

    private static OperationResult<T>? CheckGate<T>(
        GatewayStateService gatewayState,
        ModuleManager moduleManager,
        string moduleId,
        string? traceId = null)
    {
        traceId ??= Guid.NewGuid().ToString("N");
        if (gatewayState.GetSnapshot().ProcessState != RuntimeProcessState.Running)
        {
            return OperationResult<T>.Fail(
                ErrorCodes.RuntimeStopping,
                "Runtime 未运行或正在停止。",
                traceId);
        }

        if (!gatewayState.MasterEnabled)
        {
            return OperationResult<T>.Fail(
                ErrorCodes.GatewayDisabled,
                "MCP 服务总开关已关闭。",
                traceId);
        }

        if (!moduleManager.IsEnabled(moduleId))
        {
            return OperationResult<T>.Fail(
                ErrorCodes.ModuleDisabled,
                "该 MCP 模块已关闭。",
                traceId);
        }

        return null;
    }
}
