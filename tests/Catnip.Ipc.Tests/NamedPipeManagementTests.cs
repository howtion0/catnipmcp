using System.Text.Json;
using Catnip.Ipc.Framing;
using Catnip.Ipc.Management;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Tests;

public sealed class NamedPipeManagementTests
{
    [Fact]
    public async Task Ping_RoundtripsOverRealPipe()
    {
        string pipeName = CreatePipeName();
        var server = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        ManagementResponse response = await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Equal(NamedPipeManagementClient.ProtocolVersion, response.ProtocolVersion);
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentRequests_AreSerializedAndMatched()
    {
        string pipeName = CreatePipeName();
        int activeHandlers = 0;
        int maximumActiveHandlers = 0;
        var server = new NamedPipeManagementServer(
            pipeName,
            async (request, cancellationToken) =>
            {
                int active = Interlocked.Increment(ref activeHandlers);
                InterlockedExtensions.Max(ref maximumActiveHandlers, active);

                try
                {
                    await Task.Delay(5, cancellationToken);
                    return CreateSuccessResponse(request);
                }
                finally
                {
                    Interlocked.Decrement(ref activeHandlers);
                }
            });
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        ManagementRequest[] requests = Enumerable.Range(0, 20).Select(CreateRequest).ToArray();

        ManagementResponse[] responses = await Task.WhenAll(
            requests.Select(request => client.SendAsync(request, TestContext.Current.CancellationToken).AsTask()));

        Assert.Equal(1, maximumActiveHandlers);
        Assert.Equal(requests.Select(static request => request.RequestId), responses.Select(static response => response.RequestId));
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MismatchedRequestId_IsRejectedAndConnectionIsDiscarded()
    {
        string pipeName = CreatePipeName();
        var server = new NamedPipeManagementServer(
            pipeName,
            (request, _) => ValueTask.FromResult(CreateSuccessResponse(request) with { RequestId = Guid.NewGuid() }));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcError, exception.ErrorCode);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandlerException_ReturnsInternalErrorAndServerContinues()
    {
        string pipeName = CreatePipeName();
        var server = new NamedPipeManagementServer(
            pipeName,
            (request, _) => request.Command == "Fail"
                ? throw new InvalidOperationException("test failure")
                : ValueTask.FromResult(CreateSuccessResponse(request)));
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        ManagementResponse failure = await client.SendAsync(
            CreateRequest(1) with { Command = "Fail" },
            TestContext.Current.CancellationToken);
        ManagementResponse success = await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.False(failure.Success);
        Assert.Equal(ErrorCodes.InternalError, failure.ErrorCode);
        Assert.True(success.Success);
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Client_DefaultTimeoutIsFiveSeconds()
    {
        var client = new NamedPipeManagementClient(CreatePipeName());

        Assert.Equal(TimeSpan.FromSeconds(5), client.RequestTimeout);
    }

    [Fact]
    public async Task RequestTimeout_ThrowsAndDiscardsConnection()
    {
        string pipeName = CreatePipeName();
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new NamedPipeManagementServer(
            pipeName,
            async (request, cancellationToken) =>
            {
                await releaseHandler.Task.WaitAsync(cancellationToken);
                return CreateSuccessResponse(request);
            });
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName, TimeSpan.FromMilliseconds(50));
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await client.PingAsync(TestContext.Current.CancellationToken));

        Assert.False(client.IsConnected);
        releaseHandler.SetResult();
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndDiscardsConnection()
    {
        string pipeName = CreatePipeName();
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new NamedPipeManagementServer(
            pipeName,
            async (request, cancellationToken) =>
            {
                await releaseHandler.Task.WaitAsync(cancellationToken);
                return CreateSuccessResponse(request);
            });
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.PingAsync(cancellation.Token));

        Assert.False(client.IsConnected);
        releaseHandler.SetResult();
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClientDisconnect_CompletesServerReadLoop()
    {
        string pipeName = CreatePipeName();
        var server = new NamedPipeManagementServer(pipeName, SuccessResponse);
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        await client.DisposeAsync();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResponseSentCallback_ReceivesFlushedResponse()
    {
        string pipeName = CreatePipeName();
        var responseSent = new TaskCompletionSource<(Guid RequestId, bool Success)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new NamedPipeManagementServer(
            pipeName,
            SuccessResponse,
            (request, response, _) =>
            {
                responseSent.SetResult((request.RequestId, response.Success));
                return ValueTask.CompletedTask;
            });
        Task serverTask = server.RunSingleClientAsync(TestContext.Current.CancellationToken);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        ManagementResponse response = await client.PingAsync(TestContext.Current.CancellationToken);
        (Guid RequestId, bool Success) callback = await responseSent.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(response.RequestId, callback.RequestId);
        Assert.True(callback.Success);
        await client.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ServerCancellation_ReleasesClientWaitingForResponse()
    {
        string pipeName = CreatePipeName();
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new NamedPipeManagementServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The canceled handler unexpectedly resumed.");
            });
        Task serverTask = server.RunSingleClientAsync(serverCancellation.Token);
        var client = new NamedPipeManagementClient(pipeName);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        Task<ManagementResponse> responseTask = client.PingAsync(TestContext.Current.CancellationToken).AsTask();
        await handlerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        serverCancellation.Cancel();

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await responseTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serverTask);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ConnectCancellation_IsPropagatedWhenServerIsUnavailable()
    {
        var client = new NamedPipeManagementClient(CreatePipeName());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.ConnectAsync(cancellation.Token));

        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    private static string CreatePipeName() =>
        PipeNames.Management($"t-{Guid.NewGuid():N}"[..10]);

    private static ManagementRequest CreateRequest(int value) =>
        new(
            NamedPipeManagementClient.ProtocolVersion,
            Guid.NewGuid(),
            "Ping",
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { value }));

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

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            int current = Volatile.Read(ref target);

            while (current < value)
            {
                int observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
