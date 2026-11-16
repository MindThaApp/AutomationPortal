using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.ViewModels;

/// <summary>Marker for the "+" tile at the end of the grid.</summary>
public sealed class NewShortcutTile { public override string ToString() => "New shortcut"; }

public sealed partial class ShortcutTileViewModel : ObservableObject
{
    public ShortcutTileViewModel(Shortcut shortcut) { Update(shortcut); }

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
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private string _tooltip = "";

    public void Update(Shortcut s)
    {
        Shortcut = s;
        Name = s.Name;
        Alias = s.Alias;
        Symbol = Icons.Parse(s.Icon);
        Brush = Palette.Gradient(s.Color);
        Pinned = s.Pinned;
        Tooltip = BuildTooltip(s);
        OnPropertyChanged(nameof(Id));
    }

    private static string BuildTooltip(Shortcut s)
    {
        var parts = new List<string> { s.Name };
        if (!string.IsNullOrWhiteSpace(s.Description)) parts.Add(s.Description!);
        parts.Add("Click to run. Ctrl+E to edit.");
        if (!string.IsNullOrWhiteSpace(s.Hotkey)) parts.Add("Hotkey: " + s.Hotkey);
        return string.Join("\n", parts);
    }
}
