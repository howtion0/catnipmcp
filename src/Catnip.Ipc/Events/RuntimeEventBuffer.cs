using System.Threading.Channels;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Events;

public sealed class RuntimeEventBuffer
{
    public const int Capacity = 1000;

    private readonly Channel<RuntimeEvent> _channel = Channel.CreateBounded<RuntimeEvent>(
        new BoundedChannelOptions(Capacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<RuntimeEvent> Reader => _channel.Reader;

    public bool TryPublish(RuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        return _channel.Writer.TryWrite(runtimeEvent);
    }

    public bool TryComplete(Exception? error = null) =>
        _channel.Writer.TryComplete(error);
}
