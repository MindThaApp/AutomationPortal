using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public enum SentencePartKind { Plain, Token, Shortcut }

public sealed record SentencePart(string Text, SentencePartKind Kind);

public sealed class AutomationSentenceModel
{
    public required IReadOnlyList<SentencePart> Parts { get; init; }
    /// <summary>"Only if it's between 9:00 AM and 5:00 PM and I'm on battery" or empty.</summary>
    public string ConditionsText { get; init; } = "";
    public bool TriggerMissing { get; init; }
    public bool ShortcutMissing { get; init; }
}

/// <summary>Turns an automation into a readable sentence: "When Spotify opens → Run Focus Mode".</summary>
public static class AutomationSentence
{
    public static AutomationSentenceModel Build(Automation a, TriggerRegistry triggers, ConditionRegistry conditions, IShortcutLibrary library)
    {
        var parts = new List<SentencePart>();
        var trigger = triggers.Get(a.Trigger.TriggerId)?.Descriptor;
        if (trigger is null)
        {
            parts.Add(new(Strings.Get("AutomationSentence_ThisTriggerIsntAvailable"), SentencePartKind.Plain));
        }
        else
        {
            foreach (var seg in trigger.SummarySegments)
            {
                if (!seg.IsToken) { parts.Add(new(seg.Text, SentencePartKind.Plain)); continue; }
                var p = trigger.Parameters.FirstOrDefault(x => x.Key == seg.ParamKey);
                if (p is null) { parts.Add(new(seg.Text, SentencePartKind.Plain)); continue; }
                parts.Add(new(LiteralFormatter.TokenText(p, a.Trigger.Parameters, out _), SentencePartKind.Token));
            }
        }

        Agree(parts);
        var shortcut = library.GetById(a.ShortcutId);
        parts.Add(new("  →  Run ", SentencePartKind.Plain));
        parts.Add(new(shortcut?.Name ?? "a deleted shortcut", SentencePartKind.Shortcut));

        var phrases = new List<string>();
        foreach (var c in a.Conditions)
        {
            var d = conditions.Get(c.ConditionId)?.Descriptor;
            if (d is null) continue;
            phrases.Add(RenderSummary(d.SummarySegments, d.Parameters, c.Parameters));
        }
        return new AutomationSentenceModel
        {
            Parts = parts,
            ConditionsText = phrases.Count == 0 ? "" : "Only if " + string.Join(" and ", phrases),
            TriggerMissing = trigger is null,
            ShortcutMissing = shortcut is null,
        };
    }

    public static string RenderSummary(IReadOnlyList<SummarySegment> segments, IReadOnlyList<ParameterDescriptor> parameters, IReadOnlyDictionary<string, ParamValue> values)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments)
        {
            var p = seg.IsToken ? parameters.FirstOrDefault(x => x.Key == seg.ParamKey) : null;
            sb.Append(p is null ? seg.Text : LiteralFormatter.TokenText(p, values, out _));
        }
        return PluralAgreement.Fix(sb.ToString());
    }

    /// <summary>"Every 1 minutes" becomes "Every 1 minute": a value of 1 makes the unit word after it singular, whether the unit is plain text or its own token.</summary>
    internal static void Agree(List<SentencePart> parts)
    {
        for (var i = 0; i + 1 < parts.Count; i++)
        {
            if (parts[i].Kind != SentencePartKind.Token || parts[i].Text != "1") continue;
            var j = i + 1;
            if (parts[j].Kind == SentencePartKind.Plain && string.IsNullOrWhiteSpace(parts[j].Text) && j + 1 < parts.Count) j++;
            var next = parts[j];
            var fixedText = next.Kind == SentencePartKind.Plain ? PluralAgreement.SingularLeadingUnit(next.Text) : PluralAgreement.Singular(next.Text);
            parts[j] = next with { Text = fixedText };
        }
    }

    public static string LastRunText(DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (lastRun is null) return "Hasn't run yet";
        var local = lastRun.Value.ToLocalTime();
        var span = now - lastRun.Value;
        if (span < TimeSpan.FromMinutes(1)) return Strings.Get("AutomationSentence_RanJustNow");
        if (span < TimeSpan.FromHours(1)) return $"Ran {(int)span.TotalMinutes} min ago";
        if (local.Date == now.ToLocalTime().Date) return "Ran today at " + local.ToString("t");
        if (local.Date == now.ToLocalTime().Date.AddDays(-1)) return "Ran yesterday at " + local.ToString("t");
        return "Ran " + local.ToString("d");
    }
}
