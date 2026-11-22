using AutomationPortal.Core;
using Microsoft.Win32;
using System.Windows;

namespace AutomationPortal.Editor;

/// <summary>The real Windows file, folder and save dialogs, used on a normal launch.</summary>
public sealed class SystemPicker : IPicker
{
    public IReadOnlyList<string>? Pick(PickRequest r)
    {
        var owner = r.Owner as Window;
        switch (r.Kind)
        {
            case PickKind.Folder:
            {
                var d = new OpenFolderDialog { Title = r.Title };
                if (r.InitialDirectory is { } dir) { d.InitialDirectory = dir; d.FolderName = dir; }
                return (owner is null ? d.ShowDialog() : d.ShowDialog(owner)) == true ? new[] { d.FolderName } : null;
            }
            case PickKind.SaveFile:
            {
                var d = new SaveFileDialog { Title = r.Title, FileName = r.SuggestedName ?? "", OverwritePrompt = true };
                if (r.Filter is { } f) d.Filter = f;
                if (r.InitialDirectory is { } dir) d.InitialDirectory = dir;
                return (owner is null ? d.ShowDialog() : d.ShowDialog(owner)) == true ? new[] { d.FileName } : null;
            }
            default:
            {
                var d = new OpenFileDialog { Title = r.Title, Multiselect = r.Kind == PickKind.OpenFiles, CheckFileExists = true };
                if (r.Filter is { } f) d.Filter = f;
                if (r.InitialDirectory is { } dir) d.InitialDirectory = dir;
                return (owner is null ? d.ShowDialog() : d.ShowDialog(owner)) == true ? d.FileNames : null;
            }
        }
    }
}
