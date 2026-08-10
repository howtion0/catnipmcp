using System.Text.Json;
using Catnip.Ipc.Management;
using Catnip.Runtime.Hosting;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Runtime.Management;

public sealed class ManagementCommandHandler(
    GatewayStateService gatewayState,
    GatewayControlService gatewayControl)
{
    private readonly JsonSerializerOptions _jsonOptions = SharedJsonSerializerOptions.Create();

    public async ValueTask<ManagementResponse> HandleAsync(
        ManagementRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProtocolVersion != NamedPipeManagementClient.ProtocolVersion)
        {
            return Failure(
                request,
                ErrorCodes.IpcError,
                "Unsupported management protocol version.");
        }

        try
        {
            return request.Command switch
            {
                NamedPipeManagementClient.PingCommand => Success(request, new EmptyCommand()),
                NamedPipeManagementClient.GetRuntimeSnapshotCommand =>
                    Success(request, gatewayState.GetSnapshot()),
                NamedPipeManagementClient.SetMasterEnabledCommandName =>
                    await HandleSetMasterEnabledAsync(request, cancellationToken).ConfigureAwait(false),
                NamedPipeManagementClient.SetGatewayModeCommandName =>
                    await HandleSetGatewayModeAsync(request, cancellationToken).ConfigureAwait(false),
                NamedPipeManagementClient.SetModuleEnabledCommandName =>
                    await HandleSetModuleEnabledAsync(request, cancellationToken).ConfigureAwait(false),
                NamedPipeManagementClient.ShutdownRuntimeCommandName =>
                    HandleShutdown(request),
                _ => Failure(request, ErrorCodes.ValidationError, "Unknown management command."),
            };
        }
        catch (JsonException)
        {
            return Failure(
                request,
                ErrorCodes.ValidationError,
                "Management command payload was invalid.");
        }
        catch (ArgumentException exception)
        {
            return Failure(request, ErrorCodes.ValidationError, exception.Message);
        }
    }

    private async ValueTask<ManagementResponse> HandleSetMasterEnabledAsync(
        ManagementRequest request,
        CancellationToken cancellationToken)
    {
        SetMasterEnabledCommand command = request.Payload.Deserialize<SetMasterEnabledCommand>(_jsonOptions)
            ?? throw new JsonException("SetMasterEnabled payload was null.");

        await gatewayControl.SetMasterEnabledAsync(command.Enabled, cancellationToken)
            .ConfigureAwait(false);
        return Success(request, command);
    }

    private async ValueTask<ManagementResponse> HandleSetGatewayModeAsync(
        ManagementRequest request,
        CancellationToken cancellationToken)
    {
        SetGatewayModeCommand command = request.Payload.Deserialize<SetGatewayModeCommand>(_jsonOptions)
            ?? throw new JsonException("SetGatewayMode payload was null.");
        await gatewayControl.SetGatewayModeAsync(command.Mode, cancellationToken).ConfigureAwait(false);
        return Success(request, command);
    }

    private async ValueTask<ManagementResponse> HandleSetModuleEnabledAsync(
        ManagementRequest request,
        CancellationToken cancellationToken)
    {
        SetModuleEnabledCommand command = request.Payload.Deserialize<SetModuleEnabledCommand>(_jsonOptions)
            ?? throw new JsonException("SetModuleEnabled payload was null.");
        await gatewayControl.SetModuleEnabledAsync(command.ModuleId, command.Enabled, cancellationToken)
            .ConfigureAwait(false);
        return Success(request, command);
    }

    private ManagementResponse HandleShutdown(ManagementRequest request)
    {
        ShutdownRuntimeCommand command = request.Payload.Deserialize<ShutdownRuntimeCommand>(_jsonOptions)
            ?? throw new JsonException("ShutdownRuntime payload was null.");

        return command.Graceful
            ? Success(request, command)
            : Failure(
                request,
                ErrorCodes.ValidationError,
                "Only graceful Runtime shutdown is supported.");
    }

    private ManagementResponse Success<TPayload>(ManagementRequest request, TPayload payload) =>
        new(
            NamedPipeManagementClient.ProtocolVersion,
            request.RequestId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            JsonSerializer.SerializeToElement(payload, _jsonOptions));

    private static ManagementResponse Failure(
        ManagementRequest request,
        string errorCode,
        string errorMessage) =>
        new(
            NamedPipeManagementClient.ProtocolVersion,
            request.RequestId,
            Success: false,
            errorCode,
            errorMessage,
            Payload: null);
}
