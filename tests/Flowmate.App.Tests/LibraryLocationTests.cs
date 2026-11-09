using System.IO;
using Flowmate.App.Pages;
using Xunit;

namespace Flowmate.App.Tests;

/// <summary>Changing the library location must never copy over files that are already in the chosen folder.</summary>
public sealed class LibraryLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-libloc-" + Guid.NewGuid().ToString("N"));
    private readonly string _current;

    public LibraryLocationTests()
    {
        _current = Path.Combine(_root, "Library");
        Directory.CreateDirectory(Path.Combine(_current, "shortcuts"));
        File.WriteAllText(Path.Combine(_current, "automations.json"), "[ours]");
        File.WriteAllText(Path.Combine(_current, "shortcuts", "a.json"), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void A_folder_with_other_files_is_refused()
    {
        var target = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "automations.json"), "someone else's file");
        Assert.Equal(SettingsPage.LibraryTarget.NotEmpty, SettingsPage.ClassifyLibraryTarget(_current, target));
    }

    [Fact]
    public void Empty_new_existing_inside_and_same_folders_are_told_apart()
    {
        var empty = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(empty);
        var existing = Path.Combine(_root, "Other library");
        Directory.CreateDirectory(Path.Combine(existing, "shortcuts"));

        Assert.Equal(SettingsPage.LibraryTarget.EmptyOrNew, SettingsPage.ClassifyLibraryTarget(_current, empty));
        Assert.Equal(SettingsPage.LibraryTarget.EmptyOrNew, SettingsPage.ClassifyLibraryTarget(_current, Path.Combine(_root, "New")));
        Assert.Equal(SettingsPage.LibraryTarget.ExistingLibrary, SettingsPage.ClassifyLibraryTarget(_current, existing));
        Assert.Equal(SettingsPage.LibraryTarget.InsideCurrent, SettingsPage.ClassifyLibraryTarget(_current, Path.Combine(_current, "sub")));
        Assert.Equal(SettingsPage.LibraryTarget.Same, SettingsPage.ClassifyLibraryTarget(_current, _current + "\\"));
    }

    [Fact]
    public void Copying_never_replaces_a_file_that_is_already_there()
    {
        var target = Path.Combine(_root, "Target");
        Directory.CreateDirectory(target);
        var theirs = Path.Combine(target, "automations.json");
        File.WriteAllText(theirs, "someone else's file");

        Assert.ThrowsAny<IOException>(() => SettingsPage.CopyDirectory(_current, target));
        Assert.Equal("someone else's file", File.ReadAllText(theirs));
    }

    [Fact]
    public void Copying_into_a_new_folder_copies_everything()
    {
        var target = Path.Combine(_root, "New");
        SettingsPage.CopyDirectory(_current, target);
        Assert.Equal("[ours]", File.ReadAllText(Path.Combine(target, "automations.json")));
        Assert.True(File.Exists(Path.Combine(target, "shortcuts", "a.json")));
        Assert.True(File.Exists(Path.Combine(_current, "automations.json")));
    }
}
