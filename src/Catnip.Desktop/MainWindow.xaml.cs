using System.IO;
using System.Windows;
using System.Windows.Controls;
using Catnip.Desktop.Mac.ViewModels;
using Microsoft.Win32;

namespace Catnip.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnCopyAddressClicked(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_viewModel.Snapshot.McpAddress);
        _viewModel.SetWorkBuddyConfigStatus("MCP 地址已复制");
    }

    private void OnCopyWorkBuddyConfigClicked(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_viewModel.WorkBuddyConfigJson);
        _viewModel.SetWorkBuddyConfigStatus("完整 JSON 已复制，可直接粘贴到 WorkBuddy");
    }

    private async void OnSaveWorkBuddyConfigClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = "mcp.json",
            Filter = "JSON 文件 (*.json)|*.json",
            Title = "保存 WorkBuddy MCP 配置",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, _viewModel.WorkBuddyConfigJson);
        _viewModel.SetWorkBuddyConfigStatus($"已保存：{Path.GetFileName(dialog.FileName)}");
    }

    private void OnWeatherApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _viewModel.WeatherApiKey = passwordBox.Password;
        }
    }
}
