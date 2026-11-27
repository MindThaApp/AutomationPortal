using System.Globalization;
using System.Resources;

namespace AutomationPortal.Actions.Windows;

/// <summary>User-visible messages from Strings.resx. Format placeholders stay in the resource text.</summary>
internal static class Strings
{
    private static readonly ResourceManager Manager = new(typeof(Strings));

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
