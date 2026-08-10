using System.Collections.ObjectModel;
using System.Text.Json;
using Catnip.Core.Configuration;
using Catnip.Core.Modules;
using Catnip.Shared.Configuration;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Runtime.Management;

public sealed record GatewayControlState(
    bool MasterEnabled,
    GatewayMode Mode,
    IReadOnlyDictionary<string, bool> Modules)
{
    public IReadOnlyDictionary<string, bool> Modules { get; init; } =
        new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>(Modules ?? new Dictionary<string, bool>(), StringComparer.Ordinal));

    public static GatewayControlState CreateDefault() => FromSettings(CreateDefaultSettings());

    internal static GatewayControlState FromSettings(GatewaySettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, bool> modules = ModuleCatalog.Defaults.ToDictionary(
            static definition => definition.Id,
            definition => settings.Modules.TryGetValue(definition.Id, out bool enabled)
                ? enabled
                : definition.DefaultEnabled,
            StringComparer.Ordinal);
        return new GatewayControlState(settings.Gateway.MasterEnabled, settings.Gateway.Mode, modules);
    }

    internal static GatewaySettingsDto CreateDefaultSettings() =>
        new(
            1,
            new GatewayServiceSettingsDto(
                "127.0.0.1",
                5210,
                "/mcp",
                MasterEnabled: false,
                GatewayMode.Custom,
                RequestTimeoutSeconds: 15,
                MaxResponseBytes: 524288,
                MaxConcurrentCalls: 16),
            new DesktopSettingsDto(
                "system",
                "minimizeToTray",
                AutoStartRuntime: false,
                StartWithWindows: false,
                CompactMode: false),
            new IdentitySettingsDto(
                "fixedDemoUser",
                DemoUserOpenId: string.Empty,
                "sales-demo-001",
                DemoCalendarId: string.Empty),
            new LoggingSettingsDto(
                FileRetentionDays: 14,
                InvocationRetentionDays: 30,
                "Information"),
            ModuleCatalog.Defaults.ToDictionary(
                static definition => definition.Id,
                static definition => definition.DefaultEnabled,
                StringComparer.Ordinal));
}

public sealed class JsonGatewayControlPersistence : IGatewayControlPersistence, IDisposable
{
    private readonly JsonSerializerOptions _jsonOptions = SharedJsonSerializerOptions.Create();
    private readonly ISettingsStore _settingsStore;
    private readonly SemaphoreSlim _storeLock = new(1, 1);
    private GatewaySettingsDto _settings;
    private bool _disposed;

    private JsonGatewayControlPersistence(
        ISettingsStore settingsStore,
        GatewaySettingsDto settings)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        State = GatewayControlState.FromSettings(settings);
    }

    public GatewayControlState State { get; private set; }

    public static async ValueTask<JsonGatewayControlPersistence> CreateAsync(
        ISettingsStore settingsStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        JsonElement? document = await settingsStore.LoadAsync("settings", cancellationToken)
            .ConfigureAwait(false);
        GatewaySettingsDto settings = document is null
            ? GatewayControlState.CreateDefaultSettings()
            : document.Value.Deserialize<GatewaySettingsDto>(SharedJsonSerializerOptions.Create())
                ?? throw new InvalidDataException("Settings document cannot be null.");
        return new JsonGatewayControlPersistence(settingsStore, settings);
    }

    public ValueTask SaveMasterEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        SaveAsync(
            settings => settings with
            {
                Gateway = settings.Gateway with { MasterEnabled = enabled },
            },
            cancellationToken);

    public ValueTask SaveGatewayModeAsync(GatewayMode mode, CancellationToken cancellationToken) =>
        SaveAsync(
            settings => settings with
            {
                Gateway = settings.Gateway with { Mode = mode },
            },
            cancellationToken);

    public ValueTask SaveModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken) =>
        SaveAsync(
            settings =>
            {
                var modules = new Dictionary<string, bool>(settings.Modules, StringComparer.Ordinal)
                {
                    [moduleId] = enabled,
                };
                return settings with { Modules = modules };
            },
            cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_settingsStore is IDisposable disposableStore)
        {
            disposableStore.Dispose();
        }

        _storeLock.Dispose();
        _disposed = true;
    }

    private async ValueTask SaveAsync(
        Func<GatewaySettingsDto, GatewaySettingsDto> update,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _storeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GatewaySettingsDto next = update(_settings);
            JsonElement document = JsonSerializer.SerializeToElement(next, _jsonOptions);
            await _settingsStore.SaveAsync("settings", document, cancellationToken).ConfigureAwait(false);
            _settings = next;
            State = GatewayControlState.FromSettings(next);
        }
        finally
        {
            _storeLock.Release();
        }
    }
}
