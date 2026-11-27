using System.Globalization;
using System.Text.Json;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class GetItemFromListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.getItem", Title = Strings.Get("ListGetItem_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "TextBulletListLtr24",
        Keywords = new[] { "first", "last", "random", "index", "nth", "range", "pick", "element" },
        Summary = Strings.Get("ListGetItem_Summary"),
        Description = Strings.Get("ListGetItem_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = Strings.Get("ListGetItem_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", Strings.Get("ListGetItem_Param_List_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("which", Strings.Get("ListGetItem_Param_Which_Label"), "first",
                new Choice("first", Strings.Get("ListGetItem_Choice_First")), new Choice("last", Strings.Get("ListGetItem_Choice_Last")), new Choice("random", Strings.Get("ListGetItem_Choice_Random")),
                new Choice("index", Strings.Get("ListGetItem_Choice_Index")), new Choice("range", Strings.Get("ListGetItem_Choice_Range"))),
            ParameterDescriptor.Number("index", Strings.Get("ListGetItem_Param_Index_Label"), 1, 1) with { VisibleWhen = new VisibleWhen("which", "index", "range") },
            ParameterDescriptor.Number("to", Strings.Get("ListGetItem_Param_To_Label"), 2, 1) with { VisibleWhen = new VisibleWhen("which", "range") },
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
                    throw new ActionException(list.Count == 0 ? Strings.Get("ListGetItem_Error_TheListIsEmpty") : Strings.Format("ListGetItem_Error_TheListHas0", ActionHelpers.Count(list.Count, "Item"), index));
                return ContentValue.From(list[index - 1]);
            case "range":
                {
                    if (index > to) (index, to) = (to, index);
                    if (index < 1) index = 1;
                    if (index > list.Count) throw new ActionException(Strings.Format("ListGetItem_Error_TheListHas02", ActionHelpers.Count(list.Count, "Item"), index));
                    to = Math.Min(to, list.Count);
                    return new ContentValue(list.Items.Skip(index - 1).Take(to - index + 1));
                }
            default: throw new ActionException(Strings.Get("ListGetItem_Error_ChooseWhichItemTo"));
        }
    }
}

public sealed class SortListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "list.sort", Title = Strings.Get("ListSort_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "ArrowSortDown24",
        Keywords = new[] { "order", "alphabetical", "a-z", "shuffle", "reverse", "newest", "largest", "arrange", "randomize" },
        Summary = Strings.Get("ListSort_Summary"),
        Description = Strings.Get("ListSort_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = Strings.Get("ListSort_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", Strings.Get("ListSort_Param_List_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("order", Strings.Get("ListSort_Param_Order_Label"), "az",
                new Choice("az", Strings.Get("ListSort_Choice_Az")), new Choice("za", Strings.Get("ListSort_Choice_Za")), new Choice("numAsc", Strings.Get("ListSort_Choice_NumAsc")), new Choice("numDesc", Strings.Get("ListSort_Choice_NumDesc")),
                new Choice("dateAsc", Strings.Get("ListSort_Choice_DateAsc")), new Choice("dateDesc", Strings.Get("ListSort_Choice_DateDesc")),
                new Choice("property", Strings.Get("ListSort_Choice_Property")), new Choice("propertyDesc", Strings.Get("ListSort_Choice_PropertyDesc")),
                new Choice("shuffle", Strings.Get("ListSort_Choice_Shuffle")), new Choice("reverse", Strings.Get("ListSort_Choice_Reverse"))),
            ParameterDescriptor.Text("property", Strings.Get("ListSort_Param_Property_Label"), null, Strings.Get("ListSort_Param_Property_Placeholder")) with
            {
                VisibleWhen = new VisibleWhen("order", "property", "propertyDesc"),
                Hint = Strings.Get("ListSort_Param_Property_Hint"),
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
            throw new ActionException(Strings.Get("ListSort_Error_EnterThePropertyTo"));
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
            default: throw new ActionException(Strings.Get("ListSort_Error_ChooseHowToSort"));
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
        Id = "list.unique", Title = Strings.Get("ListUnique_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "Copy24",
        Keywords = new[] { "unique", "distinct", "dedupe", "duplicates", "repeat" },
        Summary = Strings.Get("ListUnique_Summary"),
        Description = Strings.Get("ListUnique_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = Strings.Get("ListUnique_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", Strings.Get("ListUnique_Param_List_Label"), isInput: true, required: true),
            ParameterDescriptor.Bool("matchCase", Strings.Get("ListUnique_Param_MatchCase_Label")),
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
        Id = "list.filter", Title = Strings.Get("ListFilter_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "Filter24",
        Keywords = new[] { "where", "only", "keep", "condition", "search", "limit", "match" },
        Summary = Strings.Get("ListFilter_Summary"),
        Description = Strings.Get("ListFilter_Description"),
        InputKinds = new[] { ContentKind.Any }, OutputKind = ContentKind.Any, OutputName = Strings.Get("ListFilter_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("list", Strings.Get("ListFilter_Param_List_Label"), isInput: true, required: true),
            new ParameterDescriptor { Key = "conditions", Label = Strings.Get("ListFilter_Param_Conditions_Label"), Type = ParamType.Conditions, AllowAskEachTime = false },
            ParameterDescriptor.Number("limit", Strings.Get("ListFilter_Param_Limit_Label"), 0, 0) with { Hint = Strings.Get("ListFilter_Param_Limit_Hint") },
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
        Id = "dict.getValue", Title = Strings.Get("DictGetValue_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "BookQuestionMark24",
        Keywords = new[] { "key", "json", "path", "property", "keys", "values", "lookup" },
        Summary = Strings.Get("DictGetValue_Summary"),
        Description = Strings.Get("DictGetValue_Description"),
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Any, OutputName = Strings.Get("DictGetValue_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("dictionary", Strings.Get("DictGetValue_Param_Dictionary_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("DictGetValue_Param_Mode_Label"), "key", new Choice("key", Strings.Get("DictGetValue_Choice_Key")), new Choice("keys", Strings.Get("DictGetValue_Choice_Keys")), new Choice("values", Strings.Get("DictGetValue_Choice_Values"))),
            ParameterDescriptor.Text("key", Strings.Get("DictGetValue_Param_Key_Label"), null, Strings.Get("DictGetValue_Param_Key_Placeholder")) with { VisibleWhen = new VisibleWhen("mode", "key") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("dictionary");
        var mode = await ctx.GetChoiceAsync("mode", "key");
        var key = (await ctx.GetTextAsync("key")).Trim();
        if (mode == "key" && key.Length == 0) throw new ActionException(Strings.Get("DictGetValue_Error_EnterTheKeyTo"));
        var dicts = DictionariesOf(input);
        if (dicts.Count == 0)
            throw new ActionException(Strings.Get("DictGetValue_Error_ThereIsNoDictionary"));
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
        Id = "dict.setValue", Title = Strings.Get("DictSetValue_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "BookAdd24",
        Keywords = new[] { "key", "add", "update", "change", "json", "put" },
        Summary = Strings.Get("DictSetValue_Summary"),
        Description = Strings.Get("DictSetValue_Description"),
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("DictSetValue_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("dictionary", Strings.Get("DictSetValue_Param_Dictionary_Label"), isInput: true),
            new ParameterDescriptor { Key = "key", Label = Strings.Get("DictSetValue_Param_Key_Label"), Type = ParamType.Text, Required = true, Placeholder = Strings.Get("DictSetValue_Param_Key_Placeholder") },
            ParameterDescriptor.Content("value", Strings.Get("DictSetValue_Param_Value_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("dictionary");
        var key = (await ctx.GetTextAsync("key")).Trim();
        var value = await ctx.GetContentAsync("value");
        if (key.Length == 0) throw new ActionException(Strings.Get("DictSetValue_Error_EnterTheKeyTo"));
        var dicts = input.IsEmpty ? new List<DictionaryItem> { DictionaryItem.Empty } : GetDictionaryValueAction.DictionariesOf(input);
        if (dicts.Count == 0) throw new ActionException(Strings.Get("DictSetValue_Error_TheInputIsnT"));
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
        Id = "dict.fromInput", Title = Strings.Get("DictFromInput_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "Braces24",
        Keywords = new[] { "json", "parse", "object", "api", "convert" },
        Summary = Strings.Get("DictFromInput_Summary"),
        Description = Strings.Get("DictFromInput_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.File }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("DictFromInput_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", Strings.Get("DictFromInput_Param_Input_Label"), isInput: true, required: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("input");
        if (input.IsEmpty) throw new ActionException(Strings.Get("DictFromInput_Error_ThereIsNoText"));
        if (input.All(i => i is DictionaryItem)) return input;
        string text;
        if (input.Count == 1 && input[0] is PathItem p and not FolderItem)
        {
            if (!File.Exists(p.Path)) throw new ActionException(Strings.Format("DictFromInput_Error_CouldnTFindThe", p.FullName));
            text = (await File.ReadAllTextAsync(p.Path, ctx.CancellationToken)).TrimStart('﻿');
        }
        else text = input.AsText();
        return Parse(text);
    }

    internal static ContentValue Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) throw new ActionException(Strings.Get("DictFromInput_Error_ThereIsNoText"));
        if (trimmed[0] is '{' or '[' or '"' || trimmed is "true" or "false" or "null" || char.IsDigit(trimmed[0]) && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            try { return ContentValue.FromJson(trimmed); }
            catch (JsonException ex)
            {
                var where = ex.LineNumber is { } line ? Strings.Format("DictFromInput_Message_NearLine0", line + 1) : "";
                throw new ActionException(Strings.Format("DictFromInput_Error_ThatTextIsnT", where), ex);
            }
        }
        if (TryKeyValues(trimmed, out var dict)) return dict;
        throw new ActionException(Strings.Get("DictFromInput_Error_ThatTextIsnT2"));
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
        Id = "dict.toJson", Title = Strings.Get("DictToJson_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "CodeBlock24",
        Keywords = new[] { "json", "serialize", "stringify", "export", "dictionary to text" },
        Summary = Strings.Get("DictToJson_Summary"),
        Description = Strings.Get("DictToJson_Description"),
        InputKinds = new[] { ContentKind.Dictionary }, OutputKind = ContentKind.Text, OutputName = Strings.Get("DictToJson_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("input", Strings.Get("DictToJson_Param_Input_Label"), isInput: true, required: true),
            ParameterDescriptor.Bool("pretty", Strings.Get("DictToJson_Param_Pretty_Label"), true),
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
