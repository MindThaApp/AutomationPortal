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
        if (sets.Count == 0) throw new ActionException(Strings.Get("PasswordAndQrActions_Error_TurnOnAtLeast"));
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
        SensitiveOutput = true, Id = "text.password", Title = Strings.Get("TextPassword_Title"), Category = ActionCategories.Text, Icon = "Password24",
        Keywords = new[] { "random", "secure", "passphrase", "strong", "secret" },
        Summary = Strings.Get("TextPassword_Summary"),
        Description = Strings.Get("TextPassword_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("TextPassword_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("style", Strings.Get("TextPassword_Param_Style_Label"), "characters", new Choice("characters", Strings.Get("TextPassword_Choice_Characters")), new Choice("words", Strings.Get("TextPassword_Choice_Words"))),
            ParameterDescriptor.Number("length", Strings.Get("TextPassword_Param_Length_Label"), 16, 4, 128, Strings.Get("TextPassword_Param_Length_Unit")) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("lowercase", Strings.Get("TextPassword_Param_Lowercase_Label"), true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("uppercase", Strings.Get("TextPassword_Param_Uppercase_Label"), true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("digits", Strings.Get("TextPassword_Param_Digits_Label"), true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("symbols", Strings.Get("TextPassword_Param_Symbols_Label"), true) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Bool("avoidLookalikes", Strings.Get("TextPassword_Param_AvoidLookalikes_Label")) with { VisibleWhen = new VisibleWhen("style", "characters") },
            ParameterDescriptor.Number("words", Strings.Get("TextPassword_Param_Words_Label"), 4, 3, 12) with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.ChoiceOf("wordSeparator", Strings.Get("TextPassword_Param_WordSeparator_Label"), "-", new Choice("-", Strings.Get("TextPassword_Choice_Blank")), new Choice(" ", Strings.Get("TextPassword_Choice_Blank2")), new Choice(".", Strings.Get("TextPassword_Choice_Blank3")), new Choice("", Strings.Get("TextPassword_Choice_Blank4"))) with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.Bool("capitalize", Strings.Get("TextPassword_Param_Capitalize_Label")) with { VisibleWhen = new VisibleWhen("style", "words") },
            ParameterDescriptor.Bool("addNumber", Strings.Get("TextPassword_Param_AddNumber_Label"), true) with { VisibleWhen = new VisibleWhen("style", "words") },
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
        Id = "text.qrMake", Title = Strings.Get("TextQrMake_Title"), Category = ActionCategories.Text, Icon = "QrCode24",
        Keywords = new[] { "barcode", "qr", "scan", "link", "wifi", "code" },
        Summary = Strings.Get("TextQrMake_Summary"),
        Description = Strings.Get("TextQrMake_Description"),
        InputKinds = new[] { ContentKind.Text, ContentKind.Url }, OutputKind = ContentKind.Image, OutputName = Strings.Get("TextQrMake_OutputName"),
        Parameters = new[]
        {
            ActionHelpers.TextInput("text", Strings.Get("TextQrMake_Param_Text_Label"), required: true),
            ParameterDescriptor.Number("size", Strings.Get("TextQrMake_Param_Size_Label"), 12, 2, 60, Strings.Get("TextQrMake_Param_Size_Unit")),
            ParameterDescriptor.ChoiceOf("repair", Strings.Get("TextQrMake_Param_Repair_Label"), "M",
                new Choice("L", Strings.Get("TextQrMake_Choice_L")), new Choice("M", Strings.Get("TextQrMake_Choice_M")), new Choice("Q", Strings.Get("TextQrMake_Choice_Q")), new Choice("H", Strings.Get("TextQrMake_Choice_H"))),
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
        if (input.IsEmpty || input.AsText().Length == 0) throw new ActionException(Strings.Get("TextQrMake_Error_ThereIsNoText"));

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
            throw new ActionException(Strings.Get("TextQrMake_Error_ThatIsTooMuch"));
        }
    }

    internal static (int Width, int Height) PngSize(byte[] png) =>
        png.Length > 24 ? (BitConverter.ToInt32(new[] { png[19], png[18], png[17], png[16] }), BitConverter.ToInt32(new[] { png[23], png[22], png[21], png[20] })) : (0, 0);
}

public sealed class ReadQrCodeAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "text.qrRead", Title = Strings.Get("TextQrRead_Title"), Category = ActionCategories.Text, Icon = "QrCode24",
        Keywords = new[] { "scan", "barcode", "decode", "qr", "ean", "upc" },
        Summary = Strings.Get("TextQrRead_Summary"),
        Description = Strings.Get("TextQrRead_Description"),
        InputKinds = new[] { ContentKind.Image }, OutputKind = ContentKind.Text, OutputName = Strings.Get("TextQrRead_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("image", Strings.Get("TextQrRead_Param_Image_Label"), isInput: true) with { Required = true },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var images = await ctx.GetImagesAsync("image");
        if (images.Count == 0) throw new ActionException(Strings.Get("TextQrRead_Error_ThereIsNoImage"));
        var found = new List<string>();
        foreach (var img in images)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(img.Path)) throw new ActionException(Strings.Format("TextQrRead_Error_CouldnTFindThe", img.FullName));
            found.AddRange(await BarcodeReading.ReadAsync(img.Path, ctx.CancellationToken));
        }
        if (found.Count == 0)
            throw new ActionException(Strings.Get("TextQrRead_Error_CouldnTFindA"));
        return ContentValue.Texts(found.Distinct());
    }
}
