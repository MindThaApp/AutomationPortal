using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace Flowmate.Actions.Documents;

public sealed record OutlookEvent(string Title, DateTime Start, DateTime End, string Location, string Notes, bool AllDay);
public sealed record MailDraft(string To, string Cc, string Subject, string Body, IReadOnlyList<string> Attachments);

/// <summary>
/// Late-bound access to classic Outlook (the "Outlook.Application" COM server). The new Outlook for Windows has no COM
/// interface, so these are only available when classic Outlook is installed. Always call on <see cref="StaWorker.Com"/>.
/// </summary>
public static class OutlookBridge
{
    public const string NotInstalled = "Classic Outlook isn't installed on this PC.";
    public const string NotResponding = "Outlook didn't respond. Open classic Outlook once, make sure an account is set up, then try again.";

    public static bool IsAvailable()
    {
        try { return Type.GetTypeFromProgID("Outlook.Application") is not null; }
        catch { return false; }
    }

    private static dynamic CreateApp()
    {
        var type = Type.GetTypeFromProgID("Outlook.Application") ?? throw new ActionException(NotInstalled);
        return Activator.CreateInstance(type) ?? throw new ActionException(NotInstalled);
    }

    private static T Guard<T>(Func<T> work)
    {
        try { return work(); }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException)
        {
            throw new ActionException(NotResponding, ex);
        }
    }

    public static void AddEvent(string title, DateTime start, DateTime end, bool allDay, string location, string notes, int alertMinutes) =>
        Guard<object?>(() =>
        {
            dynamic app = CreateApp();
            dynamic item = app.CreateItem(1); // olAppointmentItem
            item.Subject = title;
            if (allDay) { item.AllDayEvent = true; item.Start = start.Date; item.End = start.Date.AddDays(1); }
            else { item.Start = start; item.End = end; }
            item.Location = location;
            item.Body = notes;
            if (alertMinutes > 0) { item.ReminderSet = true; item.ReminderMinutesBeforeStart = alertMinutes; }
            else item.ReminderSet = false;
            item.Save();
            return null;
        });

    public static IReadOnlyList<OutlookEvent> GetEvents(DateTime from, DateTime to, int max) =>
        Guard<IReadOnlyList<OutlookEvent>>(() =>
        {
            dynamic app = CreateApp();
            dynamic session = app.GetNamespace("MAPI");
            dynamic folder = session.GetDefaultFolder(9); // olFolderCalendar
            dynamic items = folder.Items;
            items.IncludeRecurrences = true;
            items.Sort("[Start]");
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            string filter = $"[Start] <= '{to.ToString("g", culture)}' AND [End] >= '{from.ToString("g", culture)}'";
            dynamic restricted = items.Restrict(filter);
            var list = new List<OutlookEvent>();
            foreach (var o in (System.Collections.IEnumerable)restricted)
            {
                dynamic it = o;
                string body = it.Body ?? "";
                list.Add(new OutlookEvent((string)(it.Subject ?? ""), (DateTime)it.Start, (DateTime)it.End, (string)(it.Location ?? ""),
                    body.Length > 2000 ? body[..2000] : body, (bool)it.AllDayEvent));
                if (list.Count >= max) break;
            }
            return list;
        });

    public static void ComposeMail(MailDraft draft, bool send) =>
        Guard<object?>(() =>
        {
            dynamic app = CreateApp();
            dynamic mail = app.CreateItem(0); // olMailItem
            mail.To = draft.To;
            if (draft.Cc.Length > 0) mail.CC = draft.Cc;
            mail.Subject = draft.Subject;
            mail.Body = draft.Body;
            foreach (var path in draft.Attachments) mail.Attachments.Add(path);
            if (send) mail.Send(); else mail.Display(false);
            return null;
        });
}
