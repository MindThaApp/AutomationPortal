using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AutomationPortal.App.Controls;

/// <summary>The tile's focusable face. It reports whether the tile is selected, so a screen reader hears the selection state.</summary>
public sealed class SelectableTileButton : Button
{
    private bool _isTileSelected;

    public bool IsTileSelected
    {
        get => _isTileSelected;
        set
        {
            if (_isTileSelected == value) return;
            var old = _isTileSelected;
            _isTileSelected = value;
            (UIElementAutomationPeer.FromElement(this) as Peer)?.RaiseSelectionChanged(old, value);
        }
    }

    /// <summary>What a screen reader's "select" does: the same as a click in the library.</summary>
    public Action? SelectAction { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(SelectableTileButton owner) : ButtonAutomationPeer(owner), ISelectionItemProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

        public bool IsSelected => owner.IsTileSelected;
        public IRawElementProviderSimple? SelectionContainer => null;
        public void Select() => owner.SelectAction?.Invoke();
        public void AddToSelection() => owner.SelectAction?.Invoke();
        public void RemoveFromSelection() { }

        public void RaiseSelectionChanged(bool old, bool now) =>
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, old, now);
    }
}
