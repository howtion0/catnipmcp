using System.IO.Pipes;
using Catnip.Ipc.Framing;
using Catnip.Shared.Errors;
using Catnip.Shared.Management;

namespace Catnip.Ipc.Management;

public delegate ValueTask<ManagementResponse> ManagementRequestHandler(
    ManagementRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask ManagementResponseSentHandler(
    ManagementRequest request,
    ManagementResponse response,
    CancellationToken cancellationToken);

public sealed class NamedPipeManagementServer(
    string pipeName,
    ManagementRequestHandler requestHandler,
    ManagementResponseSentHandler? responseSentHandler = null)
{
    private readonly LengthPrefixedJsonFramer _framer = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunSingleClientAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunSingleClientAsync(CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        while (pipe.IsConnected)
        {
            ManagementRequest request;

            try
            {
                request = await _framer.ReadAsync<ManagementRequest>(pipe, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsDisconnectedOrInvalidFrame(exception))
            {
                return;
            }

            ManagementResponse response;

            try
            {
                response = await requestHandler(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                response = new ManagementResponse(
                    ProtocolVersion: 1,
                    request.RequestId,
                    Success: false,
                    ErrorCodes.InternalError,
                    ErrorMessage: "Management command failed.",
                    Payload: null);
            }

            try
            {
                await _framer.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
                if (responseSentHandler is not null)
                {
                    await responseSentHandler(request, response, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsDisconnectedOrInvalidFrame(exception))
            {
                return;
            }
        }
    }

    private static bool IsDisconnectedOrInvalidFrame(Exception exception) =>
        exception is IOException;
}
