using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Catnip.DemoApi.Models;
using Catnip.Ipc.Management;
using Catnip.Shared.Business;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;
using ModelContextProtocol.Client;

namespace Catnip.DemoApi.Runtime;

public sealed class RuntimeProcessSupervisor : IAsyncDisposable
{
    private const string RuntimeDataRootEnvironmentVariable = "CATNIP_DATA_ROOT";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly System.Text.Json.JsonSerializerOptions RuntimeJsonOptions =
        SharedJsonSerializerOptions.Create();

    private readonly DemoApiOptions _options;
    private readonly RuntimeLogStore _logStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private Process? _process;
    private string? _apiKey;
    private string? _managementPipeName;
    private RuntimeProcessState _processState = RuntimeProcessState.Stopped;
    private DateTimeOffset? _startedAt;
    private string? _faultCode;
    private string? _faultMessage;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    private bool _disposed;

    public RuntimeProcessSupervisor(
        DemoApiOptions options,
        RuntimeLogStore logStore,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        options.Validate();
        _options = options;
        _logStore = logStore;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<DemoControlResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { HasExited: false })
            {
                DemoRuntimeSnapshot existing = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return new DemoControlResult(true, null, "Runtime 已在运行。", existing);
            }

            if (!File.Exists(_options.RuntimeLaunchPath))
            {
                SetFault("RUNTIME_NOT_BUILT", "Runtime 程序尚未构建，请先执行 Release build。");
                return new DemoControlResult(
                    false,
                    "RUNTIME_NOT_BUILT",
                    "Runtime 程序尚未构建。",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }

            SetState(RuntimeProcessState.Starting);
            _apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _managementPipeName = CreateManagementPipeName();
            bool isManagedAssembly = string.Equals(
                Path.GetExtension(_options.RuntimeLaunchPath),
                ".dll",
                StringComparison.OrdinalIgnoreCase);
            var startInfo = new ProcessStartInfo(
                isManagedAssembly ? "dotnet" : _options.RuntimeLaunchPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_options.RuntimeLaunchPath),
            };
            if (isManagedAssembly)
            {
                startInfo.ArgumentList.Add(_options.RuntimeLaunchPath);
            }

            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(_options.RuntimeAddress);
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["CATNIP_INBOUND_API_KEY"] = _apiKey;
            startInfo.Environment["CATNIP_MANAGEMENT_PIPE_NAME"] = _managementPipeName;
            startInfo.Environment["CATNIP_DEMO_API"] = _options.ListenAddress;
            startInfo.Environment[RuntimeDataRootEnvironmentVariable] = _options.DataRoot;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("The fixed Runtime process did not start.");
                }

                _process = process;
                _startedAt = _timeProvider.GetUtcNow();
                _stdoutPump = PumpOutputAsync(process.StandardOutput, "stdout", _apiKey);
                _stderrPump = PumpOutputAsync(process.StandardError, "stderr", _apiKey);
                _ = MonitorExitAsync(process);

                await WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
                SetState(RuntimeProcessState.Running);
                return new DemoControlResult(
                    true,
                    null,
                    "服务启动成功",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetFault("RUNTIME_START_FAILED", exception.Message);
                await _logStore.AppendAsync(
                    "supervisor",
                    $"Runtime start failed: {exception.GetType().Name}: {exception.Message}",
                    _apiKey,
                    cancellationToken).ConfigureAwait(false);
                return new DemoControlResult(
                    false,
                    "RUNTIME_START_FAILED",
                    "Runtime 启动失败，请查看日志。",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<DemoControlResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is null || _process.HasExited)
            {
                SetState(RuntimeProcessState.Stopped);
                return new DemoControlResult(
                    true,
                    null,
                    "服务已停止",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }

            SetState(RuntimeProcessState.Stopping);
            try
            {
                await SendManagementCommandAsync(
                    client => client.ShutdownRuntimeAsync(graceful: true, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ShutdownTimeout);
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await AwaitOutputPumpsAsync(timeout.Token).ConfigureAwait(false);
                _process.Dispose();
                _process = null;
                ClearRuntimeSecrets();
                SetState(RuntimeProcessState.Stopped);
                return new DemoControlResult(
                    true,
                    null,
                    "服务已停止",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetFault("RUNTIME_STOP_FAILED", exception.Message);
                return new DemoControlResult(
                    false,
                    "RUNTIME_STOP_FAILED",
                    "优雅停止失败；未自动强制结束进程。",
                    await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<DemoControlResult> SetMasterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteControlAsync(
            client => client.SetMasterEnabledAsync(enabled, cancellationToken),
            enabled ? "总开关已打开" : "总开关已关闭",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DemoControlResult> SetModeAsync(
        GatewayMode mode,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteControlAsync(
            client => client.SetGatewayModeAsync(mode, cancellationToken),
            mode == GatewayMode.Full ? "已切换为全量模式" : "已切换为自定义模式",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DemoControlResult> SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || moduleId.Length > 64)
        {
            return new DemoControlResult(
                false,
                "VALIDATION_ERROR",
                "模块标识无效。",
                await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }

        return await ExecuteControlAsync(
            client => client.SetModuleEnabledAsync(moduleId, enabled, cancellationToken),
            "模块设置已保存",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult<TodayTodoData>> InvokeTodayTodosAsync(
        CancellationToken cancellationToken = default) =>
        InvokeToolAsync<TodayTodoData>("catnip_get_today_todos", arguments: null, cancellationToken);

    public Task<OperationResult<WeatherData>> InvokeWeatherAsync(
        string city,
        CancellationToken cancellationToken = default) =>
        InvokeToolAsync<WeatherData>(
            "catnip_get_weather",
            new Dictionary<string, object?> { ["city"] = city },
            cancellationToken);

    public async Task<DemoRuntimeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        Process? process = _process;
        RuntimeProcessState state;
        string? faultCode;
        string? faultMessage;
        DateTimeOffset? startedAt;
        lock (_stateLock)
        {
            state = _processState;
            faultCode = _faultCode;
            faultMessage = _faultMessage;
            startedAt = _startedAt;
        }

        if (process is { HasExited: true }
            && state is not RuntimeProcessState.Stopped and not RuntimeProcessState.Stopping)
        {
            SetFault("RUNTIME_EXITED", $"Runtime 意外退出，退出码 {process.ExitCode}。");
            state = RuntimeProcessState.Faulted;
            faultCode = "RUNTIME_EXITED";
            faultMessage = $"Runtime 意外退出，退出码 {process.ExitCode}。";
        }

        RuntimeSnapshot? runtime = state is RuntimeProcessState.Running or RuntimeProcessState.Starting
            ? await TryGetRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false)
            : null;

        return new DemoRuntimeSnapshot(
            runtime?.ProcessState ?? state,
            process is { HasExited: false } ? process.Id : null,
            _options.RuntimeAddress,
            _options.McpAddress,
            _options.ListenAddress,
            !string.IsNullOrEmpty(_apiKey),
            runtime?.Version ?? typeof(RuntimeProcessSupervisor).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            runtime?.StartedAt ?? startedAt,
            _timeProvider.GetUtcNow(),
            process is { HasExited: false } ? process.WorkingSet64 : 0,
            runtime?.MasterEnabled ?? false,
            runtime?.Mode ?? GatewayMode.Full,
            runtime?.Modules ?? [],
            _logStore.CurrentFileName,
            runtime?.FaultCode ?? faultCode,
            runtime?.FaultMessage ?? faultMessage);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_process is { HasExited: false })
            {
                await StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _disposed = true;
            _commandLock.Dispose();
        }
    }

    private async Task<DemoControlResult> ExecuteControlAsync(
        Func<NamedPipeManagementClient, ValueTask<ManagementResponse>> command,
        string successMessage,
        CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemoRuntimeSnapshot current = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (current.ProcessState != RuntimeProcessState.Running)
            {
                return new DemoControlResult(false, "RUNTIME_NOT_RUNNING", "Runtime 未运行。", current);
            }

            ManagementResponse response = await SendManagementCommandAsync(command, cancellationToken)
                .ConfigureAwait(false);
            return new DemoControlResult(
                response.Success,
                response.ErrorCode,
                response.Success ? successMessage : response.ErrorMessage ?? "操作失败。",
                await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DemoControlResult(
                false,
                "IPC_ERROR",
                "Runtime 管理连接失败，请查看日志。",
                await GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<OperationResult<T>> InvokeToolAsync<T>(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        string traceId = Guid.NewGuid().ToString("N");
        DemoRuntimeSnapshot current = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        string? apiKey = _apiKey;
        if (current.ProcessState != RuntimeProcessState.Running || string.IsNullOrEmpty(apiKey))
        {
            return OperationResult<T>.Fail(
                ErrorCodes.RuntimeStopping,
                "Runtime 未运行或正在停止。",
                traceId);
        }

        try
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(_options.McpAddress, UriKind.Absolute),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["X-API-Key"] = apiKey,
                },
            };
            await using McpClient client = await McpClient.CreateAsync(
                new HttpClientTransport(transportOptions),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var callResult = await client.CallToolAsync(
                toolName,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            string json = JsonSerializer.Serialize(callResult.StructuredContent, RuntimeJsonOptions);
            return JsonSerializer.Deserialize<OperationResult<T>>(json, RuntimeJsonOptions)
                ?? OperationResult<T>.Fail(
                    ErrorCodes.InternalError,
                    "Runtime 返回了空结果。",
                    traceId);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or JsonException
                or InvalidOperationException
                or TaskCanceledException)
        {
            return OperationResult<T>.Fail(
                ErrorCodes.RuntimeStopping,
                "Runtime MCP 通道不可用。",
                traceId);
        }
    }

    private async Task<ManagementResponse> SendManagementCommandAsync(
        Func<NamedPipeManagementClient, ValueTask<ManagementResponse>> command,
        CancellationToken cancellationToken)
    {
        string pipeName = _managementPipeName
            ?? throw new InvalidOperationException("Runtime management pipe is not available.");
        await using var client = new NamedPipeManagementClient(pipeName, TimeSpan.FromSeconds(3));
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return await command(client).ConfigureAwait(false);
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            Process? process = _process;
            if (process is null || process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Runtime exited before becoming ready (exit code {process?.ExitCode}).");
            }

            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                attempt.CancelAfter(TimeSpan.FromMilliseconds(500));
                using HttpClient client = _httpClientFactory.CreateClient("runtime");
                using HttpResponseMessage response = await client.GetAsync(
                    $"{_options.RuntimeAddress}/health/ready",
                    attempt.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    ManagementResponse ping = await SendManagementCommandAsync(
                        pipeClient => pipeClient.PingAsync(attempt.Token),
                        attempt.Token).ConfigureAwait(false);
                    if (ping.Success)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or IOException
                    or TimeoutException
                    or OperationCanceledException
                && !timeout.IsCancellationRequested)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task<RuntimeSnapshot?> TryGetRuntimeSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = _httpClientFactory.CreateClient("runtime");
            return await client.GetFromJsonAsync<RuntimeSnapshot>(
                $"{_options.RuntimeAddress}/status",
                RuntimeJsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task PumpOutputAsync(StreamReader reader, string stream, string secret)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await _logStore.AppendAsync(stream, line, secret).ConfigureAwait(false);
        }
    }

    private async Task MonitorExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await AwaitOutputPumpsAsync(CancellationToken.None).ConfigureAwait(false);
            RuntimeProcessState state;
            lock (_stateLock)
            {
                state = _processState;
            }

            if (state == RuntimeProcessState.Stopping && process.ExitCode == 0)
            {
                SetState(RuntimeProcessState.Stopped);
            }
            else if (state != RuntimeProcessState.Stopped)
            {
                SetFault("RUNTIME_EXITED", $"Runtime 意外退出，退出码 {process.ExitCode}。");
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AwaitOutputPumpsAsync(CancellationToken cancellationToken)
    {
        Task[] pumps = new[] { _stdoutPump, _stderrPump }.OfType<Task>().ToArray();
        if (pumps.Length > 0)
        {
            await Task.WhenAll(pumps).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private string CreateManagementPipeName()
    {
        string suffix = $"d{Environment.ProcessId:x}{Guid.NewGuid():N}"[..16];
        return PipeNames.Management(suffix);
    }

    private void SetState(RuntimeProcessState state)
    {
        lock (_stateLock)
        {
            _processState = state;
            if (state != RuntimeProcessState.Faulted)
            {
                _faultCode = null;
                _faultMessage = null;
            }

            if (state == RuntimeProcessState.Stopped)
            {
                _startedAt = null;
            }
        }
    }

    private void SetFault(string code, string message)
    {
        lock (_stateLock)
        {
            _processState = RuntimeProcessState.Faulted;
            _faultCode = code;
            _faultMessage = message;
        }
    }

    private void ClearRuntimeSecrets()
    {
        _apiKey = null;
        _managementPipeName = null;
    }
}
