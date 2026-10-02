using Flowmate.App.Logic;
using Flowmate.App.Pages;
using Flowmate.App.Services;
using Flowmate.Actions.Documents;
using Flowmate.Core;
using Xunit;

namespace Flowmate.App.Tests;

internal sealed class StubRecorder : IMacroRecorderSource
{
    public string StopHotkey => "Ctrl+Alt+Esc";
    public bool DropWaits { get; set; }
    public bool Started, Paused, FailToStart;
    public int PauseCalls, ResumeCalls, StopCalls;
    public IReadOnlyList<ActionInstance> Steps { get; set; } = new[] { ActionInstance.Create("input.click") };
    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;

    public void Start()
    {
        if (FailToStart) throw new InvalidOperationException("Windows wouldn't let Flow Mate watch the keyboard and mouse. Try again.");
        Started = true;
    }
    public void Pause() { Paused = true; PauseCalls++; }
    public void Resume() { Paused = false; ResumeCalls++; }
    public IReadOnlyList<ActionInstance> Stop() { StopCalls++; Started = false; Stopped?.Invoke(this, Steps); return Steps; }
    /// <summary>The stop shortcut was pressed (the recorder raises Stopped on its own).</summary>
    public void PressStopHotkey() { Started = false; Stopped?.Invoke(this, Steps); }
}

public class RecordingSessionTests
{
    private DateTime _now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private RecordingSession Make(StubRecorder r) => new(r, () => _now);

    [Fact]
    public void Start_starts_the_recorder_and_the_clock()
    {
        var r = new StubRecorder();
        var s = Make(r);
        Assert.True(s.Start());
        Assert.True(r.Started);
        _now += TimeSpan.FromSeconds(7);
        Assert.Equal("0:07", RecordingSession.FormatElapsed(s.Elapsed));
        Assert.Equal("Ctrl+Alt+Esc", s.StopHotkey);
    }

    [Fact]
    public void A_start_failure_gives_a_message_and_no_recording()
    {
        var r = new StubRecorder { FailToStart = true };
        var s = Make(r);
        Assert.False(s.Start());
        Assert.Contains("watch the keyboard", s.StartError);
        Assert.False(s.IsRunning);
    }

    [Fact]
    public void Paused_time_is_not_counted_and_pause_reaches_the_recorder()
    {
        var r = new StubRecorder();
        var s = Make(r);
        s.Start();
        _now += TimeSpan.FromSeconds(10);
        s.TogglePause();
        Assert.True(s.IsPaused); Assert.True(r.Paused);
        _now += TimeSpan.FromSeconds(60);
        Assert.Equal(TimeSpan.FromSeconds(10), s.Elapsed);
        s.TogglePause();
        Assert.False(s.IsPaused); Assert.Equal(1, r.ResumeCalls);
        _now += TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(15), s.Elapsed);
    }

    [Fact]
    public void Ignore_waits_is_forwarded_live()
    {
        var r = new StubRecorder();
        var s = Make(r);
        s.Start();
        s.DropWaits = true;
        Assert.True(r.DropWaits);
    }

    [Fact]
    public async Task Stop_button_finishes_with_the_steps_once()
    {
        var r = new StubRecorder();
        var s = Make(r);
        s.Start();
        s.Stop();
        s.Stop();
        var steps = await s.Finished;
        Assert.Single(steps);
        Assert.Equal(1, r.StopCalls);
        Assert.False(s.IsRunning);
    }

    [Fact]
    public async Task The_stop_shortcut_finishes_it_too()
    {
        var r = new StubRecorder();
        var s = Make(r);
        s.Start();
        r.PressStopHotkey();
        Assert.Single(await s.Finished);
        var at = s.Elapsed;
        _now += TimeSpan.FromSeconds(30);
        Assert.Equal(at, s.Elapsed);   // the clock stopped: it doesn't keep growing
    }

    [Fact]
    public async Task The_fake_recorder_records_fixed_steps_without_any_hooks()
    {
        var fake = new FakeRecorderSource();
        var s = new RecordingSession(fake);
        Assert.True(s.Start());
        s.Stop();
        var steps = await s.Finished;
        Assert.Equal(new[] { "app.switch", "input.click", "input.type" }, steps.Select(x => x.ActionId));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:59")]
    [InlineData(125, "2:05")]
    [InlineData(3723, "1:02:03")]
    public void Elapsed_is_formatted_like_a_stopwatch(int seconds, string text) =>
        Assert.Equal(text, RecordingSession.FormatElapsed(TimeSpan.FromSeconds(seconds)));
}

public class AiSettingsTextTests
{
    private static AiProviderInfo Info(string id) => AiProviderRegistry.Get(id)!;

    [Fact]
    public void Each_provider_row_asks_for_its_own_key_without_pointing_at_the_page_it_is_on()
    {
        var claude = AiSettingsPanel.StatusText(Info("claude"), new AiProviderSettings(), hasKey: false, endpoint: "", ready: false);
        Assert.Equal("Add your key to start using Claude.", claude);
        Assert.DoesNotContain("Settings", claude);

        var openai = AiSettingsPanel.StatusText(Info("openai"), new AiProviderSettings { Enabled = true }, false, "https://api.openai.com/v1", false);
        Assert.Equal("Add your key to start using ChatGPT.", openai);
        Assert.Equal("Turned off.", AiSettingsPanel.StatusText(Info("openai"), new AiProviderSettings(), false, "", false));
        Assert.Equal("Ready to use.", AiSettingsPanel.StatusText(Info("claude"), new AiProviderSettings(), true, "", true));
        Assert.Equal("Enter the address to start using Custom.",
            AiSettingsPanel.StatusText(Info("custom"), new AiProviderSettings { Enabled = true }, false, "", false));
    }
}
