namespace Catnip.DemoApi.Runtime;

public sealed record DemoApiOptions(
    string ListenAddress,
    string RuntimeAddress,
    string RuntimeLaunchPath,
    string DataRoot)
{
    public const string RepositoryRootEnvironmentVariable = "CATNIP_REPOSITORY_ROOT";
    public const string RuntimeLaunchPathEnvironmentVariable = "CATNIP_RUNTIME_LAUNCH_PATH";
    public const string DemoDataRootEnvironmentVariable = "CATNIP_DEMO_DATA_ROOT";

    public string McpAddress => $"{RuntimeAddress.TrimEnd('/')}/mcp";

    public string RuntimeLogDirectory => Path.Combine(DataRoot, "logs");

    public static DemoApiOptions CreateDefault()
    {
        string? configuredRuntime = Environment.GetEnvironmentVariable(RuntimeLaunchPathEnvironmentVariable);
        string runtimeLaunchPath = configuredRuntime is not null
            ? configuredRuntime
            : Path.Combine(
                ResolveRepositoryRoot(),
                "src",
                "Catnip.Runtime",
                "bin",
                "Release",
                "net10.0",
                "Catnip.Runtime.dll");
        string dataRoot = Environment.GetEnvironmentVariable(DemoDataRootEnvironmentVariable)
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Catnip",
                "mac-demo");

        return new DemoApiOptions(
            "http://127.0.0.1:5220",
            "http://127.0.0.1:5210",
            Path.GetFullPath(runtimeLaunchPath),
            Path.GetFullPath(dataRoot));
    }

    public void Validate()
    {
        ValidateLoopbackAddress(ListenAddress, nameof(ListenAddress));
        ValidateLoopbackAddress(RuntimeAddress, nameof(RuntimeAddress));

        if (!Path.IsPathFullyQualified(RuntimeLaunchPath))
        {
            throw new ArgumentException("Runtime launch path must be absolute.", nameof(RuntimeLaunchPath));
        }

        string fileName = Path.GetFileName(RuntimeLaunchPath);
        if (!string.Equals(fileName, "Catnip.Runtime", StringComparison.Ordinal)
            && !string.Equals(fileName, "Catnip.Runtime.dll", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "Catnip.Runtime.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Runtime launch path must name the fixed Catnip.Runtime apphost or assembly.",
                nameof(RuntimeLaunchPath));
        }

        if (!Path.IsPathFullyQualified(DataRoot))
        {
            throw new ArgumentException("Demo data root must be absolute.", nameof(DataRoot));
        }
    }

    private static void ValidateLoopbackAddress(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Address must be an HTTP loopback origin without a path.",
                parameterName);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        string? configured = Environment.GetEnvironmentVariable(RepositoryRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Catnip.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate Catnip.sln. Set {RepositoryRootEnvironmentVariable} explicitly.");
    }
}
