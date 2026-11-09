using System.Text.Json;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class ModelAndContentTests
{
    [Fact]
    public void ParamValues_RoundTrip_ThroughJson()
    {
        var values = new ParamValue[]
        {
            ParamValue.Text("Hi ", VarRef.Output("abc").WithProperty("Name").As(ContentKind.Text).Picking(VarPick.First), "!"),
            ParamValue.Number(12.5), ParamValue.Bool(true), ParamValue.Choice("spaces"),
            ParamValue.Var(VarRef.Named("Total")), ParamValue.Var(VarRef.ShortcutInput), ParamValue.Var(VarRef.RepeatItem),
            ParamValue.List(ParamValue.Text("a"), ParamValue.Number(2)),
            ParamValue.Dict(("k", ParamValue.Text("v"))), ParamValue.Ask(),
            ParamValue.AnyOf(ConditionRow.Of(ParamValue.Output("x"), ConditionOperators.Between, 1, 5)),
            ParamValue.Date(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(1))),
            ParamValue.Time("07:30"), ParamValue.Duration(90), ParamValue.Hotkey("Ctrl+Alt+K"),
            ParamValue.File(@"C:\a.txt"), ParamValue.App("id", "Name"),
        };
        foreach (var v in values)
        {
            var json = AutomationPortalJson.Serialize(v);
            var back = AutomationPortalJson.Deserialize<ParamValue>(json)!;
            Assert.Equal(json, AutomationPortalJson.Serialize(back));
            Assert.Equal(v.GetType(), back.GetType());
        }
    }

    [Fact]
    public void ParamValue_JsonShape_MatchesSpec()
    {
        Assert.Equal("{\"t\":\"text\",\"parts\":[\"Hello \",{\"var\":{\"kind\":\"output\",\"uid\":\"u1\"}},\"!\"]}",
            AutomationPortalJson.Serialize(ParamValue.Text("Hello ", VarRef.Output("u1"), "!")));
        Assert.Equal("{\"t\":\"num\",\"v\":5}", AutomationPortalJson.Serialize(ParamValue.Number(5)));
        Assert.Equal("{\"t\":\"ask\"}", AutomationPortalJson.Serialize(ParamValue.Ask()));
    }

    [Fact]
    public void Shortcut_RoundTrips_AndClones()
    {
        var b = new ShortcutBuilder("Demo");
        var t = b.Add("content.text", ("text", ParamValue.Text("x")));
        b.If(ParamValue.AllOf(ConditionRow.Of(ParamValue.Output(t), ConditionOperators.Contains, "x")), s => s.Add("flow.nothing"));
        var sc = b.Build();
        sc.Alias = "dm";
        var json = AutomationPortalJson.Serialize(sc, true);
        var back = AutomationPortalJson.Deserialize<Shortcut>(json)!;
        Assert.Equal(sc.Actions.Count, back.Actions.Count);
        Assert.Equal("dm", back.Alias);
        Assert.Equal(json, AutomationPortalJson.Serialize(back, true));
        Assert.NotSame(sc, sc.Clone());
    }

    [Fact]
    public void Text_Properties()
    {
        var v = ContentValue.Text("one two\nthree");
        Assert.Equal(3, v.Property("Word Count").AsNumber());
        Assert.Equal(2, v.Property("Line Count").AsNumber());
        Assert.Equal(13, v.Property("Length").AsNumber());
    }

    [Fact]
    public void Date_Properties()
    {
        var v = ContentValue.Date(new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.Zero));
        Assert.Equal(2026, v.Property("Year").AsNumber());
        Assert.Equal(10, v.Property("Month").AsNumber());
        Assert.Equal(9, v.Property("Hour").AsNumber());
        Assert.NotEmpty(v.Property("Weekday").AsText());
    }

    [Fact]
    public void File_Properties_AndSizeText()
    {
        var path = Path.Combine(Path.GetTempPath(), "ap-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        File.WriteAllBytes(path, new byte[1536]);
        try
        {
            var v = ContentValue.File(path);
            Assert.Equal(1536, v.Property("Size").AsNumber());
            Assert.Equal("1.5 KB", v.Property("Size Text").AsText());
            Assert.Equal("txt", v.Property("Extension").AsText());
            Assert.Equal(Path.GetFileNameWithoutExtension(path), v.Property("Name").AsText());
            Assert.Equal("Document", v.Property("Kind").AsText());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("12.5 kg", 12.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("-3", -3)]
    [InlineData("costs $.5 today", 0.5)]
    public void AsNumber_IsForgiving(string text, double expected) => Assert.Equal(expected, ContentValue.Text(text).AsNumber());

    [Theory]
    [InlineData("yes", true)] [InlineData("true", true)] [InlineData("1", true)]
    [InlineData("no", false)] [InlineData("0", false)] [InlineData("", false)]
    public void AsBool_IsForgiving(string text, bool expected) => Assert.Equal(expected, ContentValue.Text(text).AsBool());

    [Fact]
    public void AsUrls_FindsLinksInText()
    {
        var urls = ContentValue.Text("see https://example.com/a?b=1, and www.test.org.").AsUrls();
        Assert.Equal(2, urls.Count);
        Assert.Equal("example.com", urls[0].Host);
        Assert.Equal("www.test.org", urls[1].Host);
    }

    [Fact]
    public void Json_ToAndFromDictionary()
    {
        var v = ContentValue.FromJson("{\"name\":\"Sam\",\"age\":30,\"tags\":[\"a\",\"b\"],\"home\":{\"city\":\"Oslo\"}}");
        var d = v.AsDictionary()!;
        Assert.Equal("Sam", d.Get("name").AsText());
        Assert.Equal(2, d.Get("tags").Count);
        Assert.Equal("Oslo", d.GetPath("home.city").AsText());
        Assert.Equal("b", d.GetPath("tags.1").AsText());
        Assert.Equal("Oslo", v.Property("key:home").AsDictionary()!.Get("city").AsText());
        Assert.Equal("Sam", ContentValue.Text("{\"name\":\"Sam\"}").AsDictionary()!.Get("name").AsText());
        var round = ContentValue.FromJson(v.ToJson());
        Assert.Equal(30, round.AsDictionary()!.Get("age").AsNumber());
    }

    [Fact]
    public void ContentValue_TypedJson_RoundTrips()
    {
        var v = ContentValue.Concat(ContentValue.Text("a"), ContentValue.Number(2), ContentValue.Bool(true),
            ContentValue.Date(DateTimeOffset.Now), ContentValue.Url("https://x.org/"), ContentValue.File(@"C:\x.txt"),
            ContentValue.Dict(("k", ContentValue.Number(1))), ContentValue.From(new LocationItem(1, 2, "Home")));
        var back = AutomationPortalJson.Deserialize<ContentValue>(AutomationPortalJson.Serialize(v))!;
        Assert.Equal(v.Select(i => i.Kind), back.Select(i => i.Kind));
        Assert.Equal(v.AsText(), back.AsText());
    }

    [Fact]
    public void DictionaryItem_With_ReplacesAndAppends()
    {
        var d = DictionaryItem.Empty.With("a", 1.0).With("b", 2.0).With("A", 3.0);
        Assert.Equal(new[] { "a", "b" }, d.Keys);
        Assert.Equal(3, d.Get("a").AsNumber());
    }

    [Fact]
    public void Conditions_Operators()
    {
        bool E(string l, string op, string r = "", string r2 = "") =>
            ConditionEvaluator.Evaluate(l, op, r, r2);
        Assert.True(E("Hello", ConditionOperators.Contains, "ell"));
        Assert.True(E("Hello", ConditionOperators.BeginsWith, "he"));
        Assert.True(E("Hello", ConditionOperators.EndsWith, "LO"));
        Assert.True(E("Hello", ConditionOperators.IsNot, "x"));
        Assert.True(E("a1", ConditionOperators.Matches, @"^[a-z]\d$"));
        Assert.True(E("x", ConditionOperators.HasValue));
        Assert.True(E("", ConditionOperators.HasNoValue));
        Assert.True(E("5", ConditionOperators.GreaterThan, "3"));
        Assert.True(E("5", ConditionOperators.Between, "9", "1"));
        Assert.False(E("5", ConditionOperators.LessThan, "5"));
        Assert.True(E("5", ConditionOperators.LessOrEqual, "5"));
        Assert.True(E("yes", ConditionOperators.IsTrue));
        Assert.True(E("today", ConditionOperators.IsToday));
        Assert.True(E("2020-01-01", ConditionOperators.IsBefore, "2021-01-01"));
        Assert.True(E("2022-01-01", ConditionOperators.DateBetween, "2021-01-01", "2023-01-01"));
    }
}
