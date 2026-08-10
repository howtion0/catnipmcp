using System.IO.Pipes;
using Catnip.Ipc.Framing;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Events;

public sealed class NamedPipeEventClient(string pipeName) : IAsyncDisposable
{
    private readonly LengthPrefixedJsonFramer _framer = new();
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public bool IsConnected => _pipe?.IsConnected == true;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);

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
                pipeName,
                PipeDirection.In,
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
            _readLock.Release();
        }
    }

    public async ValueTask<RuntimeEvent> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            NamedPipeClientStream pipe = _pipe is { IsConnected: true } connectedPipe
                ? connectedPipe
                : throw new IpcFrameException(ErrorCodes.IpcError, "Event pipe is not connected.");

            try
            {
                return await _framer.ReadAsync<RuntimeEvent>(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await ResetPipeAsync().ConfigureAwait(false);
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
            _readLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _readLock.WaitAsync().ConfigureAwait(false);

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
            _readLock.Release();
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
}
