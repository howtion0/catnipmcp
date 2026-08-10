using Catnip.Infrastructure.Configuration;
using Catnip.Shared.Configuration;
using Catnip.Shared.Management;

namespace Catnip.Infrastructure.Tests;

public sealed class GatewaySettingsValidatorTests
{
    [Fact]
    public void FrozenDefaults_AreValid()
    {
        ConfigurationValidationResult result = new GatewaySettingsValidator().Validate(
            SettingsTestData.Create());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("listenAddress")]
    [InlineData("port")]
    [InlineData("mcpPath")]
    [InlineData("mode")]
    [InlineData("theme")]
    public void FrozenRules_ReportStableErrorPaths(string invalidField)
    {
        GatewaySettingsDto settings = SettingsTestData.Create(invalidField);

        ConfigurationValidationResult result = new GatewaySettingsValidator().Validate(settings);

        Assert.False(result.IsValid);
        Assert.All(result.Issues, static issue => Assert.Equal("error", issue.Severity));
        Assert.Contains(result.Issues, issue => issue.Path == SettingsTestData.GetPath(invalidField));
    }
}
