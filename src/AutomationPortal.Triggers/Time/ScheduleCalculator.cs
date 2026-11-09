namespace AutomationPortal.Triggers;

public enum MonthlyMode { DayOfMonth, NthWeekday, LastDay }

/// <summary>When a monthly trigger fires: "day 15", "first Monday" (Nth 1–4, 5 = last), or "the last day".</summary>
public sealed record MonthlyRule(MonthlyMode Mode, int Day = 1, int Nth = 1, DayOfWeek Weekday = DayOfWeek.Monday);

/// <summary>Daily window in which an interval trigger may fire. From &gt; To means overnight (22:00 to 06:00).</summary>
public sealed record ActiveHours(TimeOnly From, TimeOnly To)
{
    public bool Contains(TimeOnly t) => From <= To ? t >= From && t <= To : t >= From || t <= To;
}

/// <summary>Pure next-fire-time maths for the schedule triggers. All times are local clock times.</summary>
public static class ScheduleCalculator
{
    public static readonly IReadOnlyList<DayOfWeek> EveryDay = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    /// <summary>The first moment strictly after <paramref name="after"/> that is <paramref name="time"/> on one of <paramref name="days"/>.</summary>
    public static DateTime? NextDaily(DateTime after, TimeOnly time, IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0) return null;
        for (var i = 0; i <= 8; i++)
        {
            var date = after.Date.AddDays(i);
            if (!days.Contains(date.DayOfWeek)) continue;
            var candidate = AtLocal(date, time);
            if (candidate > after) return candidate;
        }
        return null;
    }

    public static DateTime? NextMonthly(DateTime after, MonthlyRule rule, TimeOnly time)
    {
        var month = new DateTime(after.Year, after.Month, 1);
        for (var i = 0; i < 26; i++, month = month.AddMonths(1))
        {
            var date = DateInMonth(month.Year, month.Month, rule);
            var candidate = AtLocal(date, time);
            if (candidate > after) return candidate;
        }
        return null;
    }

    public static DateTime DateInMonth(int year, int month, MonthlyRule rule)
    {
        var days = DateTime.DaysInMonth(year, month);
        switch (rule.Mode)
        {
            case MonthlyMode.LastDay:
                return new DateTime(year, month, days);
            case MonthlyMode.NthWeekday:
                if (rule.Nth >= 5)
                {
                    var last = new DateTime(year, month, days);
                    return last.AddDays(-(((int)last.DayOfWeek - (int)rule.Weekday + 7) % 7));
                }
                var first = new DateTime(year, month, 1);
                var offset = ((int)rule.Weekday - (int)first.DayOfWeek + 7) % 7;
                return first.AddDays(offset + 7 * (Math.Max(1, rule.Nth) - 1));
            default:
                return new DateTime(year, month, Math.Clamp(rule.Day, 1, days));
        }
    }

    /// <summary>The next run after <paramref name="previous"/> for "every N minutes", pushed to the next active-hours window when it falls outside.</summary>
    public static DateTime NextInterval(DateTime previous, TimeSpan interval, ActiveHours? hours)
    {
        if (interval < TimeSpan.FromMinutes(1)) interval = TimeSpan.FromMinutes(1);
        var candidate = previous + interval;
        if (hours is null || hours.Contains(TimeOnly.FromDateTime(candidate))) return candidate;

        var t = TimeOnly.FromDateTime(candidate);
        if (hours.From <= hours.To)
            return t < hours.From ? candidate.Date.Add(hours.From.ToTimeSpan()) : candidate.Date.AddDays(1).Add(hours.From.ToTimeSpan());
        // Overnight window: outside means between To and From on the same day.
        return candidate.Date.Add(hours.From.ToTimeSpan());
    }

    /// <summary>Local date + time of day, nudged past a daylight-saving gap (02:30 on the spring-forward night becomes 03:30).</summary>
    public static DateTime AtLocal(DateTime date, TimeOnly time)
    {
        var dt = date.Date + time.ToTimeSpan();
        return TimeZoneInfo.Local.IsInvalidTime(dt) ? dt.AddHours(1) : dt;
    }
}

/// <summary>
/// Fires a callback at clock times computed by <paramref name="next"/>. Re-checks the wall clock at least every 30 s, so it
/// survives sleep and clock changes. A time missed by up to <paramref name="grace"/> (the PC was asleep) still fires once.
/// </summary>
internal sealed class WallClockScheduler : IDisposable
{
    private readonly Func<DateTime, DateTime?> _next;
    private readonly Action<DateTime> _fire;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _grace;
    private readonly object _gate = new();
    private Timer? _timer;
    private DateTime? _due;
    private bool _disposed;

    public WallClockScheduler(Func<DateTime, DateTime?> next, Action<DateTime> fire, Func<DateTime>? now = null, TimeSpan? grace = null)
    {
        _next = next;
        _fire = fire;
        _now = now ?? (() => DateTime.Now);
        _grace = grace ?? TimeSpan.FromMinutes(10);
    }

    public DateTime? Due { get { lock (_gate) return _due; } }

    public void Start()
    {
        lock (_gate)
        {
            _due = _next(_now());
            _timer = new Timer(_ => TriggerLog.Guard(() => Tick(), "Schedule tick"), null, Delay(), Timeout.InfiniteTimeSpan);
        }
    }

    private TimeSpan Delay()
    {
        if (_due is null) return TimeSpan.FromMinutes(5);
        var d = _due.Value - _now();
        if (d < TimeSpan.FromMilliseconds(50)) d = TimeSpan.FromMilliseconds(50);
        return d > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : d;
    }

    /// <summary>One check of the clock. Public to the tests; returns true when it fired.</summary>
    public bool Tick()
    {
        DateTime? fireAt = null;
        lock (_gate)
        {
            if (_disposed) return false;
            var now = _now();
            if (_due is null) _due = _next(now);
            else if (now >= _due.Value)
            {
                if (now - _due.Value <= _grace) fireAt = _due.Value;
                _due = _next(now);
            }
        }
        var fired = false;
        if (fireAt is { } at)
        {
            try { _fire(at); fired = true; } catch (Exception ex) { TriggerLog.Write("Scheduled trigger failed: " + ex.Message); }
        }
        lock (_gate)
        {
            if (!_disposed) _timer?.Change(Delay(), Timeout.InfiniteTimeSpan);
        }
        return fired;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
