using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Catnip.Desktop.Mac.ViewModels;

public sealed partial class ModuleItemViewModel : ViewModelBase
{
    public ModuleItemViewModel(
        string id,
        string displayName,
        string description,
        bool enabled,
        string connectorText,
        bool canToggle,
        Func<ModuleItemViewModel, Task> toggleAsync,
        Func<Task>? testAsync = null)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        _enabled = enabled;
        _connectorText = connectorText;
        _canToggle = canToggle;
        ToggleCommand = new AsyncRelayCommand(() => toggleAsync(this));
        TestCommand = new AsyncRelayCommand(testAsync ?? (() => Task.CompletedTask));
        CanTest = testAsync is not null;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnabledText))]
    private bool _enabled;

    [ObservableProperty]
    private string _connectorText;

    public string EnabledText => Enabled ? "已启用" : "已关闭";

    public bool CanTest { get; }

    [ObservableProperty]
    private bool _canToggle;

    public IAsyncRelayCommand ToggleCommand { get; }

    public IAsyncRelayCommand TestCommand { get; }
}
