using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using QRCoder;

namespace AutomationPortal.Actions.Content.Actions;

internal static class PasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*-_=+?.,;:";
    private const string Lookalikes = "Il1O0o|";

    private static readonly Lazy<string[]> WordList = new(() =>
    {
        using var s = typeof(PasswordGenerator).Assembly.GetManifestResourceStream("AutomationPortal.Actions.Content.words.txt")
                      ?? throw new InvalidOperationException("The word list is missing from the app.");
        using var r = new StreamReader(s);
        return r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    });

    internal static int WordCount => WordList.Value.Length;

    public static string Characters(int length, bool lower, bool upper, bool digits, bool symbols, bool avoidLookalikes)
    {
        var sets = new List<string>();
        string Filter(string s) => avoidLookalikes ? new string(s.Where(c => !Lookalikes.Contains(c)).ToArray()) : s;
        if (lower) sets.Add(Filter(Lower));
        if (upper) sets.Add(Filter(Upper));
        if (digits) sets.Add(Filter(Digits));
        if (symbols) sets.Add(Filter(Symbols));
        if (sets.Count == 0) throw new ActionException("Turn on at least one kind of character (letters, numbers or symbols).");
        if (length < sets.Count) length = sets.Count;

        var all = string.Concat(sets);
        var chars = new char[length];
        // one from every chosen kind, the rest from everything, then shuffle so the guaranteed ones aren't always first
        for (var i = 0; i < sets.Count; i++) chars[i] = sets[i][RandomNumberGenerator.GetInt32(sets[i].Length)];
        for (var i = sets.Count; i < length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    public static string Passphrase(int words, string separator, bool capitalize, bool addNumber)
    {
        var list = WordList.Value;
        var parts = new List<string>();
        for (var i = 0; i < words; i++)
        {
            var w = list[RandomNumberGenerator.GetInt32(list.Length)];
            parts.Add(capitalize ? char.ToUpperInvariant(w[0]) + w[1..] : w);
        }
        if (addNumber)
        {
            var at = RandomNumberGenerator.GetInt32(parts.Count);
            parts[at] += RandomNumberGenerator.GetInt32(10, 100).ToString();
        }
        return string.Join(separator, parts);
    }
}

public sealed class GeneratePasswordAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        SensitiveOutput = true, Id = "text.password", Title = "Generate Password", Category = ActionCategories.Text, Icon = "Password24",
        Keywords = new[] { "random", "secure", "passphrase", "strong", "secret" },
        Summary = "Generate a password using {style}",
        Description = "Makes a strong random password with a secure random number generator. Choose letters, numbers and symbols, or a passphrase made of random words.",
        OutputKind = ContentKind.Text, OutputName = "Password",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("style", "Style", "characters", new Choice("characters", "random characters"), new Choice("words", "word passphrase")),
            ParameterDescriptor.Number("length", "Length", 16, 4, 128, "characters") with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("lowercase", "Lowercase letters", true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("uppercase", "Uppercase letters", true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("digits", "Numbers", true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("symbols", "Symbols", true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("avoidLookalikes", "Avoid look-alike characters (0 O l 1 I)") with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Number("words", "Number of words", 4, 3, 12) with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.ChoiceOf("wordSeparator", "Between words", "-", new Choice("-", "a dash"), new Choice(" ", "a space"), new Choice(".", "a dot"), new Choice("", "nothing")) with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.Bool("capitalize", "Capitalize each word") with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.Bool("addNumber", "Add a number", true) with { VisibleWhen = new VisibleWhen("style", "words") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var style = await ctx.GetChoiceAsync("style", "characters");
        if (style == "words")
        {
            var words = (int)Math.Clamp(await ctx.GetNumberAsync("words", 4), 3, 12);
            var sep = ctx.HasParameter("wordSeparator") ? await ctx.GetChoiceAsync("wordSeparator", "-") : "-";
            return ContentValue.Text(PasswordGenerator.Passphrase(words, sep, await ctx.GetBoolAsync("capitalize"), await ctx.GetBoolAsync("addNumber", true)));
        }
        var length = (int)Math.Clamp(await ctx.GetNumberAsync("length", 16), 4, 128);
        return ContentValue.Text(PasswordGenerator.Characters(length,
            await ctx.GetBoolAsync("lowercase", true), await ctx.GetBoolAsync("uppercase", true),
            await ctx.GetBoolAsync("digits", true), await ctx.GetBoolAsync("symbols", true), await ctx.GetBoolAsync("avoidLookalikes")));
    }
}

public sealed class MakeQrCodeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.qrMake", Title = "Make QR Code", Category = ActionCategories.Text, Icon = "QrCode24",
        Keywords = new[] { "barcode", "qr", "scan", "link", "wifi", "code" },
        Summary = "Make a QR code from {text}",
        Description = "Turns text or a link into a QR code picture that phones can scan.",
        InputKinds = new[] { ContentKind.Text, ContentKind.Url }, OutputKind = ContentKind.Image, OutputName = "QR Code",
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", "Text or link", required: true),
            ParameterDescriptor.Number("size", "Size of each square", 12, 2, 60, "pixels"),
            ParameterDescriptor.ChoiceOf("repair", "Damage protection", "M",
                new Choice("L", "Low (smallest code)"), new Choice("M", "Medium"), new Choice("Q", "High"), new Choice("H", "Highest (biggest code)")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var input = await ctx.GetContentAsync("text");
        var size = (int)Math.Clamp(await ctx.GetNumberAsync("size", 12), 2, 60);
        var level = (await ctx.GetChoiceAsync("repair", "M")) switch
        {
            "L" => QRCodeGenerator.ECCLevel.L, "Q" => QRCodeGenerator.ECCLevel.Q, "H" => QRCodeGenerator.ECCLevel.H, _ => QRCodeGenerator.ECCLevel.M,
        };
        if (input.IsEmpty || input.AsText().Length == 0) throw new ActionException("There is no text to turn into a QR code. Type something or choose a link.");

        var items = new List<ContentItem>();
        foreach (var text in input.AsTexts())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var png = Render(text, size, level);
            var path = ctx.NewTempPath("QR Code.png");
            await File.WriteAllBytesAsync(path, png, ctx.CancellationToken);
            var (w, h) = PngSize(png);
            items.Add(new ImageItem(path, w, h));
        }
        return ContentValue.FromItems(items);
    }

    internal static byte[] Render(string text, int pixelsPerModule, QRCodeGenerator.ECCLevel level)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, level);
            return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        }
        catch (QRCoder.Exceptions.DataTooLongException)
        {
            throw new ActionException("That is too much text for one QR code (the limit is roughly 2,000 characters). Use shorter text or a link.");
        }
    }

    internal static (int Width, int Height) PngSize(byte[] png) =>
        png.Length > 24 ? (BitConverter.ToInt32(new[] { png[19], png[18], png[17], png[16] }), BitConverter.ToInt32(new[] { png[23], png[22], png[21], png[20] })) : (0, 0);
}

public sealed class ReadQrCodeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.qrRead", Title = "Read QR Code / Barcode", Category = ActionCategories.Text, Icon = "QrCode24",
        Keywords = new[] { "scan", "barcode", "decode", "qr", "ean", "upc" },
        Summary = "Read the QR code or barcode in {image}",
        Description = "Finds QR codes and barcodes in a picture and gives you the text inside them.",
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Text, OutputName = "Code Text",
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", "Image", isInput: true) with { Required = true },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("image");
        if (images.Count == 0) throw new ActionException("There is no image to read. Choose a picture that contains a QR code or barcode.");
        var found = new List<string>();
        foreach (var img in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(img.Path)) throw new ActionException($"Couldn't find the image '{img.FullName}'. Check it still exists.");
            found.AddRange(await BarcodeReading.ReadAsync(img.Path, ctx.CancellationToken));
        }
        if (found.Count == 0)
            throw new ActionException("Couldn't find a QR code or barcode in that image. Try a sharper picture where the code fills more of the frame.");
        return ContentValue.Texts(found.Distinct());
    }
}
