namespace AutomationPortal.App.Logic;

/// <summary>What the person has: the free version, Plus or Pro.</summary>
public enum Tier { Free, Plus, Pro }

/// <summary>Something the app can offer to sell.</summary>
public enum StoreOffer { Plus, Pro, ProUpgrade }

/// <summary>Microsoft Store ids of the durable add-ons.</summary>
public static class AddOnIds
{
    public const string Plus = "9NVRQ54GH9MG";
    public const string Pro = "9P2SZ6TN4TZ1";
    /// <summary>Sold only inside the app, to people who own Plus and not Pro. Plus plus this is Pro.</summary>
    public const string ProUpgrade = "9NSFZH0G10B8";

    public static string For(StoreOffer offer) => offer switch
    {
        StoreOffer.Plus => Plus,
        StoreOffer.Pro => Pro,
        _ => ProUpgrade,
    };

    /// <summary>
    /// True when a license key or SkuStoreId (like "9NVRQ54GH9MG/0010") belongs to the add-on with this Store id.
    /// Matches the id itself or the id followed by "/", never a longer id that merely starts the same way.
    /// </summary>
    public static bool Matches(string? licenseId, string storeId)
    {
        if (string.IsNullOrWhiteSpace(licenseId)) return false;
        var id = licenseId.Trim();
        return id.Equals(storeId, StringComparison.OrdinalIgnoreCase)
            || (id.Length > storeId.Length && id[storeId.Length] == '/' && id.StartsWith(storeId, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One add-on license as the Store reports it.</summary>
public sealed record AddOnLicense(string Id, bool IsActive);

/// <summary>Which of the three add-ons the person owns.</summary>
public sealed record Ownership(bool Plus, bool Pro, bool ProUpgrade)
{
    public static Ownership None { get; } = new(false, false, false);

    /// <summary>Only active licenses count. An id that isn't one of ours is ignored.</summary>
    public static Ownership FromLicenses(IEnumerable<AddOnLicense> licenses)
    {
        bool plus = false, pro = false, upgrade = false;
        foreach (var l in licenses)
        {
            if (l is null || !l.IsActive) continue;
            if (AddOnIds.Matches(l.Id, AddOnIds.Plus)) plus = true;
            else if (AddOnIds.Matches(l.Id, AddOnIds.Pro)) pro = true;
            else if (AddOnIds.Matches(l.Id, AddOnIds.ProUpgrade)) upgrade = true;
        }
        return new Ownership(plus, pro, upgrade);
    }

    /// <summary>From the base Store ids kept in the local cache.</summary>
    public static Ownership FromIds(IEnumerable<string>? ids) =>
        ids is null ? None : FromLicenses(ids.Select(i => new AddOnLicense(i, true)));

    public IReadOnlyList<string> ToIds()
    {
        var ids = new List<string>();
        if (Plus) ids.Add(AddOnIds.Plus);
        if (Pro) ids.Add(AddOnIds.Pro);
        if (ProUpgrade) ids.Add(AddOnIds.ProUpgrade);
        return ids;
    }

    public Ownership With(StoreOffer offer) => offer switch
    {
        StoreOffer.Plus => this with { Plus = true },
        StoreOffer.Pro => this with { Pro = true },
        _ => this with { ProUpgrade = true },
    };

    /// <summary>Everything either side owns.</summary>
    public Ownership Union(Ownership other) => new(Plus || other.Plus, Pro || other.Pro, ProUpgrade || other.ProUpgrade);

    /// <summary>
    /// Pro, or Plus with the upgrade, is Pro. Plus is Plus. The upgrade on its own (it should only ever be sold to Plus owners)
    /// still counts as Plus, so nobody who paid ends up with nothing.
    /// </summary>
    public Tier Tier => Pro || (Plus && ProUpgrade) ? Tier.Pro : Plus || ProUpgrade ? Tier.Plus : Tier.Free;

    /// <summary>
    /// What to offer: Free sees Plus and Pro; Plus sees only the upgrade, never full Pro; Pro sees nothing.
    /// Someone who somehow has only the upgrade is offered Plus, which completes Pro.
    /// </summary>
    public IReadOnlyList<StoreOffer> Offers => Tier switch
    {
        Tier.Pro => Array.Empty<StoreOffer>(),
        Tier.Plus when Plus => new[] { StoreOffer.ProUpgrade },
        Tier.Plus => new[] { StoreOffer.Plus },
        _ => new[] { StoreOffer.Plus, StoreOffer.Pro },
    };
}

/// <summary>What each tier allows. Limits only ever stop something new being made; nothing that exists is touched.</summary>
public static class TierRules
{
    public const int FreeShortcuts = 5;
    public const int PlusShortcuts = 25;

    /// <summary>Most shortcuts the tier lets the person make, or null for no limit.</summary>
    public static int? ShortcutCap(Tier tier) => tier switch
    {
        Tier.Free => FreeShortcuts,
        Tier.Plus => PlusShortcuts,
        _ => null,
    };

    /// <summary>Whether new automations (triggers, schedules) can be made. Automations that already exist keep running on every tier.</summary>
    public static bool AutomationsAllowed(Tier tier) => tier == Tier.Pro;

    /// <summary>How many more shortcuts fit. Never negative; int.MaxValue when there is no limit.</summary>
    public static int RoomFor(Tier tier, int existing) =>
        ShortcutCap(tier) is { } cap ? Math.Max(0, cap - Math.Max(0, existing)) : int.MaxValue;

    /// <summary>
    /// Whether <paramref name="adding"/> new shortcuts may be made when the person already has <paramref name="existing"/>.
    /// Someone over the cap (from an earlier version) keeps every shortcut; only new ones wait for an upgrade.
    /// </summary>
    public static bool CanAddShortcuts(Tier tier, int existing, int adding = 1) => adding <= 0 || RoomFor(tier, existing) >= adding;
}
