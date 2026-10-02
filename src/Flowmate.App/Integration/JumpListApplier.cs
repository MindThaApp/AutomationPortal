using System.Windows;
using System.Windows.Shell;

namespace Flowmate.App.Integration;

/// <summary>Puts the pinned shortcuts on the taskbar Jump List ("Pinned shortcuts" group). Call on the UI thread.</summary>
public static class JumpListApplier
{
    public static void Apply(IReadOnlyList<JumpEntry> entries, string exePath)
    {
        var app = Application.Current;
        if (app is null) return;
        try
        {
            var list = JumpList.GetJumpList(app);
            if (list is null) { list = new JumpList(); JumpList.SetJumpList(app, list); }
            list.ShowRecentCategory = false;
            list.ShowFrequentCategory = false;
            list.JumpItems.Clear();
            foreach (var e in entries)
                list.JumpItems.Add(new JumpTask
                {
                    Title = e.Title, Description = e.Description, ApplicationPath = exePath, Arguments = e.Arguments,
                    IconResourcePath = e.IconPath, CustomCategory = "Pinned shortcuts",
                });
            list.Apply();
        }
        catch (Exception ex) { AppLog.Write("Jump List: " + ex.Message); }
    }
}
