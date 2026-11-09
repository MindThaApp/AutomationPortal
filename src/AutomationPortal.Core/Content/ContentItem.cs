using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Flowmate.Core;

/// <summary>An immutable piece of content flowing between actions. See SPEC §3.5 for kinds and properties.</summary>
public abstract record ContentItem
{
    public abstract ContentKind Kind { get; }
    /// <summary>Human-friendly text (also what a Text parameter receives): sizes as "1.2 MB", dates in local short format.</summary>
    public abstract string DisplayText { get; }
    /// <summary>Property names shown in the variable picker.</summary>
    public virtual IReadOnlyList<string> PropertyNames => Array.Empty<string>();

    /// <summary>Gets a property by name (case/space-insensitive: "Word Count" = "wordcount"). Null when unknown.</summary>
    public ContentValue? GetProperty(string name) => GetPropertyCore(Norm(name), name);

    protected virtual ContentValue? GetPropertyCore(string normalized, string original) => null;

    /// <summary>Writes the natural JSON form of this item (text → string, number → number, dictionary → object …).</summary>
    public abstract void WriteJson(Utf8JsonWriter w);

    public override string ToString() => DisplayText;

    internal static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

public sealed record TextItem(string Value) : ContentItem
{
    private static readonly string[] Props = { "Length", "Word Count", "Line Count" };
    public override ContentKind Kind => ContentKind.Text;
    public override string DisplayText => Value;
    public override IReadOnlyList<string> PropertyNames => Props;

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "length" => ContentValue.Number(Value.Length),
        "wordcount" => ContentValue.Number(Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length),
        "linecount" => ContentValue.Number(Value.Length == 0 ? 0 : Value.Replace("\r\n", "\n").Split('\n').Length),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w) => w.WriteStringValue(Value);
}

public sealed record NumberItem(double Value) : ContentItem
{
    public override ContentKind Kind => ContentKind.Number;
    public override string DisplayText => ContentFormat.Number(Value);

    public override void WriteJson(Utf8JsonWriter w)
    {
        if (Value == Math.Floor(Value) && Math.Abs(Value) < 1e15) w.WriteNumberValue((long)Value);
        else if (double.IsFinite(Value)) w.WriteNumberValue(Value);
        else w.WriteNullValue();
    }
}

public sealed record BooleanItem(bool Value) : ContentItem
{
    public override ContentKind Kind => ContentKind.Boolean;
    public override string DisplayText => Value ? "true" : "false";
    public override void WriteJson(Utf8JsonWriter w) => w.WriteBooleanValue(Value);
}

public sealed record DateItem(DateTimeOffset Value) : ContentItem
{
    private static readonly string[] Props =
        { "Year", "Month", "Day", "Hour", "Minute", "Weekday", "Week of Year", "Unix Time", "Short Date", "Long Date", "Time" };
    public override ContentKind Kind => ContentKind.Date;
    public override string DisplayText => ContentFormat.Date(Value);
    public override IReadOnlyList<string> PropertyNames => Props;

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "year" => ContentValue.Number(Value.Year),
        "month" => ContentValue.Number(Value.Month),
        "day" => ContentValue.Number(Value.Day),
        "hour" => ContentValue.Number(Value.Hour),
        "minute" => ContentValue.Number(Value.Minute),
        "weekday" => ContentValue.Text(Value.ToString("dddd", CultureInfo.CurrentCulture)),
        "weekofyear" => ContentValue.Number(ISOWeek.GetWeekOfYear(Value.DateTime)),
        "unixtime" => ContentValue.Number(Value.ToUnixTimeSeconds()),
        "shortdate" => ContentValue.Text(Value.ToString("d", CultureInfo.CurrentCulture)),
        "longdate" => ContentValue.Text(Value.ToString("D", CultureInfo.CurrentCulture)),
        "time" => ContentValue.Text(Value.ToString("t", CultureInfo.CurrentCulture)),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w) => w.WriteStringValue(Value.ToString("o", CultureInfo.InvariantCulture));
}

public sealed record UrlItem(Uri Value) : ContentItem
{
    private static readonly string[] Props = { "Host", "Path", "Query", "Scheme" };
    public override ContentKind Kind => ContentKind.Url;
    public override string DisplayText => Value.OriginalString;
    public override IReadOnlyList<string> PropertyNames => Props;

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "host" => ContentValue.Text(Value.Host),
        "path" => ContentValue.Text(Value.AbsolutePath),
        "query" => ContentValue.Text(Value.Query.TrimStart('?')),
        "scheme" => ContentValue.Text(Value.Scheme),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w) => w.WriteStringValue(Value.OriginalString);
}

/// <summary>Base of File, Folder and Image items: a full path on disk (which may not exist yet).</summary>
public abstract record PathItem(string Path) : ContentItem
{
    private static readonly string[] Props =
        { "Name", "Full Name", "Extension", "Size", "Size Text", "Created", "Modified", "Parent Folder", "Path", "Kind" };

    public override string DisplayText => Path;
    public override IReadOnlyList<string> PropertyNames => Props;
    /// <summary>File name with extension.</summary>
    public string FullName => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public bool Exists => this is FolderItem ? Directory.Exists(Path) : File.Exists(Path);

    protected override ContentValue? GetPropertyCore(string n, string o)
    {
        var isFolder = this is FolderItem;
        switch (n)
        {
            case "name": return ContentValue.Text(isFolder ? FullName : System.IO.Path.GetFileNameWithoutExtension(Path));
            case "fullname": return ContentValue.Text(FullName);
            case "extension": return ContentValue.Text(isFolder ? "" : System.IO.Path.GetExtension(Path).TrimStart('.'));
            case "path": return ContentValue.Text(Path);
            case "parentfolder":
                var parent = System.IO.Path.GetDirectoryName(Path.TrimEnd('\\', '/'));
                return parent is null ? ContentValue.Empty : ContentValue.Folder(parent);
            case "kind": return ContentValue.Text(isFolder ? "Folder" : FileKinds.Of(Path));
            case "size":
            case "sizetext":
                if (isFolder || !File.Exists(Path)) return null;
                var len = new FileInfo(Path).Length;
                return n == "size" ? ContentValue.Number(len) : ContentValue.Text(ContentFormat.Size(len));
            case "created":
                return Exists ? ContentValue.Date(isFolder ? Directory.GetCreationTime(Path) : File.GetCreationTime(Path)) : null;
            case "modified":
                return Exists ? ContentValue.Date(isFolder ? Directory.GetLastWriteTime(Path) : File.GetLastWriteTime(Path)) : null;
            default: return null;
        }
    }

    public override void WriteJson(Utf8JsonWriter w) => w.WriteStringValue(Path);
}

public record FileItem(string Path) : PathItem(Path)
{
    public override ContentKind Kind => ContentKind.File;
}

public sealed record FolderItem(string Path) : PathItem(Path)
{
    public override ContentKind Kind => ContentKind.Folder;
}

/// <summary>An image, always backed by a file. Width/Height are filled in by actions that decode images (may be null).</summary>
public sealed record ImageItem(string Path, int? Width = null, int? Height = null) : FileItem(Path)
{
    public override ContentKind Kind => ContentKind.Image;
    public override IReadOnlyList<string> PropertyNames { get; } =
        new[] { "Width", "Height", "Name", "Full Name", "Extension", "Size", "Size Text", "Created", "Modified", "Parent Folder", "Path", "Kind" };

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "width" => Width is { } w ? ContentValue.Number(w) : null,
        "height" => Height is { } h ? ContentValue.Number(h) : null,
        _ => base.GetPropertyCore(n, o),
    };
}

/// <summary>An ordered string → value map (JSON object).</summary>
public sealed record DictionaryItem(IReadOnlyList<KeyValuePair<string, ContentValue>> Entries, string? Display = null) : ContentItem
{
    private static readonly string[] Props = { "Keys", "Values" };
    public static DictionaryItem Empty { get; } = new(Array.Empty<KeyValuePair<string, ContentValue>>());

    public override ContentKind Kind => ContentKind.Dictionary;
    public override IReadOnlyList<string> PropertyNames => Props;
    public IEnumerable<string> Keys => Entries.Select(e => e.Key);
    public int Count => Entries.Count;

    /// <summary>When <see cref="Display"/> is set it is what people see ("17° and light rain in London"); the JSON form stays available via ToJson.</summary>
    public override string DisplayText
    {
        get
        {
            if (Display is not null) return Display;
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                WriteJson(w);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public bool TryGet(string key, out ContentValue value)
    {
        foreach (var e in Entries)
            if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) { value = e.Value; return true; }
        value = ContentValue.Empty;
        return false;
    }

    public ContentValue Get(string key) => TryGet(key, out var v) ? v : ContentValue.Empty;

    /// <summary>Follows a path like "a.b.0" (dictionary keys and zero-based list indexes). Empty when not found.</summary>
    public ContentValue GetPath(string path)
    {
        ContentValue current = ContentValue.From(this);
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Count == 1 && current[0] is DictionaryItem d && d.TryGet(segment, out var next)) { current = next; continue; }
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < current.Count)
            {
                current = ContentValue.From(current[idx]);
                continue;
            }
            return ContentValue.Empty;
        }
        return current;
    }

    /// <summary>Returns a copy with the key set (replacing an existing key, keeping its position).</summary>
    public DictionaryItem With(string key, ContentValue value)
    {
        var list = Entries.ToList();
        var i = list.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        var kv = new KeyValuePair<string, ContentValue>(i >= 0 ? list[i].Key : key, value);
        if (i >= 0) list[i] = kv; else list.Add(kv);
        return new DictionaryItem(list);
    }

    public DictionaryItem Without(string key) =>
        new(Entries.Where(e => !string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).ToList());

    protected override ContentValue? GetPropertyCore(string n, string o)
    {
        if (n == "keys") return ContentValue.FromItems(Keys.Select(k => (ContentItem)new TextItem(k)));
        if (n == "values") return ContentValue.Concat(Entries.Select(e => e.Value).ToArray());
        if (o.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) return Get(o[4..].Trim());
        return TryGet(o, out var v) ? v : null;
    }

    public override void WriteJson(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        foreach (var (k, v) in Entries) { w.WritePropertyName(k); v.WriteJson(w); }
        w.WriteEndObject();
    }
}

public sealed record LocationItem(double Latitude, double Longitude, string? Name = null, string? City = null, string? Country = null) : ContentItem
{
    private static readonly string[] Props = { "Latitude", "Longitude", "Name", "City", "Country" };
    public override ContentKind Kind => ContentKind.Location;
    public override IReadOnlyList<string> PropertyNames => Props;

    public override string DisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name!;
            if (!string.IsNullOrWhiteSpace(City)) return string.IsNullOrWhiteSpace(Country) ? City! : $"{City}, {Country}";
            return $"{Latitude.ToString("0.#####", CultureInfo.InvariantCulture)}, {Longitude.ToString("0.#####", CultureInfo.InvariantCulture)}";
        }
    }

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "latitude" => ContentValue.Number(Latitude),
        "longitude" => ContentValue.Number(Longitude),
        "name" => ContentValue.Text(Name ?? ""),
        "city" => ContentValue.Text(City ?? ""),
        "country" => ContentValue.Text(Country ?? ""),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("latitude", Latitude);
        w.WriteNumber("longitude", Longitude);
        if (Name is not null) w.WriteString("name", Name);
        if (City is not null) w.WriteString("city", City);
        if (Country is not null) w.WriteString("country", Country);
        w.WriteEndObject();
    }
}

public sealed record AppItem(string Id, string Name, string? Path = null) : ContentItem
{
    private static readonly string[] Props = { "Name", "Path" };
    public override ContentKind Kind => ContentKind.App;
    public override string DisplayText => Name;
    public override IReadOnlyList<string> PropertyNames => Props;

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "name" => ContentValue.Text(Name),
        "path" => ContentValue.Text(Path ?? ""),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("id", Id);
        w.WriteString("name", Name);
        if (Path is not null) w.WriteString("path", Path);
        w.WriteEndObject();
    }
}

public sealed record WindowItem(long Handle, string Title, string AppName) : ContentItem
{
    private static readonly string[] Props = { "Title", "App" };
    public override ContentKind Kind => ContentKind.Window;
    public override string DisplayText => Title;
    public override IReadOnlyList<string> PropertyNames => Props;

    protected override ContentValue? GetPropertyCore(string n, string o) => n switch
    {
        "title" => ContentValue.Text(Title),
        "app" => ContentValue.Text(AppName),
        _ => null,
    };

    public override void WriteJson(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("title", Title);
        w.WriteString("app", AppName);
        w.WriteEndObject();
    }
}
