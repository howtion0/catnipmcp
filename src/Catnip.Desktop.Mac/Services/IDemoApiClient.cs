using Catnip.Desktop.Mac.Models;
using Catnip.Shared.Management;

namespace Catnip.Desktop.Mac.Services;

public interface IDemoApiClient
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    Task<DemoRuntimeSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DemoControlResult> StartRuntimeAsync(CancellationToken cancellationToken = default);

    Task<DemoControlResult> StopRuntimeAsync(CancellationToken cancellationToken = default);

    Task<DemoControlResult> SetMasterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<DemoControlResult> SetModeAsync(
        GatewayMode mode,
        CancellationToken cancellationToken = default);

    Task<DemoControlResult> SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<DemoTodoResponse> GetTodayTodosAsync(CancellationToken cancellationToken = default);

    Task<RuntimeLogResponse> GetLogsAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<WeatherCredentialView> GetWeatherCredentialAsync(
        CancellationToken cancellationToken = default);

    Task<WeatherCredentialView> SaveWeatherCredentialAsync(
        WeatherCredentialSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<WeatherConnectionTestResult> TestWeatherAsync(
        string? city,
        CancellationToken cancellationToken = default);
}
