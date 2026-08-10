using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Catnip.Desktop.Mac.ViewModels;

namespace Catnip.Desktop.Mac.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnCopyAddressClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(viewModel.Snapshot.McpAddress);
    }

    private async void OnCopyWorkBuddyConfigClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(viewModel.WorkBuddyConfigJson);
        viewModel.SetWorkBuddyConfigStatus("完整 JSON 已复制，可直接粘贴到 WorkBuddy");
    }

    private async void OnSaveWorkBuddyConfigClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IStorageFile? target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "mcp.json",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON") { Patterns = ["*.json"] },
            ],
        });
        if (target is null)
        {
            return;
        }

        await using Stream stream = await target.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(viewModel.WorkBuddyConfigJson);
        await writer.FlushAsync();
        viewModel.SetWorkBuddyConfigStatus($"已保存：{target.Name}");
    }
}
