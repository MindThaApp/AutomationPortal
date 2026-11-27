using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class CombineTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.combine", Title = Strings.Get("TextCombine_Title"), Category = ActionCategories.Text, Icon = "TextAddSpaceAfter24",
        Keywords = new[] { "join", "merge", "concatenate", "lines", "glue" },
        Summary = Strings.Get("TextCombine_Summary"),
        Description = Strings.Get("TextCombine_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextCombine_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextCombine_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("separator", Strings.Get("TextCombine_Param_Separator_Label"), "newLines", Separators.Choices),
            ParameterDescriptor.Text("customSeparator", Strings.Get("TextCombine_Param_CustomSeparator_Label"), null, Strings.Get("TextCombine_Param_CustomSeparator_Placeholder")) with { VisibleWhen = new VisibleWhen("separator", "custom"), Hint = Strings.Get("TextCombine_Param_CustomSeparator_Hint") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var sep = Separators.Resolve(await ctx.GetChoiceAsync("separator", "newLines"), await ctx.GetTextAsync("customSeparator"));
        return ContentValue.Text(string.Join(sep, input.AsTexts()));
    }
}

public sealed class SplitTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.split", Title = Strings.Get("TextSplit_Title"), Category = ActionCategories.Text, Icon = "TextBulletList24",
        Keywords = new[] { "separate", "divide", "break", "lines", "words", "list" },
        Summary = Strings.Get("TextSplit_Summary"),
        Description = Strings.Get("TextSplit_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextSplit_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextSplit_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("separator", Strings.Get("TextSplit_Param_Separator_Label"), "newLines", Separators.SplitChoices),
            ParameterDescriptor.Text("custom", Strings.Get("TextSplit_Param_Custom_Label"), null, Strings.Get("TextSplit_Param_Custom_Placeholder")) with { VisibleWhen = new VisibleWhen("separator", "custom"), Hint = Strings.Get("TextSplit_Param_Custom_Hint") },
            ParameterDescriptor.Bool("trim", Strings.Get("TextSplit_Param_Trim_Label"), true),
            ParameterDescriptor.Bool("removeEmpty", Strings.Get("TextSplit_Param_RemoveEmpty_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var mode = await ctx.GetChoiceAsync("separator", "newLines");
        var custom = Separators.Unescape(await ctx.GetTextAsync("custom"));
        var trim = await ctx.GetBoolAsync("trim", true);
        var skipEmpty = await ctx.GetBoolAsync("removeEmpty", true);
        if (mode == "custom" && custom.Length == 0) throw new ActionException(Strings.Get("TextSplit_Error_TypeTheSeparatorTo"));

        var parts = new List<string>();
        foreach (var text in input.AsTexts())
        {
            switch (mode)
            {
                case "newLines": parts.AddRange(Regex.Split(text, @"\r\n|\n|\r")); break;
                case "spaces": parts.AddRange(Regex.Split(text, @"[ \t ]+")); break;
                case "commas": parts.AddRange(text.Split(',')); break;
                case "tabs": parts.AddRange(text.Split('\t')); break;
                case "everyCharacter":
                    var e = StringInfo.GetTextElementEnumerator(text);
                    while (e.MoveNext()) parts.Add((string)e.Current);
                    break;
                default: parts.AddRange(text.Split(custom)); break;
            }
        }
        IEnumerable<string> result = parts;
        if (trim && mode != "everyCharacter") result = result.Select(p => p.Trim());
        if (skipEmpty) result = result.Where(p => p.Length > 0);
        return ContentValue.Texts(result);
    }
}

public sealed class ReplaceTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.replace", Title = Strings.Get("TextReplace_Title"), Category = ActionCategories.Text, Icon = "TextEditStyle24",
        Keywords = new[] { "find", "substitute", "swap", "change", "pattern", "regex" },
        Summary = Strings.Get("TextReplace_Summary"),
        Description = Strings.Get("TextReplace_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextReplace_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextReplace_Param_Text_Label"), required: true),
            new ParameterDescriptor { Key = "find", Label = Strings.Get("TextReplace_Param_Find_Label"), Type = ParamType.Text, Required = true },
            ParameterDescriptor.Text("replaceWith", Strings.Get("TextReplace_Param_ReplaceWith_Label")),
            ParameterDescriptor.Bool("usePattern", Strings.Get("TextReplace_Param_UsePattern_Label")),
            ParameterDescriptor.Bool("matchCase", Strings.Get("TextReplace_Param_MatchCase_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var find = await ctx.GetTextAsync("find");
        var with = await ctx.GetTextAsync("replaceWith");
        var usePattern = await ctx.GetBoolAsync("usePattern");
        var matchCase = await ctx.GetBoolAsync("matchCase");
        if (find.Length == 0) throw new ActionException(Strings.Get("TextReplace_Error_EnterTheTextTo"));

        if (usePattern)
        {
            var rx = Patterns.Create(find, matchCase);
            return Patterns.Guard(() => ActionHelpers.MapTexts(input, t => rx.Replace(t, with)));
        }
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return ActionHelpers.MapTexts(input, t => t.Replace(find, with, cmp));
    }
}

public sealed class ChangeCaseAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.case", Title = Strings.Get("TextCase_Title"), Category = ActionCategories.Text, Icon = "TextCaseTitle24",
        Keywords = new[] { "upper", "lower", "capitalize", "title", "sentence", "camel", "snake", "kebab" },
        Summary = Strings.Get("TextCase_Summary"),
        Description = Strings.Get("TextCase_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextCase_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextCase_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("TextCase_Param_Mode_Label"), "title",
                new Choice("upper", Strings.Get("TextCase_Choice_Upper")), new Choice("lower", Strings.Get("TextCase_Choice_Lower")), new Choice("title", Strings.Get("TextCase_Choice_Title")),
                new Choice("sentence", Strings.Get("TextCase_Choice_Sentence")), new Choice("camel", Strings.Get("TextCase_Choice_Camel")), new Choice("snake", Strings.Get("TextCase_Choice_Snake")),
                new Choice("kebab", Strings.Get("TextCase_Choice_Kebab"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var mode = await ctx.GetChoiceAsync("mode", "title");
        return ActionHelpers.MapTexts(input, t => Apply(t, mode));
    }

    internal static string Apply(string text, string mode)
    {
        var culture = CultureInfo.CurrentCulture;
        switch (mode)
        {
            case "upper": return text.ToUpper(culture);
            case "lower": return text.ToLower(culture);
            case "title": return culture.TextInfo.ToTitleCase(text.ToLower(culture));
            case "sentence": return Sentence(text, culture);
            case "camel":
                {
                    var words = Words(text);
                    return string.Concat(words.Select((w, i) => i == 0 ? w.ToLower(culture) : Cap(w, culture)));
                }
            case "snake": return string.Join("_", Words(text).Select(w => w.ToLower(culture)));
            case "kebab": return string.Join("-", Words(text).Select(w => w.ToLower(culture)));
            default: return text;
        }
    }

    private static string Cap(string w, CultureInfo c) => w.Length == 0 ? w : char.ToUpper(w[0], c) + w[1..].ToLower(c);

    private static string Sentence(string text, CultureInfo c)
    {
        var sb = new StringBuilder(text.Length);
        var startOfSentence = true;
        foreach (var ch in text.ToLower(c))
        {
            if (startOfSentence && char.IsLetter(ch)) { sb.Append(char.ToUpper(ch, c)); startOfSentence = false; continue; }
            sb.Append(ch);
            if (ch is '.' or '!' or '?' or '\n') startOfSentence = true;
            else if (char.IsLetterOrDigit(ch)) startOfSentence = false;
        }
        return sb.ToString();
    }

    private static List<string> Words(string text)
    {
        var spaced = Regex.Replace(text, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return Regex.Split(spaced, @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToList();
    }
}

public sealed class CleanUpTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.cleanup", Title = Strings.Get("TextCleanup_Title"), Category = ActionCategories.Text, Icon = "TextClearFormatting24",
        Keywords = new[] { "trim", "spaces", "blank", "empty lines", "line breaks", "tidy", "whitespace" },
        Summary = Strings.Get("TextCleanup_Summary"),
        Description = Strings.Get("TextCleanup_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextCleanup_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextCleanup_Param_Text_Label"), required: true),
            ParameterDescriptor.Bool("trim", Strings.Get("TextCleanup_Param_Trim_Label"), true),
            ParameterDescriptor.Bool("extraSpaces", Strings.Get("TextCleanup_Param_ExtraSpaces_Label"), true),
            ParameterDescriptor.Bool("emptyLines", Strings.Get("TextCleanup_Param_EmptyLines_Label"), true),
            ParameterDescriptor.Bool("lineBreaks", Strings.Get("TextCleanup_Param_LineBreaks_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        bool trim = await ctx.GetBoolAsync("trim", true), spaces = await ctx.GetBoolAsync("extraSpaces", true),
             empty = await ctx.GetBoolAsync("emptyLines", true), join = await ctx.GetBoolAsync("lineBreaks");
        return ActionHelpers.MapTexts(input, t => Clean(t, trim, spaces, empty, join));
    }

    internal static string Clean(string text, bool trim, bool extraSpaces, bool emptyLines, bool joinLines)
    {
        var lines = Regex.Split(text, @"\r\n|\n|\r").AsEnumerable();
        if (extraSpaces) lines = lines.Select(l => Regex.Replace(l, @"[ \t ]{2,}", " "));
        if (trim) lines = lines.Select(l => l.Trim());
        if (emptyLines) lines = lines.Where(l => l.Trim().Length > 0);
        var list = lines.ToList();
        if (joinLines)
        {
            var joined = string.Join(" ", list.Select(l => l.Trim()).Where(l => l.Length > 0));
            return extraSpaces ? Regex.Replace(joined, @" {2,}", " ") : joined;
        }
        var result = string.Join("\n", list);
        return trim ? result.Trim() : result;
    }
}

public sealed class GetPartOfTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.getPart", Title = Strings.Get("TextGetPart_Title"), Category = ActionCategories.Text, Icon = "TextColumnTwo24",
        Keywords = new[] { "first", "last", "characters", "words", "lines", "between", "before", "after", "substring", "extract" },
        Summary = Strings.Get("TextGetPart_Summary"),
        Description = Strings.Get("TextGetPart_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextGetPart_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextGetPart_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("TextGetPart_Param_Mode_Label"), "firstWords",
                new Choice("firstChars", Strings.Get("TextGetPart_Choice_FirstChars")), new Choice("lastChars", Strings.Get("TextGetPart_Choice_LastChars")),
                new Choice("firstWords", Strings.Get("TextGetPart_Choice_FirstWords")), new Choice("lastWords", Strings.Get("TextGetPart_Choice_LastWords")),
                new Choice("firstLines", Strings.Get("TextGetPart_Choice_FirstLines")), new Choice("lastLines", Strings.Get("TextGetPart_Choice_LastLines")),
                new Choice("between", Strings.Get("TextGetPart_Choice_Between")), new Choice("before", Strings.Get("TextGetPart_Choice_Before")),
                new Choice("after", Strings.Get("TextGetPart_Choice_After"))),
            ParameterDescriptor.Number("count", Strings.Get("TextGetPart_Param_Count_Label"), 1, 1, 100000) with { VisibleWhen = new VisibleWhen("mode", "firstChars", "lastChars", "firstWords", "lastWords", "firstLines", "lastLines") },
            ParameterDescriptor.Text("from", Strings.Get("TextGetPart_Param_From_Label")) with { VisibleWhen = new VisibleWhen("mode", "between") },
            ParameterDescriptor.Text("to", Strings.Get("TextGetPart_Param_To_Label")) with { VisibleWhen = new VisibleWhen("mode", "between") },
            ParameterDescriptor.Text("marker", Strings.Get("TextGetPart_Param_Marker_Label")) with { VisibleWhen = new VisibleWhen("mode", "before", "after") },
            ParameterDescriptor.Bool("matchCase", Strings.Get("TextGetPart_Param_MatchCase_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var mode = await ctx.GetChoiceAsync("mode", "firstWords");
        var count = (int)Math.Max(1, await ctx.GetNumberAsync("count", 1));
        var from = await ctx.GetTextAsync("from");
        var to = await ctx.GetTextAsync("to");
        var marker = await ctx.GetTextAsync("marker");
        var cmp = await ctx.GetBoolAsync("matchCase") ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (mode == "between" && (from.Length == 0 || to.Length == 0)) throw new ActionException(Strings.Get("TextGetPart_Error_EnterTheTextThat"));
        if (mode is "before" or "after" && marker.Length == 0) throw new ActionException(Strings.Get("TextGetPart_Error_EnterTheTextTo"));
        return ActionHelpers.MapTexts(input, t => Apply(t, mode, count, from, to, marker, cmp));
    }

    internal static string Apply(string text, string mode, int count, string from, string to, string marker, StringComparison cmp)
    {
        switch (mode)
        {
            case "firstChars": return TakeElements(text, count, true);
            case "lastChars": return TakeElements(text, count, false);
            case "firstWords":
            case "lastWords":
                {
                    var matches = Regex.Matches(text, @"\S+");
                    if (matches.Count == 0) return "";
                    var skip = mode == "firstWords" ? 0 : Math.Max(0, matches.Count - count);
                    var take = Math.Min(count, matches.Count);
                    var first = matches[skip]; var last = matches[skip + take - 1];
                    return text.Substring(first.Index, last.Index + last.Length - first.Index);
                }
            case "firstLines":
                return string.Join("\n", Regex.Split(text, @"\r\n|\n|\r").Take(count));
            case "lastLines":
                {
                    var lines = Regex.Split(text, @"\r\n|\n|\r");
                    return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - count)));
                }
            case "between":
                {
                    var s = text.IndexOf(from, cmp);
                    if (s < 0) return "";
                    s += from.Length;
                    var e = text.IndexOf(to, s, cmp);
                    return e < 0 ? "" : text[s..e];
                }
            case "before":
                {
                    var i = text.IndexOf(marker, cmp);
                    return i < 0 ? "" : text[..i];
                }
            case "after":
                {
                    var i = text.IndexOf(marker, cmp);
                    return i < 0 ? "" : text[(i + marker.Length)..];
                }
            default: return text;
        }
    }

    // Counts what a person sees as a character (emoji and accented letters stay whole).
    private static string TakeElements(string text, int count, bool fromStart)
    {
        var idx = StringInfo.ParseCombiningCharacters(text);
        if (count >= idx.Length) return text;
        if (fromStart) return text[..idx[count]];
        return text[idx[idx.Length - count]..];
    }
}

public sealed class FindInTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.find", Title = Strings.Get("TextFind_Title"), Category = ActionCategories.Text, Icon = "DocumentSearch24",
        Keywords = new[] { "extract", "email", "phone", "link", "number", "date", "hashtag", "mention", "pattern", "regex", "match" },
        Summary = Strings.Get("TextFind_Summary"),
        Description = Strings.Get("TextFind_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Any, OutputName = Strings.Get("TextFind_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextFind_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("what", Strings.Get("TextFind_Param_What_Label"), "email",
                new Choice("email", Strings.Get("TextFind_Choice_Email")), new Choice("phone", Strings.Get("TextFind_Choice_Phone")), new Choice("link", Strings.Get("TextFind_Choice_Link")),
                new Choice("number", Strings.Get("TextFind_Choice_Number")), new Choice("date", Strings.Get("TextFind_Choice_Date")), new Choice("hashtag", Strings.Get("TextFind_Choice_Hashtag")),
                new Choice("mention", Strings.Get("TextFind_Choice_Mention")), new Choice("pattern", Strings.Get("TextFind_Choice_Pattern"))),
            ParameterDescriptor.Text("pattern", Strings.Get("TextFind_Param_Pattern_Label"), null, Strings.Get("TextFind_Param_Pattern_Placeholder")) with
            {
                VisibleWhen = new VisibleWhen("what", "pattern"),
                Hint = Strings.Get("TextFind_Param_Pattern_Hint"),
            },
            ParameterDescriptor.Bool("matchCase", Strings.Get("TextFind_Param_MatchCase_Label")) with { VisibleWhen = new VisibleWhen("what", "pattern") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex PhoneRx = new(@"(?<![\w.])(?:\+|00)?\(?\d[\d\s().\-]{5,}\d(?!\w)", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex LinkRx = new(@"\b(?:https?://|www\.)[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex NumberRx = new(@"(?<![\w.])-?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?(?![\w])", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex HashtagRx = new(@"(?<![\w#])#[\p{L}\p{N}_]+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex MentionRx = new(@"(?<![\w@.])@[A-Za-z0-9_]{1,30}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var what = await ctx.GetChoiceAsync("what", "email");
        var text = string.Join("\n", input.AsTexts());
        var pattern = what == "pattern" ? await ctx.GetTextAsync("pattern") : "";
        var matchCase = await ctx.GetBoolAsync("matchCase");
        return Patterns.Guard(() => Find(text, what, pattern, matchCase));
    }

    internal static ContentValue Find(string text, string what, string pattern = "", bool matchCase = false)
    {
        switch (what)
        {
            case "email": return ContentValue.Texts(EmailRx.Matches(text).Select(m => m.Value));
            case "phone":
                return ContentValue.Texts(PhoneRx.Matches(text).Select(m => m.Value.Trim()).Where(IsPhoneNumber));
            case "link":
                {
                    var items = new List<ContentItem>();
                    foreach (Match m in LinkRx.Matches(text))
                    {
                        var raw = m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '!', '?', '\'', '"');
                        if (raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw;
                        if (Uri.TryCreate(raw, UriKind.Absolute, out var u)) items.Add(new UrlItem(u));
                    }
                    return ContentValue.FromItems(items);
                }
            case "number":
                return ContentValue.Numbers(NumberRx.Matches(text)
                    .Select(m => double.Parse(m.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture)));
            case "date": return new ContentValue(DateParser.FindAll(text, DateTimeOffset.Now).Select(d => (ContentItem)new DateItem(d)));
            case "hashtag": return ContentValue.Texts(HashtagRx.Matches(text).Select(m => m.Value));
            case "mention": return ContentValue.Texts(MentionRx.Matches(text).Select(m => m.Value));
            case "pattern":
                {
                    var rx = Patterns.Create(pattern, matchCase);
                    var found = new List<string>();
                    foreach (Match m in rx.Matches(text))
                    {
                        if (m.Groups.Count > 1)
                        {
                            var g = m.Groups.Cast<Group>().Skip(1).FirstOrDefault(x => x.Success);
                            if (g is not null) found.Add(g.Value);
                        }
                        else found.Add(m.Value);
                    }
                    return ContentValue.Texts(found);
                }
            default: throw new ActionException(Strings.Get("TextFind_Error_ChooseWhatToFind"));
        }
    }

    private static bool IsPhoneNumber(string s)
    {
        var digits = s.Count(char.IsDigit);
        if (digits < 7 || digits > 15) return false;
        // Not an ISO date (2026-10-02) or a day/month/year date (02/10/2026).
        if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$") || Regex.IsMatch(s, @"^\d{1,2}[./\-]\d{1,2}[./\-]\d{2,4}$")) return false;
        return true;
    }
}

public sealed class CountAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.count", Title = Strings.Get("ContentCount_Title"), Category = ActionCategories.Text, Icon = "NumberSymbol24",
        Keywords = new[] { "length", "how many", "words", "characters", "lines", "sentences", "items", "size" },
        Summary = Strings.Get("ContentCount_Summary"),
        Description = Strings.Get("ContentCount_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Number, OutputName = Strings.Get("ContentCount_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", Strings.Get("ContentCount_Param_Input_Label"), isInput: true),
            ParameterDescriptor.ChoiceOf("what", Strings.Get("ContentCount_Param_What_Label"), "items",
                new Choice("items", Strings.Get("ContentCount_Choice_Items")), new Choice("characters", Strings.Get("ContentCount_Choice_Characters")), new Choice("words", Strings.Get("ContentCount_Choice_Words")),
                new Choice("lines", Strings.Get("ContentCount_Choice_Lines")), new Choice("sentences", Strings.Get("ContentCount_Choice_Sentences"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        var what = await ctx.GetChoiceAsync("what", "items");
        return ContentValue.Number(Count(input, what));
    }

    internal static int Count(ContentValue input, string what)
    {
        if (what == "items") return input.Count;
        var text = input.AsText();
        switch (what)
        {
            case "characters": return new StringInfo(text).LengthInTextElements;
            case "words": return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            case "lines": return text.Length == 0 ? 0 : Regex.Split(text, @"\r\n|\n|\r").Length;
            case "sentences":
                return Regex.Split(text, @"(?<=[.!?…])\s+|[\r\n]+").Count(s => s.Any(char.IsLetterOrDigit));
            default: return input.Count;
        }
    }
}

public sealed class GetTextFromInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.fromInput", Title = Strings.Get("TextFromInput_Title"), Category = ActionCategories.Text, Icon = "TextT24",
        Keywords = new[] { "convert", "to text", "plain text", "as text" },
        Summary = Strings.Get("TextFromInput_Summary"),
        Description = Strings.Get("TextFromInput_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextFromInput_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", Strings.Get("TextFromInput_Param_Input_Label"), isInput: true),
            ParameterDescriptor.ChoiceOf("separator", Strings.Get("TextFromInput_Param_Separator_Label"), "newLines", Separators.Choices),
            ParameterDescriptor.Text("customSeparator", Strings.Get("TextFromInput_Param_CustomSeparator_Label")) with { VisibleWhen = new VisibleWhen("separator", "custom") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        var sep = Separators.Resolve(await ctx.GetChoiceAsync("separator", "newLines"), await ctx.GetTextAsync("customSeparator"));
        return ContentValue.Text(string.Join(sep, input.AsTexts()));
    }
}

public sealed class EncodeDecodeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.encode", Title = Strings.Get("TextEncode_Title"), Category = ActionCategories.Text, Icon = "LockClosed24",
        Keywords = new[] { "base64", "url", "html", "escape", "percent", "unescape" },
        Summary = Strings.Get("TextEncode_Summary"),
        Description = Strings.Get("TextEncode_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextEncode_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextEncode_Param_Text_Label"), required: true),
            ParameterDescriptor.ChoiceOf("operation", Strings.Get("TextEncode_Param_Operation_Label"), "encode", new Choice("encode", Strings.Get("TextEncode_Choice_Encode")), new Choice("decode", Strings.Get("TextEncode_Choice_Decode"))),
            ParameterDescriptor.ChoiceOf("format", Strings.Get("TextEncode_Param_Format_Label"), "base64", new Choice("base64", Strings.Get("TextEncode_Choice_Base64")), new Choice("url", Strings.Get("TextEncode_Choice_Url")), new Choice("html", Strings.Get("TextEncode_Choice_Html"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var decode = await ctx.GetChoiceAsync("operation", "encode") == "decode";
        var format = await ctx.GetChoiceAsync("format", "base64");
        return ActionHelpers.MapTexts(input, t => Apply(t, decode, format));
    }

    internal static string Apply(string text, bool decode, string format)
    {
        switch (format)
        {
            case "base64":
                if (!decode) return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                try
                {
                    var s = text.Trim().Replace('-', '+').Replace('_', '/').Replace("\r", "").Replace("\n", "").Replace(" ", "");
                    s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
                    return Encoding.UTF8.GetString(Convert.FromBase64String(s));
                }
                catch (FormatException) { throw new ActionException(Strings.Get("TextEncode_Error_ThatTextIsnT")); }
            case "url":
                return decode ? System.Net.WebUtility.UrlDecode(text) : Uri.EscapeDataString(text);
            case "html":
                return decode ? System.Net.WebUtility.HtmlDecode(text) : System.Net.WebUtility.HtmlEncode(text);
            default: throw new ActionException(Strings.Get("TextEncode_Error_ChooseAFormat"));
        }
    }
}

public sealed class MakeHashAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.hash", Title = Strings.Get("TextHash_Title"), Category = ActionCategories.Text, Icon = "Fingerprint24",
        Keywords = new[] { "md5", "sha", "checksum", "digest", "fingerprint" },
        Summary = Strings.Get("TextHash_Summary"),
        Description = Strings.Get("TextHash_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextHash_OutputName"), IsAdvanced = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", Strings.Get("TextHash_Param_Input_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("algorithm", Strings.Get("TextHash_Param_Algorithm_Label"), "sha256",
                new Choice("md5", Strings.Get("TextHash_Choice_Md5")), new Choice("sha1", Strings.Get("TextHash_Choice_Sha1")), new Choice("sha256", Strings.Get("TextHash_Choice_Sha256")), new Choice("sha384", Strings.Get("TextHash_Choice_Sha384")), new Choice("sha512", Strings.Get("TextHash_Choice_Sha512"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        var alg = await ctx.GetChoiceAsync("algorithm", "sha256");
        var results = new List<string>();
        foreach (var item in input)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (item is PathItem { } p && p is not FolderItem && File.Exists(p.Path))
            {
                await using var fs = new FileStream(p.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                results.Add(ActionHelpers.Hex(await HashStreamAsync(alg, fs, ctx.CancellationToken)));
            }
            else results.Add(ActionHelpers.Hex(Hash(alg, Encoding.UTF8.GetBytes(item.DisplayText))));
        }
        return ContentValue.Texts(results);
    }

    internal static byte[] Hash(string alg, byte[] data) => alg switch
    {
        "md5" => System.Security.Cryptography.MD5.HashData(data),
        "sha1" => System.Security.Cryptography.SHA1.HashData(data),
        "sha384" => System.Security.Cryptography.SHA384.HashData(data),
        "sha512" => System.Security.Cryptography.SHA512.HashData(data),
        _ => System.Security.Cryptography.SHA256.HashData(data),
    };

    private static async Task<byte[]> HashStreamAsync(string alg, Stream s, CancellationToken ct) => alg switch
    {
        "md5" => await System.Security.Cryptography.MD5.HashDataAsync(s, ct),
        "sha1" => await System.Security.Cryptography.SHA1.HashDataAsync(s, ct),
        "sha384" => await System.Security.Cryptography.SHA384.HashDataAsync(s, ct),
        "sha512" => await System.Security.Cryptography.SHA512.HashDataAsync(s, ct),
        _ => await System.Security.Cryptography.SHA256.HashDataAsync(s, ct),
    };
}
