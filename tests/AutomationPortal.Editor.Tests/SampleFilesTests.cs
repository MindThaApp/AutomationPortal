using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Editor.ViewModels;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class SampleFilesTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly List<EditorViewModel> _vms = new();

    public void Dispose() { foreach (var v in _vms) v.Detach(); _host.Dispose(); }

    private EditorViewModel NewVm(IReadOnlyList<string>? files)
    {
        var vm = new EditorViewModel(new EditorContext(_host.Services, ActionRegistry.CreateWithBuiltIns()), new Shortcut { Name = "New Shortcut" }, isNew: true)
        { SampleFiles = files };
        _vms.Add(vm);
        return vm;
    }

    [Fact]
    public void Sample_files_become_the_run_input_and_nothing_is_saved()
    {
        var a = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ap-sample-" + Guid.NewGuid().ToString("N") + ".jpg");
        var b = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ap-sample-" + Guid.NewGuid().ToString("N") + ".txt");
        var vm = NewVm(new[] { a, b });
        var input = vm.SampleInput;
        Assert.NotNull(input);
        Assert.Equal(2, input!.Count);
        Assert.Contains(a, input.AsText());
        Assert.Contains(b, input.AsText());
        Assert.Null(_host.Library.GetById(vm.Shortcut.Id));   // a new shortcut isn't in the library until it is changed
    }

    [Fact]
    public void Without_sample_files_the_input_stays_empty()
    {
        Assert.Null(NewVm(null).SampleInput);
        var vm = NewVm(new[] { @"C:\a.txt" });
        vm.SampleFiles = null;   // "Don't use them"
        Assert.Null(vm.SampleInput);
    }

    [Fact]
    public void Open_options_carry_the_files()
    {
        var o = new EditorOpenOptions(SampleFiles: new[] { @"C:\a.txt" }, SampleMissing: new[] { @"C:\a.txt" }, SampleFrom: "Files Portal");
        Assert.Equal(new[] { @"C:\a.txt" }, o.SampleFiles);
        Assert.Equal("Files Portal", o.SampleFrom);
        Assert.Null(new EditorOpenOptions().SampleFiles);
    }
}
