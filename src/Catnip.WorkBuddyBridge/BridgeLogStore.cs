using System.Text.Json;

namespace Catnip.WorkBuddyBridge;

public sealed class BridgeLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _logDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BridgeLogStore()
        : this(null)
    {
    }

    public BridgeLogStore(string? suppliedDataRoot)
    {
        string dataRoot = suppliedDataRoot
            ?? Environment.GetEnvironmentVariable("CATNIP_DEMO_DATA_ROOT")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Catnip",
                "mac-demo");
        _logDirectory = Path.Combine(dataRoot, "logs");
    }

    public string CurrentLogPath => Path.Combine(
        _logDirectory,
        $"workbuddy-bridge-{DateTimeOffset.Now:yyyyMMdd}.jsonl");

    public async Task WriteAsync(
        string tool,
        string traceId,
        bool success,
        string? errorCode,
        long elapsedMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var entry = new BridgeLogEntry(
            DateTimeOffset.UtcNow,
            tool,
            traceId,
            success,
            errorCode,
            elapsedMilliseconds);
        string line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_logDirectory);
            await File.AppendAllTextAsync(CurrentLogPath, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record BridgeLogEntry(
        DateTimeOffset Timestamp,
        string Tool,
        string TraceId,
        bool Success,
        string? ErrorCode,
        long ElapsedMilliseconds);
}
