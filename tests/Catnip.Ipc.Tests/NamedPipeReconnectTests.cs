using System.Text.Json;
using Catnip.Ipc.Management;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Tests;

public sealed class NamedPipeReconnectTests
{
    [Fact]
    public async Task ServerLoop_AcceptsConsecutiveClientSessions()
    {
        string pipeName = CreatePipeName();
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var server = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task serverTask = server.RunAsync(serverCancellation.Token);

        for (int session = 0; session < 2; session++)
        {
            var client = new NamedPipeManagementClient(pipeName);
            await client.ConnectAsync(TestContext.Current.CancellationToken);
            ManagementResponse response = await client.SendWithSafeReconnectAsync(
                CreateRequest(NamedPipeManagementClient.PingCommand),
                TestContext.Current.CancellationToken);
            Assert.True(response.Success);
            await client.DisposeAsync();
        }

        serverCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serverTask);
    }

    [Theory]
    [InlineData(NamedPipeManagementClient.PingCommand)]
    [InlineData(NamedPipeManagementClient.GetRuntimeSnapshotCommand)]
    public async Task IdempotentRead_ReconnectsOnceAfterBrokenSession(string command)
    {
        string pipeName = CreatePipeName();
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var firstServer = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task firstServerTask = firstServer.RunSingleClientAsync(firstCancellation.Token);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstServerTask);

        var secondServer = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task secondServerTask = secondServer.RunSingleClientAsync(TestContext.Current.CancellationToken);

        ManagementResponse response = await client.SendWithSafeReconnectAsync(
            CreateRequest(command),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        await client.DisposeAsync();
        await secondServerTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WriteCommand_IsNeverRetriedAfterDisconnect()
    {
        string pipeName = CreatePipeName();
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var server = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task serverTask = server.RunSingleClientAsync(serverCancellation.Token);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        serverCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serverTask);

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await client.SendWithSafeReconnectAsync(
                CreateRequest("SetMasterEnabled"),
                TestContext.Current.CancellationToken));

        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Timeout_IsNotRetriedEvenForReadCommand()
    {
        string pipeName = CreatePipeName();
        int invocationCount = 0;
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new NamedPipeManagementServer(
            pipeName,
            async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref invocationCount);
                await releaseHandler.Task.WaitAsync(cancellationToken);
                return CreateSuccessResponse(request);
            });
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName, TimeSpan.FromMilliseconds(50));
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await client.SendWithSafeReconnectAsync(
                CreateRequest(NamedPipeManagementClient.PingCommand),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, invocationCount);
        releaseHandler.SetResult();
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadRetry_IsLimitedToOneAdditionalAttempt()
    {
        string pipeName = CreatePipeName();
        int invocationCount = 0;
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstServer = new NamedPipeManagementServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref invocationCount);
                firstStarted.SetResult();
                firstCancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            });
        Task firstServerTask = firstServer.RunSingleClientAsync(firstCancellation.Token);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        Task coordinator = Task.Run(
            async () =>
            {
                await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstServerTask);
                var secondServer = new NamedPipeManagementServer(
                    pipeName,
                    async (_, cancellationToken) =>
                    {
                        Interlocked.Increment(ref invocationCount);
                        secondStarted.SetResult();
                        secondCancellation.Cancel();
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        throw new InvalidOperationException();
                    });
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => await secondServer.RunSingleClientAsync(secondCancellation.Token));
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await client.SendWithSafeReconnectAsync(
                CreateRequest(NamedPipeManagementClient.PingCommand),
                TestContext.Current.CancellationToken));

        await secondStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await coordinator.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(2, invocationCount);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task CanceledReconnect_DoesNotLoop()
    {
        var client = new NamedPipeManagementClient(CreatePipeName());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.SendWithSafeReconnectAsync(
                CreateRequest(NamedPipeManagementClient.PingCommand),
                cancellation.Token));

        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    private static string CreatePipeName() =>
        PipeNames.Management($"t-{Guid.NewGuid():N}"[..10]);

    private static ManagementRequest CreateRequest(string command) =>
        new(
            NamedPipeManagementClient.ProtocolVersion,
            Guid.NewGuid(),
            command,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new EmptyCommand()));

    private static ValueTask<ManagementResponse> SuccessResponse(
        ManagementRequest request,
        CancellationToken _) =>
        ValueTask.FromResult(CreateSuccessResponse(request));

    private static ManagementResponse CreateSuccessResponse(ManagementRequest request) =>
        new(
            NamedPipeManagementClient.ProtocolVersion,
            request.RequestId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            request.Payload);
}
