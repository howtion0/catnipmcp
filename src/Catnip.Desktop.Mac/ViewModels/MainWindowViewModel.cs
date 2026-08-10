using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using Catnip.Desktop.Mac.Models;
using Catnip.Desktop.Mac.Services;
using Catnip.Shared.Management;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Catnip.Desktop.Mac.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Description)> ModuleCopy =
        new Dictionary<string, (string Name, string Description)>(StringComparer.Ordinal)
        {
            ["today-todos"] = ("今日待办", "生成并展示今日待办事项"),
            ["customer-interactions"] = ("客户沟通读取", "读取测试来源中的客户沟通"),
            ["customer-writeback"] = ("客户资料写回", "写回已确认的客户资料"),
            ["weather"] = ("天气测试", "验证普通 HTTP API 路由"),
        };

    private readonly IDemoApiClient _apiClient;
    private readonly IDemoApiProcessBootstrapper _bootstrapper;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private Task? _pollTask;
    private int _initialized;
    private bool _disposed;

    public MainWindowViewModel(
        IDemoApiClient apiClient,
        IDemoApiProcessBootstrapper bootstrapper)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        WorkBuddyConfigJson = BuildWorkBuddyConfigJson(WorkBuddyServerName);
        SeedDefaultModules();
        AddInitialFilter(
            "rule-completed",
            "隐藏已完成待办",
            "status",
            "notEquals",
            "completed",
            true);
        AddInitialFilter(
            "rule-test-source",
            "仅保留测试 API",
            "source",
            "equals",
            "local_test_api",
            true);
    }

    public ObservableCollection<ModuleItemViewModel> Modules { get; } = [];

    public ObservableCollection<RuntimeLogLineViewModel> VisibleLogs { get; } = [];

    public ObservableCollection<FilterRuleViewModel> FilterRules { get; } = [];

    private List<RuntimeLogLineViewModel> AllLogs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePage))]
    [NotifyPropertyChangedFor(nameof(IsLogsPage))]
    [NotifyPropertyChangedFor(nameof(IsKeysPage))]
    [NotifyPropertyChangedFor(nameof(IsConnectionsPage))]
    [NotifyPropertyChangedFor(nameof(IsFiltersPage))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private string _currentPage = "home";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(IsFaulted))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanControl))]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    [NotifyPropertyChangedFor(nameof(ProcessText))]
    [NotifyPropertyChangedFor(nameof(MasterText))]
    [NotifyPropertyChangedFor(nameof(MasterToggleText))]
    [NotifyPropertyChangedFor(nameof(IsFullMode))]
    [NotifyPropertyChangedFor(nameof(IsCustomMode))]
    private DemoRuntimeSnapshot _snapshot = DemoRuntimeSnapshot.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanControl))]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiConnectionText))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isApiConnected;

    [ObservableProperty]
    private string _statusMessage = "正在准备独立 TestApi 进程…";

    [ObservableProperty]
    private string _lastTestSummary = "尚未执行本地 API 测试";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogsPauseText))]
    private bool _logsPaused;

    [ObservableProperty]
    private string _logSearch = string.Empty;

    [ObservableProperty]
    private string _filterValidationMessage = "规则仅保存在当前应用会话中";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherApiHost = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherProjectName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherProjectId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherCredentialName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherCredentialId = string.Empty;

    [ObservableProperty]
    private string _weatherApiKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveWeatherCredential))]
    private string _weatherDefaultCity = "北京";

    [ObservableProperty]
    private string _weatherMaskedApiKey = "尚未配置";

    [ObservableProperty]
    private string _weatherCredentialStatus = "正在读取数据库配置…";

    [ObservableProperty]
    private string _workBuddyConfigStatus = "复制后粘贴到 WorkBuddy 的项目级 MCP 配置中";

    [ObservableProperty]
    private string _workBuddyServerName = "catnip-local";

    [ObservableProperty]
    private string _workBuddyConfigJson = string.Empty;

    public bool IsHomePage => CurrentPage == "home";

    public bool IsLogsPage => CurrentPage == "logs";

    public bool IsKeysPage => CurrentPage == "keys";

    public bool IsConnectionsPage => CurrentPage == "connections";

    public bool IsFiltersPage => CurrentPage == "filters";

    public string PageTitle => CurrentPage switch
    {
        "logs" => "日志",
        "keys" => "API 密钥配置",
        "connections" => "API 连接情况",
        "filters" => "过滤规则设置",
        _ => "首页",
    };

    public string StatusText => Snapshot.ProcessState switch
    {
        RuntimeProcessState.Stopped => "服务未启动",
        RuntimeProcessState.Starting => "正在启动",
        RuntimeProcessState.Running => "服务运行中",
        RuntimeProcessState.Stopping => "正在停止",
        RuntimeProcessState.Faulted => "服务异常",
        _ => "状态未知",
    };

    public string StatusDetail => Snapshot.FaultMessage
        ?? (IsApiConnected ? "TestApi 已连接" : "TestApi 未连接");

    public bool IsRunning => Snapshot.ProcessState == RuntimeProcessState.Running;

    public bool IsStopped => Snapshot.ProcessState is RuntimeProcessState.Stopped or RuntimeProcessState.Faulted;

    public bool IsWorking => Snapshot.ProcessState is RuntimeProcessState.Starting or RuntimeProcessState.Stopping;

    public bool IsFaulted => Snapshot.ProcessState == RuntimeProcessState.Faulted;

    public bool CanStart => IsApiConnected && !IsBusy && IsStopped;

    public bool CanStop => IsApiConnected && !IsBusy && IsRunning;

    public bool CanControl => IsApiConnected && !IsBusy && IsRunning;

    public bool IsFullMode => Snapshot.Mode == GatewayMode.Full;

    public bool IsCustomMode => Snapshot.Mode == GatewayMode.Custom;

    public string MemoryText => Snapshot.WorkingSetBytes <= 0
        ? "0.0 MB"
        : $"{Snapshot.WorkingSetBytes / 1024d / 1024d:F1} MB";

    public string ProcessText => Snapshot.ProcessId is null ? "未运行" : $"PID {Snapshot.ProcessId}";

    public string MasterText => Snapshot.MasterEnabled ? "工具调用已允许" : "工具调用已暂停";

    public string MasterToggleText => Snapshot.MasterEnabled ? "开启" : "关闭";

    public string LogsPauseText => LogsPaused ? "继续刷新" : "暂停刷新";

    public string ApiConnectionText => IsApiConnected ? "TestApi 正常" : "TestApi 未连接";

    public bool CanSaveWeatherCredential => IsApiConnected
        && !IsBusy
        && !string.IsNullOrWhiteSpace(WeatherProjectName)
        && !string.IsNullOrWhiteSpace(WeatherProjectId)
        && !string.IsNullOrWhiteSpace(WeatherCredentialName)
        && !string.IsNullOrWhiteSpace(WeatherCredentialId)
        && !string.IsNullOrWhiteSpace(WeatherDefaultCity);

    public void SetWorkBuddyConfigStatus(string status)
    {
        WorkBuddyConfigStatus = status;
    }

    [RelayCommand]
    private void RefreshWorkBuddyConfig()
    {
        WorkBuddyConfigJson = BuildWorkBuddyConfigJson(WorkBuddyServerName);
        WorkBuddyConfigStatus = "已按当前应用位置重新生成";
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            DemoApiBootstrapResult result = await _bootstrapper.EnsureRunningAsync(_lifetime.Token);
            IsApiConnected = true;
            StatusMessage = result.Message;
            await RefreshStatusAsync(_lifetime.Token);
            await RefreshLogsCoreAsync(_lifetime.Token);
            await LoadWeatherCredentialAsync(_lifetime.Token);
            _pollTask = PollStatusAsync(_lifetime.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsApiConnected = false;
            StatusMessage = $"TestApi 启动失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (page is "home" or "logs" or "keys" or "connections" or "filters")
        {
            CurrentPage = page;
        }
    }

    [RelayCommand]
    private async Task StartRuntimeAsync()
    {
        if (!CanStart)
        {
            return;
        }

        Snapshot = Snapshot with { ProcessState = RuntimeProcessState.Starting, FaultCode = null, FaultMessage = null };
        await ExecuteControlAsync(token => _apiClient.StartRuntimeAsync(token));
    }

    [RelayCommand]
    private async Task StopRuntimeAsync()
    {
        if (!CanStop)
        {
            return;
        }

        Snapshot = Snapshot with { ProcessState = RuntimeProcessState.Stopping };
        await ExecuteControlAsync(token => _apiClient.StopRuntimeAsync(token));
    }

    [RelayCommand]
    private async Task ToggleMasterAsync()
    {
        if (CanControl)
        {
            await ExecuteControlAsync(
                token => _apiClient.SetMasterEnabledAsync(!Snapshot.MasterEnabled, token));
        }
    }

    [RelayCommand]
    private async Task SetFullModeAsync()
    {
        if (CanControl && !IsFullMode)
        {
            await ExecuteControlAsync(token => _apiClient.SetModeAsync(GatewayMode.Full, token));
        }
    }

    [RelayCommand]
    private async Task SetCustomModeAsync()
    {
        if (CanControl && !IsCustomMode)
        {
            await ExecuteControlAsync(token => _apiClient.SetModeAsync(GatewayMode.Custom, token));
        }
    }

    [RelayCommand]
    private async Task TestApiAsync()
    {
        if (!IsApiConnected || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            DemoTodoResponse result = await _apiClient.GetTodayTodosAsync(_lifetime.Token);
            LastTestSummary = $"本地 API 返回 {result.Count} 条待办 · TraceId {result.TraceId}";
            StatusMessage = "连接测试成功";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastTestSummary = $"测试失败：{exception.Message}";
            StatusMessage = "操作失败，请查看日志";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (IsApiConnected && !IsBusy)
        {
            await RefreshLogsCoreAsync(_lifetime.Token);
        }
    }

    [RelayCommand]
    private async Task SaveWeatherCredentialAsync()
    {
        if (!CanSaveWeatherCredential)
        {
            WeatherCredentialStatus = "请填写 API Host、项目、凭据和默认测试城市";
            return;
        }

        IsBusy = true;
        try
        {
            WeatherCredentialView saved = await _apiClient.SaveWeatherCredentialAsync(
                new WeatherCredentialSaveRequest(
                    WeatherApiHost,
                    WeatherProjectName,
                    WeatherProjectId,
                    WeatherCredentialName,
                    WeatherCredentialId,
                    WeatherApiKey,
                    WeatherDefaultCity),
                _lifetime.Token);
            WeatherApiKey = string.Empty;
            ApplyWeatherCredential(saved);
            WeatherCredentialStatus = "已加密写入 gateway.db；API KEY 输入已清空";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WeatherCredentialStatus = $"保存失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestWeatherAsync()
    {
        if (!IsApiConnected || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            WeatherConnectionTestResult response = await _apiClient.TestWeatherAsync(
                WeatherDefaultCity,
                _lifetime.Token);
            WeatherCredentialStatus = response.Result.Success && response.Result.Data is not null
                ? $"连接成功：{response.Result.Data.City} {response.Result.Data.Condition} "
                    + $"{response.Result.Data.TemperatureC}℃ · {response.ElapsedMilliseconds} ms · "
                    + $"TraceId {response.Result.TraceId}"
                : $"连接失败：{response.Result.ErrorCode} · {response.Result.Message} · "
                    + $"TraceId {response.Result.TraceId}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WeatherCredentialStatus = $"连接失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleLogsPause()
    {
        LogsPaused = !LogsPaused;
    }

    [RelayCommand]
    private void AddFilter()
    {
        string id = $"rule-{Guid.NewGuid():N}";
        FilterRules.Add(new FilterRuleViewModel(
            id,
            "新过滤规则",
            "field",
            "equals",
            "value",
            true,
            DeleteFilter));
        FilterValidationMessage = "已新增规则，请填写并校验";
    }

    [RelayCommand]
    private void ValidateFilters()
    {
        bool valid = FilterRules.Count > 0
            && FilterRules.All(rule =>
                !string.IsNullOrWhiteSpace(rule.Name)
                && !string.IsNullOrWhiteSpace(rule.Field)
                && !string.IsNullOrWhiteSpace(rule.Comparison)
                && !string.IsNullOrWhiteSpace(rule.Value));
        FilterValidationMessage = valid
            ? $"{FilterRules.Count} 条规则校验通过（当前会话）"
            : "校验失败：名称、字段、比较方式和值均不能为空";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_bootstrapper is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }

    partial void OnLogSearchChanged(string value)
    {
        ApplyLogFilter();
    }

    partial void OnWorkBuddyServerNameChanged(string value)
    {
        WorkBuddyConfigJson = BuildWorkBuddyConfigJson(value);
        WorkBuddyConfigStatus = "Server 名称已更新，配置内容已同步";
    }

    partial void OnIsBusyChanged(bool value)
    {
        UpdateModuleAvailability();
    }

    private async Task ToggleModuleAsync(ModuleItemViewModel module)
    {
        if (!CanControl || IsFullMode)
        {
            return;
        }

        await ExecuteControlAsync(
            token => _apiClient.SetModuleEnabledAsync(module.Id, !module.Enabled, token));
    }

    private async Task ExecuteControlAsync(
        Func<CancellationToken, Task<DemoControlResult>> action)
    {
        await _controlGate.WaitAsync(_lifetime.Token);
        try
        {
            DemoControlResult result = await action(_lifetime.Token);
            ApplySnapshot(result.Snapshot);
            StatusMessage = result.Message;
            await RefreshLogsCoreAsync(_lifetime.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"操作失败：{exception.Message}";
            await TryRefreshStatusAsync(_lifetime.Token);
        }
        finally
        {
            _controlGate.Release();
        }
    }

    private async Task PollStatusAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (IsBusy || _controlGate.CurrentCount == 0)
                {
                    continue;
                }

                await TryRefreshStatusAsync(cancellationToken);
                if (!LogsPaused && DateTimeOffset.UtcNow.Second % 2 == 0)
                {
                    await RefreshLogsCoreAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryRefreshStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshStatusAsync(cancellationToken);
            IsApiConnected = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            IsApiConnected = false;
            StatusMessage = "TestApi 连接中断，正在等待恢复";
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        DemoRuntimeSnapshot snapshot = await _apiClient.GetStatusAsync(cancellationToken);
        ApplySnapshot(snapshot);
    }

    private async Task LoadWeatherCredentialAsync(CancellationToken cancellationToken)
    {
        WeatherCredentialView credential = await _apiClient.GetWeatherCredentialAsync(cancellationToken);
        ApplyWeatherCredential(credential);
        WeatherCredentialStatus = credential.Configured
            ? string.IsNullOrWhiteSpace(credential.ApiHost)
                ? "API KEY 已从 gateway.db 加载；等待填写专属 API Host"
                : "配置已从 gateway.db 加载"
            : "尚未配置和风天气；首次保存需填写 API KEY";
    }

    private void ApplyWeatherCredential(WeatherCredentialView credential)
    {
        WeatherApiHost = credential.ApiHost;
        WeatherProjectName = credential.ProjectName;
        WeatherProjectId = credential.ProjectId;
        WeatherCredentialName = credential.CredentialName;
        WeatherCredentialId = credential.CredentialId;
        WeatherDefaultCity = credential.DefaultCity;
        WeatherMaskedApiKey = credential.Configured ? credential.MaskedApiKey : "尚未配置";
    }

    private void ApplySnapshot(DemoRuntimeSnapshot snapshot)
    {
        Snapshot = snapshot;
        if (snapshot.Modules.Count == 0)
        {
            foreach (ModuleItemViewModel module in Modules)
            {
                module.Enabled = false;
                module.CanToggle = false;
            }

            return;
        }

        var incomingIds = new HashSet<string>(
            snapshot.Modules.Select(module => module.Id),
            StringComparer.Ordinal);
        for (int index = Modules.Count - 1; index >= 0; index--)
        {
            if (!incomingIds.Contains(Modules[index].Id))
            {
                Modules.RemoveAt(index);
            }
        }

        foreach (ModuleInfoDto module in snapshot.Modules)
        {
            (string name, string description) = ModuleCopy.TryGetValue(module.Id, out var copy)
                ? copy
                : (module.DisplayName, module.Description);
            Func<Task>? test = module.Id switch
            {
                "today-todos" => TestApiAsync,
                "weather" => TestWeatherAsync,
                _ => null,
            };
            string connectorText = module.RequiredConnectorIds.Count == 0
                ? "local"
                : string.Join(" · ", module.RequiredConnectorIds);
            ModuleItemViewModel? existing = Modules.FirstOrDefault(
                item => string.Equals(item.Id, module.Id, StringComparison.Ordinal));
            if (existing is null)
            {
                Modules.Add(new ModuleItemViewModel(
                    module.Id,
                    name,
                    description,
                    module.Enabled,
                    connectorText,
                    CanControl && IsCustomMode,
                    ToggleModuleAsync,
                    test));
                continue;
            }

            existing.Enabled = module.Enabled;
            existing.ConnectorText = connectorText;
            existing.CanToggle = CanControl && IsCustomMode;
        }
    }

    private void SeedDefaultModules()
    {
        Modules.Clear();
        foreach ((string id, (string name, string description)) in ModuleCopy)
        {
            Func<Task>? test = id switch
            {
                "today-todos" => TestApiAsync,
                "weather" => TestWeatherAsync,
                _ => null,
            };
            Modules.Add(new ModuleItemViewModel(
                id,
                name,
                description,
                false,
                id == "weather" ? "weather" : "feishu",
                CanControl && IsCustomMode,
                ToggleModuleAsync,
                test));
        }
    }

    private async Task RefreshLogsCoreAsync(CancellationToken cancellationToken)
    {
        if (LogsPaused)
        {
            return;
        }

        try
        {
            RuntimeLogResponse response = await _apiClient.GetLogsAsync(300, cancellationToken);
            AllLogs.Clear();
            AllLogs.AddRange(response.Lines.Select(RuntimeLogLineViewModel.FromModel));
            ApplyLogFilter();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            IsApiConnected = false;
        }
    }

    private void ApplyLogFilter()
    {
        string query = LogSearch.Trim();
        IEnumerable<RuntimeLogLineViewModel> lines = string.IsNullOrEmpty(query)
            ? AllLogs
            : AllLogs.Where(line =>
                line.Stream.Contains(query, StringComparison.OrdinalIgnoreCase)
                || line.Message.Contains(query, StringComparison.OrdinalIgnoreCase));
        VisibleLogs.Clear();
        foreach (RuntimeLogLineViewModel line in lines)
        {
            VisibleLogs.Add(line);
        }
    }

    private void AddInitialFilter(
        string id,
        string name,
        string field,
        string comparison,
        string value,
        bool enabled)
    {
        FilterRules.Add(new FilterRuleViewModel(
            id,
            name,
            field,
            comparison,
            value,
            enabled,
            DeleteFilter));
    }

    private void DeleteFilter(FilterRuleViewModel rule)
    {
        FilterRules.Remove(rule);
        FilterValidationMessage = "规则已从当前会话删除";
    }

    private void UpdateModuleAvailability()
    {
        bool canToggle = CanControl && IsCustomMode;
        foreach (ModuleItemViewModel module in Modules)
        {
            module.CanToggle = canToggle;
        }
    }

    private static string BuildWorkBuddyConfigJson(string serverName)
    {
        string safeName = string.IsNullOrWhiteSpace(serverName) ? "catnip-local" : serverName.Trim();
        string bridgePath = DesktopPackageLayout.GetWorkBuddyBridgePath(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows());
        var config = new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                [safeName] = new
                {
                    command = bridgePath,
                    args = Array.Empty<string>(),
                },
            },
        };
        return JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
    }
}
