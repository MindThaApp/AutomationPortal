using CommunityToolkit.Mvvm.ComponentModel;
using Flowmate.Core;
using Flowmate.Editor.Logic;

namespace Flowmate.Editor.ViewModels;

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

    /// <summary>True while this card is the first / last of its group's body. Used for guide lines.</summary>
    public bool HasOtherwise { get; set; }

    /// <summary>Raised when the card has to be rebuilt (a parameter or setting changed).</summary>
    public event System.Action? Refresh;
    public void RaiseRefresh() => Refresh?.Invoke();

    public void SetRunState(StepInfo? info)
    {
        State = info?.State ?? StepState.Idle;
        Error = info?.Error;
        DurationText = info is { State: StepState.Succeeded or StepState.Failed } ? FormatDuration(info.Duration) : null;
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalMilliseconds < 1 ? "<1 ms" : d.TotalSeconds < 1 ? $"{d.TotalMilliseconds:0} ms" : d.TotalSeconds < 10 ? $"{d.TotalSeconds:0.0} s" : $"{d.TotalSeconds:0} s";
}
