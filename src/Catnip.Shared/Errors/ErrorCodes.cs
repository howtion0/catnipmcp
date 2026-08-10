namespace Catnip.Shared.Errors;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string RuntimeStopping = "RUNTIME_STOPPING";
    public const string GatewayDisabled = "GATEWAY_DISABLED";
    public const string ModuleDisabled = "MODULE_DISABLED";
    public const string ConnectorDisabled = "CONNECTOR_DISABLED";
    public const string ConnectorUnavailable = "CONNECTOR_UNAVAILABLE";
    public const string ConfigurationInvalid = "CONFIGURATION_INVALID";
    public const string AuthFailed = "AUTH_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string AmbiguousResult = "AMBIGUOUS_RESULT";
    public const string UpstreamTimeout = "UPSTREAM_TIMEOUT";
    public const string UpstreamRateLimited = "UPSTREAM_RATE_LIMITED";
    public const string UpstreamError = "UPSTREAM_ERROR";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string WriteConfirmationRequired = "WRITE_CONFIRMATION_REQUIRED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string WriteResultUnknown = "WRITE_RESULT_UNKNOWN";
    public const string IpcError = "IPC_ERROR";
    public const string IpcFrameTooLarge = "IPC_FRAME_TOO_LARGE";
    public const string InternalError = "INTERNAL_ERROR";
}
