using Catnip.Shared.Configuration;
using Catnip.Shared.Management;

namespace Catnip.Infrastructure.Configuration;

public sealed class GatewaySettingsValidator
{
    private static readonly HashSet<string> SupportedThemes =
        new(StringComparer.Ordinal) { "light", "dark", "system" };

    public ConfigurationValidationResult Validate(GatewaySettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<ConfigurationValidationIssue>();

        if (settings.SchemaVersion != 1)
        {
            AddError(issues, "schemaVersion", "UNSUPPORTED_SCHEMA", "schemaVersion must be 1.");
        }

        if (settings.Gateway is null)
        {
            AddError(issues, "gateway", "REQUIRED", "Gateway settings are required.");
        }
        else
        {
            ValidateGateway(settings.Gateway, issues);
        }

        if (settings.Desktop is null)
        {
            AddError(issues, "desktop", "REQUIRED", "Desktop settings are required.");
        }
        else if (!SupportedThemes.Contains(settings.Desktop.Theme))
        {
            AddError(
                issues,
                "desktop.theme",
                "INVALID_VALUE",
                "Theme must be light, dark, or system.");
        }

        if (settings.Identity is null)
        {
            AddError(issues, "identity", "REQUIRED", "Identity settings are required.");
        }

        if (settings.Logging is null)
        {
            AddError(issues, "logging", "REQUIRED", "Logging settings are required.");
        }

        return new ConfigurationValidationResult(issues.Count == 0, issues);
    }

    private static void ValidateGateway(
        GatewayServiceSettingsDto gateway,
        ICollection<ConfigurationValidationIssue> issues)
    {
        if (!string.Equals(gateway.ListenAddress, "127.0.0.1", StringComparison.Ordinal))
        {
            AddError(
                issues,
                "gateway.listenAddress",
                "NON_LOOPBACK_REQUIRES_CONFIRMATION",
                "Only 127.0.0.1 is accepted without explicit UI confirmation.");
        }

        if (gateway.Port is < 1024 or > 65535)
        {
            AddError(issues, "gateway.port", "OUT_OF_RANGE", "Port must be between 1024 and 65535.");
        }

        if (string.IsNullOrEmpty(gateway.McpPath)
            || !gateway.McpPath.StartsWith("/", StringComparison.Ordinal)
            || gateway.McpPath.Contains("..", StringComparison.Ordinal))
        {
            AddError(
                issues,
                "gateway.mcpPath",
                "INVALID_PATH",
                "MCP path must start with '/' and cannot contain '..'.");
        }

        if (!Enum.IsDefined(gateway.Mode))
        {
            AddError(issues, "gateway.mode", "INVALID_VALUE", "Gateway mode must be full or custom.");
        }
    }

    private static void AddError(
        ICollection<ConfigurationValidationIssue> issues,
        string path,
        string code,
        string message)
    {
        issues.Add(new ConfigurationValidationIssue(path, code, message, "error"));
    }
}
