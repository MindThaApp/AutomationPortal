using System.Globalization;
using System.Text.Json;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Actions;

public sealed class GetItemFromListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.getItem", Title = "Get Item from List", Category = ActionCategories.ListsAndDictionaries, Icon = "TextBulletListLtr24",
        Keywords = new[] { "first", "last", "random", "index", "nth", "range", "pick", "element" },
        Summary = "Get {which} from {list}",
        Description = "Picks one item (or a range of items) out of a list: the first, the last, a random one, or by its position. Position 1 is the first item.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = "Item from List",
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", "List", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("which", "Item", "first",
                new Choice("first", "the first item"), new Choice("last", "the last item"), new Choice("random", "a random item"),
                new Choice("index", "the item at position"), new Choice("range", "the items from position")),
            ParameterDescriptor.Number("index", "Position", 1, 1) with { VisibleWhen = new VisibleWhen("which", "index", "range") },
            ParameterDescriptor.Number("to", "Up to position", 2, 1) with { VisibleWhen = new VisibleWhen("which", "range") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        var which = await ctx.GetChoiceAsync("which", "first");
        var index = (int)await ctx.GetNumberAsync("index", 1);
        var to = (int)await ctx.GetNumberAsync("to", index);
        return Pick(list, which, index, to);
    }

    internal static ContentValue Pick(ContentValue list, string which, int index, int to)
    {
        switch (which)
        {
            case "first": return list.IsEmpty ? list : ContentValue.From(list[0]);
            case "last": return list.IsEmpty ? list : ContentValue.From(list[^1]);
            case "random": return list.IsEmpty ? list : ContentValue.From(list[Random.Shared.Next(list.Count)]);
            case "index":
                if (index < 1 || index > list.Count)
                    throw new ActionException(list.Count == 0 ? "The list is empty, so there is no item to get." : $"The list has {ActionHelpers.Plural(list.Count, "item")}, so there is no item at position {index}.");
                return ContentValue.From(list[index - 1]);
            case "range":
                {
                    if (index > to) (index, to) = (to, index);
                    if (index < 1) index = 1;
                    if (index > list.Count) throw new ActionException($"The list has {ActionHelpers.Plural(list.Count, "item")}, so there is nothing from position {index}.");
                    to = Math.Min(to, list.Count);
                    return new ContentValue(list.Items.Skip(index - 1).Take(to - index + 1));
                }
            default: throw new ActionException("Choose which item to get.");
        }
    }
}

public sealed class SortListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.sort", Title = "Sort List", Category = ActionCategories.ListsAndDictionaries, Icon = "ArrowSortDown24",
        Keywords = new[] { "order", "alphabetical", "a-z", "shuffle", "reverse", "newest", "largest", "arrange", "randomize" },
        Summary = "Sort {list} {order}",
        Description = "Puts a list in order: A to Z, Z to A, by number, by date, by a property such as Name, Size or Modified, or shuffles or reverses it.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = "Sorted List",
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", "List", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("order", "Order", "az",
                new Choice("az", "A to Z"), new Choice("za", "Z to A"), new Choice("numAsc", "smallest number first"), new Choice("numDesc", "biggest number first"),
                new Choice("dateAsc", "oldest date first"), new Choice("dateDesc", "newest date first"),
                new Choice("property", "by property, lowest first"), new Choice("propertyDesc", "by property, highest first"),
                new Choice("shuffle", "in random order"), new Choice("reverse", "in reverse")),
            ParameterDescriptor.Text("property", "Property", null, "e.g. Name, Size, Modified") with
            {
                VisibleWhen = new VisibleWhen("order", "property", "propertyDesc"),
                Hint = "For files: Name, Size, Created, Modified, Extension. For text: Length, Word Count. For dictionaries: the name of a key.",
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        var order = await ctx.GetChoiceAsync("order", "az");
        var property = (await ctx.GetTextAsync("property")).Trim();
        if (order.StartsWith("property", StringComparison.Ordinal) && property.Length == 0)
            throw new ActionException("Enter the property to sort by, for example Name, Size or Modified.");
        return Sort(list, order, property);
    }

    internal static ContentValue Sort(ContentValue list, string order, string property)
    {
        var items = list.Items;
        switch (order)
        {
            case "az": return new ContentValue(items.OrderBy(i => i.DisplayText, NaturalComparer.Instance));
            case "za": return new ContentValue(items.OrderByDescending(i => i.DisplayText, NaturalComparer.Instance));
            case "numAsc": return new ContentValue(OrderNullsLast(items, i => ContentValue.From(i).AsNumber(), false));
            case "numDesc": return new ContentValue(OrderNullsLast(items, i => ContentValue.From(i).AsNumber(), true));
            case "dateAsc": return new ContentValue(OrderNullsLast(items, i => ActionHelpers.ToDate(ContentValue.From(i))?.UtcTicks, false));
            case "dateDesc": return new ContentValue(OrderNullsLast(items, i => ActionHelpers.ToDate(ContentValue.From(i))?.UtcTicks, true));
            case "property":
            case "propertyDesc":
                {
                    var keyed = items.Select(i => (Item: i, Key: SortKey.Of(i.GetProperty(property)))).ToList();
                    var cmp = Comparer<SortKey>.Create((a, b) => a.CompareTo(b));
                    var sorted = order == "property" ? keyed.OrderBy(k => k.Key, cmp) : keyed.OrderByDescending(k => k.Key, cmp);
                    return new ContentValue(sorted.Select(k => k.Item));
                }
            case "shuffle":
                {
                    var arr = items.ToArray();
                    for (var i = arr.Length - 1; i > 0; i--) { var j = Random.Shared.Next(i + 1); (arr[i], arr[j]) = (arr[j], arr[i]); }
                    return new ContentValue(arr);
                }
            case "reverse": return new ContentValue(items.Reverse());
            default: throw new ActionException("Choose how to sort the list.");
        }
    }

    private static IEnumerable<ContentItem> OrderNullsLast<T>(IEnumerable<ContentItem> items, Func<ContentItem, T?> key, bool descending) where T : struct, IComparable<T>
    {
        var withKey = items.Select(i => (Item: i, Key: key(i))).ToList();
        var present = withKey.Where(x => x.Key.HasValue);
        var ordered = descending ? present.OrderByDescending(x => x.Key!.Value) : present.OrderBy(x => x.Key!.Value);
        return ordered.Concat(withKey.Where(x => !x.Key.HasValue)).Select(x => x.Item);
    }

    /// <summary>Numbers sort as numbers, dates as dates, everything else as natural text; missing values go last.</summary>
    private readonly record struct SortKey(int Kind, double Number, string Text)
    {
        public static SortKey Of(ContentValue? v)
        {
            if (v is null || v.IsEmpty) return new SortKey(3, 0, "");
            return v[0] switch
            {
                NumberItem n => new SortKey(0, n.Value, ""),
                DateItem d => new SortKey(1, d.Value.UtcTicks, ""),
                BooleanItem b => new SortKey(0, b.Value ? 1 : 0, ""),
                var other => new SortKey(2, 0, other.DisplayText),
            };
        }

        public int CompareTo(SortKey o)
        {
            if (Kind != o.Kind) return Kind.CompareTo(o.Kind);
            return Kind == 2 ? NaturalComparer.Instance.Compare(Text, o.Text) : Number.CompareTo(o.Number);
        }
    }
}

public sealed class RemoveDuplicatesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.unique", Title = "Remove Duplicates", Category = ActionCategories.ListsAndDictionaries, Icon = "Copy24",
        Keywords = new[] { "unique", "distinct", "dedupe", "duplicates", "repeat" },
        Summary = "Remove duplicates from {list}",
        Description = "Keeps only the first of any items that appear more than once.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = "Unique Items",
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", "List", isInput: true, required: true),
            ParameterDescriptor.Bool("matchCase", "Treat upper and lower case as different"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        var comparer = await ctx.GetBoolAsync("matchCase") ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        return new ContentValue(list.Items.Where(i => seen.Add(i.DisplayText)));
    }
}

public sealed class FilterListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.filter", Title = "Filter List", Category = ActionCategories.ListsAndDictionaries, Icon = "Filter24",
        Keywords = new[] { "where", "only", "keep", "condition", "search", "limit", "match" },
        Summary = "Filter {list} where {conditions}",
        Description = "Keeps only the items that match your conditions. For each condition, type the property to check on the left (for example Name, Size, Kind or Modified), or leave it blank to check the item itself.",
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = "Filtered List",
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", "List", isInput: true, required: true),
            new ParameterDescriptor { Key = "conditions", Label = "Conditions", Type = ParamType.Conditions, AllowAskEachTime = false },
            ParameterDescriptor.Number("limit", "Keep at most", 0, 0) with { Hint = "0 keeps everything that matches." },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var list = await ctx.GetContentAsync("list");
        var conditions = await ctx.GetConditionsAsync("conditions");
        var limit = (int)Math.Max(0, await ctx.GetNumberAsync("limit", 0));
        var kept = new List<ContentItem>();
        foreach (var item in list)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (conditions.Evaluate(row => LeftFor(item, row.Left))) kept.Add(item);
            if (limit > 0 && kept.Count >= limit) break;
        }
        return new ContentValue(kept);
    }

    internal static ContentValue LeftFor(ContentItem item, ContentValue configuredLeft)
    {
        var name = configuredLeft.AsText().Trim();
        if (name.Length == 0) return ContentValue.From(item);
        return item.GetProperty(name) ?? ContentValue.From(item);
    }
}

public sealed class GetDictionaryValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dict.getValue", Title = "Get Dictionary Value", Category = ActionCategories.ListsAndDictionaries, Icon = "BookQuestionMark24",
        Keywords = new[] { "key", "json", "path", "property", "keys", "values", "lookup" },
        Summary = "Get {mode} from {dictionary}",
        Description = "Looks up a value in a dictionary by its key. To dig deeper use a path such as address.city or items.0.name. You can also get all the keys or all the values.",
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Any, OutputName = "Dictionary Value",
        Parameters = new[]
        {
            ParameterDescriptor.Content("dictionary", "Dictionary", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("mode", "Get", "key", new Choice("key", "the value for a key"), new Choice("keys", "all the keys"), new Choice("values", "all the values")),
            ParameterDescriptor.Text("key", "Key or path", null, "e.g. name or address.city") with { VisibleWhen = new VisibleWhen("mode", "key") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("dictionary");
        var mode = await ctx.GetChoiceAsync("mode", "key");
        var key = (await ctx.GetTextAsync("key")).Trim();
        if (mode == "key" && key.Length == 0) throw new ActionException("Enter the key to look up, for example name or address.city.");
        var dicts = DictionariesOf(input);
        if (dicts.Count == 0)
            throw new ActionException("There is no dictionary to look in. Give it a dictionary, or JSON text such as {\"name\": \"Sam\"}.");
        return Get(dicts, mode, key);
    }

    internal static List<DictionaryItem> DictionariesOf(ContentValue input)
    {
        var dicts = input.OfType<DictionaryItem>().ToList();
        if (dicts.Count == 0 && input.AsDictionary() is { } parsed) dicts.Add(parsed);
        return dicts;
    }

    internal static ContentValue Get(IReadOnlyList<DictionaryItem> dicts, string mode, string key)
    {
        switch (mode)
        {
            case "keys": return ContentValue.Texts(dicts.SelectMany(d => d.Keys));
            case "values": return ContentValue.Concat(dicts.SelectMany(d => d.Entries.Select(e => e.Value)).ToArray());
            default:
                if (key.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) key = key[4..].Trim();
                return ContentValue.Concat(dicts.Select(d => d.TryGet(key, out var direct) ? direct : d.GetPath(key)).ToArray());
        }
    }
}

public sealed class SetDictionaryValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dict.setValue", Title = "Set Dictionary Value", Category = ActionCategories.ListsAndDictionaries, Icon = "BookAdd24",
        Keywords = new[] { "key", "add", "update", "change", "json", "put" },
        Summary = "Set {key} to {value} in {dictionary}",
        Description = "Adds or changes a value in a dictionary and gives back the updated dictionary. If you don't give a dictionary it starts a new one. A path like address.city fills in nested dictionaries.",
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Dictionary, OutputName = "Updated Dictionary",
        Parameters = new[]
        {
            ParameterDescriptor.Content("dictionary", "Dictionary", isInput: true),
            new ParameterDescriptor { Key = "key", Label = "Key", Type = ParamType.Text, Required = true, Placeholder = "name or address.city" },
            ParameterDescriptor.Content("value", "Value"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("dictionary");
        var key = (await ctx.GetTextAsync("key")).Trim();
        var value = await ctx.GetContentAsync("value");
        if (key.Length == 0) throw new ActionException("Enter the key to set, for example name.");
        var dicts = input.IsEmpty ? new List<DictionaryItem> { DictionaryItem.Empty } : GetDictionaryValueAction.DictionariesOf(input);
        if (dicts.Count == 0) throw new ActionException("The input isn't a dictionary. Give it a dictionary, JSON text, or nothing to start a new one.");
        return new ContentValue(dicts.Select(d => (ContentItem)Set(d, key, value)));
    }

    internal static DictionaryItem Set(DictionaryItem dict, string path, ContentValue value)
    {
        if (dict.TryGet(path, out _)) return dict.With(path, value);
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return dict.With(path, value);
        return SetNested(dict, segments, 0, value);
    }

    private static DictionaryItem SetNested(DictionaryItem dict, string[] segments, int at, ContentValue value)
    {
        if (at == segments.Length - 1) return dict.With(segments[at], value);
        var child = dict.Get(segments[at]).FirstItem as DictionaryItem ?? DictionaryItem.Empty;
        return dict.With(segments[at], ContentValue.From(SetNested(child, segments, at + 1, value)));
    }
}

public sealed class GetDictionaryFromInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dict.fromInput", Title = "Get Dictionary from Input", Category = ActionCategories.ListsAndDictionaries, Icon = "Braces24",
        Keywords = new[] { "json", "parse", "object", "api", "convert" },
        Summary = "Get a dictionary from {input}",
        Description = "Turns JSON text (or a .json file) into a dictionary or list you can pick values from. Also understands simple lines like \"name: Sam\" and web-style name=value&age=3.",
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Dictionary, OutputName = "Dictionary",
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", "Input", isInput: true, required: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        if (input.IsEmpty) throw new ActionException("There is no text to read. Give it JSON text or a .json file.");
        if (input.All(i => i is DictionaryItem)) return input;
        string text;
        if (input.Count == 1 && input[0] is PathItem p and not FolderItem)
        {
            if (!File.Exists(p.Path)) throw new ActionException($"Couldn't find the file '{p.FullName}'. Check it still exists.");
            text = (await File.ReadAllTextAsync(p.Path, ctx.CancellationToken)).TrimStart('﻿');
        }
        else text = input.AsText();
        return Parse(text);
    }

    internal static ContentValue Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) throw new ActionException("There is no text to read. Give it JSON text or a .json file.");
        if (trimmed[0] is '{' or '[' or '"' || trimmed is "true" or "false" or "null" || char.IsDigit(trimmed[0]) && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            try { return ContentValue.FromJson(trimmed); }
            catch (JsonException ex)
            {
                var where = ex.LineNumber is { } line ? $" near line {line + 1}" : "";
                throw new ActionException($"That text isn't valid JSON{where}. Check for a missing quote, comma or bracket.", ex);
            }
        }
        if (TryKeyValues(trimmed, out var dict)) return dict;
        throw new ActionException("That text isn't JSON, so it can't be turned into a dictionary. It should start with { or [, for example {\"name\": \"Sam\"}.");
    }

    private static bool TryKeyValues(string text, out ContentValue dict)
    {
        dict = ContentValue.Empty;
        var pairs = new List<KeyValuePair<string, ContentValue>>();
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 1 && lines[0].Contains('=') && !lines[0].Contains(": "))
        {
            foreach (var part in lines[0].TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) return false;
                pairs.Add(new(System.Net.WebUtility.UrlDecode(part[..eq]), ContentValue.Text(System.Net.WebUtility.UrlDecode(part[(eq + 1)..]))));
            }
        }
        else
        {
            foreach (var line in lines)
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) return false;
                pairs.Add(new(line[..colon].Trim(), ContentValue.Text(line[(colon + 1)..].Trim())));
            }
        }
        if (pairs.Count == 0) return false;
        dict = ContentValue.Dict(pairs);
        return true;
    }
}

public sealed class MakeJsonAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dict.toJson", Title = "Make JSON Text", Category = ActionCategories.ListsAndDictionaries, Icon = "CodeBlock24",
        Keywords = new[] { "json", "serialize", "stringify", "export", "dictionary to text" },
        Summary = "Make JSON text from {input}",
        Description = "Turns a dictionary or list into JSON text, for sending to a website or saving to a file.",
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Text, OutputName = "JSON Text",
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", "Input", isInput: true, required: true),
            ParameterDescriptor.Bool("pretty", "Make it easy to read (indented)", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        var pretty = await ctx.GetBoolAsync("pretty", true);
        return ContentValue.Text(input.IsEmpty ? "" : input.ToJson(pretty));
    }
}
