namespace Catnip.Desktop.Mac.Services;

public interface IDemoApiProcessBootstrapper
{
    Task<DemoApiBootstrapResult> EnsureRunningAsync(CancellationToken cancellationToken = default);
}

public sealed record DemoApiBootstrapResult(bool StartedNewProcess, int? ProcessId, string Message);
