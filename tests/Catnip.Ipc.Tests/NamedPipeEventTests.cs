using System.Text.Json;
using Catnip.Ipc.Events;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Tests;

public sealed class NamedPipeEventTests
{
    [Fact]
    public void Buffer_HasFrozenCapacity()
    {
        Assert.Equal(1000, RuntimeEventBuffer.Capacity);
    }

    [Fact]
    public void Buffer_DropsOldestWhenFull()
    {
        var buffer = new RuntimeEventBuffer();

        for (int index = 0; index < RuntimeEventBuffer.Capacity + 5; index++)
        {
            Assert.True(buffer.TryPublish(CreateEvent("Live", index)));
        }

        var values = new List<int>();
        while (buffer.Reader.TryRead(out RuntimeEvent? runtimeEvent))
        {
            values.Add(runtimeEvent.Payload.GetProperty("value").GetInt32());
        }

        Assert.Equal(RuntimeEventBuffer.Capacity, values.Count);
        Assert.Equal(5, values[0]);
        Assert.Equal(1004, values[^1]);
    }

    [Fact]
    public async Task EventPipe_SendsSnapshotThenLiveEvent()
    {
        string pipeName = CreatePipeName();
        var buffer = new RuntimeEventBuffer();
        var server = new NamedPipeEventServer(pipeName, buffer.Reader, () => CreateEvent("Snapshot", -1));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeEventClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        RuntimeEvent snapshot = await client.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(buffer.TryPublish(CreateEvent("Live", 1)));
        RuntimeEvent live = await client.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Snapshot", snapshot.EventType);
        Assert.Equal("Live", live.EventType);
        Assert.Equal(1, live.Payload.GetProperty("value").GetInt32());
        buffer.TryComplete();
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EventPipe_PreservesPublishedOrder()
    {
        string pipeName = CreatePipeName();
        var buffer = new RuntimeEventBuffer();
        var server = new NamedPipeEventServer(pipeName, buffer.Reader, () => CreateEvent("Snapshot", -1));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeEventClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        _ = await client.ReadAsync(TestContext.Current.CancellationToken);

        for (int index = 0; index < 10; index++)
        {
            Assert.True(buffer.TryPublish(CreateEvent("Live", index)));
        }

        var values = new List<int>();
        for (int index = 0; index < 10; index++)
        {
            RuntimeEvent runtimeEvent = await client.ReadAsync(TestContext.Current.CancellationToken);
            values.Add(runtimeEvent.Payload.GetProperty("value").GetInt32());
        }

        Assert.Equal(Enumerable.Range(0, 10), values);
        buffer.TryComplete();
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void PublishingWithoutClient_RemainsNonBlocking()
    {
        var buffer = new RuntimeEventBuffer();

        for (int index = 0; index < 10_000; index++)
        {
            Assert.True(buffer.TryPublish(CreateEvent("Live", index)));
        }

        Assert.True(buffer.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ClientDisconnect_DoesNotPreventFuturePublishing()
    {
        string pipeName = CreatePipeName();
        var buffer = new RuntimeEventBuffer();
        var server = new NamedPipeEventServer(pipeName, buffer.Reader, () => CreateEvent("Snapshot", -1));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeEventClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        _ = await client.ReadAsync(TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        Assert.True(buffer.TryPublish(CreateEvent("Live", 1)));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(buffer.TryPublish(CreateEvent("Live", 2)));
    }

    [Fact]
    public async Task NewSession_ReceivesFreshSnapshotBeforeBufferedEvents()
    {
        var buffer = new RuntimeEventBuffer();
        int snapshotNumber = 0;

        string firstPipeName = CreatePipeName();
        var firstServer = new NamedPipeEventServer(
            firstPipeName,
            buffer.Reader,
            () => CreateEvent("Snapshot", Interlocked.Increment(ref snapshotNumber)));
        Task firstServerTask = firstServer.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var firstClient = new NamedPipeEventClient(firstPipeName);
        await firstClient.ConnectAsync(TestContext.Current.CancellationToken);
        RuntimeEvent firstSnapshot = await firstClient.ReadAsync(TestContext.Current.CancellationToken);
        await firstClient.DisposeAsync();
        Assert.True(buffer.TryPublish(CreateEvent("DiscardedWithBrokenSession", 0)));
        await firstServerTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(buffer.TryPublish(CreateEvent("Buffered", 9)));
        string secondPipeName = CreatePipeName();
        var secondServer = new NamedPipeEventServer(
            secondPipeName,
            buffer.Reader,
            () => CreateEvent("Snapshot", Interlocked.Increment(ref snapshotNumber)));
        Task secondServerTask = secondServer.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var secondClient = new NamedPipeEventClient(secondPipeName);
        await secondClient.ConnectAsync(TestContext.Current.CancellationToken);
        RuntimeEvent secondSnapshot = await secondClient.ReadAsync(TestContext.Current.CancellationToken);
        RuntimeEvent buffered = await secondClient.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, firstSnapshot.Payload.GetProperty("value").GetInt32());
        Assert.Equal(2, secondSnapshot.Payload.GetProperty("value").GetInt32());
        Assert.Equal("Buffered", buffered.EventType);
        buffer.TryComplete();
        await secondClient.DisposeAsync();
        await secondServerTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClientReadCancellation_DiscardsConnectionAndServerExitsOnNextEvent()
    {
        string pipeName = CreatePipeName();
        var buffer = new RuntimeEventBuffer();
        var server = new NamedPipeEventServer(pipeName, buffer.Reader, () => CreateEvent("Snapshot", -1));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeEventClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        _ = await client.ReadAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.ReadAsync(cancellation.Token));

        Assert.False(client.IsConnected);
        Assert.True(buffer.TryPublish(CreateEvent("Live", 1)));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ServerCancellation_ReleasesClientWaitingForEvent()
    {
        string pipeName = CreatePipeName();
        var buffer = new RuntimeEventBuffer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var server = new NamedPipeEventServer(pipeName, buffer.Reader, () => CreateEvent("Snapshot", -1));
        Task serverTask = server.RunSingleClientAsync(cancellation.Token);
        var client = new NamedPipeEventClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        _ = await client.ReadAsync(TestContext.Current.CancellationToken);
        Task<RuntimeEvent> readTask = client.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await readTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serverTask);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    private static string CreatePipeName() =>
        PipeNames.Events($"t-{Guid.NewGuid():N}"[..10]);

    private static RuntimeEvent CreateEvent(string eventType, int value) =>
        new(
            ProtocolVersion: 1,
            Guid.NewGuid(),
            eventType,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { value }));
}
