using Catnip.DemoApi.Models;
using Catnip.DemoApi.Runtime;

namespace Catnip.DemoApi.Tests;

public sealed class RuntimeLogStoreTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 8, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task TailAsync_WhenFileMissing_ReturnsEmptyResult()
    {
        using var directory = new TemporaryDirectory();
        RuntimeLogStore store = CreateStore(directory.Path);

        RuntimeLogResponse response = await store.TailAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.Empty(response.Lines);
        Assert.Equal("runtime-demo-20260807.jsonl", response.FileName);
    }

    [Fact]
    public async Task AppendAsync_RedactsRuntimeSecretBeforeDiskWrite()
    {
        using var directory = new TemporaryDirectory();
        RuntimeLogStore store = CreateStore(directory.Path);
        const string secret = "do-not-write-this-secret";

        await store.AppendAsync(
            "stdout",
            $"configured {secret}",
            secret,
            TestContext.Current.CancellationToken);
        string disk = await File.ReadAllTextAsync(
            store.CurrentFilePath,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(secret, disk, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", disk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailAsync_ReturnsOnlyNewestRequestedLinesInOrder()
    {
        using var directory = new TemporaryDirectory();
        RuntimeLogStore store = CreateStore(directory.Path);
        for (int index = 0; index < 6; index++)
        {
            await store.AppendAsync(
                "stdout",
                $"line-{index}",
                null,
                TestContext.Current.CancellationToken);
        }

        RuntimeLogResponse response = await store.TailAsync(
            3,
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "line-3", "line-4", "line-5" }, response.Lines.Select(line => line.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task TailAsync_RejectsUnboundedRequests(int take)
    {
        using var directory = new TemporaryDirectory();
        RuntimeLogStore store = CreateStore(directory.Path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.TailAsync(take, TestContext.Current.CancellationToken));
    }

    private static RuntimeLogStore CreateStore(string dataRoot)
    {
        var options = new DemoApiOptions(
            "http://127.0.0.1:5220",
            "http://127.0.0.1:5210",
            Path.Combine(Path.GetTempPath(), "Catnip.Runtime.dll"),
            dataRoot);
        return new RuntimeLogStore(options, new FixedTimeProvider(TestNow));
    }
}
