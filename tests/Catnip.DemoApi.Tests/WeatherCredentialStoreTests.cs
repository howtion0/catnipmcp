using System.Text;
using Catnip.DemoApi.Configuration;
using Catnip.DemoApi.Models;

namespace Catnip.DemoApi.Tests;

public sealed class WeatherCredentialStoreTests
{
    [Fact]
    public async Task Save_RoundTripsEncryptedDatabaseAndRetainsKeyWhenUpdateLeavesItBlank()
    {
        using var directory = new TemporaryDirectory();
        var store = new WeatherCredentialStore(CreateOptions(directory.Path));
        const string secret = "unit-test-weather-secret";

        await store.SaveAsync(
            new WeatherCredentialSaveRequest(
                "unit.qweatherapi.com",
                "unit-project",
                "project-1",
                "unit-credential",
                "credential-1",
                secret,
                "北京"),
            TestContext.Current.CancellationToken);
        await store.SaveAsync(
            new WeatherCredentialSaveRequest(
                "unit.qweatherapi.com",
                "unit-project",
                "project-1",
                "unit-credential",
                "credential-1",
                string.Empty,
                "上海"),
            TestContext.Current.CancellationToken);

        WeatherCredentialView view = await store.GetViewAsync(TestContext.Current.CancellationToken);
        WeatherCredential? credential = await store.GetCredentialAsync(TestContext.Current.CancellationToken);
        byte[] databaseBytes = await File.ReadAllBytesAsync(
            Path.Combine(directory.Path, "data", "gateway.db"),
            TestContext.Current.CancellationToken);

        Assert.True(view.Configured);
        Assert.Equal("••••••••cret", view.MaskedApiKey);
        Assert.Equal("上海", view.DefaultCity);
        Assert.Equal(secret, credential?.ApiKey);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(databaseBytes), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(
                Path.Combine(directory.Path, "secrets", "mac-demo.masterkey"));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [Theory]
    [InlineData("devapi.qweather.com")]
    [InlineData("https://unit.qweatherapi.com")]
    [InlineData("example.com")]
    [InlineData("unit.qweatherapi.com/path")]
    public void Validate_RejectsSharedOrNonHostValues(string apiHost)
    {
        var request = new WeatherCredentialSaveRequest(
            apiHost,
            "project",
            "project-id",
            "credential",
            "credential-id",
            "key",
            "北京");

        Assert.Throws<ArgumentException>(() => WeatherCredentialStore.ValidateRequest(request));
    }

    [Fact]
    public async Task Save_AllowsEncryptedDraftBeforeDedicatedApiHostIsKnown()
    {
        using var directory = new TemporaryDirectory();
        var store = new WeatherCredentialStore(CreateOptions(directory.Path));

        await store.SaveAsync(
            new WeatherCredentialSaveRequest(
                string.Empty,
                "mcptest",
                "project-id",
                "testAPI KEY",
                "credential-id",
                "draft-secret",
                "北京"),
            TestContext.Current.CancellationToken);

        WeatherCredentialView view = await store.GetViewAsync(TestContext.Current.CancellationToken);
        Assert.True(view.Configured);
        Assert.Equal(string.Empty, view.ApiHost);
        Assert.Equal("••••••••cret", view.MaskedApiKey);
    }

    private static Catnip.DemoApi.Runtime.DemoApiOptions CreateOptions(string dataRoot) => new(
        "http://127.0.0.1:0",
        "http://127.0.0.1:5210",
        Path.Combine(AppContext.BaseDirectory, "Catnip.Runtime.dll"),
        dataRoot);
}
