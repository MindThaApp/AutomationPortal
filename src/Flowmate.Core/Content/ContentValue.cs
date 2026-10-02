using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Flowmate.Core;

/// <summary>
/// An immutable list of <see cref="ContentItem"/>s (a single value is a list of one; nothing is the empty list).
/// All the <c>As…</c> methods are forgiving: "12.5 kg" → 12.5, "yes" → true, JSON text → dictionary.
/// </summary>
[JsonConverter(typeof(ContentValueJsonConverter))]
public sealed class ContentValue : IReadOnlyList<ContentItem>
{
    private static readonly Regex NumberRx = new(@"-?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?|-?\.\d+", RegexOptions.Compiled);
    private static readonly Regex UrlRx = new(@"\b(?:https?://|www\.)[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BareDomainRx = new(@"^[\w-]+(\.[\w-]+)+(/\S*)?$", RegexOptions.Compiled);

    private readonly ContentItem[] _items;

    public static ContentValue Empty { get; } = new(Array.Empty<ContentItem>());

    public ContentValue(IEnumerable<ContentItem> items) => _items = items.ToArray();

    public IReadOnlyList<ContentItem> Items => _items;
    public int Count => _items.Length;
    public bool IsEmpty => _items.Length == 0;
    public ContentItem this[int index] => _items[index];
    public ContentItem? FirstItem => _items.Length > 0 ? _items[0] : null;
    /// <summary>Kind of the first item, or <see cref="ContentKind.Any"/> when empty.</summary>
    public ContentKind Kind => _items.Length > 0 ? _items[0].Kind : ContentKind.Any;
    public IEnumerator<ContentItem> GetEnumerator() => ((IEnumerable<ContentItem>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    // ---- factories ---------------------------------------------------------------------------

    public static ContentValue From(ContentItem item) => new(new[] { item });
    public static ContentValue FromItems(IEnumerable<ContentItem> items) => new(items);
    public static ContentValue Text(string value) => From(new TextItem(value));
    public static ContentValue Texts(IEnumerable<string> values) => new(values.Select(v => (ContentItem)new TextItem(v)));
    public static ContentValue Number(double value) => From(new NumberItem(value));
    public static ContentValue Numbers(IEnumerable<double> values) => new(values.Select(v => (ContentItem)new NumberItem(v)));
    public static ContentValue Bool(bool value) => From(new BooleanItem(value));
    public static ContentValue Date(DateTimeOffset value) => From(new DateItem(value));
    public static ContentValue Url(Uri value) => From(new UrlItem(value));
    public static ContentValue Url(string value) => From(new UrlItem(new Uri(value, UriKind.Absolute)));
    public static ContentValue File(string path) => From(new FileItem(path));
    public static ContentValue Folder(string path) => From(new FolderItem(path));
    /// <summary>A file or folder, decided by what exists on disk (file if unknown).</summary>
    public static ContentValue FileOrFolder(string path) => From(ItemForPath(path));
    public static ContentValue Files(IEnumerable<string> paths) => new(paths.Select(ItemForPath));
    public static ContentValue Image(string path, int? width = null, int? height = null) => From(new ImageItem(path, width, height));
    public static ContentValue Images(IEnumerable<string> paths) => new(paths.Select(p => (ContentItem)new ImageItem(p)));
    public static ContentValue Dict(IEnumerable<KeyValuePair<string, ContentValue>> entries) => From(new DictionaryItem(entries.ToList()));
    public static ContentValue Dict(params (string Key, ContentValue Value)[] entries) =>
        From(new DictionaryItem(entries.Select(e => new KeyValuePair<string, ContentValue>(e.Key, e.Value)).ToList()));
    public static ContentValue Concat(params ContentValue[] values) => new(values.SelectMany(v => v._items));

    public static implicit operator ContentValue(string text) => Text(text);
    public static implicit operator ContentValue(double number) => Number(number);
    public static implicit operator ContentValue(bool value) => Bool(value);
    public static implicit operator ContentValue(ContentItem item) => From(item);

    internal static ContentItem ItemForPath(string path) =>
        Directory.Exists(path) ? new FolderItem(path)
        : FileKinds.IsImage(path) ? new ImageItem(path)
        : new FileItem(path);

    /// <summary>Builds content from common .NET values (string, numbers, bool, dates, Uri, dictionaries, enumerables, JsonElement …).</summary>
    public static ContentValue Of(object? value) => value switch
    {
        null => Empty,
        ContentValue c => c,
        ContentItem i => From(i),
        string s => Text(s),
        bool b => Bool(b),
        double d => Number(d),
        float f => Number(f),
        int n => Number(n),
        long l => Number(l),
        decimal m => Number((double)m),
        DateTimeOffset dto => Date(dto),
        DateTime dt => Date(new DateTimeOffset(dt)),
        Uri u => Url(u),
        JsonElement je => FromJsonElement(je),
        IDictionary<string, object?> dict => Dict(dict.Select(kv => new KeyValuePair<string, ContentValue>(kv.Key, Of(kv.Value)))),
        IEnumerable e => new ContentValue(e.Cast<object?>().SelectMany(x => Of(x)._items)),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    // ---- conversions -------------------------------------------------------------------------

    /// <summary>All items as text joined by new lines (or <paramref name="separator"/>).</summary>
    public string AsText(string separator = "\n") => string.Join(separator, _items.Select(i => i.DisplayText));

    /// <summary>Each item as its own text.</summary>
    public IReadOnlyList<string> AsTexts() => _items.Select(i => i.DisplayText).ToList();

    public override string ToString() => AsText();

    /// <summary>First number found: numbers as-is, true → 1, "12.5 kg" → 12.5, "1,234.5" → 1234.5. Null when none.</summary>
    public double? AsNumber()
    {
        foreach (var item in _items)
            if (TryNumber(item, out var d)) return d;
        return null;
    }

    /// <summary>Every item converted to a number (items with no number are skipped).</summary>
    public IReadOnlyList<double> AsNumbers()
    {
        var list = new List<double>();
        foreach (var item in _items)
        {
            if (item is TextItem t && NumberRx.Matches(t.Value) is { Count: > 1 } many)
            {
                // "1 2 3" is a list of numbers; "12.5 kg" is a single number.
                foreach (Match m in many) if (TryParseNumber(m.Value, out var v)) list.Add(v);
            }
            else if (TryNumber(item, out var d)) list.Add(d);
        }
        return list;
    }

    internal static bool TryNumber(ContentItem item, out double d)
    {
        switch (item)
        {
            case NumberItem n: d = n.Value; return true;
            case BooleanItem b: d = b.Value ? 1 : 0; return true;
            case DateItem dt: d = dt.Value.ToUnixTimeSeconds(); return true;
            case TextItem t:
                var m = NumberRx.Match(t.Value);
                if (m.Success && TryParseNumber(m.Value, out d)) return true;
                break;
            case PathItem p when p is not FolderItem && System.IO.File.Exists(p.Path):
                d = new FileInfo(p.Path).Length; return true;
        }
        d = 0;
        return false;
    }

    private static bool TryParseNumber(string s, out double d) =>
        double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    /// <summary>"yes"/"true"/"1"/"on" → true; "no"/"false"/"0"/"off"/empty → false; numbers ≠ 0 → true; other text → true.</summary>
    public bool AsBool(bool fallback = false)
    {
        if (_items.Length == 0) return fallback;
        switch (_items[0])
        {
            case BooleanItem b: return b.Value;
            case NumberItem n: return n.Value != 0;
            case TextItem t:
                var s = t.Value.Trim().ToLowerInvariant();
                if (s is "yes" or "true" or "1" or "on" or "y" or "ok") return true;
                if (s is "no" or "false" or "0" or "off" or "n" or "" or "nothing" or "none") return false;
                return true;
            default: return true;
        }
    }

    /// <summary>Dates from date items and from text ("2026-10-02 9:00", "today", "tomorrow"). For richer phrases use the Date actions.</summary>
    public IReadOnlyList<DateTimeOffset> AsDates()
    {
        var list = new List<DateTimeOffset>();
        foreach (var item in _items)
        {
            if (item is DateItem d) list.Add(d.Value);
            else if (item is TextItem t && TryParseDate(t.Value, out var dt)) list.Add(dt);
            else if (item is NumberItem n && n.Value > 100_000_000 && n.Value < 100_000_000_000)
                list.Add(DateTimeOffset.FromUnixTimeSeconds((long)n.Value).ToLocalTime());
        }
        return list;
    }

    public DateTimeOffset? AsDate() => AsDates() is { Count: > 0 } l ? l[0] : null;

    private static bool TryParseDate(string text, out DateTimeOffset result)
    {
        var s = text.Trim();
        var today = DateTime.Today;
        switch (s.ToLowerInvariant())
        {
            case "now": result = DateTimeOffset.Now; return true;
            case "today": result = new DateTimeOffset(today); return true;
            case "tomorrow": result = new DateTimeOffset(today.AddDays(1)); return true;
            case "yesterday": result = new DateTimeOffset(today.AddDays(-1)); return true;
        }
        return DateTimeOffset.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result)
            || DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
    }

    /// <summary>URLs from URL items and from text (finds links inside sentences; "example.com" gets https://).</summary>
    public IReadOnlyList<Uri> AsUrls()
    {
        var list = new List<Uri>();
        foreach (var item in _items)
        {
            switch (item)
            {
                case UrlItem u: list.Add(u.Value); break;
                case TextItem t:
                    var found = false;
                    foreach (Match m in UrlRx.Matches(t.Value))
                    {
                        var raw = m.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?');
                        if (raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw;
                        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)) { list.Add(uri); found = true; }
                    }
                    if (!found)
                    {
                        var trimmed = t.Value.Trim();
                        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https" or "ftp" or "mailto" && !trimmed.Contains(' '))
                            list.Add(abs);
                        else if (BareDomainRx.IsMatch(trimmed) && Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var bare))
                            list.Add(bare);
                    }
                    break;
            }
        }
        return list;
    }

    /// <summary>The first dictionary item, or JSON object text parsed into one. Null when it isn't a dictionary.</summary>
    public DictionaryItem? AsDictionary()
    {
        foreach (var item in _items)
        {
            if (item is DictionaryItem d) return d;
            if (item is TextItem t && TryParseJson(t.Value, out var parsed) && parsed.FirstItem is DictionaryItem pd) return pd;
        }
        return null;
    }

    /// <summary>Files (and images) from file items, rooted path text, URLs (downloaded) and plain text (saved to a temp .txt file). Folders are skipped unless asked for.</summary>
    public async Task<IReadOnlyList<PathItem>> AsFilesAsync(ActionContext ctx, bool includeFolders = false)
    {
        var list = new List<PathItem>();
        foreach (var item in _items)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (item)
            {
                case FolderItem f: if (includeFolders) list.Add(f); break;
                case PathItem p: list.Add(p); break;
                case TextItem t when LooksLikePath(t.Value):
                    var path = t.Value.Trim().Trim('"');
                    var pi = ItemForPath(path) as PathItem ?? new FileItem(path);
                    if (pi is FolderItem && !includeFolders) break;
                    list.Add(pi);
                    break;
                case UrlItem u:
                    list.Add(await ctx.DownloadAsync(u.Value));
                    break;
                case DictionaryItem d:
                    list.Add(ctx.WriteTempFile("Data.json", d.DisplayText));
                    break;
                default:
                    list.Add(ctx.WriteTempFile("Text.txt", item.DisplayText));
                    break;
            }
        }
        return list;
    }

    /// <summary>Images from image items, image files and image URLs (downloaded). Anything else throws a friendly <see cref="ActionException"/>.</summary>
    public async Task<IReadOnlyList<ImageItem>> AsImagesAsync(ActionContext ctx)
    {
        var list = new List<ImageItem>();
        foreach (var item in _items)
        {
            switch (item)
            {
                case ImageItem img: list.Add(img); break;
                case PathItem p when p is not FolderItem && FileKinds.IsImage(p.Path): list.Add(new ImageItem(p.Path)); break;
                case TextItem t when LooksLikePath(t.Value) && FileKinds.IsImage(t.Value.Trim().Trim('"')):
                    list.Add(new ImageItem(t.Value.Trim().Trim('"')));
                    break;
                case UrlItem u:
                    var dl = await ctx.DownloadAsync(u.Value);
                    list.Add(new ImageItem(dl.Path));
                    break;
                default:
                    throw new ActionException($"Couldn't use '{ContentFormat.Truncate(item.DisplayText, 60)}' as an image. Choose a picture file.");
            }
        }
        return list;
    }

    private static bool LooksLikePath(string s)
    {
        var t = s.Trim().Trim('"');
        if (t.Length < 3 || t.Contains('\n')) return false;
        try { return Path.IsPathRooted(t) && (t[1] == ':' || t.StartsWith(@"\\")); } catch { return false; }
    }

    // ---- properties, picking, coercion -------------------------------------------------------

    /// <summary>The named property of every item, concatenated (items without it are skipped).</summary>
    public ContentValue Property(string name)
    {
        var parts = new List<ContentItem>();
        foreach (var item in _items)
            if (item.GetProperty(name) is { } v) parts.AddRange(v._items);
        return new ContentValue(parts);
    }

    /// <summary>Union of property names across kinds present (for the variable picker).</summary>
    public IReadOnlyList<string> PropertyNames => _items.SelectMany(i => i.PropertyNames).Distinct().ToList();

    public ContentValue Pick(VarPick pick, Random? random = null)
    {
        if (_items.Length <= 1) return this;
        return pick switch
        {
            VarPick.First => From(_items[0]),
            VarPick.Last => From(_items[^1]),
            VarPick.Random => From(_items[(random ?? Random.Shared).Next(_items.Length)]),
            _ => this,
        };
    }

    /// <summary>Converts every item to the given kind where possible (items that can't convert are dropped).</summary>
    public ContentValue CoerceTo(ContentKind kind)
    {
        if (kind == ContentKind.Any) return this;
        switch (kind)
        {
            case ContentKind.Text: return new ContentValue(_items.Select(i => i is TextItem ? i : new TextItem(i.DisplayText)));
            case ContentKind.Number: return Numbers(AsNumbers());
            case ContentKind.Boolean: return IsEmpty ? this : Bool(AsBool());
            case ContentKind.Date: return new ContentValue(AsDates().Select(d => (ContentItem)new DateItem(d)));
            case ContentKind.Url: return new ContentValue(AsUrls().Select(u => (ContentItem)new UrlItem(u)));
            case ContentKind.Dictionary: return AsDictionary() is { } d ? From(d) : Empty;
            case ContentKind.File:
            case ContentKind.Folder:
            case ContentKind.Image:
                var items = new List<ContentItem>();
                foreach (var i in _items)
                {
                    var path = i is PathItem p ? p.Path : i is TextItem t && LooksLikePath(t.Value) ? t.Value.Trim().Trim('"') : null;
                    if (path is null) continue;
                    ContentItem made = kind switch
                    {
                        ContentKind.Folder => new FolderItem(path),
                        ContentKind.Image => new ImageItem(path),
                        _ => ItemForPath(path),
                    };
                    items.Add(made);
                }
                return new ContentValue(items);
            default: return new ContentValue(_items.Where(i => i.Kind == kind));
        }
    }

    // ---- JSON --------------------------------------------------------------------------------

    /// <summary>Natural JSON: one item → that value, several → an array, nothing → null.</summary>
    public void WriteJson(Utf8JsonWriter w)
    {
        if (_items.Length == 1) _items[0].WriteJson(w);
        else if (_items.Length == 0) w.WriteNullValue();
        else { w.WriteStartArray(); foreach (var i in _items) i.WriteJson(w); w.WriteEndArray(); }
    }

    public string ToJson(bool indented = false)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteJson(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Parses JSON text: object → dictionary, array → list of items, scalars → text/number/bool.</summary>
    public static ContentValue FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return FromJsonElement(doc.RootElement);
    }

    public static bool TryParseJson(string text, out ContentValue value)
    {
        value = Empty;
        var t = text.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return false;
        try { value = FromJson(text); return true; }
        catch (JsonException) { return false; }
    }

    public static ContentValue FromJsonElement(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                return Dict(e.EnumerateObject().Select(p => new KeyValuePair<string, ContentValue>(p.Name, FromJsonElement(p.Value))));
            case JsonValueKind.Array:
                return new ContentValue(e.EnumerateArray().SelectMany(x => FromJsonElement(x)._items));
            case JsonValueKind.String: return Text(e.GetString() ?? "");
            case JsonValueKind.Number: return Number(e.GetDouble());
            case JsonValueKind.True: return Bool(true);
            case JsonValueKind.False: return Bool(false);
            default: return Empty;
        }
    }
}

/// <summary>Typed (lossless) JSON for persisting content: saved values, history. [{"kind":"Number","v":5}, …].</summary>
internal sealed class ContentValueJsonConverter : JsonConverter<ContentValue>
{
    public override ContentValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var items = new List<ContentItem>();
        foreach (var e in doc.RootElement.EnumerateArray()) items.Add(ReadItem(e, options));
        return new ContentValue(items);
    }

    private static ContentItem ReadItem(JsonElement e, JsonSerializerOptions options)
    {
        string S(string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        string? SN(string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        double D(string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
        int? IN(string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        return S("kind") switch
        {
            "Text" => new TextItem(S("v")),
            "Number" => new NumberItem(D("v")),
            "Boolean" => new BooleanItem(e.GetProperty("v").GetBoolean()),
            "Date" => new DateItem(DateTimeOffset.Parse(S("v"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            "Url" => new UrlItem(new Uri(S("v"), UriKind.RelativeOrAbsolute)),
            "File" => new FileItem(S("v")),
            "Folder" => new FolderItem(S("v")),
            "Image" => new ImageItem(S("v"), IN("w"), IN("h")),
            "Dictionary" => new DictionaryItem(e.GetProperty("v").EnumerateObject()
                .Select(p => new KeyValuePair<string, ContentValue>(p.Name, JsonSerializer.Deserialize<ContentValue>(p.Value.GetRawText(), options)!)).ToList()),
            "Location" => new LocationItem(D("lat"), D("lon"), SN("name"), SN("city"), SN("country")),
            "App" => new AppItem(S("id"), S("name"), SN("path")),
            "Window" => new WindowItem((long)D("handle"), S("title"), S("app")),
            var k => throw new JsonException($"Unknown content kind '{k}'."),
        };
    }

    public override void Write(Utf8JsonWriter w, ContentValue value, JsonSerializerOptions options)
    {
        w.WriteStartArray();
        foreach (var item in value)
        {
            w.WriteStartObject();
            w.WriteString("kind", item.Kind.ToString());
            switch (item)
            {
                case TextItem t: w.WriteString("v", t.Value); break;
                case NumberItem n: w.WriteNumber("v", n.Value); break;
                case BooleanItem b: w.WriteBoolean("v", b.Value); break;
                case DateItem d: w.WriteString("v", d.Value.ToString("o", CultureInfo.InvariantCulture)); break;
                case UrlItem u: w.WriteString("v", u.Value.OriginalString); break;
                case ImageItem i:
                    w.WriteString("v", i.Path);
                    if (i.Width is { } iw) w.WriteNumber("w", iw);
                    if (i.Height is { } ih) w.WriteNumber("h", ih);
                    break;
                case PathItem p: w.WriteString("v", p.Path); break;
                case DictionaryItem di:
                    w.WriteStartObject("v");
                    foreach (var (k, v) in di.Entries) { w.WritePropertyName(k); Write(w, v, options); }
                    w.WriteEndObject();
                    break;
                case LocationItem l:
                    w.WriteNumber("lat", l.Latitude); w.WriteNumber("lon", l.Longitude);
                    if (l.Name is not null) w.WriteString("name", l.Name);
                    if (l.City is not null) w.WriteString("city", l.City);
                    if (l.Country is not null) w.WriteString("country", l.Country);
                    break;
                case AppItem a:
                    w.WriteString("id", a.Id); w.WriteString("name", a.Name);
                    if (a.Path is not null) w.WriteString("path", a.Path);
                    break;
                case WindowItem wi:
                    w.WriteNumber("handle", wi.Handle); w.WriteString("title", wi.Title); w.WriteString("app", wi.AppName);
                    break;
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
