using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catnip.Core.Modules;
using Catnip.Core.Security;
using Catnip.Infrastructure.Configuration;
using Catnip.Infrastructure.Paths;
using Catnip.Runtime.Management;
using Catnip.Runtime.Mcp;
using Catnip.Runtime.Security;
using ModelContextProtocol.AspNetCore;

namespace Catnip.Runtime.Hosting;

public static class RuntimeApplication
{
    public const string DefaultMcpAddress = "http://127.0.0.1:5210/mcp";
    public const string DefaultDemoApiAddress = "http://127.0.0.1:5220";
    public const string DemoApiAddressEnvironmentVariable = "CATNIP_DEMO_API";
    public const string DataRootEnvironmentVariable = "CATNIP_DATA_ROOT";
    public const string InboundApiKeyEnvironmentVariable = "CATNIP_INBOUND_API_KEY";
    public const string ManagementPipeEnvironmentVariable = "CATNIP_MANAGEMENT_PIPE_NAME";

    public static WebApplication Build(string[] args, RuntimeHostOptions? hostOptions = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        RuntimeVersionInfo versionInfo = CreateVersionInfo();
        (IGatewayControlPersistence controlPersistence, GatewayControlState controlState) =
            CreateControlPersistence(hostOptions);
        var moduleManager = new ModuleManager();
        foreach ((string moduleId, bool enabled) in controlState.Modules)
        {
            moduleManager.SetCustomEnabled(moduleId, enabled);
        }

        moduleManager.SetMode(controlState.Mode);
        var gatewayState = new GatewayStateService(
            DefaultMcpAddress,
            versionInfo.ProductVersion,
            moduleManager);
        gatewayState.SetMasterEnabled(controlState.MasterEnabled);
        string? inboundApiKey = hostOptions is null
            ? Environment.GetEnvironmentVariable(InboundApiKeyEnvironmentVariable)
            : hostOptions.InboundApiKey;
        string? configuredPipeName = hostOptions is null
            ? Environment.GetEnvironmentVariable(ManagementPipeEnvironmentVariable)
            : hostOptions.ManagementPipeName;
        string managementPipeName = RuntimePipeNameResolver.Resolve(configuredPipeName);
        string demoApiAddress = ResolveDemoApiAddress(hostOptions?.DemoApiAddress);
        var secretStore = new InMemorySecretStore(McpApiKeyMiddleware.SecretId, inboundApiKey);

        builder.Services.AddSingleton(versionInfo);
        builder.Services.AddSingleton(moduleManager);
        builder.Services.AddSingleton(gatewayState);
        builder.Services.AddSingleton<ISecretStore>(secretStore);
        builder.Services.AddSingleton(new RuntimeManagementOptions(managementPipeName));
        builder.Services.AddSingleton(controlPersistence);
        builder.Services.AddSingleton<GatewayControlService>();
        builder.Services.AddSingleton<ManagementCommandHandler>();
        builder.Services.AddHostedService<ManagementPipeHostedService>();
        builder.Services.AddHttpClient<IDemoToolBackendClient, DemoToolBackendClient>(client =>
        {
            client.BaseAddress = new Uri(demoApiAddress, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(12);
        });
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<GatewayTools>();
        builder.Services.ConfigureHttpJsonOptions(
            options =>
            {
                options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
                options.SerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            });

        WebApplication app = builder.Build();
        gatewayState.SetReady(!string.IsNullOrEmpty(inboundApiKey));
        app.Lifetime.ApplicationStarted.Register(gatewayState.MarkRunning);
        app.Lifetime.ApplicationStopping.Register(gatewayState.MarkStopping);

        app.UseMiddleware<McpApiKeyMiddleware>();
        app.MapGet("/health/live", () => Results.Ok(new HealthStatus("live")));
        app.MapGet(
            "/health/ready",
            () => gatewayState.IsReady
                ? Results.Ok(new HealthStatus("ready"))
                : Results.Json(new HealthStatus("notReady"), statusCode: StatusCodes.Status503ServiceUnavailable));
        app.MapGet("/version", () => Results.Ok(versionInfo));
        app.MapGet("/status", () => Results.Ok(gatewayState.GetSnapshot()));
        app.MapMcp("/mcp");

        return app;
    }

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        WebApplication app = Build(args);
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeVersionInfo CreateVersionInfo()
    {
        Assembly assembly = typeof(RuntimeApplication).Assembly;
        string productVersion = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string gitCommit = Environment.GetEnvironmentVariable("CATNIP_GIT_COMMIT") ?? "unknown";

        return new RuntimeVersionInfo(
            productVersion,
            gitCommit,
            DateTimeOffset.UtcNow,
            IpcProtocolVersion: 1,
            McpSdkVersion: GetAssemblyVersion("ModelContextProtocol.AspNetCore"));
    }

    private static (IGatewayControlPersistence Persistence, GatewayControlState State)
        CreateControlPersistence(RuntimeHostOptions? hostOptions)
    {
        string? dataRoot = hostOptions?.DataRoot
            ?? Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return (new InMemoryGatewayControlPersistence(), GatewayControlState.CreateDefault());
        }

        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("Runtime data root must be an absolute path.", nameof(hostOptions));
        }

        var settingsStore = new JsonSettingsStore(new AppDataPathProvider(dataRoot));
        JsonGatewayControlPersistence persistence = JsonGatewayControlPersistence
            .CreateAsync(settingsStore)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return (persistence, persistence.State);
    }

    private static string ResolveDemoApiAddress(string? configuredAddress)
    {
        string value = configuredAddress
            ?? Environment.GetEnvironmentVariable(DemoApiAddressEnvironmentVariable)
            ?? DefaultDemoApiAddress;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Demo API address must be an HTTP loopback origin without a path.",
                nameof(configuredAddress));
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string GetAssemblyVersion(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName).GetName().Version?.ToString() ?? "unknown";
        }
        catch (FileNotFoundException)
        {
            return "unknown";
        }
    }
}

public sealed record RuntimeHostOptions(
    string? InboundApiKey = null,
    string? ManagementPipeName = null,
    string? DemoApiAddress = null,
    string? DataRoot = null);

public sealed record HealthStatus(string Status);

public sealed record RuntimeVersionInfo(
    string ProductVersion,
    string GitCommit,
    DateTimeOffset StartedAtUtc,
    int IpcProtocolVersion,
    string McpSdkVersion);
