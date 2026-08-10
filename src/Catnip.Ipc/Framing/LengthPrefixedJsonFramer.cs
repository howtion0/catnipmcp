using System.Buffers.Binary;
using System.Text.Json;
using Catnip.Shared.Errors;
using Catnip.Shared.Serialization;

namespace Catnip.Ipc.Framing;

public sealed class LengthPrefixedJsonFramer
{
    public const int MaxFrameBytes = 1024 * 1024;

    private const int HeaderBytes = sizeof(uint);
    private readonly JsonSerializerOptions _jsonOptions = SharedJsonSerializerOptions.Create();

    public async ValueTask WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
        EnsureAllowedLength((uint)payload.Length);

        byte[] header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[HeaderBytes];
        await ReadExactlyAsync(stream, header, "length prefix", cancellationToken).ConfigureAwait(false);

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        EnsureAllowedLength(payloadLength);

        byte[] payload = GC.AllocateUninitializedArray<byte>((int)payloadLength);
        await ReadExactlyAsync(stream, payload, "JSON payload", cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<T>(payload, _jsonOptions)
                ?? throw new IpcFrameException(ErrorCodes.IpcError, "IPC frame contained a null JSON value.");
        }
        catch (JsonException exception)
        {
            throw new IpcFrameException(ErrorCodes.IpcError, "IPC frame contained invalid JSON.", exception);
        }
    }

    private static void EnsureAllowedLength(uint payloadLength)
    {
        if (payloadLength > MaxFrameBytes)
        {
            throw new IpcFrameException(
                ErrorCodes.IpcFrameTooLarge,
                $"IPC frame length {payloadLength} exceeds the {MaxFrameBytes}-byte limit.");
        }
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        string segmentName,
        CancellationToken cancellationToken)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new IpcFrameException(
                    ErrorCodes.IpcError,
                    $"IPC stream ended before the {segmentName} was complete.");
            }

            offset += bytesRead;
        }
    }
}
