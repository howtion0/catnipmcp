using Catnip.Desktop.Mac.Models;
using Catnip.Desktop.Mac.Services;
using Catnip.Desktop.Mac.ViewModels;
using Catnip.Shared.Management;

namespace Catnip.Desktop.Mac.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void PackageLayout_ResolvesWindowsExecutablesBesideDesktop()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "catnip", "windows");

        Assert.Equal(
            Path.Combine(baseDirectory, "DemoApi", "Catnip.DemoApi.exe"),
            DesktopPackageLayout.GetDemoApiPath(baseDirectory, windows: true));
        Assert.Equal(
            Path.Combine(baseDirectory, "Runtime", "Catnip.Runtime.exe"),
            DesktopPackageLayout.GetRuntimePath(baseDirectory, windows: true));
        Assert.Equal(
            Path.Combine(baseDirectory, "WorkBuddyBridge", "Catnip.WorkBuddyBridge.exe"),
            DesktopPackageLayout.GetWorkBuddyBridgePath(baseDirectory, windows: true));
    }

    [Fact]
    public void PackageLayout_PreservesMacBundleResourcesLayout()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "Catnip.app", "Contents", "MacOS");
        string resources = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Resources"));

        Assert.Equal(
            Path.Combine(resources, "DemoApi", "Catnip.DemoApi"),
            DesktopPackageLayout.GetDemoApiPath(baseDirectory, windows: false));
        Assert.Equal(
            Path.Combine(resources, "Runtime", "Catnip.Runtime"),
            DesktopPackageLayout.GetRuntimePath(baseDirectory, windows: false));
        Assert.Equal(
            Path.Combine(resources, "WorkBuddyBridge", "Catnip.WorkBuddyBridge"),
            DesktopPackageLayout.GetWorkBuddyBridgePath(baseDirectory, windows: false));
    }

    [Fact]
    public async Task InitializeAsync_BootstrapsIndependentApiAndLoadsSnapshotOnce()
    {
        var api = new FakeDemoApiClient();
        var bootstrapper = new FakeBootstrapper();
        using var viewModel = new MainWindowViewModel(api, bootstrapper);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsApiConnected);
        Assert.Equal(1, bootstrapper.CallCount);
        Assert.Equal(4, viewModel.Modules.Count);
        Assert.Equal("服务未启动", viewModel.StatusText);
    }

    [Fact]
    public async Task StartAndStopCommands_ReflectRealApiSnapshots()
    {
        var api = new FakeDemoApiClient();
        using var viewModel = new MainWindowViewModel(api, new FakeBootstrapper());
        await viewModel.InitializeAsync();

        await viewModel.StartRuntimeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRunning);
        Assert.True(viewModel.CanStop);
        Assert.Equal(1, api.StartCount);

        await viewModel.StopRuntimeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStopped);
        Assert.Equal(1, api.StopCount);
    }

    [Fact]
    public async Task CustomMode_AllowsPessimisticModuleControl()
    {
        var api = new FakeDemoApiClient();
        using var viewModel = new MainWindowViewModel(api, new FakeBootstrapper());
        await viewModel.InitializeAsync();
        await viewModel.StartRuntimeCommand.ExecuteAsync(null);
        await viewModel.SetCustomModeCommand.ExecuteAsync(null);
        ModuleItemViewModel module = viewModel.Modules.Single(item => item.Id == "today-todos");

        await module.ToggleCommand.ExecuteAsync(null);

        Assert.Same(module, viewModel.Modules.Single(item => item.Id == "today-todos"));
        Assert.False(viewModel.Modules.Single(item => item.Id == "today-todos").Enabled);
        Assert.Equal(1, api.ModuleControlCount);
    }

    [Fact]
    public async Task MasterToggle_DoesNotTemporarilyDisableModeControls()
    {
        var api = new FakeDemoApiClient
        {
            MasterControlStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously),
            MasterControlRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var viewModel = new MainWindowViewModel(api, new FakeBootstrapper());
        await viewModel.InitializeAsync();
        await viewModel.StartRuntimeCommand.ExecuteAsync(null);
        await viewModel.SetCustomModeCommand.ExecuteAsync(null);

        Task toggle = viewModel.ToggleMasterCommand.ExecuteAsync(null);
        await api.MasterControlStarted.Task;

        Assert.True(viewModel.CanControl);
        Assert.True(viewModel.IsCustomMode);
        Assert.All(viewModel.Modules, module => Assert.True(module.CanToggle));

        api.MasterControlRelease.SetResult(true);
        await toggle;
    }

    [Fact]
    public void Navigation_IncludesFilterRulesAfterConnections()
    {
        using var viewModel = new MainWindowViewModel(
            new FakeDemoApiClient(),
            new FakeBootstrapper());

        viewModel.NavigateCommand.Execute("connections");
        Assert.True(viewModel.IsConnectionsPage);

        viewModel.NavigateCommand.Execute("filters");
        Assert.True(viewModel.IsFiltersPage);
        Assert.Equal("过滤规则设置", viewModel.PageTitle);
    }

    [Fact]
    public void FilterRules_CanAddToggleDeleteAndValidateWithinSession()
    {
        using var viewModel = new MainWindowViewModel(
            new FakeDemoApiClient(),
            new FakeBootstrapper());

        viewModel.AddFilterCommand.Execute(null);
        FilterRuleViewModel added = viewModel.FilterRules[^1];
        added.ToggleCommand.Execute(null);
        viewModel.ValidateFiltersCommand.Execute(null);

        Assert.False(added.Enabled);
        Assert.Contains("校验通过", viewModel.FilterValidationMessage, StringComparison.Ordinal);

        added.DeleteCommand.Execute(null);
        Assert.DoesNotContain(added, viewModel.FilterRules);
    }

    [Fact]
    public async Task TestApiCommand_ShowsCountAndTraceId()
    {
        using var viewModel = new MainWindowViewModel(
            new FakeDemoApiClient(),
            new FakeBootstrapper());
        await viewModel.InitializeAsync();

        await viewModel.TestApiCommand.ExecuteAsync(null);

        Assert.Contains("3 条待办", viewModel.LastTestSummary, StringComparison.Ordinal);
        Assert.Contains("trace-test", viewModel.LastTestSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeatherCredential_SaveClearsPlaintextAndKeepsMaskedDatabaseState()
    {
        using var viewModel = new MainWindowViewModel(
            new FakeDemoApiClient(),
            new FakeBootstrapper());
        await viewModel.InitializeAsync();
        viewModel.WeatherApiHost = "demo.qweatherapi.com";
        viewModel.WeatherProjectName = "mcptest";
        viewModel.WeatherProjectId = "project-test";
        viewModel.WeatherCredentialName = "test-key";
        viewModel.WeatherCredentialId = "credential-test";
        viewModel.WeatherApiKey = "test-secret-value";
        viewModel.WeatherDefaultCity = "北京";

        await viewModel.SaveWeatherCredentialCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.WeatherApiKey);
        Assert.Equal("••••••••alue", viewModel.WeatherMaskedApiKey);
        Assert.Contains("gateway.db", viewModel.WeatherCredentialStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkBuddyConfig_RegeneratesWhenServerNameChanges()
    {
        using var viewModel = new MainWindowViewModel(
            new FakeDemoApiClient(),
            new FakeBootstrapper());

        viewModel.WorkBuddyServerName = "catnip-updated";

        Assert.Contains("catnip-updated", viewModel.WorkBuddyConfigJson, StringComparison.Ordinal);
        Assert.Contains("WorkBuddyBridge", viewModel.WorkBuddyConfigJson, StringComparison.Ordinal);
        Assert.Contains("已同步", viewModel.WorkBuddyConfigStatus, StringComparison.Ordinal);
    }

    private sealed class FakeBootstrapper : IDemoApiProcessBootstrapper
    {
        public int CallCount { get; private set; }

        public Task<DemoApiBootstrapResult> EnsureRunningAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DemoApiBootstrapResult(true, 4321, "独立 TestApi 进程已启动"));
        }
    }

    private sealed class FakeDemoApiClient : IDemoApiClient
    {
        private DemoRuntimeSnapshot _snapshot = CreateSnapshot(RuntimeProcessState.Stopped);

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int ModuleControlCount { get; private set; }

        public TaskCompletionSource<bool>? MasterControlStarted { get; init; }

        public TaskCompletionSource<bool>? MasterControlRelease { get; init; }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<DemoRuntimeSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snapshot);
        }

        public Task<DemoControlResult> StartRuntimeAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            _snapshot = CreateSnapshot(RuntimeProcessState.Running);
            return Success("服务启动成功");
        }

        public Task<DemoControlResult> StopRuntimeAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            _snapshot = CreateSnapshot(RuntimeProcessState.Stopped);
            return Success("服务已停止");
        }

        public async Task<DemoControlResult> SetMasterEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            MasterControlStarted?.TrySetResult(true);
            if (MasterControlRelease is not null)
            {
                await MasterControlRelease.Task.WaitAsync(cancellationToken);
            }

            _snapshot = _snapshot with { MasterEnabled = enabled };
            return await Success("总开关已更新");
        }

        public Task<DemoControlResult> SetModeAsync(
            GatewayMode mode,
            CancellationToken cancellationToken = default)
        {
            _snapshot = _snapshot with { Mode = mode };
            return Success("模式已更新");
        }

        public Task<DemoControlResult> SetModuleEnabledAsync(
            string moduleId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            ModuleControlCount++;
            _snapshot = _snapshot with
            {
                Modules = _snapshot.Modules
                    .Select(module => module.Id == moduleId ? module with { Enabled = enabled } : module)
                    .ToArray(),
            };
            return Success("模块设置已保存");
        }

        public Task<DemoTodoResponse> GetTodayTodosAsync(CancellationToken cancellationToken = default)
        {
            DemoTodoItem[] items = Enumerable.Range(1, 3)
                .Select(index => new DemoTodoItem(
                    $"todo-{index}",
                    "local_test_api",
                    "task",
                    $"测试待办 {index}",
                    "demo",
                    null,
                    "normal",
                    "pending"))
                .ToArray();
            return Task.FromResult(new DemoTodoResponse("2026-08-07", items.Length, items, "trace-test"));
        }

        public Task<RuntimeLogResponse> GetLogsAsync(
            int take,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RuntimeLogResponse("runtime-demo.jsonl", 0, []));
        }

        public Task<WeatherCredentialView> GetWeatherCredentialAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new WeatherCredentialView(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "北京",
                    null));
        }

        public Task<WeatherCredentialView> SaveWeatherCredentialAsync(
            WeatherCredentialSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            string suffix = request.ApiKey[^Math.Min(4, request.ApiKey.Length)..];
            return Task.FromResult(
                new WeatherCredentialView(
                    true,
                    request.ApiHost,
                    request.ProjectName,
                    request.ProjectId,
                    request.CredentialName,
                    request.CredentialId,
                    "••••••••" + suffix,
                    request.DefaultCity,
                    DateTimeOffset.UtcNow));
        }

        public Task<WeatherConnectionTestResult> TestWeatherAsync(
            string? city,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new WeatherConnectionTestResult(
                    Catnip.Shared.Business.OperationResult<Catnip.Shared.Business.WeatherData>.Ok(
                        new Catnip.Shared.Business.WeatherData(
                            city ?? "北京",
                            "晴",
                            30,
                            "QWeather",
                            DateTimeOffset.UtcNow),
                        "weather-trace-test"),
                    20));
        }

        private Task<DemoControlResult> Success(string message)
        {
            return Task.FromResult(new DemoControlResult(true, null, message, _snapshot));
        }

        private static DemoRuntimeSnapshot CreateSnapshot(RuntimeProcessState state)
        {
            ModuleInfoDto[] modules =
            [
                CreateModule("today-todos", true),
                CreateModule("customer-interactions", true),
                CreateModule("customer-writeback", false),
                CreateModule("weather", false),
            ];
            return DemoRuntimeSnapshot.Empty with
            {
                ProcessState = state,
                ProcessId = state == RuntimeProcessState.Running ? 9876 : null,
                MasterEnabled = state == RuntimeProcessState.Running,
                Modules = modules,
            };
        }

        private static ModuleInfoDto CreateModule(string id, bool enabled)
        {
            return new ModuleInfoDto(
                id,
                id,
                id,
                enabled,
                enabled ? ModuleStatus.Enabled : ModuleStatus.Disabled,
                ["local"],
                null,
                null);
        }
    }
}
