using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.ViewModels;

/// <summary>Marker for the "+" tile at the end of the grid.</summary>
public sealed class NewShortcutTile { public override string ToString() => Strings.Get("MainWindow_NewShortcut"); }

public sealed partial class ShortcutTileViewModel : ObservableObject
{
    /// <summary>The library shows Run and Edit buttons on each tile; a click only selects. The Quick Panel runs on a single click.</summary>
    public bool ExplicitActions { get; }

    public ShortcutTileViewModel(Shortcut shortcut, bool explicitActions = false) { ExplicitActions = explicitActions; Update(shortcut); }

    public Shortcut Shortcut { get; private set; } = null!;
    public Guid Id => Shortcut.Id;

    /// <summary>What a screen reader announces for the tile's list entry (otherwise it would say the class name).</summary>
    public override string ToString() => Pinned ? Name + ", pinned" : Name;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _alias;
    [ObservableProperty] private SymbolRegular _symbol = SymbolRegular.Flash24;
    [ObservableProperty] private Brush _brush = Palette.Gradient("blue");
    [ObservableProperty] private bool _pinned;
    [ObservableProperty] private TileRunState _state = TileRunState.Idle;
    /// <summary>While a run has lasted a couple of seconds: the step it is on and its count, shown under the name. Empty otherwise.</summary>
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _tooltip = "";

    public void Update(Shortcut s)
    {
        Shortcut = s;
        Name = s.Name;
        Alias = s.Alias;
        Symbol = Icons.Parse(s.Icon);
        Brush = Palette.Gradient(s.Color);
        var (top, bottom) = Palette.Hex(s.Color);
        var mid = Logic.TileAppearance.Mid(top, bottom);
        TopHex = top; BottomHex = bottom; MidHex = mid;
        Pinned = s.Pinned;
        Tooltip = BuildTooltip(s);
        OnPropertyChanged(nameof(Id));
    }

    public string TopHex { get; private set; } = "";
    public string BottomHex { get; private set; } = "";
    public string MidHex { get; private set; } = "";

    private string BuildTooltip(Shortcut s)
    {
        var parts = new List<string> { s.Name };
        if (!string.IsNullOrWhiteSpace(s.Description)) parts.Add(s.Description!);
        parts.Add(ExplicitActions ? "Double-click or press Enter to run. Ctrl+E to edit." : Strings.Get("ShortcutTileViewModel_ClickToRun"));
        if (!string.IsNullOrWhiteSpace(s.Hotkey)) parts.Add("Hotkey: " + s.Hotkey);
        return string.Join("\n", parts);
    }
}
