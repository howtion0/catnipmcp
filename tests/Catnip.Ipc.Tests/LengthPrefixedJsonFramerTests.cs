using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Catnip.Ipc.Framing;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Tests;

public sealed class LengthPrefixedJsonFramerTests
{
    private readonly LengthPrefixedJsonFramer _framer = new();

    [Fact]
    public async Task Roundtrip_PreservesManagementRequest()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { moduleId = "today-todos" });
        var request = new ManagementRequest(1, Guid.NewGuid(), "GetRuntimeSnapshot", DateTimeOffset.UtcNow, payload);
        await using var stream = new MemoryStream();

        await _framer.WriteAsync(stream, request, TestContext.Current.CancellationToken);
        stream.Position = 0;
        ManagementRequest actual = await _framer.ReadAsync<ManagementRequest>(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(request.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(request.RequestId, actual.RequestId);
        Assert.Equal("today-todos", actual.Payload.GetProperty("moduleId").GetString());
    }

    [Fact]
    public async Task Write_UsesLittleEndianLengthAndCamelCaseJson()
    {
        await using var stream = new MemoryStream();

        await _framer.WriteAsync(stream, new SampleMessage(7), TestContext.Current.CancellationToken);
        byte[] frame = stream.ToArray();
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, sizeof(uint)));
        string json = Encoding.UTF8.GetString(frame, sizeof(uint), (int)payloadLength);

        Assert.Equal((uint)(frame.Length - sizeof(uint)), payloadLength);
        Assert.Equal("{\"sampleValue\":7}", json);
    }

    [Fact]
    public async Task Read_HandlesOneByteSegments()
    {
        byte[] frame = await WriteFrameAsync(new SampleMessage(8));
        await using var stream = new ChunkedReadStream(frame, 1);

        SampleMessage actual = await _framer.ReadAsync<SampleMessage>(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(8, actual.SampleValue);
    }

    [Fact]
    public async Task Read_ConsumesConsecutiveFramesIndependently()
    {
        await using var stream = new MemoryStream();
        await _framer.WriteAsync(stream, new SampleMessage(1), TestContext.Current.CancellationToken);
        await _framer.WriteAsync(stream, new SampleMessage(2), TestContext.Current.CancellationToken);
        stream.Position = 0;

        SampleMessage first = await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken);
        SampleMessage second = await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.SampleValue);
        Assert.Equal(2, second.SampleValue);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task Write_RejectsPayloadOverOneMiBBeforeWriting()
    {
        await using var stream = new MemoryStream();

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.WriteAsync(
                stream,
                new string('x', LengthPrefixedJsonFramer.MaxFrameBytes),
                TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcFrameTooLarge, exception.ErrorCode);
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public async Task Read_RejectsDeclaredPayloadOverOneMiBWithoutAllocatingBody()
    {
        byte[] header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            header,
            LengthPrefixedJsonFramer.MaxFrameBytes + 1U);
        await using var stream = new MemoryStream(header);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcFrameTooLarge, exception.ErrorCode);
        Assert.Equal(sizeof(uint), stream.Position);
    }

    [Fact]
    public async Task Read_ReportsTruncatedLengthPrefixAsControlledError()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcError, exception.ErrorCode);
        Assert.Contains("length prefix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_ReportsTruncatedPayloadAsControlledError()
    {
        byte[] frame = CreateRawFrame(Encoding.UTF8.GetBytes("{}"), declaredLength: 10);
        await using var stream = new MemoryStream(frame);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcError, exception.ErrorCode);
        Assert.Contains("JSON payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_ReportsInvalidJsonAsControlledError()
    {
        byte[] frame = CreateRawFrame("not-json"u8.ToArray());
        await using var stream = new MemoryStream(frame);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcError, exception.ErrorCode);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Read_ReportsJsonNullAsControlledError()
    {
        byte[] frame = CreateRawFrame("null"u8.ToArray());
        await using var stream = new MemoryStream(frame);

        IpcFrameException exception = await Assert.ThrowsAsync<IpcFrameException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.IpcError, exception.ErrorCode);
    }

    [Fact]
    public async Task Read_PropagatesCancellation()
    {
        await using var stream = new CancellationOnlyStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _framer.ReadAsync<SampleMessage>(stream, cancellation.Token));
    }

    private async Task<byte[]> WriteFrameAsync<T>(T message)
    {
        await using var stream = new MemoryStream();
        await _framer.WriteAsync(stream, message, TestContext.Current.CancellationToken);
        return stream.ToArray();
    }

    private static byte[] CreateRawFrame(byte[] payload, uint? declaredLength = null)
    {
        byte[] frame = new byte[sizeof(uint) + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, declaredLength ?? (uint)payload.Length);
        payload.CopyTo(frame, sizeof(uint));
        return frame;
    }

    private sealed record SampleMessage(int SampleValue);

    private sealed class ChunkedReadStream(byte[] bytes, int maxChunkBytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunkBytes)], cancellationToken);
    }

    private sealed class CancellationOnlyStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
