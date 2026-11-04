using System.Net;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Documents.Tests;

public class PdfTests
{
    private static int Pages(string path) => PdfEngine.PageCount(path);
    private static string One(ContentValue v) => ((PathItem)v[0]).Path;

    [Fact]
    public async Task MakeFromImagesGivesOnePagePerPicture()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = await T.MakeImage(d.File("a.jpg"), 400, 300);
        var b = await T.MakeImage(d.File("b.png"), 300, 500);
        var r = await T.Run(host, new MakePdfAction(), ContentValue.Images(new[] { a, b }), ("name", "Photos"));
        Assert.Equal(Path.Combine(d.Path, "Photos.pdf"), One(r));
        Assert.Equal(2, Pages(One(r)));
        var info = PdfEngine.GetInfo(One(r));
        Assert.Equal("A4", info.PageSize);
    }

    [Fact]
    public async Task LetterOriginalSizeAndMargins()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = await T.MakeImage(d.File("a.jpg"), 400, 300);
        var letter = await T.Run(host, new MakePdfAction(), ContentValue.Images(new[] { a }), ("pageSize", T.Choice("letter")), ("name", "L"));
        Assert.Equal("Letter", PdfEngine.GetInfo(One(letter)).PageSize);
        var orig = await T.Run(host, new MakePdfAction(), ContentValue.Images(new[] { a }), ("imageFit", T.Choice("original")), ("name", "O"));
        var info = PdfEngine.GetInfo(One(orig));
        Assert.Equal(300, info.WidthPt, 1);
        Assert.Equal(225, info.HeightPt, 1);
    }

    [Fact]
    public async Task LongTextFlowsOverSeveralPagesAndRoundTrips()
    {
        using var d = new TempDir(); using var host = T.Host();
        var text = string.Join("\n", Enumerable.Range(1, 150).Select(i => $"Line number {i} of the long document with some words to wrap around the page edge nicely."));
        var made = await T.Run(host, new MakePdfAction(), ContentValue.Text(text), ("name", "Long"), ("saveTo", T.Choice("folder")), ("saveFolder", ParamValue.File(d.Path)));
        var pdf = One(made);
        Assert.True(Pages(pdf) >= 3);
        var read = (await T.Run(host, new GetPdfTextAction(), ContentValue.Files(new[] { pdf }))).AsText();
        Assert.Contains("Line number 1 ", read);
        Assert.Contains("Line number 150", read);
    }

    [Fact]
    public async Task MergeSplitAndTextRoundTrip()
    {
        using var d = new TempDir(); using var host = T.Host();
        var p1 = Path.Combine(d.Path, "one.pdf"); var p2 = Path.Combine(d.Path, "two.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("Alpha page content") }, p1, new PdfMakeOptions());
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("Bravo page content"), new PdfSource.Text("Charlie page content"), new PdfSource.Text("Delta page content") }, p2, new PdfMakeOptions());
        Assert.Equal(1, Pages(p1)); Assert.Equal(3, Pages(p2));

        var merged = One(await T.Run(host, new MergePdfAction(), ContentValue.Files(new[] { p1, p2 }), ("name", "All")));
        Assert.Equal(4, Pages(merged));
        var text = (await T.Run(host, new GetPdfTextAction(), ContentValue.Files(new[] { merged }))).AsText();
        Assert.True(text.IndexOf("Alpha") < text.IndexOf("Bravo") && text.IndexOf("Bravo") < text.IndexOf("Delta"));

        var each = await T.Run(host, new SplitPdfAction(), ContentValue.Files(new[] { merged }), ("mode", T.Choice("each")));
        Assert.Equal(4, each.Count);
        Assert.EndsWith("All - page 1.pdf", T.Paths(each)[0]);
        Assert.Contains("Charlie", (await T.Run(host, new GetPdfTextAction(), ContentValue.Files(new[] { T.Paths(each)[2] }))).AsText());

        var ranges = await T.Run(host, new SplitPdfAction(), ContentValue.Files(new[] { merged }), ("mode", T.Choice("ranges")), ("pages", "1-2, 4"));
        Assert.Equal(new[] { 2, 1 }, T.Paths(ranges).Select(Pages).ToArray());

        var extract = await T.Run(host, new SplitPdfAction(), ContentValue.Files(new[] { merged }), ("mode", T.Choice("extract")), ("pages", "4, 1"));
        Assert.Equal(1, extract.Count);
        var extracted = (await T.Run(host, new GetPdfTextAction(), ContentValue.Files(new[] { One(extract) }))).AsText();
        Assert.True(extracted.IndexOf("Delta") < extracted.IndexOf("Alpha"));

        var chunks = await T.Run(host, new SplitPdfAction(), ContentValue.Files(new[] { merged }), ("mode", T.Choice("chunks")), ("chunk", 3));
        Assert.Equal(new[] { 3, 1 }, T.Paths(chunks).Select(Pages).ToArray());

        var onlySecond = (await T.Run(host, new GetPdfTextAction(), ContentValue.Files(new[] { merged }), ("pages", "2"))).AsText();
        Assert.Contains("Bravo", onlySecond);
        Assert.DoesNotContain("Alpha", onlySecond);
    }

    [Fact]
    public async Task MakeCanIncludeExistingPdfsAndTextFiles()
    {
        using var d = new TempDir(); using var host = T.Host();
        var p1 = Path.Combine(d.Path, "one.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("existing") }, p1, new PdfMakeOptions());
        var r = await T.Run(host, new MakePdfAction(), ContentValue.Files(new[] { p1, d.File("n.txt", "from a text file") }), ("name", "Mixed"));
        Assert.Equal(2, Pages(One(r)));
    }

    [Fact]
    public async Task DetailsAndPageRangeErrors()
    {
        using var d = new TempDir(); using var host = T.Host();
        var p = Path.Combine(d.Path, "x.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("a"), new PdfSource.Text("b") }, p, new PdfMakeOptions());
        var input = ContentValue.Files(new[] { p });
        Assert.Equal(2, (await T.Run(host, new GetPdfDetailsAction(), input)).AsNumber());
        Assert.Equal("A4", (await T.Run(host, new GetPdfDetailsAction(), input, ("detail", T.Choice("pageSize")))).AsText());
        var all = (DictionaryItem)(await T.Run(host, new GetPdfDetailsAction(), input, ("detail", T.Choice("all"))))[0];
        Assert.Equal("x.pdf", all.Get("Name").AsText());
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new SplitPdfAction(), input, ("mode", T.Choice("extract")), ("pages", "5")));
        Assert.Contains("has 2 pages", ex.Message);
        var notPdf = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new MergePdfAction(), ContentValue.Files(new[] { d.File("a.txt", "x"), p })));
        Assert.Contains("isn't a PDF", notPdf.Message);
    }

    [Fact]
    public void PageRangeParsing()
    {
        Assert.Equal(new[] { 1, 2, 3, 5, 8, 9, 10 }, PageRanges.Parse("1-3, 5, 8-", 10));
        Assert.Equal(new[] { 1, 2, 3 }, PageRanges.Parse("", 3));
        Assert.Equal(new[] { 3, 4 }, PageRanges.Parse("4-3", 5));
        Assert.Throws<ActionException>(() => PageRanges.Parse("0", 5));
        Assert.Throws<ActionException>(() => PageRanges.Parse("abc", 5));
    }

    [Fact]
    public void PdfDatesParse()
    {
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 15, 30, TimeSpan.FromHours(1)), PdfEngine.ParsePdfDate("D:20240501101530+01'00'"));
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero), PdfEngine.ParsePdfDate("D:20240501000000Z"));
        Assert.Null(PdfEngine.ParsePdfDate("garbage"));
    }

    [Fact]
    public async Task ToImagesRendersEveryPage()
    {
        using var d = new TempDir(); using var host = T.Host();
        var p = Path.Combine(d.Path, "doc.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("First page"), new PdfSource.Text("Second page") }, p, new PdfMakeOptions());
        var r = await T.Run(host, new PdfToImagesAction(), ContentValue.Files(new[] { p }), ("width", 400));
        Assert.Equal(2, r.Count);
        Assert.Equal("doc page 1.png", Path.GetFileName(((ImageItem)r[0]).Path));
        var info = await T.Info(((ImageItem)r[0]).Path);
        Assert.Equal(400, info.Width);
        Assert.InRange(info.Height, 560, 570);
        var only = await T.Run(host, new PdfToImagesAction(), ContentValue.Files(new[] { p }), ("pages", "2"), ("width", 200), ("saveTo", T.Choice("temp")));
        Assert.Single(only);
        // page is mostly white
        var c = await T.PixelAt(((ImageItem)only[0]).Path, 5, 5);
        Assert.True(c.R > 240 && c.G > 240);
    }
}

public class NoteReminderTests
{
    [Fact]
    public async Task AddToNoteBottomTopAndTimestamp()
    {
        using var d = new TempDir(); using var host = T.Host();
        var folder = ParamValue.File(d.Path);
        var r = await T.Run(host, new AddToNoteAction(), ContentValue.Text("first"), ("name", "Ideas"), ("folder", folder));
        Assert.Equal(Path.Combine(d.Path, "Ideas.md"), ((PathItem)r[0]).Path);
        await T.Run(host, new AddToNoteAction(), ContentValue.Text("second"), ("name", "Ideas"), ("folder", folder));
        await T.Run(host, new AddToNoteAction(), ContentValue.Text("zeroth"), ("name", "Ideas"), ("folder", folder), ("position", T.Choice("top")));
        var lines = File.ReadAllText(Path.Combine(d.Path, "Ideas.md")).Split(new[] { "\r\n" }, StringSplitOptions.None).Where(l => l.Length > 0).ToArray();
        Assert.Equal(new[] { "zeroth", "first", "second" }, lines);
        await T.Run(host, new AddToNoteAction(), ContentValue.Text("stamped"), ("name", "Log"), ("folder", folder), ("timestamp", true));
        Assert.Matches(@"\*\*\d{4}-\d{2}-\d{2} \d{2}:\d{2}\*\*\r?\nstamped", File.ReadAllText(Path.Combine(d.Path, "Log.md")));
    }

    [Fact]
    public async Task GetNoteByNameAndLatest()
    {
        using var d = new TempDir(); using var host = T.Host();
        var folder = ParamValue.File(d.Path);
        File.WriteAllText(Path.Combine(d.Path, "Old.md"), "old note");
        File.SetLastWriteTime(Path.Combine(d.Path, "Old.md"), DateTime.Now.AddDays(-3));
        File.WriteAllText(Path.Combine(d.Path, "New.md"), "new note");
        Assert.Equal("old note", (await T.Run(host, new GetNoteAction(), null, ("name", "Old"), ("folder", folder))).AsText());
        Assert.Equal("new note", (await T.Run(host, new GetNoteAction(), null, ("folder", folder))).AsText());
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new GetNoteAction(), null, ("name", "Nope"), ("folder", folder)));
        Assert.Contains("Couldn't find a note called 'Nope'", ex.Message);
    }

    [Fact]
    public void NoteNamesAreSafeAndDefaultFolderIsInDocuments()
    {
        Assert.EndsWith("Safe_name.md", NoteStore.PathFor(@"C:\x", "Safe:name"));
        Assert.EndsWith("Notes.md", NoteStore.PathFor(@"C:\x", "  "));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flowmate Notes"), NoteStore.Root(null));
    }

    [Fact]
    public async Task RemindInMinutesAndAtTime()
    {
        using var host = T.Host();
        var r = await T.Run(host, new RemindMeAction(), null, ("title", "Stretch"), ("mode", T.Choice("in")), ("after", ParamValue.Duration(900)), ("notes", "legs"));
        var reminder = Assert.Single(host.Reminders.List());
        Assert.Equal("Stretch", reminder.Title);
        Assert.Equal("legs", reminder.Body);
        Assert.InRange((reminder.When - DateTimeOffset.Now).TotalMinutes, 14.5, 15.1);
        Assert.Equal(reminder.When, r.AsDate());

        var at = DateTimeOffset.Now.AddHours(3);
        await T.Run(host, new RemindMeAction(), null, ("title", "Meeting"), ("mode", T.Choice("at")), ("when", ParamValue.Date(at)));
        Assert.Contains(host.Reminders.List(), x => x.Title == "Meeting" && x.When == at);
    }

    [Fact]
    public async Task ReminderValidationAndShortcutLookup()
    {
        using var host = T.Host();
        var past = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new RemindMeAction(), null, ("title", "x"), ("mode", T.Choice("at")), ("when", ParamValue.Date(DateTimeOffset.Now.AddHours(-1)))));
        Assert.Contains("already passed", past.Message);
        await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new RemindMeAction(), null, ("title", " ")));
        var s = new Shortcut { Name = "Stretch Break" };
        host.Library.Save(s);
        await T.Run(host, new RemindMeAction(), null, ("title", "go"), ("after", ParamValue.Duration(60)), ("shortcut", "stretch break"));
        Assert.Equal(s.Id, host.Reminders.List().Single().ShortcutIdToRun);
        var bad = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new RemindMeAction(), null, ("title", "go"), ("shortcut", "No such shortcut")));
        Assert.Contains("Couldn't find a shortcut", bad.Message);
    }

    [Fact]
    public async Task TimerAndList()
    {
        using var host = T.Host();
        await T.Run(host, new StartTimerAction(), null, ("duration", ParamValue.Duration(120)), ("label", "Tea"));
        await T.Run(host, new RemindMeAction(), null, ("title", "Far away"), ("after", ParamValue.Duration(86400 * 5)));
        var all = await T.Run(host, new ListRemindersAction(), null);
        Assert.Equal(2, all.Count);
        Assert.Equal("Tea", ((DictionaryItem)all[0]).Get("Title").AsText());
        Assert.Equal("Time's up!", ((DictionaryItem)all[0]).Get("Details").AsText());
        var soon = await T.Run(host, new ListRemindersAction(), null, ("days", 1));
        Assert.Single(soon);
    }
}

public class CalendarMailShareTests
{
    [Fact]
    public void IcsHasEscapedTextUtcTimesAndAlarm()
    {
        var start = new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Local);
        var ics = IcsBuilder.Build("Lunch, with; Sam", start, start.AddHours(1), false, "Cafe\nCorner", "Bring notes", 15, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), "abc");
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("SUMMARY:Lunch\\, with\\; Sam\r\n", ics);
        Assert.Contains("LOCATION:Cafe\\nCorner\r\n", ics);
        Assert.Contains("UID:abc@flowmate", ics);
        Assert.Contains("DTSTAMP:20261002T000000Z", ics);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        Assert.Contains($"DTSTART:{utc:yyyyMMdd'T'HHmmss}Z", ics);
        Assert.Contains("TRIGGER:-PT15M", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.DoesNotContain("VALARM", IcsBuilder.Build("x", start, start.AddHours(1), false, "", "", 0, DateTime.UtcNow));
    }

    [Fact]
    public void IcsAllDayAndLineFolding()
    {
        var ics = IcsBuilder.Build("Holiday", new DateTime(2026, 12, 25), new DateTime(2026, 12, 25), true, "", new string('é', 100), 0, DateTime.UtcNow);
        Assert.Contains("DTSTART;VALUE=DATE:20261225", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261226", ics);
        foreach (var line in ics.Split("\r\n")) Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line);
        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("DESCRIPTION:" + new string('é', 100), unfolded);
    }

    [Fact]
    public async Task OutlookDetectionDoesNotThrow()
    {
        var available = await StaWorker.Com.RunAsync(OutlookBridge.IsAvailable);
        Assert.True(available || !available);
    }

    [Fact]
    public void RecipientsAndMailtoLinks()
    {
        Assert.Equal(new[] { "a@x.com", "b@y.org", "c@z.io" }, MailTo.ParseRecipients("a@x.com; b@y.org,\n<c@z.io>"));
        Assert.Throws<ActionException>(() => MailTo.ParseRecipients("not an address"));
        var link = MailTo.Build(new[] { "a@x.com" }, new[] { "b@y.org" }, "Hi there & welcome", "Line 1\nLine 2");
        Assert.Equal("mailto:a@x.com?cc=b@y.org&subject=Hi%20there%20%26%20welcome&body=Line%201%0D%0ALine%202", link);
        var long1 = MailTo.Build(new[] { "a@x.com" }, Array.Empty<string>(), "s", new string('x', 5000), 500);
        Assert.True(long1.Length <= 500);
    }

    [Fact]
    public void SendEmailIsDestructiveAndShowsFirstByDefault()
    {
        var d = new SendEmailAction().Descriptor;
        Assert.True(d.IsDestructive);
        Assert.True(d.FindParameter("showFirst")!.Default is BoolValue { Value: true });
        Assert.False(new ComposeEmailAction().Descriptor.IsDestructive);
    }

    [Fact]
    public void NtfyRequestHeadersAndBody()
    {
        using var req = NtfyRequest.Build("my-topic_1", "Hello phone", "Alert", "high", "https://example.com/x", "tada, warning");
        Assert.Equal("https://ntfy.sh/my-topic_1", req.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("Hello phone", req.Content!.ReadAsStringAsync().Result);
        Assert.Equal("Alert", req.Headers.GetValues("Title").Single());
        Assert.Equal("high", req.Headers.GetValues("Priority").Single());
        Assert.Equal("https://example.com/x", req.Headers.GetValues("Click").Single());
        Assert.Equal("tada,warning", req.Headers.GetValues("Tags").Single());
        using var plain = NtfyRequest.Build("t", "m", null, "default", null, null);
        Assert.False(plain.Headers.Contains("Title") || plain.Headers.Contains("Priority") || plain.Headers.Contains("Click"));
        using var utf = NtfyRequest.Build("t", "m", "Привет", "default", null, null);
        Assert.StartsWith("=?UTF-8?B?", utf.Headers.GetValues("Title").Single());
    }

    [Theory]
    [InlineData("", "Choose a topic name")]
    [InlineData("bad topic!", "letters, numbers")]
    public void NtfyTopicValidation(string topic, string expected) =>
        Assert.Contains(expected, Assert.Throws<ActionException>(() => NtfyRequest.Build(topic, "m", null, "default", null, null)).Message);

    [Fact]
    public async Task SendToPhonePostsUsingSettingsTopic()
    {
        using var host = T.Host();
        var handler = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) };
        host.Services.Http = new HttpClient(handler);
        host.Settings.Current.NtfyTopic = "from-settings";
        await T.Run(host, new SendToPhoneAction(), ContentValue.Text("Dinner is ready"), ("title", "Home"));
        var (req, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://ntfy.sh/from-settings", req.RequestUri!.ToString());
        Assert.Equal("Dinner is ready", body);
        await T.Run(host, new SendToPhoneAction(), ContentValue.Text("x"), ("topic", "explicit"));
        Assert.Equal("https://ntfy.sh/explicit", handler.Calls[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendToPhoneFailuresArePlain()
    {
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new SendToPhoneAction(), ContentValue.Text("x")));
        Assert.Contains("topic name", ex.Message);
        var handler = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) };
        host.Services.Http = new HttpClient(handler);
        host.Settings.Current.NtfyTopic = "t";
        Assert.Contains("too quickly", (await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new SendToPhoneAction(), ContentValue.Text("x")))).Message);
        host.Services.Http = new HttpClient(new StubHandler { Respond = _ => throw new HttpRequestException("down") });
        Assert.Contains("Couldn't reach ntfy", (await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new SendToPhoneAction(), ContentValue.Text("x")))).Message);
    }

    [Fact]
    public async Task ShareWithNothingGivesFriendlyError()
    {
        using var host = T.Host();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ShareAction(), null));
        Assert.Contains("nothing to share", ex.Message);
    }

    [Fact]
    public async Task ShareManagerCanBeReached()
    {
        var (ok, closed) = await StaWorker.Imaging.RunAsync(() =>
        {
            var hwnd = ShareSheet.HelperWindow.Create();
            var connected = ShareSheet.CanConnect(hwnd);
            // The helper window closes itself from its timer; pump this thread's messages until it has gone.
            ShareSheet.HelperWindow.DestroyAfter(hwnd, TimeSpan.FromMilliseconds(20));
            var until = Environment.TickCount64 + 3000;
            while (IsWindow(hwnd) && Environment.TickCount64 < until)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
                Thread.Sleep(10);
            }
            return (connected, !IsWindow(hwnd));
        });
        Assert.True(ok, "IDataTransferManagerInterop could not be reached; the Share action will show its friendly error here.");
        Assert.True(closed, "The share helper window didn't close itself.");
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
}

public class CatalogTests
{
    private static readonly string[] Expected =
    {
        "file.get", "file.getFolder", "file.contents", "file.find", "file.filter", "file.details", "file.rename", "file.move", "file.copy",
        "file.delete", "file.newFolder", "file.save", "file.append", "file.readText", "file.zip", "file.unzip", "file.selected", "file.renamePattern", "file.compare", "file.hash",
        "img.resize", "img.convert", "img.crop", "img.rotate", "img.combine", "img.details", "img.stripInfo", "img.addText",
        "pdf.make", "pdf.merge", "pdf.split", "pdf.text", "pdf.details", "pdf.toImages",
        "note.add", "note.get", "note.open", "rem.add", "rem.timer", "rem.list", "cal.addEvent", "cal.getEvents",
        "mail.compose", "mail.send", "share.phone", "share.share",
        "ai.ask", "ai.summarize", "ai.rewrite", "ai.translate", "ai.extract", "ai.describeImage",
    };

    [Fact]
    public void EveryActionIsRegisteredWithoutDuplicates()
    {
        var registry = new ActionRegistry();
        registry.RegisterAssembly(typeof(RenameFilesAction).Assembly);
        Assert.Empty(registry.Diagnostics);
        foreach (var id in Expected) Assert.True(registry.TryGet(id, out _), id);
        Assert.Equal(Expected.Length, registry.All.Count);
    }

    [Fact]
    public void DescriptorsAreWellFormed()
    {
        var registry = new ActionRegistry();
        registry.RegisterAssembly(typeof(RenameFilesAction).Assembly);
        foreach (var a in registry.All)
        {
            var d = a.Descriptor;
            Assert.False(string.IsNullOrWhiteSpace(d.Title), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Summary), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id);
            Assert.Contains(d.Category, ActionCategories.All);
            Assert.NotEmpty(d.Keywords);
            var keys = d.Parameters.Select(p => p.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            foreach (var seg in d.SummarySegments.Where(s => s.IsToken)) Assert.Contains(seg.ParamKey, keys);
            foreach (var p in d.Parameters)
            {
                if (p.VisibleWhen is { } vw) Assert.Contains(vw.Key, keys);
                if (p.Type == ParamType.Choice && p.Choices is { } ch && p.Default is ChoiceValue cv) Assert.Contains(ch, c => c.Id == cv.Value);
                Assert.False(string.IsNullOrWhiteSpace(p.Label), $"{d.Id}.{p.Key}");
            }
        }
    }

    [Fact]
    public void OnlyDeleteAndSendAreDestructive()
    {
        var registry = new ActionRegistry();
        registry.RegisterAssembly(typeof(RenameFilesAction).Assembly);
        Assert.Equal(new[] { "file.delete", "mail.send" }, registry.All.Where(a => a.Descriptor.IsDestructive).Select(a => a.Descriptor.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ActionsRunThroughTheEngine()
    {
        using var host = T.Host();
        var result = ActionTestHarness.RunInShortcutAsync(host, "file.find", null, ("folder", ParamValue.File(@"C:\no\such-b3"))).Result;
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("Couldn't find the folder", result.ErrorMessage);
    }
}
