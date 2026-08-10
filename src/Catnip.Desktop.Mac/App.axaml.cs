using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Catnip.Desktop.Mac.Services;
using Catnip.Desktop.Mac.ViewModels;
using Catnip.Desktop.Mac.Views;

namespace Catnip.Desktop.Mac;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(DemoApiClient.DefaultAddress),
                Timeout = TimeSpan.FromSeconds(15),
            };
            var apiClient = new DemoApiClient(httpClient);
            var bootstrapper = new DemoApiProcessBootstrapper(apiClient);
            var viewModel = new MainWindowViewModel(apiClient, bootstrapper);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
