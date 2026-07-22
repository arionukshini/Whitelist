namespace FocusGuard.UI.ViewModels;

public sealed class SelectableItem<T> : ObservableObject
{
    private bool _isSelected;

    public SelectableItem(T value, string displayName, string detail)
    {
        Value = value;
        DisplayName = displayName;
        Detail = detail;
    }

    public T Value { get; }
    public string DisplayName { get; }
    public string Detail { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

