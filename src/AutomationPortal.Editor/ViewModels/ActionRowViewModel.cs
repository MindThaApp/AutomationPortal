using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.ViewModels;

/// <summary>One row of the action list: an action, an Otherwise / End bar or a menu option bar.</summary>
internal sealed partial class ActionRowViewModel : ObservableObject
{
    public ActionRowViewModel(ActionInstance action, ActionDescriptor? descriptor, EditorViewModel owner)
    {
        Owner = owner;
        Action = action;
        Descriptor = descriptor;
    }

    public EditorViewModel Owner { get; }
    public ActionInstance Action { get; }
    public ActionDescriptor? Descriptor { get; }
    public string Uid => Action.Uid;
    public FlowRole? Role => Action.Flow?.Role;
    public FlowKind FlowKind => Descriptor?.FlowKind ?? FlowIds.KindOf(Action.ActionId);
    public bool IsBar => Role is FlowRole.Middle or FlowRole.End;
    public bool IsGroupStart => Role == FlowRole.Start;
    public bool IsUnknown => Descriptor is null;

    [ObservableProperty] private int _depth;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private StepState _state;
    [ObservableProperty] private string? _durationText;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private IReadOnlyList<ValidationIssue> _issues = Array.Empty<ValidationIssue>();
    [ObservableProperty] private bool _showMore;
    [ObservableProperty] private bool _hasBreakpoint;

    partial void OnStateChanged(StepState value) => OnPropertyChanged(nameof(AutomationName));
    partial void OnHasBreakpointChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(AutomationName));
    partial void OnIssuesChanged(IReadOnlyList<ValidationIssue> value) => OnPropertyChanged(nameof(AutomationName));

    /// <summary>True while this card is the first / last of its group's body. Used for guide lines.</summary>
    public bool HasOtherwise { get; set; }

    /// <summary>Raised when the card has to be rebuilt (a parameter or setting changed).</summary>
    public event System.Action? Refresh;
    public void RaiseRefresh() { OnPropertyChanged(nameof(AutomationName)); Refresh?.Invoke(); }

    /// <summary>What a screen reader says for this row: the action title and its sentence, plus anything notable ("turned off", "problem: …").</summary>
    public string AutomationName => RowAutomationName.Build(this);
    public override string ToString() => AutomationName;

    public void SetRunState(StepInfo? info)
    {
        State = info?.State ?? StepState.Idle;
        Error = info?.Error;
        DurationText = info is { State: StepState.Succeeded or StepState.Failed } ? FormatDuration(info.Duration) : null;
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalMilliseconds < 1 ? "<1 ms" : d.TotalSeconds < 1 ? $"{d.TotalMilliseconds:0} ms" : d.TotalSeconds < 10 ? $"{d.TotalSeconds:0.0} s" : $"{d.TotalSeconds:0} s";
}
