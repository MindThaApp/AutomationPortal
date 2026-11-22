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
    /// <summary>The library shows Run and Edit buttons on each tile; a click only selects, and Run, Enter and a double-click go through the run gate. The Quick Panel runs on a single click.</summary>
    public bool ExplicitActions { get; }

    private readonly ActionRegistry? _actions;

    public ShortcutTileViewModel(Shortcut shortcut, bool explicitActions = false, ActionRegistry? actions = null)
    {
        ExplicitActions = explicitActions;
        _actions = actions;
        Update(shortcut);
    }

    /// <summary>What a shortcut does to files, as the badge shows it: the text key, an icon and whether it gets the caution tone. Text and icon always carry the meaning, never colour alone.</summary>
    public static (string TextKey, SymbolRegular Symbol, bool Caution) EffectBadge(ShortcutEffect effect) => effect switch
    {
        ShortcutEffect.ChangesFiles => ("Templates_ChangesFiles", SymbolRegular.Warning16, true),
        ShortcutEffect.MakesCopies => ("Templates_MakesCopies", SymbolRegular.Copy16, false),
        _ => ("Templates_NoFileChanges", SymbolRegular.ShieldCheckmark16, false),
    };

    public Shortcut Shortcut { get; private set; } = null!;
    public Guid Id => Shortcut.Id;

    /// <summary>What a screen reader announces for the tile's list entry (otherwise it would say the class name).</summary>
    public override string ToString() => AutomationName;

    /// <summary>The tile read as one sentence: name, what it does, and whether it is pinned.</summary>
    public string AutomationName =>
        string.Join(". ", new[] { Name, Shortcut?.Description, EffectText, Pinned ? Strings.Get("ShortcutTileViewModel_Pinned") : null }.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Worked out once per load of the shortcut, not per render.</summary>
    public ShortcutEffect Effect { get; private set; }
    public string EffectText { get; private set; } = "";
    public SymbolRegular EffectSymbol { get; private set; } = SymbolRegular.ShieldCheckmark16;
    public bool EffectCaution { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _alias;
    [ObservableProperty] private SymbolRegular _symbol = SymbolRegular.Flash24;
    /// <summary>The token resource key of the solid colour square (nearest design category colour to the shortcut's chosen colour).</summary>
    [ObservableProperty] private string _categoryKey = Palette.CategoryResourceKey("blue");
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
        CategoryKey = Palette.CategoryResourceKey(s.Color);
        Pinned = s.Pinned;
        Effect = ShortcutEffects.Of(s, _actions);
        var (textKey, symbol, caution) = EffectBadge(Effect);
        EffectText = Strings.Get(textKey);
        EffectSymbol = symbol;
        EffectCaution = caution;
        OnPropertyChanged(nameof(Effect));
        OnPropertyChanged(nameof(EffectText));
        OnPropertyChanged(nameof(EffectSymbol));
        OnPropertyChanged(nameof(EffectCaution));        OnPropertyChanged(nameof(AutomationName));
        Tooltip = BuildTooltip(s);
        OnPropertyChanged(nameof(Id));
    }

    private string BuildTooltip(Shortcut s)
    {
        var parts = new List<string> { s.Name, EffectText };
        if (!string.IsNullOrWhiteSpace(s.Description)) parts.Add(s.Description!);
        parts.Add(Strings.Get(ExplicitActions ? "ShortcutTileViewModel_RunHint" : "ShortcutTileViewModel_ClickToRun"));
        if (!string.IsNullOrWhiteSpace(s.Hotkey)) parts.Add("Hotkey: " + s.Hotkey);
        return string.Join("\n", parts);
    }
}
