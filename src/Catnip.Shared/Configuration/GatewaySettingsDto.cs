using System.Collections.ObjectModel;
using Catnip.Shared.Management;

namespace Catnip.Shared.Configuration;

public sealed record GatewaySettingsDto(
    int SchemaVersion,
    GatewayServiceSettingsDto Gateway,
    DesktopSettingsDto Desktop,
    IdentitySettingsDto Identity,
    LoggingSettingsDto Logging,
    IReadOnlyDictionary<string, bool> Modules)
{
    public IReadOnlyDictionary<string, bool> Modules { get; init; } = CopyModules(Modules);

    private static IReadOnlyDictionary<string, bool> CopyModules(
        IReadOnlyDictionary<string, bool>? modules)
    {
        var copy = new Dictionary<string, bool>(StringComparer.Ordinal);

        if (modules is not null)
        {
            foreach ((string key, bool value) in modules)
            {
                copy.Add(key, value);
            }
        }

        return new ReadOnlyDictionary<string, bool>(copy);
    }
}

public sealed record GatewayServiceSettingsDto(
    string ListenAddress,
    int Port,
    string McpPath,
    bool MasterEnabled,
    GatewayMode Mode,
    int RequestTimeoutSeconds,
    int MaxResponseBytes,
    int MaxConcurrentCalls);

public sealed record DesktopSettingsDto(
    string Theme,
    string CloseBehavior,
    bool AutoStartRuntime,
    bool StartWithWindows,
    bool CompactMode);

public sealed record IdentitySettingsDto(
    string Mode,
    string DemoUserOpenId,
    string DemoOwnerId,
    string DemoCalendarId);

public sealed record LoggingSettingsDto(
    int FileRetentionDays,
    int InvocationRetentionDays,
    string MinimumLevel);
