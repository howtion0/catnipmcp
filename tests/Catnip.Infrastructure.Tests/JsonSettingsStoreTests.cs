using System.Text.Json;
using Catnip.Infrastructure.Configuration;
using Catnip.Infrastructure.Paths;
using Catnip.Shared.Configuration;
using Catnip.Shared.Serialization;

namespace Catnip.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task MissingDocument_ReturnsNullWithoutCreatingDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = Path.Combine(temporaryDirectory.Path, "user-data");
        using var store = new JsonSettingsStore(new AppDataPathProvider(root));

        JsonElement? result = await store.LoadAsync(
            "settings",
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsUsingSharedJsonShape()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppDataPathProvider(temporaryDirectory.Path);
        using var store = new JsonSettingsStore(paths);

        await store.SaveAsync(
            "settings",
            SettingsTestData.ToElement(SettingsTestData.Create()),
            TestContext.Current.CancellationToken);
        JsonElement? loaded = await store.LoadAsync(
            "settings",
            TestContext.Current.CancellationToken);
        string persisted = await File.ReadAllTextAsync(
            Path.Combine(paths.RootPath, "settings.json"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("custom", loaded.Value.GetProperty("gateway").GetProperty("mode").GetString());
        Assert.Contains("\"schemaVersion\": 1", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("SchemaVersion", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondSave_AtomicallyCreatesSingleBackupOfPreviousDocument()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppDataPathProvider(temporaryDirectory.Path);
        using var store = new JsonSettingsStore(paths);
        GatewaySettingsDto original = SettingsTestData.Create();
        GatewaySettingsDto replacement = original with
        {
            Gateway = original.Gateway with { Port = 5211 },
        };

        await store.SaveAsync(
            "settings",
            SettingsTestData.ToElement(original),
            TestContext.Current.CancellationToken);
        await store.SaveAsync(
            "settings",
            SettingsTestData.ToElement(replacement),
            TestContext.Current.CancellationToken);

        JsonElement current = await ReadDocumentAsync(Path.Combine(paths.RootPath, "settings.json"));
        JsonElement backup = await ReadDocumentAsync(Path.Combine(paths.RootPath, "settings.json.bak"));
        Assert.Equal(5211, current.GetProperty("gateway").GetProperty("port").GetInt32());
        Assert.Equal(5210, backup.GetProperty("gateway").GetProperty("port").GetInt32());
        Assert.Empty(Directory.EnumerateFiles(paths.RootPath, "*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("settings.json")]
    [InlineData("../settings")]
    [InlineData("secrets/vault")]
    public async Task UnsupportedDocumentName_IsRejectedBeforeFileAccess(string documentName)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(new AppDataPathProvider(temporaryDirectory.Path));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.SaveAsync(
                documentName,
                SettingsTestData.ToElement(SettingsTestData.Create()),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnsupportedSchema_IsRejectedWithoutCreatingAFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(new AppDataPathProvider(temporaryDirectory.Path));
        GatewaySettingsDto invalid = SettingsTestData.Create() with { SchemaVersion = 2 };

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await store.SaveAsync(
                "settings",
                SettingsTestData.ToElement(invalid),
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(temporaryDirectory.Path, "settings.json")));
    }

    [Fact]
    public async Task CorruptExistingDocument_IsNeverOverwritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string settingsPath = Path.Combine(temporaryDirectory.Path, "settings.json");
        const string corruptContent = "{not-json";
        await File.WriteAllTextAsync(
            settingsPath,
            corruptContent,
            TestContext.Current.CancellationToken);
        using var store = new JsonSettingsStore(new AppDataPathProvider(temporaryDirectory.Path));

        await Assert.ThrowsAsync<JsonException>(
            async () => await store.SaveAsync(
                "settings",
                SettingsTestData.ToElement(SettingsTestData.Create()),
                TestContext.Current.CancellationToken));

        Assert.Equal(corruptContent, await File.ReadAllTextAsync(
            settingsPath,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(settingsPath + ".bak"));
    }

    [Fact]
    public async Task CommitFailure_LeavesOldDocumentAndCleansTemporaryFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppDataPathProvider(temporaryDirectory.Path);
        GatewaySettingsDto original = SettingsTestData.Create();
        using (var initialStore = new JsonSettingsStore(paths))
        {
            await initialStore.SaveAsync(
                "settings",
                SettingsTestData.ToElement(original),
                TestContext.Current.CancellationToken);
        }

        var failingWriter = new AtomicFileWriter(
            static () => throw new IOException("Simulated commit failure."));
        using var failingStore = new JsonSettingsStore(
            paths,
            new GatewaySettingsValidator(),
            failingWriter);
        GatewaySettingsDto replacement = original with
        {
            Gateway = original.Gateway with { Port = 5211 },
        };

        await Assert.ThrowsAsync<IOException>(
            async () => await failingStore.SaveAsync(
                "settings",
                SettingsTestData.ToElement(replacement),
                TestContext.Current.CancellationToken));

        JsonElement current = await ReadDocumentAsync(Path.Combine(paths.RootPath, "settings.json"));
        Assert.Equal(5210, current.GetProperty("gateway").GetProperty("port").GetInt32());
        Assert.Empty(Directory.EnumerateFiles(paths.RootPath, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentSaves_AreSerializedAndLeaveOneValidDocument()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppDataPathProvider(temporaryDirectory.Path);
        using var store = new JsonSettingsStore(paths);

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(index => store.SaveAsync(
                    "settings",
                    SettingsTestData.ToElement(SettingsTestData.Create() with
                    {
                        Gateway = SettingsTestData.Create().Gateway with { Port = 5200 + index },
                    }),
                    TestContext.Current.CancellationToken).AsTask()));

        JsonElement? current = await store.LoadAsync(
            "settings",
            TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        int port = current.Value.GetProperty("gateway").GetProperty("port").GetInt32();
        Assert.InRange(port, 5200, 5219);
        Assert.True(File.Exists(Path.Combine(paths.RootPath, "settings.json.bak")));
        Assert.Empty(Directory.EnumerateFiles(paths.RootPath, "*.tmp"));
    }

    private static async Task<JsonElement> ReadDocumentAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<JsonElement>(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
