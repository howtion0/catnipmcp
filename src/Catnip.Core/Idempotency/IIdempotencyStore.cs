namespace Catnip.Core.Idempotency;

public sealed record IdempotencyRecord(
    string IdempotencyKey,
    string ToolName,
    string RequestHash,
    string ResultJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface IIdempotencyStore
{
    ValueTask<IdempotencyRecord?> GetAsync(string idempotencyKey, CancellationToken cancellationToken);

    ValueTask SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken);

    ValueTask<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
