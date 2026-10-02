using Flowmate.Core;

namespace Flowmate.Triggers;

/// <summary>Small helpers shared by the trigger and condition providers.</summary>
internal static class TP
{
    public const string CatTime = "Time", CatCalls = "Calls & Screen", CatWeb = "Web & Weather", CatSystem = "System", CatApps = "Apps",
        CatFiles = "Files & Drives", CatNetwork = "Network", CatPower = "Power", CatDevices = "Devices", CatKeyboard = "Keyboard", CatClipboard = "Clipboard";

    public static TriggerEnvironment Env => TriggerEnvironment.Current;

    public static readonly Choice[] DayChoices =
    {
        new("mon", "Monday"), new("tue", "Tuesday"), new("wed", "Wednesday"), new("thu", "Thursday"),
        new("fri", "Friday"), new("sat", "Saturday"), new("sun", "Sunday"),
    };

    public static ParamValue AllDays => ParamValue.List("mon", "tue", "wed", "thu", "fri", "sat", "sun");

    public static ParameterDescriptor DaysParam(string key = "days", string label = "On these days") =>
        new() { Key = key, Label = label, Type = ParamType.List, Choices = DayChoices, Default = AllDays, AllowVariables = false, AllowAskEachTime = false };

    public static IReadOnlyList<DayOfWeek> ParseDays(IReadOnlyList<string> ids)
    {
        var list = new List<DayOfWeek>();
        foreach (var id in ids)
        {
            var d = DayFromId(id);
            if (d is { } v && !list.Contains(v)) list.Add(v);
        }
        return list;
    }

    public static DayOfWeek? DayFromId(string id) => id.Trim().ToLowerInvariant() switch
    {
        "mon" or "monday" => DayOfWeek.Monday,
        "tue" or "tuesday" => DayOfWeek.Tuesday,
        "wed" or "wednesday" => DayOfWeek.Wednesday,
        "thu" or "thursday" => DayOfWeek.Thursday,
        "fri" or "friday" => DayOfWeek.Friday,
        "sat" or "saturday" => DayOfWeek.Saturday,
        "sun" or "sunday" => DayOfWeek.Sunday,
        _ => null,
    };

    public static string DaysText(IReadOnlyList<DayOfWeek> days)
    {
        if (days.Count == 7) return "every day";
        if (days.Count == 5 && !days.Contains(DayOfWeek.Saturday) && !days.Contains(DayOfWeek.Sunday)) return "weekdays";
        if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday)) return "weekends";
        return string.Join(", ", days.Select(d => d.ToString()[..3]));
    }

    public static ParameterDescriptor TimeParam(string key, string label, string def) =>
        new() { Key = key, Label = label, Type = ParamType.Time, Default = ParamValue.Time(def), AllowVariables = false, AllowAskEachTime = false };

    public static ParameterDescriptor AppParam(string key = "app", string label = "App", bool required = true) =>
        new() { Key = key, Label = label, Type = ParamType.App, Required = required, AllowVariables = false, AllowAskEachTime = false };

    public static ContentValue Now() => ContentValue.Date(DateTimeOffset.Now);

    public static string TimeText(TimeOnly t) => t.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Runs a poll loop until disposed. The first poll waits <paramref name="firstDelay"/> so startup stays quiet.</summary>
    public static IDisposable Poll(Func<CancellationToken, Task> poll, TimeSpan firstDelay, Func<TimeSpan> interval)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(firstDelay, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    try { await poll(cts.Token); }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
                    catch (Exception ex) { TriggerLog.Write("Poll failed: " + ex.Message); }
                    await Task.Delay(interval(), cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        return new DisposableAction(() => { cts.Cancel(); cts.Dispose(); });
    }

    public static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>Subscribes to a shared monitor and an event; disposing undoes both.</summary>
    public static IDisposable Watch(SharedMonitor monitor, Action subscribe, Action unsubscribe)
    {
        var sub = monitor.Subscribe();
        subscribe();
        return new DisposableAction(() => { unsubscribe(); sub.Dispose(); });
    }
}
