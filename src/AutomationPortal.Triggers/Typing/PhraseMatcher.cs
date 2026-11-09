using System.Text;

namespace AutomationPortal.Triggers;

/// <summary>What the user typed to complete a phrase.</summary>
/// <param name="Typed">The characters as typed (their case, not the phrase's).</param>
/// <param name="Phrase">The phrase that matched.</param>
/// <param name="Generation">Key counter at the time; text replacement is refused if more keys were typed since.</param>
/// <param name="Window">The window being typed into.</param>
public sealed record PhraseMatch(string Typed, string Phrase, long Generation, IntPtr Window);

internal sealed record PhraseDef(string Phrase, bool MatchCase, object Tag);

/// <summary>Rolling record of the last characters typed. Pure and unit-tested.</summary>
internal sealed class PhraseBuffer
{
    public const int Capacity = 96;
    private readonly StringBuilder _sb = new();

    public string Text => _sb.ToString();
    public int Length => _sb.Length;

    public void Append(string text)
    {
        _sb.Append(text);
        if (_sb.Length > Capacity) _sb.Remove(0, _sb.Length - Capacity);
    }

    public void Backspace()
    {
        if (_sb.Length > 0) _sb.Length--;
    }

    public void Clear() => _sb.Clear();

    /// <summary>
    /// The phrase the buffer now ends with (longest wins). A phrase that starts with a letter or digit only counts at the
    /// start of a word, so "addr" doesn't fire in the middle of "headdress"; phrases starting with a symbol (";date") fire anywhere.
    /// </summary>
    public (PhraseDef Def, string Typed)? FindMatch(IReadOnlyList<PhraseDef> defs) => Match(Text, defs);

    public static (PhraseDef Def, string Typed)? Match(string buffer, IReadOnlyList<PhraseDef> defs)
    {
        PhraseDef? best = null;
        foreach (var d in defs)
        {
            var p = d.Phrase;
            if (p.Length == 0 || p.Length > buffer.Length) continue;
            if (best is not null && p.Length <= best.Phrase.Length) continue;
            var cmp = d.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!buffer.EndsWith(p, cmp)) continue;
            if (char.IsLetterOrDigit(p[0]) && buffer.Length > p.Length && char.IsLetterOrDigit(buffer[buffer.Length - p.Length - 1])) continue;
            best = d;
        }
        return best is null ? null : (best, buffer[^best.Phrase.Length..]);
    }
}
