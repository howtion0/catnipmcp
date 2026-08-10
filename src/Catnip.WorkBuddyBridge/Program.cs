using Catnip.WorkBuddyBridge;
using Catnip.WorkBuddyBridge.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<BridgeLogStore>();
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = DemoApiBridgeAddress.Resolve(),
    Timeout = TimeSpan.FromSeconds(12),
});
builder.Services.AddSingleton<DemoApiBridgeClient>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<BridgeTools>();

await builder.Build().RunAsync();
