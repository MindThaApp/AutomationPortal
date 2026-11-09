using System.Globalization;
using Flowmate.Actions.Content.Actions;
using Flowmate.Core;
using Flowmate.Core.Testing;
using static Flowmate.Actions.Content.Tests.Kit;

namespace Flowmate.Actions.Content.Tests;

public class TextTests
{
    [Theory]
    [InlineData("newLines", "a\nb\nc")]
    [InlineData("spaces", "a b c")]
    [InlineData("commas", "a, b, c")]
    [InlineData("nothing", "abc")]
    public async Task Combine_uses_the_chosen_separator(string sep, string expected)
    {
        var r = await Run(new CombineTextAction(), Texts("a", "b", "c"), ("separator", C(sep)));
        Assert.Equal(expected, r.AsText());
        Assert.Single(r);
    }

    [Fact]
    public async Task Combine_custom_separator_understands_backslash_n()
    {
        var r = await Run(new CombineTextAction(), Texts("a", "b"), ("separator", C("custom")), ("customSeparator", "-\\n"));
        Assert.Equal("a-\nb", r.AsText());
    }

    [Fact]
    public async Task Combine_takes_the_previous_output_by_default_and_joins_numbers()
    {
        var r = await Run(new CombineTextAction(), ContentValue.Numbers(new double[] { 1, 2.5 }), ("separator", C("commas")));
        Assert.Equal("1, 2.5", r.AsText());
    }

    [Fact]
    public async Task Split_by_commas_trims_and_skips_empty()
    {
        var r = await Run(new SplitTextAction(), null, ("text", "a,b, c,,d"), ("separator", C("commas")));
        Assert.Equal(new[] { "a", "b", "c", "d" }, Lines(r));
    }

    [Fact]
    public async Task Split_can_keep_empty_pieces()
    {
        var r = await Run(new SplitTextAction(), null, ("text", "a,,b"), ("separator", C("commas")), ("removeEmpty", false));
        Assert.Equal(new[] { "a", "", "b" }, Lines(r));
    }

    [Fact]
    public async Task Split_by_lines_handles_all_line_endings()
    {
        var r = await Run(new SplitTextAction(), null, ("text", "one\r\ntwo\nthree\rfour"));
        Assert.Equal(new[] { "one", "two", "three", "four" }, Lines(r));
    }

    [Fact]
    public async Task Split_by_spaces_and_every_character_and_custom()
    {
        Assert.Equal(new[] { "a", "b", "c" }, Lines(await Run(new SplitTextAction(), null, ("text", "a   b\tc"), ("separator", C("spaces")))));
        Assert.Equal(new[] { "h", "é", "y" }, Lines(await Run(new SplitTextAction(), null, ("text", "héy"), ("separator", C("everyCharacter")))));
        Assert.Equal(new[] { "x", "y", "z" }, Lines(await Run(new SplitTextAction(), null, ("text", "x::y::z"), ("separator", C("custom")), ("custom", "::"))));
    }

    [Fact]
    public async Task Split_custom_needs_a_separator()
    {
        await Fails(Run(new SplitTextAction(), null, ("text", "a"), ("separator", C("custom"))));
    }

    [Fact]
    public async Task Replace_ignores_case_by_default()
    {
        var r = await Run(new ReplaceTextAction(), null, ("text", "Hello World"), ("find", "world"), ("replaceWith", "There"));
        Assert.Equal("Hello There", r.AsText());
    }

    [Fact]
    public async Task Replace_can_match_case()
    {
        var r = await Run(new ReplaceTextAction(), null, ("text", "Hello World"), ("find", "world"), ("replaceWith", "x"), ("matchCase", true));
        Assert.Equal("Hello World", r.AsText());
    }

    [Fact]
    public async Task Replace_with_nothing_deletes()
    {
        var r = await Run(new ReplaceTextAction(), null, ("text", "a-b-c"), ("find", "-"));
        Assert.Equal("abc", r.AsText());
    }

    [Fact]
    public async Task Replace_with_pattern_supports_groups()
    {
        var r = await Run(new ReplaceTextAction(), null, ("text", "a1b22"), ("find", "\\d+"), ("replaceWith", "#"), ("usePattern", true));
        Assert.Equal("a#b#", r.AsText());
        var g = await Run(new ReplaceTextAction(), null, ("text", "sam@x.com"), ("find", "(\\w+)@"), ("replaceWith", "<$1> at "), ("usePattern", true));
        Assert.Equal("<sam> at x.com", g.AsText());
    }

    [Fact]
    public async Task Replace_rejects_bad_patterns_and_empty_find_with_friendly_messages()
    {
        var bad = await Fails(Run(new ReplaceTextAction(), null, ("text", "a"), ("find", "("), ("usePattern", true)));
        Assert.Contains("isn't valid", bad.Message);
        var empty = await Fails(Run(new ReplaceTextAction(), null, ("text", "a"), ("find", "")));
        Assert.Contains("Enter the text to find", empty.Message);
    }

    [Fact]
    public async Task Replace_works_on_every_item_of_a_list()
    {
        var r = await Run(new ReplaceTextAction(), Texts("cat", "concat"), ("find", "cat"), ("replaceWith", "dog"));
        Assert.Equal(new[] { "dog", "condog" }, Lines(r));
    }

    [Theory]
    [InlineData("upper", "hello WORLD", "HELLO WORLD")]
    [InlineData("lower", "hello WORLD", "hello world")]
    [InlineData("title", "hello WORLD", "Hello World")]
    [InlineData("sentence", "hello world. how ARE you? fine", "Hello world. How are you? Fine")]
    [InlineData("camel", "hello big-world_now", "helloBigWorldNow")]
    [InlineData("snake", "Hello World", "hello_world")]
    [InlineData("snake", "helloWorld again", "hello_world_again")]
    [InlineData("kebab", "Hello  World Again", "hello-world-again")]
    public async Task Change_case(string mode, string input, string expected)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, (await Run(new ChangeCaseAction(), null, ("text", input), ("mode", C(mode)))).AsText()); }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Clean_up_text_options()
    {
        Assert.Equal("a b\nc", CleanUpTextAction.Clean("  a   b  \n\n  c  ", true, true, true, false));
        Assert.Equal("a b c", CleanUpTextAction.Clean("a\nb\n c", true, true, true, true));
        Assert.Equal("a\n\nb", CleanUpTextAction.Clean("a\n\nb", true, true, false, false));
        Assert.Equal("  a  b", CleanUpTextAction.Clean("  a  b", false, false, false, false));
    }

    [Fact]
    public async Task Clean_up_action_uses_sensible_defaults()
    {
        var r = await Run(new CleanUpTextAction(), null, ("text", "  Hello    there  \r\n\r\n  you "));
        Assert.Equal("Hello there\nyou", r.AsText());
    }

    [Theory]
    [InlineData("firstWords", 2, "Hello big world today", "Hello big")]
    [InlineData("lastWords", 2, "Hello big world today", "world today")]
    [InlineData("firstChars", 3, "Hello", "Hel")]
    [InlineData("lastChars", 3, "Hello", "llo")]
    [InlineData("firstChars", 99, "Hello", "Hello")]
    [InlineData("firstLines", 2, "a\nb\nc", "a\nb")]
    [InlineData("lastLines", 1, "a\nb\nc", "c")]
    public async Task Get_part_by_count(string mode, int count, string input, string expected)
    {
        var r = await Run(new GetPartOfTextAction(), null, ("text", input), ("mode", C(mode)), ("count", count));
        Assert.Equal(expected, r.AsText());
    }

    [Fact]
    public async Task Get_part_between_before_after()
    {
        Assert.Equal("abc", (await Run(new GetPartOfTextAction(), null, ("text", "x[abc]y"), ("mode", C("between")), ("from", "["), ("to", "]"))).AsText());
        Assert.Equal("me", (await Run(new GetPartOfTextAction(), null, ("text", "me@site.com"), ("mode", C("before")), ("marker", "@"))).AsText());
        Assert.Equal("site.com", (await Run(new GetPartOfTextAction(), null, ("text", "me@site.com"), ("mode", C("after")), ("marker", "@"))).AsText());
        Assert.Equal("", (await Run(new GetPartOfTextAction(), null, ("text", "nothing"), ("mode", C("after")), ("marker", "@"))).AsText());
    }

    [Fact]
    public async Task Get_part_counts_emoji_as_one_character()
    {
        var r = await Run(new GetPartOfTextAction(), null, ("text", "😀😀ab"), ("mode", C("firstChars")), ("count", 2));
        Assert.Equal("😀😀", r.AsText());
    }

    [Fact]
    public async Task Get_part_between_needs_both_markers()
    {
        await Fails(Run(new GetPartOfTextAction(), null, ("text", "x"), ("mode", C("between")), ("from", "[")));
    }

    [Fact]
    public void Find_emails_links_numbers_hashtags_mentions()
    {
        Assert.Equal(new[] { "a@b.com", "c.d@e.org" }, Lines(FindInTextAction.Find("mail a@b.com or c.d@e.org.", "email")));

        var links = FindInTextAction.Find("see https://x.com/a, and www.y.org.", "link");
        Assert.All(links, i => Assert.IsType<UrlItem>(i));
        Assert.Equal(new[] { "https://x.com/a", "https://www.y.org/" }, links.AsUrls().Select(u => u.AbsoluteUri));

        Assert.Equal(new double[] { 3, 1234.5, -7 }, FindInTextAction.Find("I have 3 apples and 1,234.5 pears, -7", "number").AsNumbers());
        Assert.Equal(new[] { "#fun", "#Swift_5" }, Lines(FindInTextAction.Find("#fun and #Swift_5 not a#b", "hashtag")));
        Assert.Equal(new[] { "@sam", "@x_y" }, Lines(FindInTextAction.Find("hi @sam and a@b.com @x_y.", "mention")));
    }

    [Fact]
    public void Find_phone_numbers_skips_dates()
    {
        var phones = Lines(FindInTextAction.Find("Call +44 20 7946 0958 or 555-1234 on 2026-10-02 or 02/10/2026", "phone"));
        Assert.Equal(new[] { "+44 20 7946 0958", "555-1234" }, phones);
    }

    [Fact]
    public void Find_custom_pattern_returns_group_when_present()
    {
        Assert.Equal(new[] { "10", "20" }, Lines(FindInTextAction.Find("10px 20px", "pattern", "(\\d+)px")));
        Assert.Equal(new[] { "10", "20" }, Lines(FindInTextAction.Find("10px 20px", "pattern", "\\d+")));
        Assert.Empty(FindInTextAction.Find("none", "pattern", "\\d+"));
    }

    [Fact]
    public async Task Find_dates_returns_date_items()
    {
        var r = await Run(new FindInTextAction(), null, ("text", "Meet on 2026-12-25 and again 2027-01-02"), ("what", C("date")));
        Assert.Equal(2, r.Count);
        Assert.All(r, i => Assert.IsType<DateItem>(i));
        Assert.Equal(new DateTime(2026, 12, 25), r.AsDates()[0].DateTime);
    }

    [Fact]
    public async Task Find_with_a_bad_pattern_explains_itself()
    {
        var ex = await Fails(Run(new FindInTextAction(), null, ("text", "x"), ("what", C("pattern")), ("pattern", "[")));
        Assert.Contains("isn't valid", ex.Message);
    }

    [Fact]
    public async Task Count_everything()
    {
        Assert.Equal(3, (await Run(new CountAction(), Texts("a", "b", "c"))).AsNumber());
        Assert.Equal(3, (await Run(new CountAction(), null, ("input", "one two  three"), ("what", C("words")))).AsNumber());
        Assert.Equal(5, (await Run(new CountAction(), null, ("input", "héllo"), ("what", C("characters")))).AsNumber());
        Assert.Equal(3, (await Run(new CountAction(), null, ("input", "a\nb\nc"), ("what", C("lines")))).AsNumber());
        Assert.Equal(3, (await Run(new CountAction(), null, ("input", "Hi there. How are you? Fine!"), ("what", C("sentences")))).AsNumber());
        Assert.Equal(0, (await Run(new CountAction(), ContentValue.Empty)).AsNumber());
    }

    [Fact]
    public async Task Text_from_input_turns_anything_into_text()
    {
        var r = await Run(new GetTextFromInputAction(), ContentValue.Concat(ContentValue.Number(1), ContentValue.Bool(true), ContentValue.Text("x")), ("separator", C("commas")));
        Assert.Equal("1, true, x", r.AsText());
    }

    [Fact]
    public async Task Encode_decode_base64_url_html()
    {
        string E(string fmt, string text, bool decode = false) =>
            Run(new EncodeDecodeAction(), null, ("text", text), ("format", C(fmt)), ("operation", C(decode ? "decode" : "encode"))).Result.AsText();

        var b64 = E("base64", "héllo wörld");
        Assert.Equal("héllo wörld", E("base64", b64, true));
        Assert.Equal("Hello", E("base64", "SGVsbG8", true));
        Assert.Equal("a%20b%26c", E("url", "a b&c"));
        Assert.Equal("a b c", E("url", "a%20b+c", true));
        Assert.Equal("&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;", E("html", "<a href=\"x\">&</a>"));
        Assert.Equal("<b>&</b>", E("html", "&lt;b&gt;&amp;&lt;/b&gt;", true));
    }

    [Fact]
    public async Task Decoding_bad_base64_is_a_friendly_error()
    {
        var ex = await Fails(Run(new EncodeDecodeAction(), null, ("text", "!!!not base64"), ("format", C("base64")), ("operation", C("decode"))));
        Assert.Contains("Base64", ex.Message);
    }

    [Fact]
    public async Task Hash_text_and_files()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", (await Run(new MakeHashAction(), null, ("input", "abc"))).AsText());
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", (await Run(new MakeHashAction(), null, ("input", "abc"), ("algorithm", C("md5")))).AsText());
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", (await Run(new MakeHashAction(), null, ("input", "abc"), ("algorithm", C("sha1")))).AsText());

        using var host = new TestHost();
        Directory.CreateDirectory(host.Root);
        var path = Path.Combine(host.Root, "abc.txt");
        await File.WriteAllTextAsync(path, "abc");
        var r = await Run(host, new MakeHashAction(), ContentValue.File(path));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", r.AsText());
        Assert.True(new MakeHashAction().Descriptor.IsAdvanced);
    }

    [Fact]
    public async Task Password_has_every_chosen_kind_of_character()
    {
        for (var i = 0; i < 100; i++)
        {
            var pw = (await Run(new GeneratePasswordAction(), null, ("length", 12))).AsText();
            Assert.Equal(12, pw.Length);
            Assert.Contains(pw, char.IsLower);
            Assert.Contains(pw, char.IsUpper);
            Assert.Contains(pw, char.IsDigit);
            Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public async Task Password_options()
    {
        var plain = (await Run(new GeneratePasswordAction(), null, ("length", 64), ("symbols", false))).AsText();
        Assert.All(plain, c => Assert.True(char.IsLetterOrDigit(c)));
        var digitsOnly = (await Run(new GeneratePasswordAction(), null, ("length", 20), ("symbols", false), ("uppercase", false), ("lowercase", false))).AsText();
        Assert.All(digitsOnly, c => Assert.True(char.IsDigit(c)));
        var clear = (await Run(new GeneratePasswordAction(), null, ("length", 128), ("avoidLookalikes", true))).AsText();
        Assert.DoesNotContain(clear, c => "Il1O0o|".Contains(c));
        await Fails(Run(new GeneratePasswordAction(), null, ("symbols", false), ("uppercase", false), ("lowercase", false), ("digits", false)));
        Assert.NotEqual((await Run(new GeneratePasswordAction(), null)).AsText(), (await Run(new GeneratePasswordAction(), null)).AsText());
    }

    [Fact]
    public async Task Passphrase_uses_words_from_the_embedded_list()
    {
        Assert.True(PasswordGenerator.WordCount > 1000);
        var p = (await Run(new GeneratePasswordAction(), null, ("style", C("words")), ("words", 5), ("addNumber", false))).AsText();
        var parts = p.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.All(parts, w => Assert.Matches("^[a-z]+$", w));

        var spaced = (await Run(new GeneratePasswordAction(), null, ("style", C("words")), ("wordSeparator", C(" ")), ("capitalize", true), ("addNumber", true))).AsText();
        var words = spaced.Split(' ');
        Assert.Equal(4, words.Length);
        Assert.All(words, w => Assert.True(char.IsUpper(w[0])));
        Assert.Contains(spaced, char.IsDigit);
    }
}
