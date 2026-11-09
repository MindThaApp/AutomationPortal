using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

/// <summary>Works out which pieces of an action's summary sentence to show when some parameters are hidden by VisibleWhen.</summary>
public static class SummaryLayout
{
    /// <summary>
    /// Removes tokens whose parameter is not visible, together with their connector words: the text just before the token
    /// (unless it opens the sentence) and the text just after it when no visible token follows that text.
    /// "Resize {images} to {width} wide" with width hidden reads "Resize {images}".
    /// </summary>
    public static IReadOnlyList<SummarySegment> Visible(IReadOnlyList<SummarySegment> segments, Func<string, bool> isVisible)
    {
        var drop = new bool[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (!s.IsToken || isVisible(s.ParamKey!)) continue;
            drop[i] = true;
            if (i > 0 && !segments[i - 1].IsToken && i - 1 > 0) drop[i - 1] = true;
            if (i + 1 < segments.Count && !segments[i + 1].IsToken)
            {
                var visibleTokenFollows = false;
                for (var j = i + 2; j < segments.Count; j++)
                    if (segments[j].IsToken && isVisible(segments[j].ParamKey!)) { visibleTokenFollows = true; break; }
                if (!visibleTokenFollows) drop[i + 1] = true;
            }
        }
        var result = new List<SummarySegment>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (drop[i]) continue;
            var s = segments[i];
            if (!s.IsToken)
            {
                var text = System.Text.RegularExpressions.Regex.Replace(s.Text, " {2,}", " ");
                if (result.Count == 0 || result[^1].Text.EndsWith(' ')) text = text.TrimStart();
                if (text.Length == 0) continue;
                s = s with { Text = text };
            }
            result.Add(s);
        }
        if (result.Count > 0 && !result[^1].IsToken) result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        return result.Where(s => s.IsToken || s.Text.Length > 0).ToList();
    }

    /// <summary>
    /// Number and unit agreement: when a token's value is 1, the unit word right after it is made singular ("Repeat 1 times" reads "Repeat 1 time").
    /// A unit that is plain text is changed in the returned list; a unit that is its own token is reported in <paramref name="singularTokens"/> so the view can show it singular.
    /// </summary>
    public static IReadOnlyList<SummarySegment> Agree(IReadOnlyList<SummarySegment> segments, Func<string, ParamValue?> valueOf, out HashSet<string> singularTokens)
    {
        singularTokens = new HashSet<string>();
        var result = segments.ToList();
        for (var i = 0; i + 1 < result.Count; i++)
        {
            if (!result[i].IsToken || !PluralAgreement.IsOne(valueOf(result[i].ParamKey!))) continue;
            var j = i + 1;
            if (!result[j].IsToken && string.IsNullOrWhiteSpace(result[j].Text) && j + 1 < result.Count) j++;   // "{every} {unit}": skip the space between
            var next = result[j];
            if (next.IsToken) singularTokens.Add(next.ParamKey!);
            else result[j] = next with { Text = PluralAgreement.SingularLeadingUnit(next.Text) };
        }
        return result;
    }
}