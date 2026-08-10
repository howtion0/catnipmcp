namespace Catnip.Shared.Business;

public sealed record OperationResult<T>(
    bool Success,
    string? ErrorCode,
    string? Message,
    T? Data,
    string TraceId,
    IReadOnlyList<OperationWarning> Warnings)
{
    public static OperationResult<T> Ok(
        T data,
        string traceId,
        IReadOnlyList<OperationWarning>? warnings = null) =>
        new(
            true,
            null,
            null,
            data,
            traceId,
            warnings ?? []);

    public static OperationResult<T> Fail(
        string errorCode,
        string message,
        string traceId,
        IReadOnlyList<OperationWarning>? warnings = null) =>
        new(
            false,
            errorCode,
            message,
            default,
            traceId,
            warnings ?? []);
}

public sealed record OperationWarning(
    string Source,
    string ErrorCode,
    string Message);
