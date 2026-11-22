using AutomationPortal.App.Logic;

namespace AutomationPortal.App.Services;

/// <summary>
/// Developer aid for test harnesses: <c>--simulate-plan free|trial:N|trial-ended|pro</c> makes an unpackaged build behave like a Store
/// build on that plan. Only honoured with a custom data root. No Store calls, no registry, and the only file written is trial.json
/// inside that data root.
/// </summary>
public static class SimulatedPlan
{
    public const string Switch = "--simulate-plan";

    private enum Mode { Free, Trial, TrialEnded, Pro }

    /// <summary>True when the value is one of the four forms.</summary>
    public static bool IsValid(string? value) => TryParse(value, out _, out _);

    private static bool TryParse(string? value, out Mode mode, out int days)
    {
        mode = Mode.Free; days = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();
        switch (v)
        {
            case "free": mode = Mode.Free; return true;
            case "pro": mode = Mode.Pro; return true;
            case "trial-ended": mode = Mode.TrialEnded; return true;
        }
        if (v.StartsWith("trial:", StringComparison.Ordinal)
            && int.TryParse(v.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out days)
            && days is >= 1 and <= ProTrial.TrialDays)
        {
            mode = Mode.Trial;
            return true;
        }
        return false;
    }

    /// <summary>The plan to simulate, or null (and a log line) when the switch is absent, invalid or there is no custom data root.</summary>
    public static EntitlementService? TryCreate(string? value, bool customDataRoot, string localRoot, TimeProvider? clock = null)
    {
        if (value is null) return null;
        if (!customDataRoot) { AppLog.Write("Plan simulation ignored: it needs --data-root."); return null; }
        if (!TryParse(value, out var mode, out var days)) { AppLog.Write("Plan simulation ignored: unknown value '" + value + "'."); return null; }

        ITrialStorage storage;
        if (mode is Mode.Trial or Mode.TrialEnded)
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            var start = mode == Mode.Trial ? now.AddDays(days - ProTrial.TrialDays).AddHours(-1) : now.AddDays(-(ProTrial.TrialDays + 1));
            storage = new FileTrialStorage(Path.Combine(localRoot, "trial.json"));
            var stamp = start.UtcDateTime.ToString("O");
            storage.Write($"{{\"V\":1,\"Start\":\"{stamp}\",\"Seen\":\"{stamp}\",\"Toast\":false,\"Summary\":false}}");
        }
        else storage = new MemoryTrialStorage();

        var trial = new ProTrial(storage, null, clock);
        AppLog.Write("Plan simulation: " + value);
        return EntitlementService.ForStore(new SimulatedLicensing(mode == Mode.Pro), new SimulatedCache(mode == Mode.Pro), trial: trial);
    }

    private sealed class MemoryTrialStorage : ITrialStorage
    {
        private string? _text;
        public string? Read() => _text;
        public void Write(string text) => _text = text;
    }

    private sealed class SimulatedLicensing(bool pro) : IStoreLicensing
    {
        public event EventHandler? LicensesChanged { add { } remove { } }
        public Task<LicenseRead> ReadLicensesAsync() =>
            Task.FromResult(pro ? LicenseRead.Of(new AddOnLicense(AddOnIds.Pro, true)) : LicenseRead.Of());
        public Task<IReadOnlyDictionary<string, string>> GetPricesAsync(IReadOnlyList<string> storeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<PurchaseStatus> PurchaseAsync(string storeId, IntPtr ownerHandle) => Task.FromResult(PurchaseStatus.Failed);
    }

    private sealed class SimulatedCache(bool pro) : IEntitlementCache
    {
        public IReadOnlyList<string>? Load() => pro ? new[] { AddOnIds.Pro } : Array.Empty<string>();
        public void Save(IReadOnlyList<string> ownedStoreIds) { }
    }
}

