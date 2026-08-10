using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catnip.DemoApi.Configuration;
using Catnip.DemoApi.Demo;
using Catnip.DemoApi.Models;
using Catnip.DemoApi.Runtime;
using Catnip.DemoApi.Weather;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Catnip.DemoApi.Hosting;

public static class DemoApiApplication
{
    public static WebApplication Build(string[] args, DemoApiOptions? suppliedOptions = null)
    {
        DemoApiOptions options = suppliedOptions ?? DemoApiOptions.CreateDefault();
        options.Validate();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(options.ListenAddress);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RuntimeLogStore>();
        builder.Services.AddSingleton<RuntimeProcessSupervisor>();
        builder.Services.AddSingleton<DemoTodoService>();
        builder.Services.AddSingleton<WeatherCredentialStore>();
        builder.Services.AddSingleton<QWeatherService>();
        builder.Services.AddHttpClient("runtime", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        builder.Services
            .AddHttpClient("qweather", client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,
            });
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
            json.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });

        WebApplication app = builder.Build();
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthStatus("live")));
        app.MapGet(
            "/api/runtime/status",
            (RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.GetSnapshotAsync(cancellationToken));
        app.MapPost(
            "/api/runtime/start",
            (RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.StartAsync(cancellationToken));
        app.MapPost(
            "/api/runtime/stop",
            (RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.StopAsync(cancellationToken));
        app.MapPut(
            "/api/runtime/master",
            (SetEnabledRequest request, RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.SetMasterEnabledAsync(request.Enabled, cancellationToken));
        app.MapPut(
            "/api/runtime/mode",
            (SetModeRequest request, RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.SetModeAsync(request.Mode, cancellationToken));
        app.MapPut(
            "/api/runtime/modules/{moduleId}",
            (
                string moduleId,
                SetEnabledRequest request,
                RuntimeProcessSupervisor supervisor,
                CancellationToken cancellationToken) =>
                supervisor.SetModuleEnabledAsync(moduleId, request.Enabled, cancellationToken));
        app.MapGet(
            "/api/mcp/todos",
            (RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.InvokeTodayTodosAsync(cancellationToken));
        app.MapGet(
            "/api/mcp/weather",
            (string city, RuntimeProcessSupervisor supervisor, CancellationToken cancellationToken) =>
                supervisor.InvokeWeatherAsync(city, cancellationToken));
        app.MapGet("/api/demo/todos", (DemoTodoService service) => TypedResults.Ok(service.GetToday()));
        app.MapGet(
            "/api/config/weather",
            (WeatherCredentialStore store, CancellationToken cancellationToken) =>
                store.GetViewAsync(cancellationToken));
        app.MapPut(
            "/api/config/weather",
            async Task<Results<Ok<WeatherCredentialView>, BadRequest<ProblemDetails>>> (
                WeatherCredentialSaveRequest request,
                WeatherCredentialStore store,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await store.SaveAsync(request, cancellationToken).ConfigureAwait(false);
                    return TypedResults.Ok(await store.GetViewAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (ArgumentException exception)
                {
                    return TypedResults.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid weather credential configuration",
                        Detail = exception.Message,
                    });
                }
            });
        app.MapGet(
            "/api/weather",
            async (
                string city,
                QWeatherService service,
                RuntimeLogStore logStore,
                CancellationToken cancellationToken) =>
            {
                Catnip.Shared.Business.OperationResult<Catnip.Shared.Business.WeatherData> result =
                    await service.GetCurrentAsync(city, cancellationToken).ConfigureAwait(false);
                await logStore.AppendAsync(
                    "weather",
                    $"tool=catnip_get_weather traceId={result.TraceId} city={city} "
                        + $"success={result.Success} errorCode={result.ErrorCode ?? "none"}",
                    secret: null,
                    cancellationToken).ConfigureAwait(false);
                return result;
            });
        app.MapPost(
            "/api/config/weather/test",
            (WeatherConnectionTestRequest request, QWeatherService service, CancellationToken cancellationToken) =>
                service.TestAsync(request.City, cancellationToken));
        app.MapGet(
            "/api/logs",
            async Task<Results<Ok<RuntimeLogResponse>, BadRequest<ProblemDetails>>> (
                int? take,
                RuntimeLogStore store,
                CancellationToken cancellationToken) =>
            {
                int requested = take ?? 200;
                if (requested is < 1 or > RuntimeLogStore.MaximumTailLines)
                {
                    return TypedResults.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid log tail size",
                        Detail = $"take must be between 1 and {RuntimeLogStore.MaximumTailLines}.",
                    });
                }

                return TypedResults.Ok(await store.TailAsync(requested, cancellationToken).ConfigureAwait(false));
            });

        return app;
    }

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        WebApplication app = Build(args);
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
