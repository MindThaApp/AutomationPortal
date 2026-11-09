using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomationPortal.Core;

public enum VarKind { Output, Named, Input, Clipboard, CurrentDate, RepeatItem, RepeatIndex, MenuResult, Device }
public enum VarPick { All, First, Last, Random }

/// <summary>
/// A reference to a variable. JSON: {"kind":"output","uid":"…","prop":"Name","as":"Number","pick":"first"}.
/// Build with the static factories, refine with <see cref="WithProperty"/>, <see cref="As"/>, <see cref="Picking"/>.
/// </summary>
[JsonConverter(typeof(VarRefConverter))]
public sealed record VarRef(
    VarKind Kind,
    string? Uid = null,
    string? Name = null,
    string? Property = null,
    ContentKind? CoerceTo = null,
    VarPick Pick = VarPick.All)
{
    /// <summary>Magic variable: the output of the action with this uid (must be above the user).</summary>
    public static VarRef Output(string uid) => new(VarKind.Output, Uid: uid);
    /// <summary>Named variable (Set Variable / Add to Variable).</summary>
    public static VarRef Named(string name) => new(VarKind.Named, Name: name);
    public static VarRef ShortcutInput { get; } = new(VarKind.Input);
    public static VarRef Clipboard { get; } = new(VarKind.Clipboard);
    public static VarRef CurrentDate { get; } = new(VarKind.CurrentDate);
    public static VarRef RepeatItem { get; } = new(VarKind.RepeatItem);
    public static VarRef RepeatIndex { get; } = new(VarKind.RepeatIndex);
    public static VarRef MenuResult { get; } = new(VarKind.MenuResult);
    public static VarRef Device { get; } = new(VarKind.Device);

    /// <summary>Property of each item, e.g. "Name" for a File or "key:city" for a Dictionary.</summary>
    public VarRef WithProperty(string property) => this with { Property = property };
    /// <summary>Coerce the value to a content kind (e.g. Number).</summary>
    public VarRef As(ContentKind kind) => this with { CoerceTo = kind };
    public VarRef Picking(VarPick pick) => this with { Pick = pick };

    public override string ToString() => Kind switch
    {
        VarKind.Output => $"Output({Uid})",
        VarKind.Named => $"Variable({Name})",
        _ => Kind.ToString(),
    } + (Property is null ? "" : "." + Property);
}

internal sealed class VarRefConverter : JsonConverter<VarRef>
{
    private static readonly string[] KindNames =
        { "output", "named", "input", "clipboard", "currentDate", "repeatItem", "repeatIndex", "menuResult", "device" };

    public override VarRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var e = doc.RootElement;
        var kindText = e.GetProperty("kind").GetString() ?? "";
        var idx = Array.FindIndex(KindNames, k => string.Equals(k, kindText, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) throw new JsonException($"Unknown variable kind '{kindText}'.");
        string? Str(string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        ContentKind? coerce = null;
        if (Str("as") is { } asText && Enum.TryParse<ContentKind>(asText, true, out var ck)) coerce = ck;
        var pick = VarPick.All;
        if (Str("pick") is { } pickText && Enum.TryParse<VarPick>(pickText, true, out var pk)) pick = pk;
        return new VarRef((VarKind)idx, Str("uid"), Str("name"), Str("prop"), coerce, pick);
    }

    public override void Write(Utf8JsonWriter writer, VarRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", KindNames[(int)value.Kind]);
        if (value.Uid is not null) writer.WriteString("uid", value.Uid);
        if (value.Name is not null) writer.WriteString("name", value.Name);
        if (value.Property is not null) writer.WriteString("prop", value.Property);
        if (value.CoerceTo is { } k) writer.WriteString("as", k.ToString());
        if (value.Pick != VarPick.All) writer.WriteString("pick", value.Pick.ToString().ToLowerInvariant());
        writer.WriteEndObject();
    }
}
