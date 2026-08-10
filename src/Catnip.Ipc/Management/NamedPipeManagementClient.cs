using System.IO.Pipes;
using System.Text.Json;
using Catnip.Ipc.Framing;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Ipc.Management;

public sealed class NamedPipeManagementClient : IAsyncDisposable
{
    public const int ProtocolVersion = 1;
    public const string PingCommand = "Ping";
    public const string GetRuntimeSnapshotCommand = "GetRuntimeSnapshot";
    public const string SetMasterEnabledCommandName = "SetMasterEnabled";
    public const string SetGatewayModeCommandName = "SetGatewayMode";
    public const string SetModuleEnabledCommandName = "SetModuleEnabled";
    public const string ShutdownRuntimeCommandName = "ShutdownRuntime";

    private readonly string _pipeName;
    private readonly LengthPrefixedJsonFramer _framer = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public NamedPipeManagementClient(string pipeName, TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be positive.");
        }

        _pipeName = pipeName;
    }

    public TimeSpan RequestTimeout { get; }

    public bool IsConnected => _pipe?.IsConnected == true;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsConnected)
            {
                return;
            }

            await ResetPipeAsync().ConfigureAwait(false);
            var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                _pipe = pipe;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async ValueTask<ManagementResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateRequest(PingCommand, new EmptyCommand()),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> GetRuntimeSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        return await SendWithSafeReconnectAsync(
            CreateRequest(GetRuntimeSnapshotCommand, new EmptyCommand()),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> ShutdownRuntimeAsync(
        bool graceful,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateRequest(ShutdownRuntimeCommandName, new ShutdownRuntimeCommand(graceful)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> SetMasterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateRequest(SetMasterEnabledCommandName, new SetMasterEnabledCommand(enabled)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> SetGatewayModeAsync(
        GatewayMode mode,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateRequest(SetGatewayModeCommandName, new SetGatewayModeCommand(mode)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateRequest(SetModuleEnabledCommandName, new SetModuleEnabledCommand(moduleId, enabled)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagementResponse> SendAsync(
        ManagementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            NamedPipeClientStream pipe = _pipe is { IsConnected: true } connectedPipe
                ? connectedPipe
                : throw new IpcFrameException(ErrorCodes.IpcError, "Management pipe is not connected.");

            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation.CancelAfter(RequestTimeout);

            try
            {
                await _framer.WriteAsync(pipe, request, requestCancellation.Token).ConfigureAwait(false);
                ManagementResponse response = await _framer.ReadAsync<ManagementResponse>(
                    pipe,
                    requestCancellation.Token).ConfigureAwait(false);

                if (response.RequestId != request.RequestId)
                {
                    throw new IpcFrameException(
                        ErrorCodes.IpcError,
                        "Management response requestId did not match the request.");
                }

                return response;
            }
            catch (OperationCanceledException exception)
            {
                await ResetPipeAsync().ConfigureAwait(false);

                if (!cancellationToken.IsCancellationRequested && requestCancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Management request exceeded the {RequestTimeout.TotalMilliseconds:0}-ms timeout.",
                        exception);
                }

                throw;
            }
            catch (IOException)
            {
                await ResetPipeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async ValueTask<ManagementResponse> SendWithSafeReconnectAsync(
        ManagementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (IsIdempotentReadCommand(request.Command))
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _requestLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await ResetPipeAsync().ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask ResetPipeAsync()
    {
        if (_pipe is null)
        {
            return;
        }

        await _pipe.DisposeAsync().ConfigureAwait(false);
        _pipe = null;
    }

    private static bool IsIdempotentReadCommand(string command) =>
        string.Equals(command, PingCommand, StringComparison.Ordinal)
        || string.Equals(command, GetRuntimeSnapshotCommand, StringComparison.Ordinal);

    private static ManagementRequest CreateRequest<TPayload>(string command, TPayload payload) =>
        new(
            ProtocolVersion,
            Guid.NewGuid(),
            command,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(payload, SharedJsonSerializerOptions.Create()));
}
