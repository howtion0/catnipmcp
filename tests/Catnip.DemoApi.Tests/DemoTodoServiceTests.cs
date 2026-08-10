using Catnip.DemoApi.Demo;
using Catnip.DemoApi.Models;

namespace Catnip.DemoApi.Tests;

public sealed class DemoTodoServiceTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 8, 7, 8, 15, 0, TimeSpan.FromHours(8));

    [Fact]
    public void GetToday_ReturnsFixedLocalApiDemoItems()
    {
        var service = new DemoTodoService(new FixedTimeProvider(TestNow));

        DemoTodoResponse response = service.GetToday();

        Assert.Equal(3, response.Count);
        Assert.All(response.Items, item => Assert.Equal("local_test_api", item.Source));
    }

    [Fact]
    public void GetToday_UsesConfiguredLocalDate()
    {
        var service = new DemoTodoService(new FixedTimeProvider(TestNow));

        DemoTodoResponse response = service.GetToday();

        Assert.Equal("2026-08-07", response.Date);
        Assert.All(response.Items, item => Assert.Equal(TestNow.Offset, item.DueTime?.Offset));
    }

    [Fact]
    public void GetToday_ReturnsUniqueStableTypesAndIds()
    {
        var service = new DemoTodoService(new FixedTimeProvider(TestNow));

        DemoTodoResponse response = service.GetToday();

        Assert.Equal(response.Items.Count, response.Items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(
            new[] { "task", "calendar", "customer_followup" },
            response.Items.Select(item => item.Type));
    }

    [Fact]
    public void GetToday_ReturnsNonEmptyTraceId()
    {
        var service = new DemoTodoService(new FixedTimeProvider(TestNow));

        DemoTodoResponse response = service.GetToday();

        Assert.False(string.IsNullOrWhiteSpace(response.TraceId));
        Assert.True(response.TraceId.Length >= 16);
    }
}
