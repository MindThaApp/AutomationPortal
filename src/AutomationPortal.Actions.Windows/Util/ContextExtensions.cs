namespace AutomationPortal.Actions.Windows;

internal static class ContextExtensions
{
    /// <summary>Reads an App parameter. Accepts an app, a program file, a window, or plain text (a name or program file name).</summary>
    public static async Task<AppItem?> GetAppAsync(this ActionContext ctx, string key = "app")
    {
        var c = await ctx.GetContentAsync(key).ConfigureAwait(false);
        if (c.IsEmpty) return null;
        switch (c[0])
        {
            case AppItem a: return a;
            case WindowItem w: return new AppItem(w.AppName, w.AppName);
            case PathItem p: return new AppItem(p.Path, p.FullName, p.Path);
            default:
                var t = c.AsText().Trim().Trim('"');
                return t.Length == 0 ? null : new AppItem(t, Path.GetFileNameWithoutExtension(t));
        }
    }

    public static async Task<AppItem> RequireAppAsync(this ActionContext ctx, string key = "app") =>
        await ctx.GetAppAsync(key).ConfigureAwait(false) ?? throw new ActionException("Choose an app first.");

    public static async Task<string> GetTextOrNullAsync(this ActionContext ctx, string key, string fallback)
    {
        var t = await ctx.GetTextAsync(key, fallback).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(t) ? fallback : t;
    }
}
