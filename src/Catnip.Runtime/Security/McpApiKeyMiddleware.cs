using System.Net;
using System.Security.Cryptography;
using System.Text;
using Catnip.Core.Security;

namespace Catnip.Runtime.Security;

public sealed class McpApiKeyMiddleware(
    RequestDelegate next,
    ILogger<McpApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-API-Key";
    public const string SecretId = "workbuddy.inbound.api-key";
    private long _authenticationFailures;

    public async Task InvokeAsync(HttpContext context, ISecretStore secretStore)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? expected = await secretStore
            .GetAsync(SecretId, context.RequestAborted)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(expected)
            || !context.Request.Headers.TryGetValue(HeaderName, out var suppliedValues)
            || suppliedValues.Count != 1
            || !Matches(expected, suppliedValues[0]))
        {
            long failureCount = Interlocked.Increment(ref _authenticationFailures);
            logger.LogWarning(
                "MCP authentication failed from {Source}; failure count {FailureCount}.",
                SummarizeSource(context.Connection.RemoteIpAddress),
                failureCount);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool Matches(string expected, string? supplied)
    {
        if (supplied is null)
        {
            return false;
        }

        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));

        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(suppliedHash);
        }
    }

    private static string SummarizeSource(IPAddress? remoteAddress) =>
        remoteAddress switch
        {
            null => "unknown",
            _ when IPAddress.IsLoopback(remoteAddress) => "loopback",
            _ => "remote",
        };
}
