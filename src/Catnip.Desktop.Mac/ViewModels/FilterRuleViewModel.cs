using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Catnip.Desktop.Mac.ViewModels;

public sealed partial class FilterRuleViewModel : ViewModelBase
{
    public FilterRuleViewModel(
        string id,
        string name,
        string field,
        string comparison,
        string value,
        bool enabled,
        Action<FilterRuleViewModel> delete)
    {
        Id = id;
        _name = name;
        _field = field;
        _comparison = comparison;
        _value = value;
        _enabled = enabled;
        DeleteCommand = new RelayCommand(() => delete(this));
        ToggleCommand = new RelayCommand(() => Enabled = !Enabled);
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _field;

    [ObservableProperty]
    private string _comparison;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnabledText))]
    private bool _enabled;

    public string EnabledText => Enabled ? "启用" : "停用";

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand ToggleCommand { get; }
}
