namespace Catnip.Core.Security;

public interface ISecretStore
{
    ValueTask<string?> GetAsync(string secretId, CancellationToken cancellationToken);

    ValueTask SaveAsync(string secretId, string secretValue, CancellationToken cancellationToken);

    ValueTask DeleteAsync(string secretId, CancellationToken cancellationToken);
}
