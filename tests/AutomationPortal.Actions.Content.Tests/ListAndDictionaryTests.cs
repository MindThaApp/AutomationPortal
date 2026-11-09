using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

public class ListTests
{
    private static readonly ContentValue Abc = Texts("a", "b", "c", "d");

    [Fact]
    public async Task Get_item_first_last_index_range()
    {
        Assert.Equal("a", (await Run(new GetItemFromListAction(), Abc, ("which", C("first")))).AsText());
        Assert.Equal("d", (await Run(new GetItemFromListAction(), Abc, ("which", C("last")))).AsText());
        Assert.Equal("c", (await Run(new GetItemFromListAction(), Abc, ("which", C("index")), ("index", 3))).AsText());
        Assert.Equal(new[] { "b", "c" }, Lines(await Run(new GetItemFromListAction(), Abc, ("which", C("range")), ("index", 2), ("to", 3))));
        Assert.Equal(new[] { "b", "c" }, Lines(await Run(new GetItemFromListAction(), Abc, ("which", C("range")), ("index", 3), ("to", 2))));
        Assert.Equal(new[] { "c", "d" }, Lines(await Run(new GetItemFromListAction(), Abc, ("which", C("range")), ("index", 3), ("to", 99))));
    }

    [Fact]
    public async Task Get_random_item_eventually_returns_each_item()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++) seen.Add((await Run(new GetItemFromListAction(), Abc, ("which", C("random")))).AsText());
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public async Task Get_item_keeps_the_item_type()
    {
        var files = ContentValue.Concat(ContentValue.Number(5), ContentValue.Date(Now));
        Assert.IsType<NumberItem>((await Run(new GetItemFromListAction(), files, ("which", C("first"))))[0]);
        Assert.IsType<DateItem>((await Run(new GetItemFromListAction(), files, ("which", C("last"))))[0]);
    }

    [Fact]
    public async Task Get_item_out_of_range_explains()
    {
        var ex = await Fails(Run(new GetItemFromListAction(), Abc, ("which", C("index")), ("index", 9)));
        Assert.Contains("4 items", ex.Message);
        Assert.Contains("position 9", ex.Message);
        await Fails(Run(new GetItemFromListAction(), Abc, ("which", C("index")), ("index", 0)));
        await Fails(Run(new GetItemFromListAction(), Abc, ("which", C("range")), ("index", 9), ("to", 10)));
        Assert.True((await Run(new GetItemFromListAction(), ContentValue.Empty, ("which", C("first")))).IsEmpty);
        await Fails(Run(new GetItemFromListAction(), ContentValue.Empty, ("which", C("index")), ("index", 1)));
    }

    [Fact]
    public async Task Sort_text_naturally_and_ignoring_case()
    {
        var items = Texts("item 10", "item 2", "Item 1", "banana", "Apple");
        Assert.Equal(new[] { "Apple", "banana", "Item 1", "item 2", "item 10" }, Lines(await Run(new SortListAction(), items, ("order", C("az")))));
        Assert.Equal(new[] { "item 10", "item 2", "Item 1", "banana", "Apple" }, Lines(await Run(new SortListAction(), items, ("order", C("za")))));
    }

    [Fact]
    public async Task Sort_by_number_puts_non_numbers_last()
    {
        var items = Texts("10", "9", "x", "2", "-5");
        Assert.Equal(new[] { "-5", "2", "9", "10", "x" }, Lines(await Run(new SortListAction(), items, ("order", C("numAsc")))));
        Assert.Equal(new[] { "10", "9", "2", "-5", "x" }, Lines(await Run(new SortListAction(), items, ("order", C("numDesc")))));
    }

    [Fact]
    public async Task Sort_by_date_understands_dates_and_phrases()
    {
        var items = ContentValue.Concat(Texts("2026-03-01"), ContentValue.Date(L(2026, 1, 1)), Texts("2026-02-01"), Texts("not a date"));
        var asc = await Run(new SortListAction(), items, ("order", C("dateAsc")));
        Assert.Equal(new[] { "2026-01-01", "2026-02-01", "2026-03-01" }, asc.Take(3).Select(i => ActionHelpersDate(i)));
        Assert.Equal("not a date", asc[3].DisplayText);
        var desc = await Run(new SortListAction(), items, ("order", C("dateDesc")));
        Assert.Equal("2026-03-01", ActionHelpersDate(desc[0]));
        Assert.Equal("not a date", desc[3].DisplayText);
    }

    private static string ActionHelpersDate(ContentItem i) => ContentValue.From(i).AsDate()!.Value.DateTime.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Sort_by_property_of_text_and_files()
    {
        var words = Texts("ccc", "a", "bb");
        Assert.Equal(new[] { "a", "bb", "ccc" }, Lines(await Run(new SortListAction(), words, ("order", C("property")), ("property", "Length"))));
        Assert.Equal(new[] { "ccc", "bb", "a" }, Lines(await Run(new SortListAction(), words, ("order", C("propertyDesc")), ("property", "length"))));

        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        string Make(string name, int size) { var p = Path.Combine(host.Root, name); File.WriteAllBytes(p, new byte[size]); return p; }
        var files = ContentValue.Files(new[] { Make("big.bin", 300), Make("small.bin", 10), Make("mid.bin", 100) });
        var bySize = await Run(host, new SortListAction(), files, ("order", C("property")), ("property", "Size"));
        Assert.Equal(new[] { "small.bin", "mid.bin", "big.bin" }, bySize.Property("Full Name").AsTexts());
        var byName = await Run(host, new SortListAction(), files, ("order", C("property")), ("property", "Name"));
        Assert.Equal(new[] { "big", "mid", "small" }, byName.Property("Name").AsTexts());
    }

    [Fact]
    public async Task Sort_dictionaries_by_a_key_with_mixed_types()
    {
        ContentItem Person(string name, double age) => new DictionaryItem(new[]
        {
            new KeyValuePair<string, ContentValue>("name", name), new KeyValuePair<string, ContentValue>("age", age),
        });
        var people = new ContentValue(new[] { Person("Cy", 30), Person("Al", 9), Person("Bo", 100) });
        var byAge = await Run(new SortListAction(), people, ("order", C("property")), ("property", "age"));
        Assert.Equal(new[] { "Al", "Cy", "Bo" }, byAge.Select(i => ((DictionaryItem)i).Get("name").AsText()));
        var byName = await Run(new SortListAction(), people, ("order", C("propertyDesc")), ("property", "key:name"));
        Assert.Equal(new[] { "Cy", "Bo", "Al" }, byName.Select(i => ((DictionaryItem)i).Get("name").AsText()));
    }

    [Fact]
    public async Task Sort_by_property_needs_a_property_name()
    {
        var ex = await Fails(Run(new SortListAction(), Abc, ("order", C("property"))));
        Assert.Contains("property", ex.Message);
    }

    [Fact]
    public async Task Shuffle_and_reverse()
    {
        var many = ContentValue.Numbers(Enumerable.Range(1, 30).Select(i => (double)i));
        var shuffled = await Run(new SortListAction(), many, ("order", C("shuffle")));
        Assert.Equal(many.AsNumbers().OrderBy(x => x), shuffled.AsNumbers().OrderBy(x => x));
        Assert.NotEqual(many.AsNumbers(), shuffled.AsNumbers());
        Assert.Equal(new[] { "d", "c", "b", "a" }, Lines(await Run(new SortListAction(), Abc, ("order", C("reverse")))));
    }

    [Fact]
    public async Task Remove_duplicates()
    {
        var items = Texts("a", "A", "b", "a", "B", "c");
        Assert.Equal(new[] { "a", "b", "c" }, Lines(await Run(new RemoveDuplicatesAction(), items)));
        Assert.Equal(new[] { "a", "A", "b", "B", "c" }, Lines(await Run(new RemoveDuplicatesAction(), items, ("matchCase", true))));
        var mixed = await Run(new RemoveDuplicatesAction(), ContentValue.Concat(ContentValue.Number(1), Texts("1"), ContentValue.Number(2)));
        Assert.Equal(2, mixed.Count);
    }

    [Fact]
    public async Task Filter_by_the_item_itself()
    {
        var conditions = ParamValue.AllOf(ConditionRow.Of(ParamValue.Text(""), ConditionOperators.Contains, "an"));
        var r = await Run(new FilterListAction(), Texts("banana", "apple", "mango", "kiwi"), ("conditions", conditions));
        Assert.Equal(new[] { "banana", "mango" }, Lines(r));
    }

    [Fact]
    public async Task Filter_by_a_property_with_all_and_any()
    {
        var items = Texts("a", "bb", "ccc", "dddd");
        var gt = ParamValue.AllOf(ConditionRow.Of("Length", ConditionOperators.GreaterThan, 1), ConditionRow.Of("Length", ConditionOperators.LessThan, 4));
        Assert.Equal(new[] { "bb", "ccc" }, Lines(await Run(new FilterListAction(), items, ("conditions", gt))));
        var any = ParamValue.AnyOf(ConditionRow.Of("Length", ConditionOperators.Equals_, 1), ConditionRow.Of("Length", ConditionOperators.Equals_, 4));
        Assert.Equal(new[] { "a", "dddd" }, Lines(await Run(new FilterListAction(), items, ("conditions", any))));
        var between = ParamValue.AllOf(ConditionRow.Of("Length", ConditionOperators.Between, 2, 3));
        Assert.Equal(new[] { "bb", "ccc" }, Lines(await Run(new FilterListAction(), items, ("conditions", between))));
    }

    [Fact]
    public async Task Filter_files_by_kind_and_size_and_limit()
    {
        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        string Make(string name, int size) { var p = Path.Combine(host.Root, name); File.WriteAllBytes(p, new byte[size]); return p; }
        var files = ContentValue.Files(new[] { Make("a.png", 5), Make("b.pdf", 500), Make("c.png", 600), Make("d.png", 700) });

        var png = ParamValue.AllOf(ConditionRow.Of("Kind", ConditionOperators.Is, "Image"));
        Assert.Equal(new[] { "a.png", "c.png", "d.png" }, (await Run(host, new FilterListAction(), files, ("conditions", png))).Property("Full Name").AsTexts());

        var big = ParamValue.AllOf(ConditionRow.Of("Kind", ConditionOperators.Is, "Image"), ConditionRow.Of("Size", ConditionOperators.GreaterThan, 100));
        Assert.Equal(new[] { "c.png", "d.png" }, (await Run(host, new FilterListAction(), files, ("conditions", big))).Property("Full Name").AsTexts());

        var limited = await Run(host, new FilterListAction(), files, ("conditions", png), ("limit", 2));
        Assert.Equal(new[] { "a.png", "c.png" }, limited.Property("Full Name").AsTexts());
    }

    [Fact]
    public async Task Filter_dictionaries_by_key_and_dates_by_property()
    {
        var people = new ContentValue(new[]
        {
            (ContentItem)new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("city", "Paris") }),
            new DictionaryItem(new[] { new KeyValuePair<string, ContentValue>("city", "Rome") }),
        });
        var r = await Run(new FilterListAction(), people, ("conditions", ParamValue.AllOf(ConditionRow.Of("key:city", ConditionOperators.Is, "Rome"))));
        Assert.Single(r);

        var dates = ContentValue.Concat(ContentValue.Date(L(2025, 5, 1)), ContentValue.Date(L(2026, 5, 1)));
        var y2026 = await Run(new FilterListAction(), dates, ("conditions", ParamValue.AllOf(ConditionRow.Of("Year", ConditionOperators.Equals_, 2026))));
        Assert.Single(y2026);
    }

    [Fact]
    public async Task Filter_without_conditions_keeps_everything()
    {
        var r = await Run(new FilterListAction(), Abc);
        Assert.Equal(4, r.Count);
    }
}

public class DictionaryTests
{
    private static ContentValue Json(string json) => ContentValue.FromJson(json);

    [Fact]
    public async Task Get_value_by_key_and_path()
    {
        var d = Json("{\"name\":\"Sam\",\"age\":7,\"address\":{\"city\":\"Rome\",\"tags\":[\"a\",\"b\"]},\"a.b\":\"literal\"}");
        Assert.Equal("Sam", (await Run(new GetDictionaryValueAction(), d, ("key", "name"))).AsText());
        Assert.Equal("sam", (await Run(new GetDictionaryValueAction(), Json("{\"Name\":\"sam\"}"), ("key", "NAME"))).AsText());
        Assert.Equal(7, (await Run(new GetDictionaryValueAction(), d, ("key", "age"))).AsNumber());
        Assert.Equal("Rome", (await Run(new GetDictionaryValueAction(), d, ("key", "address.city"))).AsText());
        Assert.Equal("b", (await Run(new GetDictionaryValueAction(), d, ("key", "address.tags.1"))).AsText());
        Assert.Equal(new[] { "a", "b" }, Lines(await Run(new GetDictionaryValueAction(), d, ("key", "address.tags"))));
        Assert.Equal("literal", (await Run(new GetDictionaryValueAction(), d, ("key", "a.b"))).AsText());
        Assert.Equal("Sam", (await Run(new GetDictionaryValueAction(), d, ("key", "key:name"))).AsText());
        Assert.True((await Run(new GetDictionaryValueAction(), d, ("key", "nope.deeper"))).IsEmpty);
    }

    [Fact]
    public async Task Get_keys_values_and_json_text_input()
    {
        var d = Json("{\"a\":1,\"b\":[2,3]}");
        Assert.Equal(new[] { "a", "b" }, Lines(await Run(new GetDictionaryValueAction(), d, ("mode", C("keys")))));
        Assert.Equal(new double[] { 1, 2, 3 }, (await Run(new GetDictionaryValueAction(), d, ("mode", C("values")))).AsNumbers());
        Assert.Equal("Sam", (await Run(new GetDictionaryValueAction(), ContentValue.Text("{\"name\":\"Sam\"}"), ("key", "name"))).AsText());
    }

    [Fact]
    public async Task Get_value_from_a_list_of_dictionaries_maps_over_them()
    {
        var rows = Json("[{\"n\":\"a\"},{\"n\":\"b\"},{\"x\":1}]");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b" }, Lines(await Run(new GetDictionaryValueAction(), rows, ("key", "n"))));
    }

    [Fact]
    public async Task Get_value_errors_are_friendly()
    {
        var ex = await Fails(Run(new GetDictionaryValueAction(), ContentValue.Text("just words"), ("key", "x")));
        Assert.Contains("no dictionary", ex.Message);
        await Fails(Run(new GetDictionaryValueAction(), Json("{\"a\":1}")));
    }

    [Fact]
    public async Task Set_value_adds_replaces_and_nests()
    {
        var d = Json("{\"name\":\"Sam\"}");
        var added = (DictionaryItem)(await Run(new SetDictionaryValueAction(), d, ("key", "age"), ("value", 7)))[0];
        Assert.Equal(new[] { "name", "age" }, added.Keys);
        Assert.Equal(7, added.Get("age").AsNumber());

        var replaced = (DictionaryItem)(await Run(new SetDictionaryValueAction(), d, ("key", "NAME"), ("value", "Al")))[0];
        Assert.Equal(new[] { "name" }, replaced.Keys);
        Assert.Equal("Al", replaced.Get("name").AsText());

        var nested = (DictionaryItem)(await Run(new SetDictionaryValueAction(), d, ("key", "address.city"), ("value", "Rome")))[0];
        Assert.Equal("Rome", nested.GetPath("address.city").AsText());
        var deeper = (DictionaryItem)(await Run(new SetDictionaryValueAction(), ContentValue.From(nested), ("key", "address.zip"), ("value", "00100")))[0];
        Assert.Equal("Rome", deeper.GetPath("address.city").AsText());
        Assert.Equal("00100", deeper.GetPath("address.zip").AsText());
        Assert.Equal("Sam", d.AsDictionary()!.Get("name").AsText());   // original untouched
    }

    [Fact]
    public async Task Set_value_starts_a_new_dictionary_and_keeps_value_types()
    {
        var made = (DictionaryItem)(await Run(new SetDictionaryValueAction(), ContentValue.Empty, ("key", "when"), ("value", ParamValue.Date(Now))))[0];
        Assert.IsType<DateItem>(made.Get("when")[0]);
        var list = await Run(new SetDictionaryValueAction(), Json("[{\"a\":1},{\"a\":2}]"), ("key", "b"), ("value", true));
        Assert.Equal(2, list.Count);
        Assert.All(list, i => Assert.True(((DictionaryItem)i).Get("b").AsBool()));
        await Fails(Run(new SetDictionaryValueAction(), Json("{}"), ("value", 1)));
        await Fails(Run(new SetDictionaryValueAction(), ContentValue.Text("not json"), ("key", "a")));
    }

    [Fact]
    public async Task From_input_parses_json_objects_arrays_and_scalars()
    {
        var obj = await Run(new GetDictionaryFromInputAction(), ContentValue.Text("{\"a\":1,\"b\":{\"c\":[1,2]}}"));
        Assert.IsType<DictionaryItem>(obj[0]);
        Assert.Equal(2, ((DictionaryItem)obj[0]).GetPath("b.c").Count);

        var arr = await Run(new GetDictionaryFromInputAction(), ContentValue.Text("[{\"a\":1},{\"a\":2}]"));
        Assert.Equal(2, arr.Count);

        var padded = await Run(new GetDictionaryFromInputAction(), ContentValue.Text("\n  {\"ok\": true}  \n"));
        Assert.True(((DictionaryItem)padded[0]).Get("ok").AsBool());
        var trailing = await Run(new GetDictionaryFromInputAction(), ContentValue.Text("{\"a\":1,}"));
        Assert.IsType<DictionaryItem>(trailing[0]);
    }

    [Fact]
    public async Task From_input_passes_dictionaries_through_and_reads_files()
    {
        var d = Json("{\"a\":1}");
        Assert.Same(d[0], (await Run(new GetDictionaryFromInputAction(), d))[0]);

        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        var path = Path.Combine(host.Root, "data.json");
        await File.WriteAllTextAsync(path, "﻿{\"fromFile\": \"yes\"}");
        var r = await Run(host, new GetDictionaryFromInputAction(), ContentValue.File(path));
        Assert.Equal("yes", ((DictionaryItem)r[0]).Get("fromFile").AsText());
        await Fails(Run(host, new GetDictionaryFromInputAction(), ContentValue.File(Path.Combine(host.Root, "missing.json"))));
    }

    [Fact]
    public async Task From_input_understands_simple_lines_and_query_strings()
    {
        var lines = (DictionaryItem)(await Run(new GetDictionaryFromInputAction(), ContentValue.Text("Name: Sam\nAge: 7")))[0];
        Assert.Equal("Sam", lines.Get("Name").AsText());
        var query = (DictionaryItem)(await Run(new GetDictionaryFromInputAction(), ContentValue.Text("?a=1&b=hello%20world")))[0];
        Assert.Equal("hello world", query.Get("b").AsText());
    }

    [Fact]
    public async Task From_input_reports_bad_json_with_a_hint()
    {
        var ex = await Fails(Run(new GetDictionaryFromInputAction(), ContentValue.Text("{\"a\": 1\n\"b\": 2}")));
        Assert.Contains("isn't valid JSON", ex.Message);
        Assert.Contains("line", ex.Message);
        var words = await Fails(Run(new GetDictionaryFromInputAction(), ContentValue.Text("hello there")));
        Assert.Contains("isn't JSON", words.Message);
        await Fails(Run(new GetDictionaryFromInputAction(), ContentValue.Empty));
    }

    [Fact]
    public async Task To_json_pretty_and_compact_round_trip()
    {
        var d = ContentValue.Dict(("name", "Sam"), ("age", 7), ("tags", Texts("a", "b")), ("ok", true));
        var compact = (await Run(new MakeJsonAction(), d, ("pretty", false))).AsText();
        Assert.Equal("{\"name\":\"Sam\",\"age\":7,\"tags\":[\"a\",\"b\"],\"ok\":true}", compact);
        var pretty = (await Run(new MakeJsonAction(), d)).AsText();
        Assert.Contains("\n", pretty);
        var back = ContentValue.FromJson(pretty);
        Assert.Equal(compact, back.ToJson());
        Assert.Equal("\"héllo\"", (await Run(new MakeJsonAction(), Texts("héllo"), ("pretty", false))).AsText());
        Assert.Equal("", (await Run(new MakeJsonAction(), ContentValue.Empty)).AsText());
    }
}
