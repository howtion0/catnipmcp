using System.Collections.Concurrent;
using Catnip.Core.Security;

namespace Catnip.Runtime.Security;

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public InMemorySecretStore(string secretId, string? secretValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        if (!string.IsNullOrEmpty(secretValue))
        {
            _secrets[secretId] = secretValue;
        }
    }

    public ValueTask<string?> GetAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.TryGetValue(secretId, out string? secretValue);
        return ValueTask.FromResult(secretValue);
    }

    public ValueTask SaveAsync(
        string secretId,
        string secretValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentException.ThrowIfNullOrEmpty(secretValue);
        _secrets[secretId] = secretValue;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.TryRemove(secretId, out _);
        return ValueTask.CompletedTask;
    }
}
