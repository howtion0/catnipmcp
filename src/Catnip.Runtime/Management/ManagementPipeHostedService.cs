using Catnip.Ipc.Management;
using Catnip.Runtime.Hosting;
using Catnip.Shared.Management;

namespace Catnip.Runtime.Management;

public sealed class ManagementPipeHostedService(
    RuntimeManagementOptions options,
    ManagementCommandHandler commandHandler,
    GatewayStateService gatewayState,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new NamedPipeManagementServer(
            options.PipeName,
            commandHandler.HandleAsync,
            AfterResponseSentAsync);

        return server.RunAsync(stoppingToken);
    }

    private ValueTask AfterResponseSentAsync(
        ManagementRequest request,
        ManagementResponse response,
        CancellationToken _)
    {
        if (response.Success
            && string.Equals(
                request.Command,
                NamedPipeManagementClient.ShutdownRuntimeCommandName,
                StringComparison.Ordinal))
        {
            gatewayState.SetReady(false);
            gatewayState.MarkStopping();
            applicationLifetime.StopApplication();
        }

        return ValueTask.CompletedTask;
    }
}
