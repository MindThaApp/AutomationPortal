using System.IO;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Xunit;

namespace AutomationPortal.App.Tests;

public class EntitlementModelTests
{
    private static Ownership Of(params string[] ids) => Ownership.FromLicenses(ids.Select(i => new AddOnLicense(i, true)));

    [Fact]
    public void No_add_ons_is_free() => Assert.Equal(Tier.Free, Of().Tier);

    [Theory]
    [InlineData("9NVRQ54GH9MG/0010", Tier.Plus)]
    [InlineData("9NVRQ54GH9MG", Tier.Plus)]
    [InlineData("9nvrq54gh9mg/0010", Tier.Plus)]
    [InlineData("9P2SZ6TN4TZ1/0010", Tier.Pro)]
    [InlineData("9NSFZH0G10B8/0010", Tier.Plus)]   // the upgrade on its own still gives something
    public void A_single_license_maps_to_its_tier(string id, Tier expected) => Assert.Equal(expected, Of(id).Tier);

    [Fact]
    public void Plus_with_the_upgrade_is_pro() => Assert.Equal(Tier.Pro, Of("9NVRQ54GH9MG/0010", "9NSFZH0G10B8/0010").Tier);

    [Fact]
    public void Pro_with_anything_is_pro() => Assert.Equal(Tier.Pro, Of("9P2SZ6TN4TZ1/0010", "9NVRQ54GH9MG/0010").Tier);

    [Fact]
    public void Inactive_licenses_do_not_count()
    {
        var owned = Ownership.FromLicenses(new[] { new AddOnLicense("9P2SZ6TN4TZ1/0010", false), new AddOnLicense("9NVRQ54GH9MG/0010", true) });
        Assert.Equal(Tier.Plus, owned.Tier);
    }

    [Theory]
    [InlineData("9NVRQ54GH9MGX/0010")]   // a longer id that only starts the same
    [InlineData("9NVRQ54GH9M")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("XYZ/9NVRQ54GH9MG")]
    public void Other_ids_are_ignored(string? id) => Assert.Equal(Tier.Free, Ownership.FromLicenses(new[] { new AddOnLicense(id!, true) }).Tier);

    [Fact]
    public void Free_is_offered_plus_and_pro() => Assert.Equal(new[] { StoreOffer.Plus, StoreOffer.Pro }, Of().Offers);

    [Fact]
    public void Plus_is_offered_only_the_upgrade_never_full_pro() => Assert.Equal(new[] { StoreOffer.ProUpgrade }, Of(AddOnIds.Plus).Offers);

    [Fact]
    public void Pro_is_offered_nothing()
    {
        Assert.Empty(Of(AddOnIds.Pro).Offers);
        Assert.Empty(Of(AddOnIds.Plus, AddOnIds.ProUpgrade).Offers);
    }

    [Fact]
    public void Upgrade_alone_is_offered_plus_which_completes_pro()
    {
        var owned = Of(AddOnIds.ProUpgrade);
        Assert.Equal(new[] { StoreOffer.Plus }, owned.Offers);
        Assert.Equal(Tier.Pro, owned.With(StoreOffer.Plus).Tier);
    }

    [Fact]
    public void Ids_round_trip_through_the_cache_form()
    {
        var owned = Of(AddOnIds.Plus, AddOnIds.ProUpgrade);
        Assert.Equal(owned, Ownership.FromIds(owned.ToIds()));
        Assert.Equal(Ownership.None, Ownership.FromIds(null));
    }

    [Fact]
    public void Limits_per_tier()
    {
        Assert.Equal(5, TierRules.ShortcutCap(Tier.Free));
        Assert.Equal(25, TierRules.ShortcutCap(Tier.Plus));
        Assert.Null(TierRules.ShortcutCap(Tier.Pro));
        Assert.False(TierRules.AutomationsAllowed(Tier.Free));
        Assert.False(TierRules.AutomationsAllowed(Tier.Plus));
        Assert.True(TierRules.AutomationsAllowed(Tier.Pro));
    }

    [Theory]
    [InlineData(Tier.Free, 0, 1, true)]
    [InlineData(Tier.Free, 4, 1, true)]
    [InlineData(Tier.Free, 5, 1, false)]
    [InlineData(Tier.Free, 3, 2, true)]
    [InlineData(Tier.Free, 3, 3, false)]
    [InlineData(Tier.Free, 40, 1, false)]   // over the cap from an earlier version: nothing new, nothing taken away
    [InlineData(Tier.Free, 40, 0, true)]
    [InlineData(Tier.Plus, 24, 1, true)]
    [InlineData(Tier.Plus, 25, 1, false)]
    [InlineData(Tier.Pro, 10_000, 500, true)]
    public void Cap_check(Tier tier, int existing, int adding, bool allowed) => Assert.Equal(allowed, TierRules.CanAddShortcuts(tier, existing, adding));

    [Fact]
    public void Room_is_never_negative()
    {
        Assert.Equal(0, TierRules.RoomFor(Tier.Free, 12));
        Assert.Equal(2, TierRules.RoomFor(Tier.Free, 3));
        Assert.Equal(int.MaxValue, TierRules.RoomFor(Tier.Pro, 3));
    }
}

public class EntitlementServiceTests
{
    // Store trouble is logged; keep the log in the temp folder, away from the real one.
    public EntitlementServiceTests() => AppLog.FilePath = Path.Combine(Path.GetTempPath(), "AutomationPortal.App.Tests", "app.log");

    private sealed class FakeStore : IStoreLicensing
    {
        public Queue<LicenseRead> Reads { get; } = new();
        public LicenseRead Fallback { get; set; } = LicenseRead.Failed;
        public PurchaseStatus NextPurchase { get; set; } = PurchaseStatus.NotPurchased;
        public bool ThrowOnRead { get; set; }
        public int ReadCount { get; private set; }
        public List<string> Purchased { get; } = new();
        public Dictionary<string, string> Prices { get; } = new();

        public Task<LicenseRead> ReadLicensesAsync()
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("store down");
            return Task.FromResult(Reads.Count > 0 ? Reads.Dequeue() : Fallback);
        }

        public Task<IReadOnlyDictionary<string, string>> GetPricesAsync(IReadOnlyList<string> storeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Prices));

        public Task<PurchaseStatus> PurchaseAsync(string storeId, IntPtr ownerHandle)
        {
            Purchased.Add(storeId);
            return Task.FromResult(NextPurchase);
        }

        public event EventHandler? LicensesChanged;
        public void RaiseChanged() => LicensesChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MemoryCache : IEntitlementCache
    {
        public List<string>? Stored { get; set; }
        public int Saves { get; private set; }
        public IReadOnlyList<string>? Load() => Stored?.ToList();
        public void Save(IReadOnlyList<string> ids) { Stored = ids.ToList(); Saves++; }
    }

    private static readonly Func<TimeSpan, Task> NoWait = _ => Task.CompletedTask;
    private static AddOnLicense Lic(string id, bool active = true) => new(id + "/0010", active);

    [Fact]
    public void Unpackaged_is_pro_and_sells_nothing()
    {
        var plan = EntitlementService.Unpackaged();
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.False(plan.SellsAddOns);
        Assert.Empty(plan.Offers);
        Assert.True(plan.CanAddAutomation);
        Assert.True(plan.CanAddShortcuts(100_000));
    }

    [Fact]
    public async Task Unpackaged_never_calls_the_store()
    {
        var plan = EntitlementService.Unpackaged();
        Assert.Equal(RefreshOutcome.Unchanged, await plan.RefreshAsync());
        Assert.Equal(PurchaseStatus.NotPurchased, (await plan.BuyAsync(StoreOffer.Pro, IntPtr.Zero)).Status);
    }

    [Fact]
    public void Starts_free_with_no_cache()
    {
        var plan = EntitlementService.ForStore(new FakeStore(), new MemoryCache(), NoWait);
        Assert.Equal(Tier.Free, plan.Tier);
        Assert.Equal(new[] { StoreOffer.Plus, StoreOffer.Pro }, plan.Offers);
    }

    [Fact]
    public async Task An_offline_start_keeps_the_cached_paid_tier()
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Pro } };
        var store = new FakeStore { Fallback = LicenseRead.Failed };
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.Equal(RefreshOutcome.StoreUnavailable, await plan.RefreshAsync());
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.Equal(0, cache.Saves);
    }

    [Fact]
    public async Task A_store_exception_never_downgrades()
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Plus } };
        var plan = EntitlementService.ForStore(new FakeStore { ThrowOnRead = true }, cache, NoWait);
        Assert.Equal(RefreshOutcome.StoreUnavailable, await plan.RefreshAsync());
        Assert.Equal(Tier.Plus, plan.Tier);
        Assert.Equal(new List<string> { AddOnIds.Plus }, cache.Stored);
    }

    [Fact]
    public async Task A_successful_read_upgrades_and_is_cached()
    {
        var cache = new MemoryCache();
        var store = new FakeStore();
        store.Reads.Enqueue(LicenseRead.Of(Lic(AddOnIds.Plus)));
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        var changed = 0;
        plan.Changed += (_, _) => changed++;
        Assert.Equal(RefreshOutcome.Changed, await plan.RefreshAsync());
        Assert.Equal(Tier.Plus, plan.Tier);
        Assert.Equal(new List<string> { AddOnIds.Plus }, cache.Stored);
        Assert.Equal(1, changed);
        Assert.Equal(new[] { StoreOffer.ProUpgrade }, plan.Offers);
    }

    [Fact]
    public async Task A_lower_read_needs_a_second_successful_read_to_downgrade()
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Plus } };
        var store = new FakeStore();
        store.Reads.Enqueue(LicenseRead.Of());
        store.Reads.Enqueue(LicenseRead.Failed);
        var waited = TimeSpan.Zero;
        var plan = EntitlementService.ForStore(store, cache, t => { waited = t; return Task.CompletedTask; });
        Assert.Equal(RefreshOutcome.StoreUnavailable, await plan.RefreshAsync());
        Assert.Equal(Tier.Plus, plan.Tier);
        Assert.Equal(EntitlementService.ConfirmDelay, waited);

        store.Reads.Enqueue(LicenseRead.Of());
        store.Reads.Enqueue(LicenseRead.Of(Lic(AddOnIds.Plus)));   // the second read sees it again: nothing is lost
        Assert.Equal(RefreshOutcome.Unchanged, await plan.RefreshAsync());
        Assert.Equal(Tier.Plus, plan.Tier);

        store.Reads.Enqueue(LicenseRead.Of());
        store.Reads.Enqueue(LicenseRead.Of(Lic(AddOnIds.Plus, active: false)));   // two successful reads agree: refunded
        Assert.Equal(RefreshOutcome.Changed, await plan.RefreshAsync());
        Assert.Equal(Tier.Free, plan.Tier);
        Assert.Equal(new List<string>(), cache.Stored);
    }

    [Fact]
    public async Task An_unchanged_read_does_not_rewrite_the_cache()
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Pro } };
        var store = new FakeStore { Fallback = LicenseRead.Of(Lic(AddOnIds.Pro)) };
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        Assert.Equal(RefreshOutcome.Unchanged, await plan.RefreshAsync());
        Assert.Equal(0, cache.Saves);
    }

    [Fact]
    public async Task First_read_with_nothing_owned_is_cached_once()
    {
        var cache = new MemoryCache();
        var plan = EntitlementService.ForStore(new FakeStore { Fallback = LicenseRead.Of() }, cache, NoWait);
        await plan.RefreshAsync();
        await plan.RefreshAsync();
        Assert.Equal(1, cache.Saves);
        Assert.Equal(new List<string>(), cache.Stored);
    }

    [Theory]
    [InlineData(PurchaseStatus.Succeeded)]
    [InlineData(PurchaseStatus.AlreadyPurchased)]
    public async Task A_purchase_counts_at_once_even_when_the_store_lags(PurchaseStatus status)
    {
        var cache = new MemoryCache { Stored = new() };
        var store = new FakeStore { NextPurchase = status, Fallback = LicenseRead.Of() };   // licenses haven't caught up yet
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        var reply = await plan.BuyAsync(StoreOffer.Pro, IntPtr.Zero);
        Assert.Equal(status, reply.Status);
        Assert.Equal(Tier.Pro, reply.Tier);
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.Equal(new[] { AddOnIds.Pro }, store.Purchased);
        Assert.Contains(AddOnIds.Pro, cache.Stored!);

        await plan.RefreshAsync();   // still lagging: the purchase stays
        Assert.Equal(Tier.Pro, plan.Tier);
    }

    [Fact]
    public async Task The_upgrade_on_top_of_plus_gives_pro()
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Plus } };
        var store = new FakeStore { NextPurchase = PurchaseStatus.Succeeded, Fallback = LicenseRead.Of(Lic(AddOnIds.Plus), Lic(AddOnIds.ProUpgrade)) };
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        Assert.Equal(new[] { StoreOffer.ProUpgrade }, plan.Offers);
        await plan.BuyAsync(StoreOffer.ProUpgrade, IntPtr.Zero);
        Assert.Equal(new[] { AddOnIds.ProUpgrade }, store.Purchased);
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.Empty(plan.Offers);
    }

    [Theory]
    [InlineData(PurchaseStatus.NotPurchased)]
    [InlineData(PurchaseStatus.NetworkError)]
    [InlineData(PurchaseStatus.ServerError)]
    [InlineData(PurchaseStatus.Failed)]
    public async Task A_purchase_that_did_not_happen_changes_nothing(PurchaseStatus status)
    {
        var cache = new MemoryCache { Stored = new() { AddOnIds.Plus } };
        var store = new FakeStore { NextPurchase = status };
        var plan = EntitlementService.ForStore(store, cache, NoWait);
        var reply = await plan.BuyAsync(StoreOffer.ProUpgrade, IntPtr.Zero);
        Assert.Equal(status, reply.Status);
        Assert.Equal(Tier.Plus, plan.Tier);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, cache.Saves);
    }

    [Fact]
    public async Task Licenses_changed_triggers_a_read()
    {
        var store = new FakeStore { Fallback = LicenseRead.Of(Lic(AddOnIds.Pro)) };
        var plan = EntitlementService.ForStore(store, new MemoryCache(), NoWait);
        var tcs = new TaskCompletionSource();
        plan.Changed += (_, _) => tcs.TrySetResult();
        store.RaiseChanged();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Tier.Pro, plan.Tier);
    }

    [Fact]
    public async Task Prices_are_shown_when_known_and_absent_otherwise()
    {
        var store = new FakeStore();
        var plan = EntitlementService.ForStore(store, new MemoryCache(), NoWait);
        await plan.LoadPricesAsync();
        Assert.Null(plan.PriceOf(StoreOffer.Plus));
        store.Prices[AddOnIds.Plus] = "$6.99";
        await plan.LoadPricesAsync();
        Assert.Equal("$6.99", plan.PriceOf(StoreOffer.Plus));
        Assert.Null(plan.PriceOf(StoreOffer.Pro));
    }

    [Fact]
    public void A_broken_cache_starts_free_without_throwing()
    {
        var plan = EntitlementService.ForStore(new FakeStore(), new ThrowingCache(), NoWait);
        Assert.Equal(Tier.Free, plan.Tier);
    }

    private sealed class ThrowingCache : IEntitlementCache
    {
        public IReadOnlyList<string>? Load() => throw new IOException("locked");
        public void Save(IReadOnlyList<string> ids) => throw new IOException("locked");
    }
}
