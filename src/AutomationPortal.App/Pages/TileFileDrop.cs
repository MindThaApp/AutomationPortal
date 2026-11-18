using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AutomationPortal.App.Controls;
using AutomationPortal.App.Logic;
using AutomationPortal.App.ViewModels;

namespace AutomationPortal.App.Pages;

/// <summary>
/// Files dragged over a library tile: an outline on a tile that takes files, a "not allowed" cursor on one that doesn't, and on drop
/// the paths are handed on to start the run. The dropped originals are never opened, changed, moved or deleted here.
/// </summary>
internal sealed class TileFileDrop
{
    private readonly Action<ShortcutTileViewModel, IReadOnlyList<string>> _run;
    private Outline? _outline;

    public TileFileDrop(Action<ShortcutTileViewModel, IReadOnlyList<string>> run) => _run = run;

    /// <summary>True when the tile dealt with the drag; false leaves it to the library (shortcut files to add).</summary>
    public bool DragOver(object sender, DragEventArgs e)
    {
        if (sender is not ShortcutTile { Tile: { } tile } element) return false;
        var paths = DroppedFiles.Paths(e.Data);
        if (!DroppedFiles.IsForTile(paths)) { Clear(); return false; }
        e.Handled = true;
        if (DroppedFiles.TakesFiles(tile.Shortcut)) { e.Effects = Effect(e); Show(element); }
        else { e.Effects = DragDropEffects.None; Clear(); }
        return true;
    }

    public void DragLeave(object sender, DragEventArgs e)
    {
        // Moving between parts of the tile also raises DragLeave; only a real exit removes the outline.
        if (sender is UIElement element && new Rect(element.RenderSize).Contains(e.GetPosition(element))) return;
        Clear();
    }

    public bool Drop(object sender, DragEventArgs e)
    {
        Clear();
        if (sender is not ShortcutTile { Tile: { } tile } element) return false;
        var paths = DroppedFiles.Paths(e.Data);
        if (!DroppedFiles.IsForTile(paths)) return false;
        e.Handled = true;
        if (!DroppedFiles.TakesFiles(tile.Shortcut)) { e.Effects = DragDropEffects.None; return true; }
        e.Effects = Effect(e);
        // The app the files came from (Explorer) waits until Drop returns, so the run, and any check it shows first, starts after.
        element.Dispatcher.BeginInvoke(() => _run(tile, paths));
        return true;
    }

    // Never Move: a drag source may take a Move result as leave to delete its originals.
    private static DragDropEffects Effect(DragEventArgs e) =>
        e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private void Show(UIElement element)
    {
        if (_outline?.AdornedElement == element) return;
        Clear();
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer) return;
        _outline = new Outline(element);
        layer.Add(_outline);
    }

    public void Clear()
    {
        if (_outline is null) return;
        AdornerLayer.GetAdornerLayer(_outline.AdornedElement)?.Remove(_outline);
        _outline = null;
    }

    private sealed class Outline : Adorner
    {
        public Outline(UIElement element) : base(element) => IsHitTestVisible = false;

        protected override void OnRender(DrawingContext dc)
        {
            var accent = (AdornedElement as FrameworkElement)?.TryFindResource("AccentFillColorDefaultBrush") as Brush ?? SystemColors.HighlightBrush;
            // The tile's face sits 6 px inside the control and has 12 px corners.
            var face = new Rect(AdornedElement.RenderSize);
            face.Inflate(-6, -6);
            if (face.Width <= 0 || face.Height <= 0) return;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), null, face, 12, 12);
            var ring = face;
            ring.Inflate(2.5, 2.5);
            dc.DrawRoundedRectangle(null, new Pen(accent, 3), ring, 14.5, 14.5);
        }
    }
}
