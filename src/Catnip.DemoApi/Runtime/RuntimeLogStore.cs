using System.Text.Json;
using Catnip.DemoApi.Models;
using Catnip.Shared.Serialization;

namespace Catnip.DemoApi.Runtime;

public sealed class RuntimeLogStore
{
    public const int MaximumTailLines = 500;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = SharedJsonSerializerOptions.Create();
    private readonly string _logDirectory;
    private readonly TimeProvider _timeProvider;

    public RuntimeLogStore(DemoApiOptions options, TimeProvider timeProvider)
    {
        _logDirectory = options.RuntimeLogDirectory;
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_logDirectory);
    }

    public string CurrentFilePath => Path.Combine(
        _logDirectory,
        $"runtime-demo-{_timeProvider.GetLocalNow():yyyyMMdd}.jsonl");

    public string CurrentFileName => Path.GetFileName(CurrentFilePath);

    public async ValueTask AppendAsync(
        string stream,
        string message,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentNullException.ThrowIfNull(message);

        string sanitized = string.IsNullOrEmpty(secret)
            ? message
            : message.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        var line = new RuntimeLogLine(_timeProvider.GetUtcNow(), stream, sanitized);
        string json = JsonSerializer.Serialize(line, _jsonOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(
                CurrentFilePath,
                json + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<RuntimeLogResponse> TailAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > MaximumTailLines)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                $"Log tail must be between 1 and {MaximumTailLines} lines.");
        }

        string path = CurrentFilePath;
        if (!File.Exists(path))
        {
            return new RuntimeLogResponse(CurrentFileName, 0, []);
        }

        var tail = new Queue<RuntimeLogLine>(take);
        await foreach (string line in File.ReadLinesAsync(path, cancellationToken))
        {
            RuntimeLogLine? parsed = JsonSerializer.Deserialize<RuntimeLogLine>(line, _jsonOptions);
            if (parsed is null)
            {
                continue;
            }

            if (tail.Count == take)
            {
                tail.Dequeue();
            }

            tail.Enqueue(parsed);
        }

        RuntimeLogLine[] lines = [.. tail];
        return new RuntimeLogResponse(CurrentFileName, lines.Length, lines);
    }
}
