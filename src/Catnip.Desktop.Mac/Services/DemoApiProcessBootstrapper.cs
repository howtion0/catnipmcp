using System.Diagnostics;
using System.IO;

namespace Catnip.Desktop.Mac.Services;

public sealed class DemoApiProcessBootstrapper : IDemoApiProcessBootstrapper, IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private readonly IDemoApiClient _apiClient;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _ownedProcess;
    private bool _disposed;

    public DemoApiProcessBootstrapper(IDemoApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<DemoApiBootstrapResult> EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (await _apiClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DemoApiBootstrapResult(false, null, "已连接现有 TestApi 进程");
        }

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _apiClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DemoApiBootstrapResult(false, null, "已连接现有 TestApi 进程");
            }

            DemoApiLaunchConfiguration launch = ResolveLaunchConfiguration();
            if (!File.Exists(launch.DemoApiLaunchPath))
            {
                throw new FileNotFoundException("TestApi 尚未完成 Release 构建。", launch.DemoApiLaunchPath);
            }

            bool isManagedAssembly = string.Equals(
                Path.GetExtension(launch.DemoApiLaunchPath),
                ".dll",
                StringComparison.OrdinalIgnoreCase);
            var startInfo = new ProcessStartInfo(
                isManagedAssembly ? "dotnet" : launch.DemoApiLaunchPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(launch.DemoApiLaunchPath),
            };
            if (isManagedAssembly)
            {
                startInfo.ArgumentList.Add(launch.DemoApiLaunchPath);
            }

            if (launch.RepositoryRoot is not null)
            {
                startInfo.Environment["CATNIP_REPOSITORY_ROOT"] = launch.RepositoryRoot;
            }

            startInfo.Environment["CATNIP_RUNTIME_LAUNCH_PATH"] = launch.RuntimeLaunchPath;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动独立 TestApi 进程。");
            }

            _ownedProcess = process;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartupTimeout);
            while (!await _apiClient.IsHealthyAsync(timeout.Token).ConfigureAwait(false))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"TestApi 启动失败，退出码 {process.ExitCode}。");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token).ConfigureAwait(false);
            }

            return new DemoApiBootstrapResult(true, process.Id, "独立 TestApi 进程已启动");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ownedProcess?.Dispose();
        _startLock.Dispose();
        _disposed = true;
    }

    internal static string ResolveRepositoryRoot()
    {
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

        throw new DirectoryNotFoundException("无法定位 Catnip.sln，不能安全启动固定 TestApi 程序。");
    }

    private static DemoApiLaunchConfiguration ResolveLaunchConfiguration()
    {
        bool windows = OperatingSystem.IsWindows();
        string packagedDemoApi = DesktopPackageLayout.GetDemoApiPath(AppContext.BaseDirectory, windows);
        string packagedRuntime = DesktopPackageLayout.GetRuntimePath(AppContext.BaseDirectory, windows);
        if (File.Exists(packagedDemoApi) && File.Exists(packagedRuntime))
        {
            return new DemoApiLaunchConfiguration(packagedDemoApi, packagedRuntime, null);
        }

        string repositoryRoot = ResolveRepositoryRoot();
        return new DemoApiLaunchConfiguration(
            Path.Combine(
                repositoryRoot,
                "src",
                "Catnip.DemoApi",
                "bin",
                "Release",
                "net10.0",
                "Catnip.DemoApi.dll"),
            Path.Combine(
                repositoryRoot,
                "src",
                "Catnip.Runtime",
                "bin",
                "Release",
                "net10.0",
                "Catnip.Runtime.dll"),
            repositoryRoot);
    }

    private sealed record DemoApiLaunchConfiguration(
        string DemoApiLaunchPath,
        string RuntimeLaunchPath,
        string? RepositoryRoot);
}
