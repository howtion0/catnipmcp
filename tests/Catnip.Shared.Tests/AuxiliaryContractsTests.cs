using System.Text.Json;
using Catnip.Shared.Business;
using Catnip.Shared.Configuration;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Shared.Tests;

public sealed class AuxiliaryContractsTests
{
    [Fact]
    public void GatewayStatus_SerializesFrozenShapeAndOrder()
    {
        var data = new GatewayStatusData(
            "running",
            true,
            "custom",
            "http://127.0.0.1:5210/mcp",
            "0.8.0",
            new DateTimeOffset(2026, 8, 7, 8, 0, 0, TimeSpan.Zero),
            [],
            []);

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"runtimeState\":\"running\",\"masterEnabled\":true,\"gatewayMode\":\"custom\",\"mcpAddress\":\"http://127.0.0.1:5210/mcp\",\"version\":\"0.8.0\",\"startedAt\":\"2026-08-07T08:00:00+00:00\",\"modules\":[],\"connectors\":[]}",
            json);
    }

    [Fact]
    public void GatewayStatus_ConstructorNormalizesNullCollections()
    {
        var data = new GatewayStatusData(
            "stopped",
            false,
            "custom",
            "http://127.0.0.1:5210/mcp",
            "0.8.0",
            null,
            null!,
            null!);

        Assert.Empty(data.Modules);
        Assert.Empty(data.Connectors);
    }

    [Fact]
    public void GatewayStatus_MissingCollectionsDeserializeAsEmpty()
    {
        GatewayStatusData? data = JsonSerializer.Deserialize<GatewayStatusData>(
            "{\"runtimeState\":\"stopped\",\"masterEnabled\":false,\"gatewayMode\":\"custom\",\"mcpAddress\":\"http://127.0.0.1:5210/mcp\",\"version\":\"0.8.0\"}",
            SharedJsonSerializerOptions.Create());

        Assert.NotNull(data);
        Assert.Empty(data.Modules);
        Assert.Empty(data.Connectors);
    }

    [Fact]
    public void GatewayStatus_ReusesFrozenModuleAndConnectorShapes()
    {
        var data = new GatewayStatusData(
            "running",
            true,
            "full",
            "http://127.0.0.1:5210/mcp",
            "0.8.0",
            null,
            [new ModuleInfoDto("today-todos", "Today", "Demo", true, ModuleStatus.Enabled, [], null, null)],
            [new ConnectorInfoDto("demo", "Demo", "fake", true, ConnectorStatus.Healthy, 2, null, null, null, null, [])]);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        GatewayStatusData? result = JsonSerializer.Deserialize<GatewayStatusData>(
            JsonSerializer.Serialize(data, options),
            options);

        Assert.NotNull(result);
        Assert.Equal(ModuleStatus.Enabled, Assert.Single(result.Modules).Status);
        Assert.Equal(ConnectorStatus.Healthy, Assert.Single(result.Connectors).Status);
    }

    [Fact]
    public void WeatherInput_SerializesFrozenShape()
    {
        string json = JsonSerializer.Serialize(
            new GetWeatherInput("Demo City"),
            SharedJsonSerializerOptions.Create());

        Assert.Equal("{\"city\":\"Demo City\"}", json);
    }

    [Fact]
    public void WeatherData_SerializesDecimalAndOffset()
    {
        var data = new WeatherData(
            "Demo City",
            "clear",
            23.5m,
            "demo",
            new DateTimeOffset(2026, 8, 7, 18, 30, 0, TimeSpan.FromHours(8)));

        string json = JsonSerializer.Serialize(data, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"city\":\"Demo City\",\"condition\":\"clear\",\"temperatureC\":23.5,\"source\":\"demo\",\"observedAt\":\"2026-08-07T18:30:00+08:00\"}",
            json);
    }

    [Fact]
    public void WeatherData_PreservesNullableTemperature()
    {
        var data = new WeatherData("Demo City", "unknown", null, "demo", DateTimeOffset.UnixEpoch);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        WeatherData? result = JsonSerializer.Deserialize<WeatherData>(
            JsonSerializer.Serialize(data, options),
            options);

        Assert.NotNull(result);
        Assert.Null(result.TemperatureC);
    }

    [Fact]
    public void ConfigurationValidation_SerializesFrozenShape()
    {
        var result = new ConfigurationValidationResult(
            false,
            [new ConfigurationValidationIssue("gateway.port", "OUT_OF_RANGE", "Demo issue", "error")]);

        string json = JsonSerializer.Serialize(result, SharedJsonSerializerOptions.Create());

        Assert.Equal(
            "{\"isValid\":false,\"issues\":[{\"path\":\"gateway.port\",\"code\":\"OUT_OF_RANGE\",\"message\":\"Demo issue\",\"severity\":\"error\"}]}",
            json);
    }

    [Fact]
    public void ConfigurationValidation_NormalizesNullAndMissingIssues()
    {
        var constructed = new ConfigurationValidationResult(true, null!);
        ConfigurationValidationResult? deserialized =
            JsonSerializer.Deserialize<ConfigurationValidationResult>(
                "{\"isValid\":true,\"futureField\":1}",
                SharedJsonSerializerOptions.Create());

        Assert.Empty(constructed.Issues);
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Issues);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("warning")]
    public void ConfigurationValidation_PreservesStableSeverity(string severity)
    {
        var issue = new ConfigurationValidationIssue("gateway.port", "DEMO", "Demo issue", severity);
        JsonSerializerOptions options = SharedJsonSerializerOptions.Create();

        ConfigurationValidationIssue? result = JsonSerializer.Deserialize<ConfigurationValidationIssue>(
            JsonSerializer.Serialize(issue, options),
            options);

        Assert.NotNull(result);
        Assert.Equal(severity, result.Severity);
    }
}
