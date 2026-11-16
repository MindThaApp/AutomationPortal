using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>
/// A folder picked in a preview is stamped with a summary of everything inside it, so a change deep inside counts as a change,
/// and a folder too big to sum up (or partly unreadable) never counts as unchanged.
/// </summary>
public sealed class PreviewPickStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-stamp-tests", Guid.NewGuid().ToString("N")[..8]);

    public PreviewPickStampTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private string Write(string relative, string text)
    {
        var p = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    private static void KeepFolderTime(string folder, Action change)
    {
        var before = Directory.GetLastWriteTimeUtc(folder);
        change();
        Directory.SetLastWriteTimeUtc(folder, before);   // the folder itself looks untouched
    }

    [Fact]
    public void An_unchanged_folder_matches()
    {
        Write("f/a.txt", "a");
        Write("f/sub/b.txt", "b");
        var folder = Path.Combine(_root, "f");
        var stamp = PreviewPicks.PathStamp.Of(folder);
        Assert.True(stamp.SameAs(PreviewPicks.PathStamp.Of(folder)));
        Assert.Equal(3, stamp.Entries);   // a.txt, sub, sub\b.txt
        Assert.Equal(2, stamp.Length);
    }

    [Fact]
    public void A_file_added_deep_inside_counts_as_a_change()
    {
        Write("f/sub/deeper/old.txt", "old");
        var folder = Path.Combine(_root, "f");
        var stamp = PreviewPicks.PathStamp.Of(folder);
        KeepFolderTime(folder, () => Write("f/sub/deeper/new.txt", "x"));
        Assert.False(stamp.SameAs(PreviewPicks.PathStamp.Of(folder)));
    }

    [Fact]
    public void A_file_edited_deep_inside_with_the_same_size_counts_as_a_change()
    {
        var old = Write("f/sub/deeper/old.txt", "old");
        var folder = Path.Combine(_root, "f");
        var stamp = PreviewPicks.PathStamp.Of(folder);
        KeepFolderTime(folder, () =>
        {
            File.WriteAllText(old, "new");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(1));
        });
        Assert.False(stamp.SameAs(PreviewPicks.PathStamp.Of(folder)));
    }

    [Fact]
    public void A_file_removed_deep_inside_counts_as_a_change()
    {
        Write("f/sub/a.txt", "a");
        var b = Write("f/sub/b.txt", "b");
        var folder = Path.Combine(_root, "f");
        var stamp = PreviewPicks.PathStamp.Of(folder);
        KeepFolderTime(folder, () => File.Delete(b));
        Assert.False(stamp.SameAs(PreviewPicks.PathStamp.Of(folder)));
    }

    [Fact]
    public void A_folder_with_more_inside_than_the_limit_never_counts_as_unchanged()
    {
        Write("f/a.txt", "a");
        Write("f/b.txt", "b");
        Write("f/c.txt", "c");
        var folder = Path.Combine(_root, "f");
        var stamp = PreviewPicks.PathStamp.Of(folder, maxEntries: 2);
        Assert.True(stamp.Unsure);
        Assert.False(stamp.SameAs(PreviewPicks.PathStamp.Of(folder, maxEntries: 2)));
        Assert.False(stamp.SameAs(stamp));
        Assert.True(PreviewPicks.PathStamp.Of(folder, maxEntries: 3).SameAs(PreviewPicks.PathStamp.Of(folder, maxEntries: 3)));
    }

    [Fact]
    public void A_picked_folder_that_changed_inside_is_listed_as_changed()
    {
        Write("f/sub/old.txt", "old");
        var folder = Path.Combine(_root, "f");
        var picks = new PreviewPicks(Guid.NewGuid(), "steps");
        picks.Record("k", ContentValue.Folder(folder));
        Assert.Empty(picks.ChangedItems());
        KeepFolderTime(folder, () => Write("f/sub/new.txt", "x"));
        Assert.Equal(new[] { folder }, picks.ChangedItems());
    }
}
