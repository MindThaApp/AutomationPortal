using System.Security;
using System.Text.Json;

namespace AutomationPortal.App.Logic;

/// <summary>One place the trial record is kept (a file, a registry value, memory in tests). Read returns null when nothing is stored.</summary>
public interface ITrialStorage
{
    string? Read();
    void Write(string text);
}

public enum TrialPhase
{
    /// <summary>Never started on this user. The only phase from which <see cref="ProTrial.Start"/> works.</summary>
    NotStarted,
    Active,
    /// <summary>Ended, or a trial was used before and its record can't be trusted. Never restartable.</summary>
    Expired,
    /// <summary>The record couldn't be read right now (locked file). No trial, and no new one can start.</summary>
    Unavailable,
}

public sealed record TrialStatus(TrialPhase Phase, int DaysLeft)
{
    public bool IsActive => Phase == TrialPhase.Active;
}

/// <summary>
/// The in-app 14-day Pro trial. The start time and the last time the app saw is kept in a file and a second copy in the registry, so
/// deleting one doesn't restart it. The countdown never runs backwards: it counts from the latest of the clock and the last-seen time,
/// so moving the clock back freezes it instead of extending it. A record that can't be understood counts as a used trial, never a fresh one.
/// Nothing here throws into the UI.
/// </summary>
public sealed class ProTrial
{
    public const int TrialDays = 14;
    /// <summary>The chip "Pro trial: N days left" shows from this many days left.</summary>
    public const int ChipFromDaysLeft = 7;
    /// <summary>The one "ends soon" notification is due from this many days left.</summary>
    public const int ToastFromDaysLeft = 1;
    public static readonly TimeSpan LastSeenGranularity = TimeSpan.FromHours(1);

    private sealed record Record(DateTimeOffset StartedUtc, DateTimeOffset LastSeenUtc, bool ToastShown, bool SummaryShown);

    private readonly object _gate = new();
    private readonly ITrialStorage _primary;
    private readonly ITrialStorage? _marker;
    private readonly TimeProvider _clock;
    private bool _loaded;
    private TrialPhase _phase;
    private Record? _record;
    private DateTimeOffset _floor = DateTimeOffset.MinValue;
    private DateTimeOffset _persistedSeen = DateTimeOffset.MinValue;

    /// <param name="primary">The record file in the app's data folder.</param>
    /// <param name="marker">The second copy (registry). Null when none is wanted (throwaway data roots, tests).</param>
    public ProTrial(ITrialStorage primary, ITrialStorage? marker = null, TimeProvider? clock = null)
    {
        _primary = primary;
        _marker = marker;
        _clock = clock ?? TimeProvider.System;
    }

    public TrialStatus Status { get { lock (_gate) return Evaluate(); } }

    public bool IsActive => Status.IsActive;

    /// <summary>True when no trial was ever started and the record can be read, so the offer may be shown.</summary>
    public bool CanStart => Status.Phase == TrialPhase.NotStarted;

    /// <summary>Days left to show in the chip, or null (not in a trial, or more than a week left).</summary>
    public int? ChipDays => Status is { IsActive: true } s && s.DaysLeft <= ChipFromDaysLeft ? s.DaysLeft : null;

    /// <summary>Starts the trial now. Only from NotStarted; false when it already ran or the record could not be saved.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_phase != TrialPhase.NotStarted) return false;
            var now = _clock.GetUtcNow();
            var record = new Record(now, now, false, false);
            if (!Save(record)) return false;
            _record = record;
            _phase = TrialPhase.Active;
            _floor = now;
            _persistedSeen = now;
            return true;
        }
    }

    /// <summary>Saves the last-seen time (at most hourly, and once when the end is first seen) so a later clock rollback can't revive the trial.</summary>
    public void Persist()
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_record is null || _phase is not (TrialPhase.Active or TrialPhase.Expired)) return;
            var seen = Observe();
            var end = _record.StartedUtc.AddDays(TrialDays);
            var due = seen < end ? seen - _persistedSeen >= LastSeenGranularity : _persistedSeen < end;
            if (due) Update(r => r with { LastSeenUtc = seen });
        }
    }

    /// <summary>True once, near the end: not yet shown and one day or less is left.</summary>
    public bool ToastDue { get { lock (_gate) { var s = Evaluate(); return s.IsActive && s.DaysLeft <= ToastFromDaysLeft && _record is { ToastShown: false }; } } }

    public void MarkToastShown() { lock (_gate) { EnsureLoaded(); if (_record is { ToastShown: false }) Update(r => r with { ToastShown = true }); } }

    /// <summary>True once, after the end: the summary of what stopped hasn't been shown. A record that couldn't be read never asks for one.</summary>
    public bool SummaryDue { get { lock (_gate) { var s = Evaluate(); return s.Phase == TrialPhase.Expired && _record is { SummaryShown: false }; } } }

    public void MarkSummaryShown() { lock (_gate) { EnsureLoaded(); if (_record is { SummaryShown: false }) Update(r => r with { SummaryShown = true }); } }

    /// <summary>Forgets what was read so the next call reads both copies again.</summary>
    public void Reload() { lock (_gate) _loaded = false; }

    private TrialStatus Evaluate()
    {
        EnsureLoaded();
        if (_phase is TrialPhase.NotStarted or TrialPhase.Unavailable) return new TrialStatus(_phase, 0);
        if (_record is null) return new TrialStatus(TrialPhase.Expired, 0);
        var remaining = _record.StartedUtc.AddDays(TrialDays) - Observe();
        if (remaining <= TimeSpan.Zero) return new TrialStatus(TrialPhase.Expired, 0);
        var days = (int)((remaining.Ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);
        return new TrialStatus(TrialPhase.Active, Math.Clamp(days, 1, TrialDays));
    }

    /// <summary>The latest of the clock, the stored last-seen time and anything seen in this run. Never goes backwards.</summary>
    private DateTimeOffset Observe()
    {
        var seen = _clock.GetUtcNow();
        if (_record is not null && _record.LastSeenUtc > seen) seen = _record.LastSeenUtc;
        if (_floor > seen) seen = _floor;
        _floor = seen;
        return seen;
    }

    private void Update(Func<Record, Record> change)
    {
        var updated = change(_record!) with { LastSeenUtc = Observe() };
        if (Save(updated)) _persistedSeen = updated.LastSeenUtc;
        _record = updated;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _record = null;

        var a = Read(_primary);
        var b = _marker is null ? new Copy(CopyKind.Missing, null) : Read(_marker);
        // The registry copy is a bonus: a locked-down HKCU must not block anything.
        if (b.Kind == CopyKind.Unreadable) b = new Copy(CopyKind.Missing, null);

        if (a.Kind == CopyKind.Unreadable && b.Record is null) { _phase = TrialPhase.Unavailable; return; }
        var merged = Merge(a.Record, b.Record);
        if (merged is null)
        {
            // Nothing usable. A copy that exists but can't be understood means a trial was used: never a fresh one.
            _phase = a.Kind == CopyKind.Invalid || b.Kind == CopyKind.Invalid ? TrialPhase.Expired : TrialPhase.NotStarted;
            return;
        }

        _record = merged;
        _phase = TrialPhase.Active;
        _persistedSeen = merged.LastSeenUtc;
        if (merged.LastSeenUtc > _floor) _floor = merged.LastSeenUtc;
        // Bring back a copy that was deleted, corrupted or fell behind, so losing one location never loses the trial.
        if (a.Record != merged || (_marker is not null && b.Record != merged)) Save(merged);
    }

    private static Record? Merge(Record? a, Record? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return new Record(a.StartedUtc < b.StartedUtc ? a.StartedUtc : b.StartedUtc,
            a.LastSeenUtc > b.LastSeenUtc ? a.LastSeenUtc : b.LastSeenUtc,
            a.ToastShown || b.ToastShown, a.SummaryShown || b.SummaryShown);
    }

    private enum CopyKind { Missing, Valid, Invalid, Unreadable }
    private sealed record Copy(CopyKind Kind, Record? Record);

    private static Copy Read(ITrialStorage storage)
    {
        string? text;
        try { text = storage.Read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return new Copy(CopyKind.Unreadable, null); }
        catch { return new Copy(CopyKind.Unreadable, null); }
        if (text is null) return new Copy(CopyKind.Missing, null);
        var record = Decode(text);
        return record is null ? new Copy(CopyKind.Invalid, null) : new Copy(CopyKind.Valid, record);
    }

    private bool Save(Record record)
    {
        var text = Encode(record);
        var saved = TryWrite(_primary, text);
        if (_marker is not null) saved |= TryWrite(_marker, text);
        return saved;
    }

    private static bool TryWrite(ITrialStorage storage, string text)
    {
        try { storage.Write(text); return true; }
        catch { return false; }
    }

    private sealed class Dto
    {
        public int V { get; set; }
        public string? Start { get; set; }
        public string? Seen { get; set; }
        public bool Toast { get; set; }
        public bool Summary { get; set; }
    }

    private static string Encode(Record r) => JsonSerializer.Serialize(new Dto
    {
        V = 1, Start = r.StartedUtc.UtcDateTime.ToString("O"), Seen = r.LastSeenUtc.UtcDateTime.ToString("O"), Toast = r.ToastShown, Summary = r.SummaryShown,
    });

    private static Record? Decode(string text)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null || dto.V != 1) return null;
            if (!DateTimeOffset.TryParse(dto.Start, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var start)) return null;
            if (!DateTimeOffset.TryParse(dto.Seen, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var seen)) return null;
            return new Record(start, seen < start ? start : seen, dto.Toast, dto.Summary);
        }
        catch (JsonException) { return null; }
    }
}
