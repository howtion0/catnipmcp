using System.Reflection;
using Catnip.Shared.Errors;

namespace Catnip.Shared.Tests;

public sealed class ErrorCodesTests
{
    [Fact]
    public void Constants_MatchFrozenContractExactly()
    {
        string[] expected =
        [
            "AMBIGUOUS_RESULT",
            "AUTH_FAILED",
            "CONFIGURATION_INVALID",
            "CONNECTOR_DISABLED",
            "CONNECTOR_UNAVAILABLE",
            "GATEWAY_DISABLED",
            "IDEMPOTENCY_CONFLICT",
            "INTERNAL_ERROR",
            "IPC_ERROR",
            "IPC_FRAME_TOO_LARGE",
            "MODULE_DISABLED",
            "NOT_FOUND",
            "RUNTIME_STOPPING",
            "TOO_MANY_REQUESTS",
            "UNAUTHORIZED",
            "UPSTREAM_ERROR",
            "UPSTREAM_RATE_LIMITED",
            "UPSTREAM_TIMEOUT",
            "VALIDATION_ERROR",
            "WRITE_CONFIRMATION_REQUIRED",
            "WRITE_RESULT_UNKNOWN",
        ];

        string[] actual = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
