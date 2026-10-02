using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flowmate.Core;

/// <summary>One piece of a text template: either literal text or an inline variable.</summary>
public readonly record struct TextPart(string? Literal, VarRef? Var)
{
    public static TextPart Of(string literal) => new(literal, null);
    public static TextPart Of(VarRef variable) => new(null, variable);
    public static implicit operator TextPart(string literal) => Of(literal);
    public static implicit operator TextPart(VarRef variable) => Of(variable);
}

public enum ConditionMatch { All, Any }

/// <summary>A parameter value stored in a shortcut. JSON tagged union discriminated by "t" (see SPEC §3.3).</summary>
[JsonConverter(typeof(ParamValueConverter))]
public abstract record ParamValue
{
    // ---- fluent factories -------------------------------------------------------------------

    /// <summary>Text template. Parts may be strings or <see cref="VarRef"/>s: <c>ParamValue.Text("Hi ", VarRef.Output(uid), "!")</c>.</summary>
    public static TextValue Text(params object[] parts)
    {
        var list = new List<TextPart>(parts.Length);
        foreach (var p in parts)
        {
            list.Add(p switch
            {
                string s => TextPart.Of(s),
                VarRef v => TextPart.Of(v),
                TextPart tp => tp,
                null => throw new ArgumentNullException(nameof(parts)),
                _ => TextPart.Of(Convert.ToString(p, CultureInfo.InvariantCulture) ?? ""),
            });
        }
        return new TextValue(list);
    }

    public static NumberValue Number(double value) => new(value);
    public static BoolValue Bool(bool value) => new(value);
    public static ChoiceValue Choice(string value) => new(value);
    public static VarValue Var(VarRef reference) => new(reference);
    /// <summary>Whole value is the output of an earlier action.</summary>
    public static VarValue Output(string uid) => new(VarRef.Output(uid));
    public static VarValue Named(string name) => new(VarRef.Named(name));
    public static VarValue ShortcutInput() => new(VarRef.ShortcutInput);
    public static ListValue List(params ParamValue[] items) => new(items);
    public static ListValue List(params string[] items) => new(items.Select(i => (ParamValue)Text(i)).ToList());
    public static DictValue Dict(params (string Key, ParamValue Value)[] entries) =>
        new(entries.Select(e => new DictEntry(Text(e.Key), e.Value)).ToList());
    public static AskValue Ask() => new();
    public static ConditionsValue Conditions(ConditionMatch match, params ConditionRow[] rows) => new(match, rows);
    public static ConditionsValue AllOf(params ConditionRow[] rows) => new(ConditionMatch.All, rows);
    public static ConditionsValue AnyOf(params ConditionRow[] rows) => new(ConditionMatch.Any, rows);
    public static DateValue Date(DateTimeOffset value) => new(value);
    public static TimeValue Time(TimeOnly value) => new(value);
    public static TimeValue Time(string hhmm) => new(TimeOnly.Parse(hhmm, CultureInfo.InvariantCulture));
    public static DurationValue Duration(double seconds) => new(seconds);
    public static DurationValue Duration(TimeSpan value) => new(value.TotalSeconds);
    public static HotkeyValue Hotkey(string keys) => new(keys);
    public static FileValue File(string path) => new(path);
    public static AppValue App(string id, string? name = null) => new(id, name);

    public static implicit operator ParamValue(string text) => Text(text);
    public static implicit operator ParamValue(double number) => Number(number);
    public static implicit operator ParamValue(int number) => Number(number);
    public static implicit operator ParamValue(bool value) => Bool(value);
    public static implicit operator ParamValue(VarRef reference) => Var(reference);

    // ---- inspection -------------------------------------------------------------------------

    /// <summary>Every variable reference contained in this value (recursively). Used by the validator and the editor.</summary>
    public virtual IEnumerable<VarRef> VarRefs() => Enumerable.Empty<VarRef>();

    /// <summary>Plain-text rendering ignoring variables. Used for trigger/condition parameters, which are literal-only.</summary>
    public virtual string ToLiteralText() => "";
}

public sealed record TextValue(IReadOnlyList<TextPart> Parts) : ParamValue
{
    public override IEnumerable<VarRef> VarRefs() => Parts.Where(p => p.Var is not null).Select(p => p.Var!);
    public override string ToLiteralText() => string.Concat(Parts.Select(p => p.Literal ?? ""));
    /// <summary>True when there are no variables and no text.</summary>
    public bool IsBlank => Parts.All(p => p.Var is null && string.IsNullOrEmpty(p.Literal));
}

public sealed record NumberValue(double Value) : ParamValue
{
    public override string ToLiteralText() => ContentFormat.Number(Value);
}

public sealed record BoolValue(bool Value) : ParamValue
{
    public override string ToLiteralText() => Value ? "true" : "false";
}

public sealed record ChoiceValue(string Value) : ParamValue
{
    public override string ToLiteralText() => Value;
}

public sealed record VarValue(VarRef Ref) : ParamValue
{
    public override IEnumerable<VarRef> VarRefs() { yield return Ref; }
}

public sealed record ListValue(IReadOnlyList<ParamValue> Items) : ParamValue
{
    public override IEnumerable<VarRef> VarRefs() => Items.SelectMany(i => i.VarRefs());
    public override string ToLiteralText() => string.Join("\n", Items.Select(i => i.ToLiteralText()));
}

public sealed record DictEntry(ParamValue Key, ParamValue Value);

public sealed record DictValue(IReadOnlyList<DictEntry> Items) : ParamValue
{
    public override IEnumerable<VarRef> VarRefs() => Items.SelectMany(i => i.Key.VarRefs().Concat(i.Value.VarRefs()));
}

/// <summary>Ask Each Time: the user is prompted at run time.</summary>
public sealed record AskValue : ParamValue;

public sealed record ConditionRow(ParamValue Left, string Op, ParamValue? Right = null, ParamValue? Right2 = null)
{
    public static ConditionRow Of(ParamValue left, string op, ParamValue? right = null, ParamValue? right2 = null) => new(left, op, right, right2);
}

public sealed record ConditionsValue(ConditionMatch Match, IReadOnlyList<ConditionRow> Items) : ParamValue
{
    public override IEnumerable<VarRef> VarRefs() =>
        Items.SelectMany(i => i.Left.VarRefs().Concat(i.Right?.VarRefs() ?? []).Concat(i.Right2?.VarRefs() ?? []));
}

public sealed record DateValue(DateTimeOffset Value) : ParamValue
{
    public override string ToLiteralText() => Value.ToString("o", CultureInfo.InvariantCulture);
}

public sealed record TimeValue(TimeOnly Value) : ParamValue
{
    public override string ToLiteralText() => Value.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed record DurationValue(double Seconds) : ParamValue
{
    public TimeSpan ToTimeSpan() => TimeSpan.FromSeconds(Seconds);
    public override string ToLiteralText() => ContentFormat.Number(Seconds);
}

public sealed record HotkeyValue(string Keys) : ParamValue
{
    public override string ToLiteralText() => Keys;
}

public sealed record FileValue(string Path) : ParamValue
{
    public override string ToLiteralText() => Path;
}

public sealed record AppValue(string Id, string? Name = null) : ParamValue
{
    public override string ToLiteralText() => Name ?? Id;
}

internal sealed class ParamValueConverter : JsonConverter<ParamValue>
{
    public override ParamValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return Parse(doc.RootElement, options);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static ParamValue Parse(JsonElement e, JsonSerializerOptions options)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "t") is not { } tag)
            throw new JsonException("A parameter value needs a \"t\" type tag.");
        switch (tag)
        {
            case "text":
                {
                    var parts = new List<TextPart>();
                    if (e.TryGetProperty("parts", out var arr))
                        foreach (var p in arr.EnumerateArray())
                        {
                            if (p.ValueKind == JsonValueKind.String) parts.Add(TextPart.Of(p.GetString() ?? ""));
                            else parts.Add(TextPart.Of(p.GetProperty("var").Deserialize<VarRef>(options)!));
                        }
                    return new TextValue(parts);
                }
            case "num": return new NumberValue(e.GetProperty("v").GetDouble());
            case "bool": return new BoolValue(e.GetProperty("v").GetBoolean());
            case "choice": return new ChoiceValue(Str(e, "v") ?? "");
            case "var": return new VarValue(e.GetProperty("ref").Deserialize<VarRef>(options)!);
            case "list":
                return new ListValue(e.GetProperty("items").EnumerateArray().Select(i => Parse(i, options)).ToList());
            case "dict":
                return new DictValue(e.GetProperty("items").EnumerateArray()
                    .Select(i => new DictEntry(Parse(i.GetProperty("k"), options), Parse(i.GetProperty("v"), options))).ToList());
            case "ask": return new AskValue();
            case "conds":
                {
                    var match = string.Equals(Str(e, "match"), "any", StringComparison.OrdinalIgnoreCase) ? ConditionMatch.Any : ConditionMatch.All;
                    var rows = new List<ConditionRow>();
                    foreach (var r in e.GetProperty("items").EnumerateArray())
                    {
                        rows.Add(new ConditionRow(
                            Parse(r.GetProperty("left"), options),
                            Str(r, "op") ?? ConditionOperators.Is,
                            r.TryGetProperty("right", out var rt) ? Parse(rt, options) : null,
                            r.TryGetProperty("right2", out var rt2) ? Parse(rt2, options) : null));
                    }
                    return new ConditionsValue(match, rows);
                }
            case "date": return new DateValue(DateTimeOffset.Parse(Str(e, "v")!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            case "time": return new TimeValue(TimeOnly.Parse(Str(e, "v")!, CultureInfo.InvariantCulture));
            case "duration": return new DurationValue(e.GetProperty("seconds").GetDouble());
            case "hotkey": return new HotkeyValue(Str(e, "v") ?? "");
            case "file": return new FileValue(Str(e, "path") ?? "");
            case "app": return new AppValue(Str(e, "id") ?? "", Str(e, "name"));
            default: throw new JsonException($"Unknown parameter value type '{tag}'.");
        }
    }

    public override void Write(Utf8JsonWriter w, ParamValue value, JsonSerializerOptions options)
    {
        w.WriteStartObject();
        switch (value)
        {
            case TextValue t:
                w.WriteString("t", "text");
                w.WriteStartArray("parts");
                foreach (var p in t.Parts)
                {
                    if (p.Var is { } v) { w.WriteStartObject(); w.WritePropertyName("var"); JsonSerializer.Serialize(w, v, options); w.WriteEndObject(); }
                    else w.WriteStringValue(p.Literal ?? "");
                }
                w.WriteEndArray();
                break;
            case NumberValue n: w.WriteString("t", "num"); w.WriteNumber("v", n.Value); break;
            case BoolValue b: w.WriteString("t", "bool"); w.WriteBoolean("v", b.Value); break;
            case ChoiceValue c: w.WriteString("t", "choice"); w.WriteString("v", c.Value); break;
            case VarValue v: w.WriteString("t", "var"); w.WritePropertyName("ref"); JsonSerializer.Serialize(w, v.Ref, options); break;
            case ListValue l:
                w.WriteString("t", "list");
                w.WriteStartArray("items");
                foreach (var i in l.Items) JsonSerializer.Serialize(w, i, options);
                w.WriteEndArray();
                break;
            case DictValue d:
                w.WriteString("t", "dict");
                w.WriteStartArray("items");
                foreach (var i in d.Items)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("k"); JsonSerializer.Serialize(w, i.Key, options);
                    w.WritePropertyName("v"); JsonSerializer.Serialize(w, i.Value, options);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                break;
            case AskValue: w.WriteString("t", "ask"); break;
            case ConditionsValue cv:
                w.WriteString("t", "conds");
                w.WriteString("match", cv.Match == ConditionMatch.Any ? "any" : "all");
                w.WriteStartArray("items");
                foreach (var r in cv.Items)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("left"); JsonSerializer.Serialize(w, r.Left, options);
                    w.WriteString("op", r.Op);
                    if (r.Right is not null) { w.WritePropertyName("right"); JsonSerializer.Serialize(w, r.Right, options); }
                    if (r.Right2 is not null) { w.WritePropertyName("right2"); JsonSerializer.Serialize(w, r.Right2, options); }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                break;
            case DateValue dt: w.WriteString("t", "date"); w.WriteString("v", dt.Value.ToString("o", CultureInfo.InvariantCulture)); break;
            case TimeValue tm: w.WriteString("t", "time"); w.WriteString("v", tm.Value.ToString("HH:mm", CultureInfo.InvariantCulture)); break;
            case DurationValue du: w.WriteString("t", "duration"); w.WriteNumber("seconds", du.Seconds); break;
            case HotkeyValue hk: w.WriteString("t", "hotkey"); w.WriteString("v", hk.Keys); break;
            case FileValue f: w.WriteString("t", "file"); w.WriteString("path", f.Path); break;
            case AppValue a:
                w.WriteString("t", "app"); w.WriteString("id", a.Id);
                if (a.Name is not null) w.WriteString("name", a.Name);
                break;
            default: throw new JsonException($"Can't save a value of type {value.GetType().Name}.");
        }
        w.WriteEndObject();
    }
}
