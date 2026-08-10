using Catnip.DemoApi.Runtime;

namespace Catnip.DemoApi.Tests;

public sealed class DemoApiOptionsTests
{
    [Fact]
    public void Validate_AcceptsAbsoluteLoopbackConfiguration()
    {
        DemoApiOptions options = CreateValidOptions();

        options.Validate();

        Assert.Equal("http://127.0.0.1:5210/mcp", options.McpAddress);
    }

    [Theory]
    [InlineData("http://0.0.0.0:5220")]
    [InlineData("https://127.0.0.1:5220")]
    [InlineData("http://127.0.0.1:5220/api")]
    public void Validate_RejectsUnsafeListenAddress(string address)
    {
        DemoApiOptions options = CreateValidOptions() with { ListenAddress = address };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsRelativeRuntimeLaunchPath()
    {
        DemoApiOptions options = CreateValidOptions() with
        {
            RuntimeLaunchPath = "Catnip.Runtime.dll",
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_AcceptsFixedExtensionlessRuntimeAppHost()
    {
        DemoApiOptions options = CreateValidOptions() with
        {
            RuntimeLaunchPath = Path.Combine(Path.GetTempPath(), "Catnip.Runtime"),
        };

        options.Validate();
    }

    [Fact]
    public void Validate_RejectsUnexpectedRuntimeProgramName()
    {
        DemoApiOptions options = CreateValidOptions() with
        {
            RuntimeLaunchPath = Path.Combine(Path.GetTempPath(), "other-runtime"),
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsRelativeDataRoot()
    {
        DemoApiOptions options = CreateValidOptions() with { DataRoot = "local-data" };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    private static DemoApiOptions CreateValidOptions() => new(
        "http://127.0.0.1:5220",
        "http://127.0.0.1:5210",
        Path.Combine(Path.GetTempPath(), "Catnip.Runtime.dll"),
        Path.Combine(Path.GetTempPath(), "catnip-demo-data"));
}
