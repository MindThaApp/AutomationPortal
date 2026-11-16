namespace AutomationPortal.Core.AppControl;

/// <summary>Finds the button a dialog's Enter key would press, so recording Enter can be replayed by pressing that button.</summary>
public static class UiDefaultButton
{
    private const int MaxNodes = 400;
    private const int MaxDepth = 8;

    public static IUiElement? Find(IUiElement window)
    {
        var queue = new Queue<(IUiElement El, int Depth)>();
        queue.Enqueue((window, 0));
        var seen = 0;
        while (queue.Count > 0 && seen++ < MaxNodes)
        {
            var (e, depth) = queue.Dequeue();
            try
            {
                if (e.IsDefaultButton && e.IsEnabled) return e;
                if (depth >= MaxDepth) continue;
                foreach (var c in e.Children) queue.Enqueue((c, depth + 1));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return null;
    }
}
