using System.Collections.ObjectModel;
using AutomationPortal.App.Infrastructure;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TileMotionTests
{
    private static object[] Make(int n) => Enumerable.Range(0, n).Select(i => (object)$"t{i}").ToArray();

    private static void SyncAndCheck(object[] start, object[] desired, out int removes, out int moves, out int adds)
    {
        var items = new ObservableCollection<object>(start);
        int r = 0, m = 0, a = 0;
        items.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove: r++; break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Move: m++; break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add: a++; break;
            }
        };
        TileMotion.Sync(items, desired);
        Assert.Equal(desired.Length, items.Count);
        for (var i = 0; i < desired.Length; i++) Assert.Same(desired[i], items[i]);
        removes = r; moves = m; adds = a;
    }

    [Fact]
    public void Delete_removes_only_that_tile()
    {
        var t = Make(300);
        SyncAndCheck(t, t.Where((_, i) => i != 100).ToArray(), out var r, out var m, out var a);
        Assert.Equal((1, 0, 0), (r, m, a));
    }

    [Fact]
    public void Restore_inserts_only_that_tile()
    {
        var t = Make(300);
        SyncAndCheck(t.Where((_, i) => i != 100).ToArray(), t, out var r, out var m, out var a);
        Assert.Equal((0, 0, 1), (r, m, a));
    }

    [Theory]
    [InlineData(5, 250)]
    [InlineData(250, 5)]
    [InlineData(0, 299)]
    [InlineData(299, 0)]
    public void Reorder_of_one_tile_moves_once(int from, int to)
    {
        var t = Make(300).ToList();
        var start = t.ToArray();
        var x = t[from]; t.RemoveAt(from); t.Insert(to, x);
        SyncAndCheck(start, t.ToArray(), out var r, out var m, out var a);
        Assert.Equal((0, 1, 0), (r, m, a));
    }

    [Fact]
    public void Any_shuffle_reaches_the_target()
    {
        var rnd = new Random(7);
        var t = Make(40);
        var shuffled = t.OrderBy(_ => rnd.Next()).Take(35).Append("new").ToArray();
        SyncAndCheck(t, shuffled, out _, out _, out _);
    }
}
