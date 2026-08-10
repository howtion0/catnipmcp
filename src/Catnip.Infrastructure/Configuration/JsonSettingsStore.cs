using System.Text.Json;
using Catnip.Core.Configuration;
using Catnip.Infrastructure.Paths;
using Catnip.Shared.Configuration;
using Catnip.Shared.Serialization;

namespace Catnip.Infrastructure.Configuration;

public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private static readonly HashSet<string> AllowedDocumentNames = new(StringComparer.Ordinal)
    {
        "settings",
        "connectors",
        "routes",
        "field-mappings",
    };

    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AppDataPathProvider _paths;
    private readonly SemaphoreSlim _storeLock = new(1, 1);
    private readonly GatewaySettingsValidator _validator;

    public JsonSettingsStore(AppDataPathProvider paths)
        : this(paths, new GatewaySettingsValidator(), new AtomicFileWriter())
    {
    }

    internal JsonSettingsStore(
        AppDataPathProvider paths,
        GatewaySettingsValidator validator,
        IAtomicFileWriter atomicFileWriter)
    {
        _paths = paths;
        _validator = validator;
        _atomicFileWriter = atomicFileWriter;
        _jsonOptions = SharedJsonSerializerOptions.Create();
        _jsonOptions.WriteIndented = true;
    }

    public async ValueTask<JsonElement?> LoadAsync(
        string documentName,
        CancellationToken cancellationToken)
    {
        string documentPath = GetDocumentPath(documentName);
        await _storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(documentPath))
            {
                return null;
            }

            return await LoadAndValidateAsync(documentName, documentPath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _storeLock.Release();
        }
    }

    public async ValueTask SaveAsync(
        string documentName,
        JsonElement document,
        CancellationToken cancellationToken)
    {
        string documentPath = GetDocumentPath(documentName);
        JsonElement stableDocument = document.Clone();
        ValidateDocument(documentName, stableDocument);

        await _storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(documentPath))
            {
                _ = await LoadAndValidateAsync(documentName, documentPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            _paths.EnsureDirectories();
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(stableDocument, _jsonOptions);
            await _atomicFileWriter.WriteAsync(documentPath, content, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _storeLock.Release();
        }
    }

    public void Dispose()
    {
        _storeLock.Dispose();
    }

    private async ValueTask<JsonElement> LoadAndValidateAsync(
        string documentName,
        string documentPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            documentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        JsonElement document = await JsonSerializer.DeserializeAsync<JsonElement>(
            stream,
            _jsonOptions,
            cancellationToken).ConfigureAwait(false);
        ValidateDocument(documentName, document);
        return document.Clone();
    }

    private void ValidateDocument(string documentName, JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Configuration document must be a JSON object.");
        }

        if (!document.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
            || !schemaVersion.TryGetInt32(out int version)
            || version != 1)
        {
            throw new InvalidDataException("Configuration schemaVersion must be 1.");
        }

        if (!string.Equals(documentName, "settings", StringComparison.Ordinal))
        {
            return;
        }

        GatewaySettingsDto settings;
        try
        {
            settings = document.Deserialize<GatewaySettingsDto>(_jsonOptions)
                ?? throw new InvalidDataException("Settings document cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Settings document has an invalid JSON shape.", exception);
        }

        ConfigurationValidationResult result = _validator.Validate(settings);
        if (!result.IsValid)
        {
            string paths = string.Join(", ", result.Issues.Select(static issue => issue.Path));
            throw new InvalidDataException($"Settings document is invalid at: {paths}.");
        }
    }

    private string GetDocumentPath(string documentName)
    {
        if (string.IsNullOrWhiteSpace(documentName)
            || !AllowedDocumentNames.Contains(documentName))
        {
            throw new ArgumentException("Configuration document name is not supported.", nameof(documentName));
        }

        return Path.Combine(_paths.RootPath, documentName + ".json");
    }
}
