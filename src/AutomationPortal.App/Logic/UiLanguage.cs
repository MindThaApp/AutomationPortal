using System.Globalization;
using System.IO;
using System.Text.Json;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>One language the app ships text for.</summary>
/// <param name="Code">Culture name used for the resources ("de", "pt-BR", "zh-Hant").</param>
/// <param name="NativeName">The name in the language itself.</param>
/// <param name="EnglishName">The name in English (secondary text).</param>
/// <param name="FlagRegion">Region whose flag stands for the language, or null when <paramref name="Badge"/> is used.</param>
/// <param name="Badge">Short text shown instead of a flag.</param>
/// <param name="Welcome">A short welcome in the language.</param>
public sealed record UiLanguageInfo(string Code, string NativeName, string EnglishName, string? FlagRegion, string? Badge, string Welcome)
{
    public bool IsRightToLeft => CultureInfo.GetCultureInfo(Code).TextInfo.IsRightToLeft;
}

/// <summary>
/// The app's display language. The culture is set once, at the very start, because action descriptors read their text on
/// first use; a change in Settings therefore takes effect after a restart. Number and date formats stay with Windows.
/// </summary>
public static class UiLanguage
{
    public const string English = "en-US";

    public static readonly IReadOnlyList<UiLanguageInfo> All = new UiLanguageInfo[]
    {
        new(English, "English", "English", "US", null, "Welcome!"),
        new("de", "Deutsch", "German", "DE", null, "Willkommen!"),
        new("fr", "Français", "French", "FR", null, "Bienvenue !"),
        new("es", "Español", "Spanish", "ES", null, "¡Bienvenido!"),
        new("it", "Italiano", "Italian", "IT", null, "Benvenuto!"),
        new("pt-BR", "Português (Brasil)", "Portuguese (Brazil)", "BR", null, "Bem-vindo!"),
        new("ja", "日本語", "Japanese", "JP", null, "ようこそ!"),
        new("zh-Hans", "简体中文", "Chinese (Simplified)", "CN", null, "欢迎!"),
        new("zh-Hant", "繁體中文", "Chinese (Traditional)", null, "繁", "歡迎!"),
        new("ko", "한국어", "Korean", "KR", null, "환영합니다!"),
        new("ru", "Русский", "Russian", "RU", null, "Добро пожаловать!"),
        new("pl", "Polski", "Polish", "PL", null, "Witamy!"),
        new("nl", "Nederlands", "Dutch", "NL", null, "Welkom!"),
        new("tr", "Türkçe", "Turkish", "TR", null, "Hoş geldiniz!"),
        new("ar", "العربية", "Arabic", "SA", null, "أهلاً وسهلاً!"),
        new("hi", "हिन्दी", "Hindi", "IN", null, "स्वागत है!"),
        new("id", "Bahasa Indonesia", "Indonesian", "ID", null, "Selamat datang!"),
        new("vi", "Tiếng Việt", "Vietnamese", "VN", null, "Chào mừng!"),
        new("th", "ไทย", "Thai", "TH", null, "ยินดีต้อนรับ!"),
        new("sv", "Svenska", "Swedish", "SE", null, "Välkommen!"),
    };

    private static CultureInfo? _windows;

    /// <summary>The Windows display language, captured before the app changes the UI culture.</summary>
    public static CultureInfo WindowsUiCulture => _windows ??= CultureInfo.CurrentUICulture;

    /// <summary>The setting the running app started with ("" = Windows). A different choice needs a restart.</summary>
    public static string StartupSetting { get; private set; } = "";

    /// <summary>The language the running app shows.</summary>
    public static string ActiveCode { get; private set; } = English;

    /// <summary>The listed language with this code, or null.</summary>
    public static UiLanguageInfo? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The listed language that best serves a culture, or null when none does.</summary>
    public static UiLanguageInfo? Match(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (Find(c.Name) is { } exact) return exact;
            if (c.Parent == c) break;
        }
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            var traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            return Find(traditional ? "zh-Hant" : "zh-Hans");
        }
        var two = culture.TwoLetterISOLanguageName;
        if (two == "en") return Find(English);
        return All.FirstOrDefault(l => string.Equals(CultureInfo.GetCultureInfo(l.Code).TwoLetterISOLanguageName, two, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The language a setting stands for: a listed code as is, otherwise (empty or unknown) the Windows language if the app has it, else English.</summary>
    public static UiLanguageInfo Resolve(string? setting, CultureInfo? windows = null) =>
        Find(setting) ?? Match(windows ?? WindowsUiCulture) ?? Find(English)!;

    /// <summary>Sets the UI culture for every thread. Formatting (CurrentCulture) is left to Windows.</summary>
    public static UiLanguageInfo Apply(AppSettings settings) => Apply(settings.Language);

    public static UiLanguageInfo Apply(string? setting)
    {
        _ = WindowsUiCulture;
        var language = Resolve(setting);
        var culture = CultureInfo.GetCultureInfo(language.Code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        StartupSetting = setting?.Trim() ?? "";
        ActiveCode = language.Code;
        return language;
    }

    /// <summary>True when the stored choice shows a different language from the one running now.</summary>
    public static bool NeedsRestart(string? setting) => !string.Equals(Resolve(setting).Code, ActiveCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads only the language from settings.json, read-only and quietly; anything odd means "follow Windows".</summary>
    public static string ReadSetting(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile)) return "";
            using var stream = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "";
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, nameof(AppSettings.Language), StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return "";
    }

    /// <summary>First thing at startup: read the stored choice for this data root and apply it.</summary>
    public static UiLanguageInfo ApplyAtStartup(string? dataRoot) => Apply(ReadSetting(AutomationPortalPaths.SettingsFileFor(dataRoot)));
}
