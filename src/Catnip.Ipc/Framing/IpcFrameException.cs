namespace Catnip.Ipc.Framing;

public sealed class IpcFrameException(
    string errorCode,
    string message,
    Exception? innerException = null) : IOException(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
