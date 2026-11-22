using System.IO;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Xunit;

namespace AutomationPortal.App.Tests;

public class ProTrialTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Memory : ITrialStorage
    {
        public string? Text { get; set; }
        public bool ThrowOnRead { get; set; }
        public string? Read() => ThrowOnRead ? throw new IOException("locked") : Text;
        public void Write(string text) => Text = text;
    }

    private sealed class Store : IStoreLicensing
    {
        public LicenseRead Read { get; set; } = LicenseRead.Of();
        public Task<LicenseRead> ReadLicensesAsync() => Task.FromResult(Read);
        public Task<IReadOnlyDictionary<string, string>> GetPricesAsync(IReadOnlyList<string> ids) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<PurchaseStatus> PurchaseAsync(string storeId, IntPtr ownerHandle) => Task.FromResult(PurchaseStatus.NotPurchased);
        public event EventHandler? LicensesChanged { add { } remove { } }
    }

    private sealed class Cache : IEntitlementCache
    {
        public List<string>? Stored { get; set; }
        public IReadOnlyList<string>? Load() => Stored;
        public void Save(IReadOnlyList<string> ids) => Stored = ids.ToList();
    }

    private static readonly DateTimeOffset T0 = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    private static (ProTrial Trial, Clock Clock, Memory File, Memory Marker) Make()
    {
        var clock = new Clock(T0);
        var file = new Memory();
        var marker = new Memory();
        return (new ProTrial(file, marker, clock), clock, file, marker);
    }

    private static ProTrial Reopen(Memory file, Memory marker, Clock clock) => new(file, marker, clock);

    [Fact]
    public void Not_started_is_not_a_trial_and_never_starts_by_itself()
    {
        var (t, clock, file, marker) = Make();
        clock.Now = T0.AddDays(30);
        t.Persist();
        Assert.Equal(TrialPhase.NotStarted, t.Status.Phase);
        Assert.True(t.CanStart);
        Assert.False(t.IsActive);
        Assert.Null(file.Text);
        Assert.Null(marker.Text);
    }

    [Fact]
    public void Starting_gives_14_days_and_writes_both_copies()
    {
        var (t, _, file, marker) = Make();
        Assert.True(t.Start());
        Assert.Equal(new TrialStatus(TrialPhase.Active, 14), t.Status);
        Assert.NotNull(file.Text);
        Assert.Equal(file.Text, marker.Text);
    }

    [Fact]
    public void Days_left_is_the_ceiling_and_never_negative()
    {
        var (t, clock, _, _) = Make();
        t.Start();
        clock.Now = T0.AddHours(1);
        Assert.Equal(14, t.Status.DaysLeft);
        clock.Now = T0.AddDays(13).AddHours(1);
        Assert.Equal(1, t.Status.DaysLeft);
        clock.Now = T0.AddDays(14);
        Assert.Equal(new TrialStatus(TrialPhase.Expired, 0), t.Status);
        clock.Now = T0.AddDays(40);
        Assert.Equal(0, t.Status.DaysLeft);
    }

    [Fact]
    public void The_chip_shows_only_in_the_last_seven_days()
    {
        var (t, clock, _, _) = Make();
        t.Start();
        clock.Now = T0.AddDays(6).AddHours(23);   // 7 days and 1 hour left: 8 days
        Assert.Equal(8, t.Status.DaysLeft);
        Assert.Null(t.ChipDays);
        clock.Now = T0.AddDays(7);                // exactly 7 days left
        Assert.Equal(7, t.ChipDays);
        clock.Now = T0.AddDays(13).AddHours(1);
        Assert.Equal(1, t.ChipDays);
        clock.Now = T0.AddDays(15);
        Assert.Null(t.ChipDays);
    }

    [Fact]
    public void The_toast_is_due_once_in_the_last_day()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(12);
        Assert.False(t.ToastDue);
        clock.Now = T0.AddDays(13).AddHours(2);
        Assert.True(t.ToastDue);
        t.MarkToastShown();
        Assert.False(t.ToastDue);
        Assert.False(Reopen(file, marker, clock).ToastDue);
    }

    [Fact]
    public void The_summary_is_due_once_after_the_end()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        Assert.False(t.SummaryDue);
        clock.Now = T0.AddDays(14).AddMinutes(1);
        Assert.True(t.SummaryDue);
        t.MarkSummaryShown();
        Assert.False(t.SummaryDue);
        Assert.False(Reopen(file, marker, clock).SummaryDue);
    }

    [Fact]
    public void Moving_the_clock_back_does_not_extend_the_trial()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(10);
        t.Persist();
        Assert.Equal(4, t.Status.DaysLeft);
        clock.Now = T0.AddDays(2);
        Assert.Equal(4, t.Status.DaysLeft);
        clock.Now = T0.AddHours(-12);   // within a day of the start: still the same countdown
        Assert.Equal(4, Reopen(file, marker, clock).Status.DaysLeft);
        clock.Now = T0.AddDays(-5);     // before the start by more than a day: the record cannot be real, so it is used, never extended
        Assert.Equal(TrialPhase.Expired, Reopen(file, marker, clock).Status.Phase);
        clock.Now = T0.AddDays(15);
        Assert.Equal(TrialPhase.Expired, Reopen(file, marker, clock).Status.Phase);
    }

    [Fact]
    public void A_clock_set_back_after_the_end_does_not_revive_it()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(20);
        Assert.Equal(TrialPhase.Expired, t.Status.Phase);
        t.Persist();
        clock.Now = T0.AddDays(1);
        Assert.Equal(TrialPhase.Expired, Reopen(file, marker, clock).Status.Phase);
    }

    [Fact]
    public void A_far_future_jump_expires_the_trial()
    {
        var (t, clock, _, _) = Make();
        t.Start();
        clock.Now = T0.AddYears(5);
        Assert.Equal(TrialPhase.Expired, t.Status.Phase);
    }

    [Fact]
    public void Deleting_the_file_or_the_marker_does_not_restart_it()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(5);
        file.Text = null;
        var again = Reopen(file, marker, clock);
        Assert.Equal(9, again.Status.DaysLeft);
        Assert.False(again.CanStart);
        Assert.NotNull(file.Text);   // brought back from the marker
        marker.Text = null;
        var third = Reopen(file, marker, clock);
        Assert.Equal(9, third.Status.DaysLeft);
        Assert.NotNull(marker.Text);
    }

    [Fact]
    public void A_corrupt_file_with_the_marker_present_is_never_a_fresh_trial()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(3);
        file.Text = "{ not json";
        var again = Reopen(file, marker, clock);
        Assert.False(again.CanStart);
        Assert.Equal(11, again.Status.DaysLeft);
        Assert.False(again.Start());
    }

    [Fact]
    public void A_corrupt_record_and_no_other_copy_counts_as_used()
    {
        var (_, clock, file, marker) = Make();
        file.Text = "garbage";
        var t = Reopen(file, marker, clock);
        Assert.Equal(TrialPhase.Expired, t.Status.Phase);
        Assert.False(t.CanStart);
        Assert.False(t.Start());
        Assert.False(t.SummaryDue);
        Assert.Equal("garbage", file.Text);   // the unreadable file is left alone

        var (_, clock2, file2, marker2) = Make();
        marker2.Text = "garbage";
        var t2 = Reopen(file2, marker2, clock2);
        Assert.Equal(TrialPhase.Expired, t2.Status.Phase);
        Assert.False(t2.Start());
    }

    [Fact]
    public void Restarting_after_the_end_is_refused()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(30);
        Assert.False(t.Start());
        Assert.False(Reopen(file, marker, clock).Start());
        file.Text = null;
        marker.Text = null;
        // Every copy removed: the honest-user stance accepts a new trial only when nothing at all remains.
        Assert.True(Reopen(file, marker, clock).CanStart);
    }

    [Fact]
    public void A_locked_record_never_throws_and_never_starts_a_trial()
    {
        var (_, clock, file, marker) = Make();
        file.ThrowOnRead = true;
        var t = Reopen(file, marker, clock);
        Assert.Equal(TrialPhase.Unavailable, t.Status.Phase);
        Assert.False(t.CanStart);
        Assert.False(t.Start());
        t.Persist();
        t.MarkToastShown();
        t.MarkSummaryShown();
    }

    [Fact]
    public void A_broken_marker_does_not_block_the_trial()
    {
        var (_, clock, file, marker) = Make();
        marker.ThrowOnRead = true;
        var t = Reopen(file, marker, clock);
        Assert.True(t.Start());
        Assert.Equal(14, t.Status.DaysLeft);
    }

    // ---- with the plan ----

    private static EntitlementService Plan(ProTrial trial, params string[] owned) =>
        EntitlementService.ForStore(new Store(), new Cache { Stored = owned.ToList() }, _ => Task.CompletedTask, trial);

    [Fact]
    public void The_trial_gives_pro_and_only_after_the_button()
    {
        var (t, clock, _, _) = Make();
        var plan = Plan(t);
        Assert.Equal(Tier.Free, plan.Tier);
        Assert.True(plan.CanStartTrial);
        Assert.False(plan.CanAddAutomation);
        var changed = 0;
        plan.Changed += (_, _) => changed++;
        Assert.True(plan.StartTrial());
        Assert.Equal(1, changed);
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.True(plan.CanAddAutomation);
        Assert.True(plan.CanAddShortcuts(500));
        Assert.False(plan.CanStartTrial);
        Assert.Equal(14, plan.TrialDaysLeft);
        Assert.Null(plan.TrialChipDays);
        clock.Now = T0.AddDays(8);
        Assert.Equal(6, plan.TrialChipDays);
    }

    [Fact]
    public void Bought_pro_wins_over_the_trial()
    {
        var (t, _, _, _) = Make();
        t.Start();
        var plan = Plan(t, AddOnIds.Pro);
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.Null(plan.TrialDaysLeft);
        Assert.False(plan.CanStartTrial);

        var (t2, _, _, _) = Make();
        var bought = Plan(t2, AddOnIds.Pro);
        Assert.False(bought.CanStartTrial);
        Assert.False(bought.StartTrial());
    }

    [Fact]
    public void After_the_end_the_tier_drops_back_and_the_trial_cannot_restart()
    {
        var (t, clock, _, _) = Make();
        var plan = Plan(t, AddOnIds.Plus);
        Assert.True(plan.StartTrial());
        Assert.Equal(Tier.Pro, plan.Tier);
        clock.Now = T0.AddDays(15);
        Assert.Equal(Tier.Plus, plan.Tier);
        Assert.False(plan.CanAddAutomation);
        Assert.False(plan.CanStartTrial);
        Assert.False(plan.StartTrial());
    }

    [Fact]
    public void Expiry_only_gates_new_things_and_never_touches_what_exists()
    {
        var (t, clock, _, _) = Make();
        var plan = Plan(t);
        plan.StartTrial();
        clock.Now = T0.AddDays(15);
        Assert.Equal(Tier.Free, plan.Tier);
        // Someone with 40 shortcuts keeps all 40; only a new one waits.
        Assert.False(plan.CanAddShortcuts(40));
        Assert.Equal(0, plan.ShortcutRoom(40));
        Assert.Equal(TierRules.FreeShortcuts, plan.ShortcutCap);
        Assert.True(plan.CanAddShortcuts(TierRules.FreeShortcuts - 1));
    }

    [Fact]
    public void Unpackaged_builds_have_no_trial_and_stay_pro()
    {
        var plan = EntitlementService.Unpackaged();
        Assert.Equal(Tier.Pro, plan.Tier);
        Assert.False(plan.CanStartTrial);
        Assert.Null(plan.TrialDaysLeft);
    }

    [Fact]
    public void The_file_storage_reads_missing_as_null_and_replaces_atomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ap-trial", Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FileTrialStorage(Path.Combine(dir, "sub", "trial.json"));
            Assert.Null(s.Read());
            s.Write("one");
            s.Write("two");
            Assert.Equal("two", s.Read());
            Assert.Single(Directory.GetFiles(Path.Combine(dir, "sub")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---- records from the future, the periodic check, ownership, data roots, grandfathering ----

    private static string RecordText(DateTimeOffset start, DateTimeOffset seen) =>
        "{\"V\":1,\"Start\":\"" + start.UtcDateTime.ToString("O") + "\",\"Seen\":\"" + seen.UtcDateTime.ToString("O") + "\",\"Toast\":false,\"Summary\":false}";

    [Fact]
    public void A_record_that_starts_more_than_a_day_ahead_counts_as_used()
    {
        var (_, clock, file, marker) = Make();
        file.Text = RecordText(T0.AddDays(2), T0.AddDays(2));
        var t = Reopen(file, marker, clock);
        Assert.Equal(TrialPhase.Expired, t.Status.Phase);
        Assert.False(t.CanStart);
        Assert.False(t.Start());
        Assert.False(t.SummaryDue);
    }

    [Fact]
    public void A_start_within_a_day_ahead_is_fine_and_a_late_last_seen_is_normal()
    {
        var (_, clock, file, marker) = Make();
        file.Text = RecordText(T0.AddHours(20), T0.AddHours(20));
        Assert.Equal(TrialPhase.Active, Reopen(file, marker, clock).Status.Phase);

        file.Text = RecordText(T0.AddDays(-20), T0.AddDays(-1));   // seen long after start + 14 days is not an error
        var t = Reopen(file, marker, clock);
        Assert.Equal(TrialPhase.Expired, t.Status.Phase);
        Assert.True(t.SummaryDue);
    }

    [Fact]
    public void Moments_wait_for_ownership_but_still_remember_the_time()
    {
        var (t, clock, file, marker) = Make();
        t.Start();
        clock.Now = T0.AddDays(13).AddHours(2);
        Assert.Equal(TrialAction.None, TrialMoments.Next(t, ownershipKnown: false, boughtPro: false));
        Assert.True(Reopen(file, marker, clock).ToastDue);     // not marked shown
        Assert.Contains(clock.Now.UtcDateTime.ToString("O")[..16], file.Text);   // last-seen was saved
        Assert.Equal(TrialAction.Toast, TrialMoments.Next(t, true, false));
    }

    [Fact]
    public void Moments_show_the_toast_once_then_the_summary_once()
    {
        var (t, clock, _, _) = Make();
        t.Start();
        clock.Now = T0.AddDays(13).AddHours(2);
        Assert.Equal(TrialAction.Toast, TrialMoments.Next(t, true, false));
        Assert.Equal(TrialAction.None, TrialMoments.Next(t, true, false));
        clock.Now = T0.AddDays(14).AddMinutes(5);
        Assert.Equal(TrialAction.Summary, TrialMoments.Next(t, true, false));
        Assert.Equal(TrialAction.None, TrialMoments.Next(t, true, false));
    }

    [Fact]
    public void Moments_show_nothing_to_someone_who_bought_pro()
    {
        var (t, clock, _, _) = Make();
        t.Start();
        clock.Now = T0.AddDays(13).AddHours(2);
        Assert.Equal(TrialAction.None, TrialMoments.Next(t, true, true));
        clock.Now = T0.AddDays(15);
        Assert.Equal(TrialAction.None, TrialMoments.Next(t, true, true));
    }

    [Fact]
    public void Moments_recover_from_a_record_that_was_locked()
    {
        var (_, clock, file, marker) = Make();
        new ProTrial(file, marker, clock).Start();
        file.Text = file.Text;   // keep the content
        marker.Text = null;
        file.ThrowOnRead = true;
        var t = Reopen(file, marker, clock);
        Assert.Equal(TrialPhase.Unavailable, t.Status.Phase);
        file.ThrowOnRead = false;
        TrialMoments.Next(t, true, false);
        Assert.Equal(TrialPhase.Active, t.Status.Phase);
    }

    [Fact]
    public async Task The_trial_is_not_offered_until_ownership_is_known()
    {
        var (t, _, _, _) = Make();
        var store = new Store { Read = LicenseRead.Of() };
        var plan = EntitlementService.ForStore(store, new Cache(), _ => Task.CompletedTask, t);
        Assert.False(plan.OwnershipKnown);
        Assert.False(plan.CanStartTrial);
        Assert.False(plan.StartTrial());
        await plan.RefreshAsync();
        Assert.True(plan.OwnershipKnown);
        Assert.True(plan.CanStartTrial);
        Assert.True(Plan(t).OwnershipKnown);   // a cached answer counts
    }

    [Fact]
    public void A_custom_data_root_gets_the_file_only_and_never_the_registry_marker()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-trial", Guid.NewGuid().ToString("N"));
        try
        {
            var asked = false;
            var trial = TrialFactory.Create(root, customDataRoot: true, () => { asked = true; return new Memory(); });
            Assert.False(asked);
            Assert.True(trial.Start());
            Assert.True(File.Exists(Path.Combine(root, "trial.json")));

            TrialFactory.Create(root, customDataRoot: false, () => { asked = true; return new Memory(); });
            Assert.True(asked);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Expiry_keeps_every_shortcut_and_automation_and_only_gates_new_ones()
    {
        var library = new AutomationPortal.Core.Testing.InMemoryShortcutLibrary();
        var made = new List<AutomationPortal.Core.Shortcut>();
        for (var i = 0; i < 9; i++)
        {
            var b = new AutomationPortal.Core.ShortcutBuilder("Shortcut " + i);
            b.Add("flow.nothing");
            made.Add(b.Build());
            library.Save(made[i]);
        }
        var automation = new AutomationPortal.Core.Automation { ShortcutId = made[0].Id, Enabled = true };
        library.SaveAutomation(automation);
        string Snapshot() => AutomationPortal.Core.AutomationPortalJson.Serialize(library.All.OrderBy(s => s.Id).ToList()) +
                             AutomationPortal.Core.AutomationPortalJson.Serialize(library.Automations.ToList());

        var (t, clock, _, _) = Make();
        var plan = Plan(t);
        plan.StartTrial();
        Assert.True(plan.CanAddAutomation);
        Assert.True(plan.CanAddShortcuts(library.All.Count));
        var before = Snapshot();

        clock.Now = T0.AddDays(15);
        TrialMoments.Next(t, true, false);

        Assert.Equal(Tier.Free, plan.Tier);
        Assert.Equal(before, Snapshot());                      // nothing changed or removed
        Assert.Equal(9, library.All.Count);
        Assert.False(plan.CanAddShortcuts(library.All.Count));  // only new ones wait
        Assert.False(plan.CanAddAutomation);
        // What exists stays usable: still editable and switchable.
        made[3].Name = "Renamed";
        library.Save(made[3]);
        Assert.Equal("Renamed", library.GetById(made[3].Id)!.Name);
        var existing = library.Automations.Single();
        existing.Enabled = false;
        library.SaveAutomation(existing);
        Assert.False(library.Automations.Single().Enabled);
        Assert.Equal(9, library.All.Count);
    }
}
