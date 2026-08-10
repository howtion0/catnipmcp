namespace Catnip.Infrastructure.Paths;

public sealed class AppDataPathProvider
{
    public AppDataPathProvider(string? rootPath = null)
    {
        string resolvedRoot;
        if (rootPath is null)
        {
            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException(
                    "The operating system did not provide a user-local application data directory.");
            }

            resolvedRoot = Path.Combine(localApplicationData, "Catnip");
        }
        else
        {
            resolvedRoot = rootPath;
        }

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            throw new ArgumentException("The application data root path is required.", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(resolvedRoot);
        SecretsPath = Path.Combine(RootPath, "secrets");
        DataPath = Path.Combine(RootPath, "data");
        LogsPath = Path.Combine(RootPath, "logs");
        StatePath = Path.Combine(RootPath, "state");
    }

    public string RootPath { get; }

    public string SecretsPath { get; }

    public string DataPath { get; }

    public string LogsPath { get; }

    public string StatePath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(SecretsPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(StatePath);
    }
}
