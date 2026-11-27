using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class UpdateNudgeTests
{
    private sealed class FakeProbe(StoreUpdateInfo? answer) : IStoreUpdateProbe
    {
        public int Calls;
        public Task<StoreUpdateInfo?> QueryAsync() { Calls++; return Task.FromResult(answer); }
    }

    private static (UpdateNudge Nudge, FakeProbe Probe, Func<string?> Saved) Make(bool packaged, StoreUpdateInfo? answer, string? dismissed = null)
    {
        var probe = new FakeProbe(answer);
        string? saved = dismissed;
        return (new UpdateNudge(packaged, probe, () => saved, v => saved = v), probe, () => saved);
    }

    [Fact]
    public async Task Update_found_shows_the_notice()
    {
        var (n, _, _) = Make(true, new StoreUpdateInfo(true, "1.2.0.0"));
        Assert.Equal("1.2.0.0", await n.CheckAsync());
        Assert.Equal("1.2.0.0", n.Visible);
    }

    [Fact]
    public async Task No_update_shows_nothing()
    {
        var (n, _, _) = Make(true, new StoreUpdateInfo(false, null));
        Assert.Null(await n.CheckAsync());
        Assert.Null(n.Visible);
    }

    [Fact]
    public async Task Failed_check_shows_nothing()
    {
        var (n, _, _) = Make(true, null);
        Assert.Null(await n.CheckAsync());
    }

    [Fact]
    public async Task Same_version_after_dismissal_stays_hidden()
    {
        var (n, _, _) = Make(true, new StoreUpdateInfo(true, "1.2.0.0"), dismissed: "1.2.0.0");
        Assert.Null(await n.CheckAsync());
    }

    [Fact]
    public async Task Newer_version_after_dismissal_shows_again()
    {
        var (n, _, _) = Make(true, new StoreUpdateInfo(true, "1.10.0.0"), dismissed: "1.9.0.0");
        Assert.Equal("1.10.0.0", await n.CheckAsync());
    }

    [Fact]
    public async Task Asks_the_store_once_per_session()
    {
        var (n, probe, _) = Make(true, new StoreUpdateInfo(true, "1.2.0.0"));
        await n.CheckAsync();
        Assert.Null(await n.CheckAsync());
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Unpackaged_build_never_asks()
    {
        var (n, probe, _) = Make(false, new StoreUpdateInfo(true, "1.2.0.0"));
        Assert.Null(await n.CheckAsync());
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Later_hides_and_remembers_the_version()
    {
        var (n, _, saved) = Make(true, new StoreUpdateInfo(true, "1.2.0.0"));
        await n.CheckAsync();
        n.Dismiss();
        Assert.Null(n.Visible);
        Assert.Equal("1.2.0.0", saved());
    }
}
