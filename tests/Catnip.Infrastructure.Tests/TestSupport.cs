using System.Text.Json;
using Catnip.Shared.Configuration;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Infrastructure.Tests;

internal static class SettingsTestData
{
    public static GatewaySettingsDto Create(string? invalidField = null)
    {
        var settings = new GatewaySettingsDto(
            1,
            new GatewayServiceSettingsDto(
                "127.0.0.1",
                5210,
                "/mcp",
                true,
                GatewayMode.Custom,
                15,
                524288,
                16),
            new DesktopSettingsDto("system", "minimizeToTray", false, false, false),
            new IdentitySettingsDto("fixedDemoUser", string.Empty, "sales-demo-001", string.Empty),
            new LoggingSettingsDto(14, 30, "Information"),
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["today-todos"] = true,
                ["customer-interactions"] = true,
                ["customer-writeback"] = false,
                ["weather"] = false,
            });

        return invalidField switch
        {
            "schema" => settings with { SchemaVersion = 2 },
            "listenAddress" => settings with
            {
                Gateway = settings.Gateway with { ListenAddress = "0.0.0.0" },
            },
            "port" => settings with { Gateway = settings.Gateway with { Port = 80 } },
            "mcpPath" => settings with { Gateway = settings.Gateway with { McpPath = "../mcp" } },
            "mode" => settings with { Gateway = settings.Gateway with { Mode = (GatewayMode)99 } },
            "theme" => settings with { Desktop = settings.Desktop with { Theme = "neon" } },
            _ => settings,
        };
    }

    public static string GetPath(string invalidField) => invalidField switch
    {
        "schema" => "schemaVersion",
        "listenAddress" => "gateway.listenAddress",
        "port" => "gateway.port",
        "mcpPath" => "gateway.mcpPath",
        "mode" => "gateway.mode",
        "theme" => "desktop.theme",
        _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
    };

    public static JsonElement ToElement(GatewaySettingsDto settings) =>
        JsonSerializer.SerializeToElement(settings, SharedJsonSerializerOptions.Create());
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Catnip.Infrastructure.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
