using CommunityToolkit.Mvvm.ComponentModel;

namespace Flow.ViewModels;

public partial class ConditionEntry : ObservableObject
{
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isLinked;        // pre: satisfied by some item; post: consumed by some item
    [ObservableProperty] private string _linkDetail = ""; // e.g. "← ItemA" or "→ 2 Items"

    public ConditionEntry() { }
    public ConditionEntry(string value) => _value = value;

    public override string ToString() => Value;
}
