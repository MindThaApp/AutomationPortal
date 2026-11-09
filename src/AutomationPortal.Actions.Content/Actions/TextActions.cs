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
        Id = "text.combine", Title = "Combine Text", Category = ActionCategories.Text, Icon = "TextAddSpaceAfter24",
        Keywords = new[] { "join", "merge", "concatenate", "lines", "glue" },
        Summary = "Combine {text} using {separator}",
        Description = "Joins several pieces of text into one, with new lines, spaces, commas or your own separator between them.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Combined Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("separator", "Separator", "newLines", Separators.Choices),
            ParameterDescriptor.Text("customSeparator", "Custom separator", null, "e.g. - or \\t") with { VisibleWhen = new VisibleWhen("separator", "custom"), Hint = "Use \\n for a new line and \\t for a tab." },
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
        Id = "text.split", Title = "Split Text", Category = ActionCategories.Text, Icon = "TextBulletList24",
        Keywords = new[] { "separate", "divide", "break", "lines", "words", "list" },
        Summary = "Split {text} at {separator}",
        Description = "Cuts text into a list of smaller pieces, for example one piece per line or per comma.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Split Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("separator", "Split at", "newLines", Separators.SplitChoices),
            ParameterDescriptor.Text("custom", "Custom separator", null, "e.g. ;") with { VisibleWhen = new VisibleWhen("separator", "custom"), Hint = "Use \\n for a new line and \\t for a tab." },
            ParameterDescriptor.Bool("trim", "Trim spaces around each piece", true),
            ParameterDescriptor.Bool("removeEmpty", "Skip empty pieces", true),
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
        if (mode == "custom" && custom.Length == 0) throw new ActionException("Type the separator to split at, or choose another option.");

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
        Id = "text.replace", Title = "Replace Text", Category = ActionCategories.Text, Icon = "TextEditStyle24",
        Keywords = new[] { "find", "substitute", "swap", "change", "pattern", "regex" },
        Summary = "Replace {find} with {replaceWith} in {text}",
        Description = "Finds text and swaps it for something else. Turn on \"Find pattern\" to match things like any number or any email address.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Updated Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            new ParameterDescriptor { Key = "find", Label = "Find", Type = ParamType.Text, Required = true },
            ParameterDescriptor.Text("replaceWith", "Replace with"),
            ParameterDescriptor.Bool("usePattern", "Find pattern"),
            ParameterDescriptor.Bool("matchCase", "Match upper/lower case"),
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
        if (find.Length == 0) throw new ActionException("Enter the text to find.");

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
        Id = "text.case", Title = "Change Case", Category = ActionCategories.Text, Icon = "TextCaseTitle24",
        Keywords = new[] { "upper", "lower", "capitalize", "title", "sentence", "camel", "snake", "kebab" },
        Summary = "Change {text} to {mode}",
        Description = "Changes capital letters: UPPERCASE, lowercase, Title Case, Sentence case and more.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Text with New Case",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("mode", "Case", "title",
                new Choice("upper", "UPPERCASE"), new Choice("lower", "lowercase"), new Choice("title", "Title Case"),
                new Choice("sentence", "Sentence case"), new Choice("camel", "camelCase"), new Choice("snake", "snake_case"),
                new Choice("kebab", "kebab-case")),
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
        Id = "text.cleanup", Title = "Clean Up Text", Category = ActionCategories.Text, Icon = "TextClearFormatting24",
        Keywords = new[] { "trim", "spaces", "blank", "empty lines", "line breaks", "tidy", "whitespace" },
        Summary = "Clean up {text}",
        Description = "Tidies text: trims spaces at the start and end, squeezes extra spaces, removes empty lines and can join lines into one.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Cleaned Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.Bool("trim", "Trim spaces at the start and end of each line", true),
            ParameterDescriptor.Bool("extraSpaces", "Squeeze extra spaces into one", true),
            ParameterDescriptor.Bool("emptyLines", "Remove empty lines", true),
            ParameterDescriptor.Bool("lineBreaks", "Join all lines into one line"),
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
        Id = "text.getPart", Title = "Get Part of Text", Category = ActionCategories.Text, Icon = "TextColumnTwo24",
        Keywords = new[] { "first", "last", "characters", "words", "lines", "between", "before", "after", "substring", "extract" },
        Summary = "Get {mode} of {text}",
        Description = "Takes a piece out of text: the first or last few characters, words or lines, or whatever sits between, before or after some text.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Part of Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("mode", "Part", "firstWords",
                new Choice("firstChars", "the first characters"), new Choice("lastChars", "the last characters"),
                new Choice("firstWords", "the first words"), new Choice("lastWords", "the last words"),
                new Choice("firstLines", "the first lines"), new Choice("lastLines", "the last lines"),
                new Choice("between", "the text between two pieces of text"), new Choice("before", "the text before some text"),
                new Choice("after", "the text after some text")),
            ParameterDescriptor.Number("count", "How many", 1, 1, 100000) with { VisibleWhen = new VisibleWhen("mode", "firstChars", "lastChars", "firstWords", "lastWords", "firstLines", "lastLines") },
            ParameterDescriptor.Text("from", "Starting after") with { VisibleWhen = new VisibleWhen("mode", "between") },
            ParameterDescriptor.Text("to", "Ending before") with { VisibleWhen = new VisibleWhen("mode", "between") },
            ParameterDescriptor.Text("marker", "Text to look for") with { VisibleWhen = new VisibleWhen("mode", "before", "after") },
            ParameterDescriptor.Bool("matchCase", "Match upper/lower case"),
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
        if (mode == "between" && (from.Length == 0 || to.Length == 0)) throw new ActionException("Enter the text that comes before and after the part you want.");
        if (mode is "before" or "after" && marker.Length == 0) throw new ActionException("Enter the text to look for.");
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
        Id = "text.find", Title = "Find in Text", Category = ActionCategories.Text, Icon = "DocumentSearch24",
        Keywords = new[] { "extract", "email", "phone", "link", "number", "date", "hashtag", "mention", "pattern", "regex", "match" },
        Summary = "Find {what} in {text}",
        Description = "Picks out things from text: email addresses, phone numbers, links, numbers, dates, hashtags, @mentions, or anything that matches your own pattern.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Any, OutputName = "Found Items",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("what", "Find", "email",
                new Choice("email", "email addresses"), new Choice("phone", "phone numbers"), new Choice("link", "links"),
                new Choice("number", "numbers"), new Choice("date", "dates"), new Choice("hashtag", "hashtags"),
                new Choice("mention", "@mentions"), new Choice("pattern", "a pattern I write")),
            ParameterDescriptor.Text("pattern", "Pattern", null, "e.g. \\d{4}-\\d{2}") with
            {
                VisibleWhen = new VisibleWhen("what", "pattern"),
                Hint = "A pattern describes what to look for, like \\d+ for any number. If it has a (group), only the group is returned.",
            },
            ParameterDescriptor.Bool("matchCase", "Match upper/lower case") with { VisibleWhen = new VisibleWhen("what", "pattern") },
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
            default: throw new ActionException("Choose what to find.");
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
        Id = "content.count", Title = "Count", Category = ActionCategories.Text, Icon = "NumberSymbol24",
        Keywords = new[] { "length", "how many", "words", "characters", "lines", "sentences", "items", "size" },
        Summary = "Count {what} in {input}",
        Description = "Counts the items in a list, or the characters, words, lines or sentences in text.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Number, OutputName = "Count",
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", "Input", isInput: true),
            ParameterDescriptor.ChoiceOf("what", "Count", "items",
                new Choice("items", "items"), new Choice("characters", "characters"), new Choice("words", "words"),
                new Choice("lines", "lines"), new Choice("sentences", "sentences")),
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
        Id = "text.fromInput", Title = "Get Text from Input", Category = ActionCategories.Text, Icon = "TextT24",
        Keywords = new[] { "convert", "to text", "plain text", "as text" },
        Summary = "Get text from {input}",
        Description = "Turns whatever you give it (numbers, links, dates, lists, dictionaries) into plain text. Lists are joined with the separator you choose.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Text, OutputName = "Text",
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", "Input", isInput: true),
            ParameterDescriptor.ChoiceOf("separator", "Join items with", "newLines", Separators.Choices),
            ParameterDescriptor.Text("customSeparator", "Custom separator") with { VisibleWhen = new VisibleWhen("separator", "custom") },
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
        Id = "text.encode", Title = "Encode / Decode", Category = ActionCategories.Text, Icon = "LockClosed24",
        Keywords = new[] { "base64", "url", "html", "escape", "percent", "unescape" },
        Summary = "{operation} {text} as {format}",
        Description = "Converts text to or from Base64, web address (URL) encoding or HTML encoding.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Encoded Text",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text", required: true),
            ParameterDescriptor.ChoiceOf("operation", "Action", "encode", new Choice("encode", "Encode"), new Choice("decode", "Decode")),
            ParameterDescriptor.ChoiceOf("format", "Format", "base64", new Choice("base64", "Base64"), new Choice("url", "Web address (URL)"), new Choice("html", "HTML")),
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
                catch (FormatException) { throw new ActionException("That text isn't valid Base64, so it can't be decoded. Check it was copied completely."); }
            case "url":
                return decode ? System.Net.WebUtility.UrlDecode(text) : Uri.EscapeDataString(text);
            case "html":
                return decode ? System.Net.WebUtility.HtmlDecode(text) : System.Net.WebUtility.HtmlEncode(text);
            default: throw new ActionException("Choose a format.");
        }
    }
}

public sealed class MakeHashAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.hash", Title = "Make Hash", Category = ActionCategories.Text, Icon = "Fingerprint24",
        Keywords = new[] { "md5", "sha", "checksum", "digest", "fingerprint" },
        Summary = "Make {algorithm} hash of {input}",
        Description = "Makes a short fingerprint (hash) of some text or a file. Handy for checking that a file wasn't changed.",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Text, OutputName = "Hash", IsAdvanced = true,
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", "Text or file", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("algorithm", "Type", "sha256",
                new Choice("md5", "MD5"), new Choice("sha1", "SHA-1"), new Choice("sha256", "SHA-256"), new Choice("sha384", "SHA-384"), new Choice("sha512", "SHA-512")),
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
