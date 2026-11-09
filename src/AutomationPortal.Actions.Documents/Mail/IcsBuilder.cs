using System.Globalization;
using System.Text;

namespace AutomationPortal.Actions.Documents;

/// <summary>Makes an iCalendar (.ics) file for one event, which any calendar app can open.</summary>
public static class IcsBuilder
{
    public static string Build(string title, DateTime start, DateTime end, bool allDay, string location, string notes,
        int alertMinutes, DateTime nowUtc, string? uid = null)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//AutomationPortal//Calendar Event//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{uid ?? Guid.NewGuid().ToString("N")}@automationportal",
            $"DTSTAMP:{Utc(nowUtc)}",
        };
        if (allDay)
        {
            lines.Add($"DTSTART;VALUE=DATE:{start:yyyyMMdd}");
            lines.Add($"DTEND;VALUE=DATE:{start.Date.AddDays(1):yyyyMMdd}");
        }
        else
        {
            lines.Add($"DTSTART:{Utc(ToUtc(start))}");
            lines.Add($"DTEND:{Utc(ToUtc(end))}");
        }
        lines.Add("SUMMARY:" + Escape(title));
        if (!string.IsNullOrWhiteSpace(location)) lines.Add("LOCATION:" + Escape(location));
        if (!string.IsNullOrWhiteSpace(notes)) lines.Add("DESCRIPTION:" + Escape(notes));
        if (alertMinutes > 0)
        {
            lines.Add("BEGIN:VALARM");
            lines.Add($"TRIGGER:-PT{alertMinutes}M");
            lines.Add("ACTION:DISPLAY");
            lines.Add("DESCRIPTION:" + Escape(title));
            lines.Add("END:VALARM");
        }
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.SelectMany(Fold)) + "\r\n";
    }

    private static DateTime ToUtc(DateTime local) =>
        local.Kind == DateTimeKind.Utc ? local : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);

    private static string Utc(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>Lines are limited to 75 bytes; longer ones continue on the next line starting with a space.</summary>
    public static IEnumerable<string> Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) { yield return line; yield break; }
        var sb = new StringBuilder();
        var bytes = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var len = rune.Utf8SequenceLength;
            if (bytes + len > limit)
            {
                yield return sb.ToString();
                sb.Clear();
                sb.Append(' ');
                bytes = 1;
                limit = 75;
            }
            sb.Append(rune.ToString());
            bytes += len;
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
