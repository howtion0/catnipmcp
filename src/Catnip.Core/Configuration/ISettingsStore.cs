using System.Text.Json;

namespace Catnip.Core.Configuration;

public interface ISettingsStore
{
    ValueTask<JsonElement?> LoadAsync(string documentName, CancellationToken cancellationToken);

    ValueTask SaveAsync(string documentName, JsonElement document, CancellationToken cancellationToken);
}
