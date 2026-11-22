using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

/// <summary>A license read from the Store. <see cref="Succeeded"/> is false when the Store couldn't be asked or gave an unreliable answer.</summary>
public sealed record LicenseRead(bool Succeeded, IReadOnlyList<AddOnLicense> AddOns)
{
    public static LicenseRead Failed { get; } = new(false, Array.Empty<AddOnLicense>());
    public static LicenseRead Of(params AddOnLicense[] addOns) => new(true, addOns);
}

public enum PurchaseStatus { Succeeded, AlreadyPurchased, NotPurchased, NetworkError, ServerError, Failed }

/// <summary>The Microsoft Store, behind an interface so tests can fake it.</summary>
public interface IStoreLicensing
{
    /// <summary>Reads the app's add-on licenses. Never throws: trouble comes back as <see cref="LicenseRead.Failed"/>.</summary>
    Task<LicenseRead> ReadLicensesAsync();
    /// <summary>Formatted prices by base Store id, for the add-ons the Store returned. Never throws; empty when the Store can't be reached.</summary>
    Task<IReadOnlyDictionary<string, string>> GetPricesAsync(IReadOnlyList<string> storeIds);
    /// <summary>Shows the Store's purchase window over <paramref name="ownerHandle"/>. Never throws.</summary>
    Task<PurchaseStatus> PurchaseAsync(string storeId, IntPtr ownerHandle);
    /// <summary>The Store says licenses changed (a purchase, a refund, a sign-in). May be raised on any thread.</summary>
    event EventHandler? LicensesChanged;
}

/// <summary>Where the last known add-ons are kept, so a start without a network keeps the paid tier.</summary>
public interface IEntitlementCache
{
    /// <summary>Base Store ids last seen as owned, or null when the Store was never read.</summary>
    IReadOnlyList<string>? Load();
    void Save(IReadOnlyList<string> ownedStoreIds);
}

/// <summary>Keeps the cache in app-ui.json (the app's own settings file).</summary>
public sealed class UiSettingsEntitlementCache : IEntitlementCache
{
    private readonly LocalUiSettings _settings;
    public UiSettingsEntitlementCache(LocalUiSettings settings) { _settings = settings; }
    public IReadOnlyList<string>? Load() => _settings.Current.OwnedAddOns?.ToList();
    public void Save(IReadOnlyList<string> ownedStoreIds) => _settings.Update(d => d.OwnedAddOns = ownedStoreIds.ToList());
}

public enum RefreshOutcome { Unchanged, Changed, StoreUnavailable }

public sealed record PurchaseReply(PurchaseStatus Status, Tier Tier);

/// <summary>
/// The person's tier and what may be offered. Starts from the cached tier, then asks the Store. A Store error never lowers the tier;
/// only a successful read can, and only when a second read a little later agrees. Something bought in this session is never taken
/// away by a read that hasn't caught up yet. Builds without package identity (betas run from Explorer) are always Pro and sell nothing.
/// </summary>
public sealed class EntitlementService
{
    private readonly IStoreLicensing? _store;
    private readonly IEntitlementCache? _cache;
    private readonly ProTrial? _trial;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly object _gate = new();
    private Ownership _owned;
    private Ownership _boughtThisSession = Ownership.None;
    private Ownership? _saved;
    private bool _known;
    private IReadOnlyDictionary<string, string> _prices = new Dictionary<string, string>();

    /// <summary>How long to wait before the read that confirms a lower tier.</summary>
    public static TimeSpan ConfirmDelay { get; } = TimeSpan.FromSeconds(15);

    private EntitlementService(IStoreLicensing? store, IEntitlementCache? cache, Func<TimeSpan, Task>? delay, ProTrial? trial = null)
    {
        _store = store;
        _trial = trial;
        _cache = cache;
        _delay = delay ?? (t => Task.Delay(t));
        if (store is null) { _owned = new Ownership(false, true, false); _known = true; }
        else
        {
            var cached = SafeLoad(cache);
            _owned = Ownership.FromIds(cached);
            _saved = cached is null ? null : _owned;
            _known = cached is not null;
        }
        if (_store is not null) _store.LicensesChanged += (_, _) => _ = RefreshAsync();
    }

    /// <summary>A Store build: tier from the cache, then the Store.</summary>
    public static EntitlementService ForStore(IStoreLicensing store, IEntitlementCache cache, Func<TimeSpan, Task>? delay = null, ProTrial? trial = null) =>
        new(store, cache, delay, trial);

    /// <summary>A build without package identity: Pro, nothing for sale.</summary>
    public static EntitlementService Unpackaged() => new(null, null, null);

    /// <summary>Raised (any thread) when the tier or prices change.</summary>
    public event EventHandler? Changed;

    /// <summary>False for builds without package identity: no buy buttons, no Store calls.</summary>
    public bool SellsAddOns => _store is not null;

    public Ownership Owned { get { lock (_gate) return _owned.Union(_boughtThisSession); } }
    /// <summary>What was bought (or the trial gives). Bought Pro always wins; the trial only ever lifts the tier to Pro, and only while it runs.</summary>
    public Tier Tier => Owned.Tier == Tier.Pro || !TrialActive ? Owned.Tier : Tier.Pro;

    /// <summary>The in-app trial, or null in builds that sell nothing.</summary>
    public ProTrial? Trial => _trial;
    public bool TrialActive => _trial is { IsActive: true };
    /// <summary>Days left in a running trial, or null when there is none or Pro was bought (bought Pro wins).</summary>
    public int? TrialDaysLeft => Owned.Tier != Tier.Pro && _trial is { IsActive: true } t ? t.Status.DaysLeft : null;
    /// <summary>Days left for the footer chip: only in the last week.</summary>
    public int? TrialChipDays => TrialDaysLeft is { } d && d <= ProTrial.ChipFromDaysLeft ? d : null;
    /// <summary>The offer to start the trial: never started, nothing Pro bought, and the build sells add-ons.</summary>
    public bool CanStartTrial => OwnershipKnown && _trial is { CanStart: true } && Owned.Tier != Tier.Pro;

    /// <summary>True once what the person owns is known: from the cache, or from a successful Store read.</summary>
    public bool OwnershipKnown { get { lock (_gate) return _known; } }

    /// <summary>Starts the trial (the person pressed the button). False when it can't start. Raises Changed.</summary>
    public bool StartTrial()
    {
        if (!CanStartTrial || !_trial!.Start()) return false;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Tells listeners the trial moved on (a new day, the end).</summary>
    public void NotifyTrialChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public IReadOnlyList<StoreOffer> Offers => SellsAddOns ? Owned.Offers : Array.Empty<StoreOffer>();
    public int? ShortcutCap => TierRules.ShortcutCap(Tier);
    public bool CanAddShortcuts(int existing, int adding = 1) => TierRules.CanAddShortcuts(Tier, existing, adding);
    public int ShortcutRoom(int existing) => TierRules.RoomFor(Tier, existing);
    public bool CanAddAutomation => TierRules.AutomationsAllowed(Tier);

    /// <summary>The Store's formatted price for the offer, or null when it isn't known (then the button shows no price).</summary>
    public string? PriceOf(StoreOffer offer)
    {
        lock (_gate) return _prices.TryGetValue(AddOnIds.For(offer), out var p) && !string.IsNullOrWhiteSpace(p) ? p : null;
    }

    /// <summary>Fetches prices once they're needed (Settings, the upgrade window). Keeps the last ones on failure.</summary>
    public async Task LoadPricesAsync()
    {
        if (_store is null) return;
        IReadOnlyDictionary<string, string> prices;
        try { prices = await _store.GetPricesAsync(new[] { AddOnIds.Plus, AddOnIds.Pro, AddOnIds.ProUpgrade }).ConfigureAwait(false); }
        catch (Exception ex) { AppLog.Write("Store prices: " + ex.Message); return; }
        if (prices.Count == 0) return;
        lock (_gate) _prices = prices;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the Store (app start, licenses changed, Restore purchases).</summary>
    public async Task<RefreshOutcome> RefreshAsync()
    {
        if (_store is null) return RefreshOutcome.Unchanged;
        var first = await SafeRead().ConfigureAwait(false);
        if (!first.Succeeded) return RefreshOutcome.StoreUnavailable;

        var seen = Ownership.FromLicenses(first.AddOns);
        if (Loses(seen))
        {
            // A read that shows less than before has to be confirmed by a second one; an error on the second keeps everything.
            await _delay(ConfirmDelay).ConfigureAwait(false);
            var second = await SafeRead().ConfigureAwait(false);
            if (!second.Succeeded) return RefreshOutcome.StoreUnavailable;
            // Keep whatever either read showed: only something missing from both is gone.
            seen = seen.Union(Ownership.FromLicenses(second.AddOns));
        }
        return Apply(seen) ? RefreshOutcome.Changed : RefreshOutcome.Unchanged;
    }

    /// <summary>
    /// Buys an add-on. Succeeded and AlreadyPurchased give it straight away for this session (the Store's license list can lag),
    /// then the Store is read again.
    /// </summary>
    public async Task<PurchaseReply> BuyAsync(StoreOffer offer, IntPtr ownerHandle)
    {
        if (_store is null) return new PurchaseReply(PurchaseStatus.NotPurchased, Tier);
        PurchaseStatus status;
        try { status = await _store.PurchaseAsync(AddOnIds.For(offer), ownerHandle); }
        catch (Exception ex) { AppLog.Write("Store purchase: " + ex.Message); status = PurchaseStatus.Failed; }

        if (status is PurchaseStatus.Succeeded or PurchaseStatus.AlreadyPurchased)
        {
            bool changed;
            lock (_gate)
            {
                var before = _owned.Union(_boughtThisSession);
                _boughtThisSession = _boughtThisSession.With(offer);
                var now = _owned.Union(_boughtThisSession);
                changed = now != before;
                SaveIfNew(now);
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
            await RefreshAsync().ConfigureAwait(false);
        }
        return new PurchaseReply(status, Tier);
    }

    private bool Loses(Ownership seen)
    {
        lock (_gate)
        {
            var now = _owned;
            return (now.Plus && !seen.Plus) || (now.Pro && !seen.Pro) || (now.ProUpgrade && !seen.ProUpgrade);
        }
    }

    private bool Apply(Ownership seen)
    {
        bool changed;
        lock (_gate)
        {
            var before = _owned.Union(_boughtThisSession);
            _known = true;
            _owned = seen;
            var after = _owned.Union(_boughtThisSession);
            changed = after != before;
            SaveIfNew(after);
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private async Task<LicenseRead> SafeRead()
    {
        try { return await _store!.ReadLicensesAsync().ConfigureAwait(false) ?? LicenseRead.Failed; }
        catch (Exception ex) { AppLog.Write("Store licenses: " + ex.Message); return LicenseRead.Failed; }
    }

    private static IReadOnlyList<string>? SafeLoad(IEntitlementCache? cache)
    {
        try { return cache?.Load(); }
        catch (Exception ex) { AppLog.Write("Plan cache: " + ex.Message); return null; }
    }

    /// <summary>Writes the cache only when it would change (or was never written), so a normal start doesn't touch the file. Call under the gate.</summary>
    private void SaveIfNew(Ownership owned)
    {
        if (_cache is null || _saved == owned) return;
        try { _cache.Save(owned.ToIds()); _saved = owned; }
        catch (Exception ex) { AppLog.Write("Plan cache: " + ex.Message); }
    }
}
