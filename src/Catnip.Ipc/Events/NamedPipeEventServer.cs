using System.IO.Pipes;
using System.Threading.Channels;
using Catnip.Ipc.Framing;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Events;

public sealed class NamedPipeEventServer(
    string pipeName,
    ChannelReader<RuntimeEvent> eventReader,
    Func<RuntimeEvent> snapshotFactory)
{
    private readonly LengthPrefixedJsonFramer _framer = new();

    public async Task RunSingleClientAsync(CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (!await TryWriteAsync(pipe, snapshotFactory(), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await foreach (RuntimeEvent runtimeEvent in eventReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await TryWriteAsync(pipe, runtimeEvent, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async ValueTask<bool> TryWriteAsync(
        Stream pipe,
        RuntimeEvent runtimeEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _framer.WriteAsync(pipe, runtimeEvent, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
