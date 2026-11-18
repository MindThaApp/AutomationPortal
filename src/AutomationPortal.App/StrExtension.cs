using System.Windows.Markup;

namespace AutomationPortal.App;

/// <summary>XAML access to Strings.resx: Text="{local:Str Key}".</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
