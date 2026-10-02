using Flowmate.Triggers;

namespace Flowmate.Triggers.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", "Ctrl+Alt+M")]
    [InlineData("control + alt + m", "Ctrl+Alt+M")]
    [InlineData("Win+Shift+Ctrl+Alt+k", "Ctrl+Alt+Shift+Win+K")]
    [InlineData("ctrl+space", "Ctrl+Space")]
    [InlineData("Ctrl+Alt+Esc", "Ctrl+Alt+Esc")]
    [InlineData("Ctrl+Escape", "Ctrl+Esc")]
    [InlineData("Alt+F4", "Alt+F4")]
    [InlineData("Ctrl+Shift+F12", "Ctrl+Shift+F12")]
    [InlineData("Ctrl+Alt+Numpad5", "Ctrl+Alt+Numpad5")]
    [InlineData("Ctrl+PgDn", "Ctrl+PageDown")]
    [InlineData("Ctrl+Alt+Del", "Ctrl+Alt+Delete")]
    [InlineData("Ctrl+;", "Ctrl+;")]
    [InlineData("Ctrl+`", "Ctrl+`")]
    [InlineData("Win+1", "Win+1")]
    [InlineData("Ctrl+Alt+Left", "Ctrl+Alt+Left")]
    public void Parses_and_formats_in_the_standard_order(string input, string expected)
    {
        Assert.True(HotkeyParser.TryParse(input, out var chord));
        Assert.Equal(expected, HotkeyParser.Format(chord));
        Assert.Equal(expected, HotkeyParser.Normalize(input));
    }

    [Fact]
    public void Plus_key_can_be_written_as_Plus_or_trailing_plus()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Plus", out var a));
        Assert.True(HotkeyParser.TryParse("Ctrl++", out var b));
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Foo+M")]
    [InlineData("Ctrl+F25")]
    public void Rejects_what_it_cannot_understand(string input)
    {
        Assert.False(HotkeyParser.TryParse(input, out _));
        Assert.Null(HotkeyParser.Normalize(input));
    }

    [Fact]
    public void Parse_throws_a_friendly_message()
    {
        var ex = Assert.Throws<FormatException>(() => HotkeyParser.Parse("nonsense"));
        Assert.Contains("isn't a valid keyboard shortcut", ex.Message);
    }

    [Fact]
    public void Virtual_key_codes_match_Windows()
    {
        Assert.Equal(0x4D, HotkeyParser.Parse("Ctrl+M").VirtualKey);
        Assert.Equal(0x20, HotkeyParser.Parse("Ctrl+Space").VirtualKey);
        Assert.Equal(0x70, HotkeyParser.Parse("F1").VirtualKey);
        Assert.Equal(0x87, HotkeyParser.Parse("F24").VirtualKey);
        Assert.Equal(0x1B, HotkeyParser.Parse("Ctrl+Esc").VirtualKey);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Win, HotkeyParser.Parse("Win+Ctrl+Q").Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+Alt+M", true)]
    [InlineData("Win+E", true)]
    [InlineData("F9", true)]
    [InlineData("M", false)]
    [InlineData("Shift+M", false)]
    [InlineData("Shift+Space", false)]
    [InlineData("Ctrl+C", false)]
    [InlineData("Ctrl+v", false)]
    [InlineData("Ctrl+Shift+Z", false)]
    [InlineData("Ctrl+Alt+C", true)]
    [InlineData("Ctrl+Shift+C", true)]
    public void Global_shortcuts_must_not_hijack_normal_typing(string input, bool safe) =>
        Assert.Equal(safe, HotkeyParser.IsSafeGlobal(HotkeyParser.Parse(input)));

    [Fact]
    public void Round_trips_every_letter_digit_and_function_key()
    {
        foreach (var key in Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                     .Concat(Enumerable.Range(0, 10).Select(d => d.ToString()))
                     .Concat(Enumerable.Range(1, 24).Select(n => "F" + n)))
        {
            var text = "Ctrl+" + key;
            Assert.Equal(text, HotkeyParser.Format(HotkeyParser.Parse(text)));
        }
    }
}

public class AppMatcherTests
{
    [Theory]
    [InlineData("C:\\Program Files\\Spotify\\Spotify.exe", "Spotify", "spotify", "C:\\Program Files\\Spotify\\Spotify.exe", true)]
    [InlineData("spotify.exe", null, "Spotify", null, true)]
    [InlineData("Spotify", "Spotify", "Spotify", "C:\\x\\Spotify.exe", true)]
    [InlineData("C:\\Other\\Spotify.exe", null, "Spotify", "C:\\x\\Spotify.exe", true)]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator", "CalculatorApp", "C:\\Program Files\\WindowsApps\\Microsoft.WindowsCalculator_11.0_x64__8wekyb3d8bbwe\\CalculatorApp.exe", true)]
    [InlineData("Notepad", "Notepad", "chrome", "C:\\x\\chrome.exe", false)]
    [InlineData("", "", "chrome", null, false)]
    public void Matches_the_many_ways_an_app_can_be_named(string id, string? name, string process, string? path, bool expected) =>
        Assert.Equal(expected, AppMatcher.Matches(id, name, process, path));

    [Fact]
    public void Matches_the_friendly_name_from_the_file_description()
    {
        Assert.True(AppMatcher.Matches("Google Chrome", "Google Chrome", "chrome", "C:\\x\\chrome.exe", "Google Chrome"));
    }
}
