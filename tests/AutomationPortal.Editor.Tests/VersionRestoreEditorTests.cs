using System.Windows.Threading;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Editor;
using AutomationPortal.Editor.ViewModels;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class VersionRestoreEditorTests
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new System.Action(() => { }));

    private static (TestHost Host, Shortcut Saved, EditorViewModel Vm) Open()
    {
        var host = new TestHost();
        var s = new Shortcut { Name = "S" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "x")));
        host.Library.Save(s);
        var vm = new EditorViewModel(new EditorContext(host.Services, ActionRegistry.CreateWithBuiltIns()), host.Library.GetById(s.Id)!.Clone(), isNew: false);
        return (host, s, vm);
    }

    private static void RestoreInLibrary(TestHost host, Guid id)
    {
        var restored = host.Library.GetById(id)!.Clone();
        restored.Actions.Add(ActionInstance.Create("content.text", ("text", "restored")));
        restored.Description = "restored";
        host.Library.Save(restored);
        Pump();
    }

    [Fact]
    public void ReloadFromLibrary_WithUnsavedChanges_AsksAndKeepsThemWhenDeclined()
    {
        var (host, s, vm) = Open();
        using var _ = host;
        vm.AutosavePaused = true;
        vm.Shortcut.Description = "unsaved edit";
        vm.MetadataEdited();
        RestoreInLibrary(host, s.Id);

        var asked = 0;
        Assert.False(vm.ReloadFromLibrary(() => { asked++; return false; }));
        Assert.Equal(1, asked);
        Assert.Equal("unsaved edit", vm.Shortcut.Description);
        Assert.Equal("restored", host.Library.GetById(s.Id)!.Description);

        Assert.True(vm.ReloadFromLibrary(() => { asked++; return true; }));
        Assert.Equal(2, asked);
        Assert.Equal("restored", vm.Shortcut.Description);
        Assert.Equal(2, vm.Doc.Actions.Count);

        vm.AutosavePaused = false;
        Assert.True(vm.SaveNow());
        Assert.Equal("restored", host.Library.GetById(s.Id)!.Description);
        vm.Detach();
    }

    [Fact]
    public void ReloadFromLibrary_WithoutChanges_DoesNotAsk()
    {
        var (host, s, vm) = Open();
        using var _ = host;
        RestoreInLibrary(host, s.Id);

        Assert.True(vm.ReloadFromLibrary(() => throw new InvalidOperationException("should not ask")));
        Assert.Equal("restored", vm.Shortcut.Description);
        vm.Detach();
    }

    [Fact]
    public void AutosavePaused_StopsTheTimerFromSaving()
    {
        var (host, s, vm) = Open();
        using var _ = host;
        vm.AutosavePaused = true;
        vm.Shortcut.Description = "stale";
        vm.MetadataEdited();

        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        stop.Stop();

        Assert.NotEqual("stale", host.Library.GetById(s.Id)!.Description);
        vm.Detach();
    }
}
