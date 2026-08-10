using Catnip.Shared.Business;
using Catnip.Shared.Management;

namespace Catnip.Desktop.Mac.Models;

public sealed record DemoRuntimeSnapshot(
    RuntimeProcessState ProcessState,
    int? ProcessId,
    string RuntimeAddress,
    string McpAddress,
    string TestApiAddress,
    bool ApiKeyConfigured,
    string Version,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    long WorkingSetBytes,
    bool MasterEnabled,
    GatewayMode Mode,
    IReadOnlyList<ModuleInfoDto> Modules,
    string LogFileName,
    string? FaultCode,
    string? FaultMessage)
{
    public static DemoRuntimeSnapshot Empty { get; } = new(
        RuntimeProcessState.Stopped,
        null,
        "http://127.0.0.1:5210",
        "http://127.0.0.1:5210/mcp",
        DemoApiClientAddress.Value,
        false,
        "0.0.0",
        null,
        DateTimeOffset.MinValue,
        0,
        false,
        GatewayMode.Full,
        [],
        "runtime-demo.jsonl",
        null,
        null);
}

public static class DemoApiClientAddress
{
    public const string Value = "http://127.0.0.1:5220";
}

public sealed record DemoControlResult(
    bool Success,
    string? ErrorCode,
    string Message,
    DemoRuntimeSnapshot Snapshot);

public sealed record SetEnabledRequest(bool Enabled);

public sealed record SetModeRequest(GatewayMode Mode);

public sealed record DemoTodoItem(
    string Id,
    string Source,
    string Type,
    string Title,
    string Description,
    DateTimeOffset? DueTime,
    string Priority,
    string Status);

public sealed record DemoTodoResponse(
    string Date,
    int Count,
    IReadOnlyList<DemoTodoItem> Items,
    string TraceId);

public sealed record RuntimeLogLine(
    DateTimeOffset Timestamp,
    string Stream,
    string Message);

public sealed record RuntimeLogResponse(
    string FileName,
    int Count,
    IReadOnlyList<RuntimeLogLine> Lines);

public sealed record WeatherCredentialSaveRequest(
    string ApiHost,
    string ProjectName,
    string ProjectId,
    string CredentialName,
    string CredentialId,
    string ApiKey,
    string DefaultCity);

public sealed record WeatherCredentialView(
    bool Configured,
    string ApiHost,
    string ProjectName,
    string ProjectId,
    string CredentialName,
    string CredentialId,
    string MaskedApiKey,
    string DefaultCity,
    DateTimeOffset? UpdatedAt);

public sealed record WeatherConnectionTestRequest(string? City);

public sealed record WeatherConnectionTestResult(
    OperationResult<WeatherData> Result,
    long ElapsedMilliseconds);
