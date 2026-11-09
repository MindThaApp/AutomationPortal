using System.Windows;
using AutomationPortal.Core;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor;

/// <summary>Things to do as soon as a new shortcut's editor opens.</summary>
/// <param name="Describe">A sentence to turn into an AI draft right away (the library's "Describe it").</param>
/// <param name="Record">Start recording clicks and typing right away ("Record a new shortcut").</param>
public sealed record EditorOpenOptions(string? Describe = null, bool Record = false);

/// <summary>Entry point: opens the shortcut editor window.</summary>
public static class ShortcutEditor
{
    /// <summary>
    /// Opens the editor for a shortcut in the library. If it is already open, that window is brought to the front.
    /// Returns null when no shortcut with this id exists. Call on the UI thread.
    /// </summary>
    public static Window? Open(Guid shortcutId, EditorContext context, Window? owner = null)
    {
        if (ShortcutEditorWindow.FindOpen(shortcutId) is { } existing) { Bring(existing); return existing; }
        var shortcut = context.Library.GetById(shortcutId);
        if (shortcut is null) return null;
        return Show(shortcut.Clone(), isNew: false, context, owner);
    }

    /// <summary>
    /// Opens the editor for a new, unsaved shortcut. It is saved to the library as soon as you change something.
    /// If the shortcut's id already exists in the library the existing one is edited instead.
    /// </summary>
    public static Window Open(Shortcut shortcut, EditorContext context, Window? owner = null, EditorOpenOptions? options = null)
    {
        if (ShortcutEditorWindow.FindOpen(shortcut.Id) is { } existing) { Bring(existing); return existing; }
        var inLibrary = context.Library.GetById(shortcut.Id) is not null;
        return Show(inLibrary ? context.Library.GetById(shortcut.Id)!.Clone() : shortcut, isNew: !inLibrary, context, owner, options);
    }

    private static Window Show(Shortcut working, bool isNew, EditorContext context, Window? owner, EditorOpenOptions? options = null)
    {
        EditorResources.Ensure();
        var vm = new EditorViewModel(context, working, isNew) { AutoDescribe = options?.Describe, StartRecording = options?.Record ?? false };
        var window = new ShortcutEditorWindow(vm);
        if (owner is { IsLoaded: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Register();
        window.Show();
        return window;
    }

    private static void Bring(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }
}
