using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>Every translated Strings resource has exactly the English keys, with the same format placeholders.</summary>
public class LocalizationParityTests
{
    /// <summary>The Store languages the app ships besides English.</summary>
    public static readonly string[] Languages =
    {
        "de", "fr", "es", "it", "pt-BR", "ja", "zh-Hans", "zh-Hant", "ko", "ru",
        "pl", "nl", "tr", "ar", "hi", "id", "vi", "th", "sv",
    };

    public static IEnumerable<object[]> Resources()
    {
        yield return new object[] { "AutomationPortal", "AutomationPortal.App.Strings" };
        yield return new object[] { "AutomationPortal.Editor", "AutomationPortal.Editor.Strings" };
        yield return new object[] { "AutomationPortal.Core", "AutomationPortal.Core.Strings" };
    }

    private static readonly Regex Placeholder = new(@"\{\d+(:[^}]*)?\}", RegexOptions.CultureInvariant);

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var map = new Dictionary<string, string>();
        if (set is null) return map;
        foreach (DictionaryEntry e in set)
            if (e.Value is string s) map[(string)e.Key] = s;
        return map;
    }

    private static string Tokens(string text) =>
        string.Join("|", Placeholder.Matches(text).Select(m => m.Value).OrderBy(v => v, System.StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Resources))]
    public void Every_language_has_every_key_with_the_same_placeholders(string assemblyName, string baseName)
    {
        var assembly = Assembly.Load(assemblyName);
        var manager = new ResourceManager(baseName, assembly);
        var english = Read(manager, CultureInfo.InvariantCulture);
        Assert.NotEmpty(english);

        var problems = new List<string>();
        var folder = Path.GetDirectoryName(assembly.Location)!;
        foreach (var language in Languages)
        {
            var satellite = Path.Combine(folder, language, assemblyName + ".resources.dll");
            if (!File.Exists(satellite)) { problems.Add($"{language}: no satellite assembly"); continue; }
            var translated = Read(manager, CultureInfo.GetCultureInfo(language));
            foreach (var key in english.Keys.Where(k => !translated.ContainsKey(k)).Take(5)) problems.Add($"{language}: missing {key}");
            foreach (var key in translated.Keys.Where(k => !english.ContainsKey(k)).Take(5)) problems.Add($"{language}: extra {key}");
            foreach (var (key, text) in english)
                if (translated.TryGetValue(key, out var t) && Tokens(text) != Tokens(t)) problems.Add($"{language}: placeholders differ in {key}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
    }
}
