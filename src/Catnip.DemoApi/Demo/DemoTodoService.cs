using System.Diagnostics;
using System.Globalization;
using Catnip.DemoApi.Models;

namespace Catnip.DemoApi.Demo;

public sealed class DemoTodoService(TimeProvider timeProvider)
{
    public DemoTodoResponse GetToday()
    {
        DateTimeOffset now = timeProvider.GetLocalNow();
        string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        IReadOnlyList<DemoTodoItem> items =
        [
            new(
                "demo-task-001",
                "local_test_api",
                "task",
                "确认最小 MCP 链路",
                "从 WorkBuddy 调用本地 API，并在控制台日志中核对 TraceId。",
                AtLocalTime(now, 10, 30),
                "high",
                "pending"),
            new(
                "demo-calendar-001",
                "local_test_api",
                "calendar",
                "查看 Runtime 启动过程",
                "在 Mac 控制台观察 Starting 到 Running 的真实状态变化。",
                AtLocalTime(now, 14, 0),
                "normal",
                "pending"),
            new(
                "demo-followup-001",
                "local_test_api",
                "customer_followup",
                "检查日志持续写入",
                "停止并重新启动 Runtime，确认日志仍可在日志页检索。",
                AtLocalTime(now, 17, 30),
                "normal",
                "pending"),
        ];

        string traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        return new DemoTodoResponse(date, items.Count, items, traceId);
    }

    private static DateTimeOffset AtLocalTime(DateTimeOffset value, int hour, int minute) =>
        new(value.Year, value.Month, value.Day, hour, minute, 0, value.Offset);
}
