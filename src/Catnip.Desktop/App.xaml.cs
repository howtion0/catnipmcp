using System.Net.Http;
using System.Threading;
using System.Windows;
using Catnip.Desktop.Mac.Services;
using Catnip.Desktop.Mac.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Catnip.Desktop;

public partial class App : System.Windows.Application
{
    private const string DesktopMutexName = @"Local\Catnip.Desktop";
    private IHost? _host;
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, DesktopMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Catnip 已经在运行。",
                "Catnip",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(
            new HttpClient
            {
                BaseAddress = new Uri(DemoApiClient.DefaultAddress),
                Timeout = TimeSpan.FromSeconds(15),
            });
        builder.Services.AddSingleton<IDemoApiClient, DemoApiClient>();
        builder.Services.AddSingleton<IDemoApiProcessBootstrapper, DemoApiProcessBootstrapper>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetService<MainWindowViewModel>()?.Dispose();
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
