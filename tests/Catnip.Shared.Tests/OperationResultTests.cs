using Catnip.Shared.Business;

namespace Catnip.Shared.Tests;

public sealed class OperationResultTests
{
    [Fact]
    public void Ok_CreatesSuccessfulResultWithEmptyWarnings()
    {
        var data = new object();

        var result = OperationResult<object>.Ok(data, "trace-ok");

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.Message);
        Assert.Same(data, result.Data);
        Assert.Equal("trace-ok", result.TraceId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Ok_PreservesWarnings()
    {
        OperationWarning[] warnings =
        [
            new("calendar", "PARTIAL_DATA", "One calendar was unavailable"),
        ];

        var result = OperationResult<string>.Ok("data", "trace-warning", warnings);

        Assert.Same(warnings, result.Warnings);
    }

    [Fact]
    public void Fail_CreatesFailedResultWithEmptyWarnings()
    {
        var result = OperationResult<object>.Fail(
            "VALIDATION_ERROR",
            "Invalid input",
            "trace-fail");

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Equal("Invalid input", result.Message);
        Assert.Null(result.Data);
        Assert.Equal("trace-fail", result.TraceId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fail_PreservesWarnings()
    {
        OperationWarning[] warnings =
        [
            new("connector", "DEGRADED", "Fallback was used"),
        ];

        var result = OperationResult<string>.Fail(
            "UPSTREAM_ERROR",
            "Upstream failed",
            "trace-fail-warning",
            warnings);

        Assert.Same(warnings, result.Warnings);
    }
}
