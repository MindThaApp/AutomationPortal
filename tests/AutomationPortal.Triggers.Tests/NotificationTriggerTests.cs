using AutomationPortal.Core;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class NotificationTriggerTests
{
    private sealed class FakeSource : INotificationSource
    {
        public string? UnavailableReason { get; set; }
        public bool Allowed { get; set; } = true;
        public List<NotificationInfo> Current { get; } = new();
        public Task<bool> RequestAccessAsync() => Task.FromResult(Allowed);
        public Task<IReadOnlyList<NotificationInfo>> GetCurrentAsync() => Task.FromResult<IReadOnlyList<NotificationInfo>>(Current.ToList());
    }

    private sealed class Sink : ITriggerSink, ITriggerSinkExtras
    {
        public List<(ContentValue Output, string? Description)> Fired { get; } = new();
        public List<string?> Problems { get; } = new();
        public void Fire(ContentValue output, string? description = null) => Fired.Add((output, description));
        public void ReportProblem(string? message) => Problems.Add(message);
        public Task<ContentValue?> FireAndWaitAsync(ContentValue output, string? description) => Task.FromResult<ContentValue?>(null);
    }

    private static NotificationInfo N(uint id, string app = "Mail", string title = "Hello", string text = "World", string appId = "mail!app") => new(id, app, appId, title, text);

    private static NotificationMatcher Matcher(string app = "", string title = "", string text = "", bool own = false, double cooldown = 10) =>
        new(app, title, text, own, TimeSpan.FromSeconds(cooldown), NotificationArrivesTrigger.IsOwn);

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Existing_notifications_are_not_fired_on_the_first_look()
    {
        var m = Matcher();
        Assert.Empty(m.Process(new[] { N(1) }, T0));
        Assert.Single(m.Process(new[] { N(1), N(2, text: "new") }, T0.AddSeconds(2)));
    }

    [Fact]
    public void Filters_are_case_insensitive_contains_and_all_must_match()
    {
        var m = Matcher(app: "mai", title: "HEL", text: "orl");
        m.Process(Array.Empty<NotificationInfo>(), T0);
        var r = m.Process(new[] { N(1), N(2, app: "Chat"), N(3, title: "Bye"), N(4, text: "none") }, T0.AddSeconds(1));
        Assert.Equal(new uint[] { 1 }, r.Select(n => n.Id));
    }

    [Fact]
    public void The_same_text_fires_at_most_once_per_cooldown()
    {
        var m = Matcher();
        m.Process(Array.Empty<NotificationInfo>(), T0);
        Assert.Single(m.Process(new[] { N(1) }, T0.AddSeconds(1)));
        Assert.Empty(m.Process(new[] { N(1), N(2) }, T0.AddSeconds(5)));
        Assert.Single(m.Process(new[] { N(3) }, T0.AddSeconds(20)));
        Assert.Single(m.Process(new[] { N(3), N(4, text: "different") }, T0.AddSeconds(21)));
    }

    [Fact]
    public void Our_own_notifications_are_ignored_unless_asked_for()
    {
        var own = N(1, app: "Automation Portal", title: "Done");
        var m = Matcher();
        m.Process(Array.Empty<NotificationInfo>(), T0);
        Assert.Empty(m.Process(new[] { own }, T0.AddSeconds(1)));

        var m2 = Matcher(own: true);
        m2.Process(Array.Empty<NotificationInfo>(), T0);
        Assert.Single(m2.Process(new[] { own }, T0.AddSeconds(1)));
    }

    [Fact]
    public async Task Watcher_fires_title_text_and_app_and_reports_missing_permission()
    {
        var source = new FakeSource { Allowed = false };
        var sink = new Sink();
        var watcher = new NotificationWatcher(source, Matcher(), sink, () => T0);

        await watcher.PollOnceAsync();
        Assert.Empty(sink.Fired);
        Assert.NotNull(sink.Problems.Last());

        source.Allowed = true;
        await watcher.PollOnceAsync();
        source.Current.Add(N(7, "Calendar", "Standup", "In 5 minutes"));
        await watcher.PollOnceAsync();

        var fired = Assert.Single(sink.Fired);
        var dict = Assert.IsType<DictionaryItem>(fired.Output.Items.Single());
        Assert.Contains(dict.Entries, e => e.Key == "Title" && e.Value.AsText() == "Standup");
        Assert.Contains(dict.Entries, e => e.Key == "Text" && e.Value.AsText() == "In 5 minutes");
        Assert.Contains(dict.Entries, e => e.Key == "App" && e.Value.AsText() == "Calendar");
        Assert.Null(sink.Problems.Last());
    }

    [Fact]
    public void Unpackaged_start_reports_the_reason_and_watches_nothing()
    {
        var old = NotificationArrivesTrigger.SourceFactory;
        NotificationArrivesTrigger.SourceFactory = () => new FakeSource { UnavailableReason = WindowsNotificationSource.UnpackagedReason };
        try
        {
            var sink = new Sink();
            new NotificationArrivesTrigger().Start(TriggerConfig.Of(), sink).Dispose();
            Assert.Contains(sink.Problems, p => p!.Contains("Store version"));
        }
        finally { NotificationArrivesTrigger.SourceFactory = old; }
    }
}
