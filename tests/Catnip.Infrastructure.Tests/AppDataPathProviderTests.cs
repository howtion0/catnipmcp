using Catnip.Infrastructure.Paths;

namespace Catnip.Infrastructure.Tests;

public sealed class AppDataPathProviderTests
{
    [Fact]
    public void DefaultRoot_IsUnderUserLocalApplicationData()
    {
        var paths = new AppDataPathProvider();
        string localApplicationData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        Assert.StartsWith(localApplicationData, paths.RootPath, StringComparison.Ordinal);
        Assert.NotEqual(Path.GetFullPath(AppContext.BaseDirectory), paths.RootPath);
    }

    [Fact]
    public void OverrideRoot_NormalizesAndCreatesFrozenDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppDataPathProvider(Path.Combine(temporaryDirectory.Path, ".", "user-data"));

        paths.EnsureDirectories();

        Assert.Equal(Path.Combine(temporaryDirectory.Path, "user-data"), paths.RootPath);
        Assert.True(Directory.Exists(paths.RootPath));
        Assert.True(Directory.Exists(paths.SecretsPath));
        Assert.True(Directory.Exists(paths.DataPath));
        Assert.True(Directory.Exists(paths.LogsPath));
        Assert.True(Directory.Exists(paths.StatePath));
    }
}
