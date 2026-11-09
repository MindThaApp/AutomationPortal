using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using IOFile = System.IO.File;

namespace AutomationPortal.GalleryBuilder;

/// <summary>Runs the safe starters in-process with fake host services, on temp copies. Never touches real files, the real clipboard or the PC's settings.</summary>
internal static class DryRun
{
    private sealed record Case(string Name, Func<TestHost, Shortcut, Task<string>> Run);

    public static async Task<int> RunAllAsync(IReadOnlyList<Starter> starters, ActionRegistry registry)
    {
        var failures = 0;
        foreach (var c in Cases())
        {
            var starter = starters.First(s => s.Shortcut.Name == c.Name);
            using var host = new TestHost(registry);
            try
            {
                var detail = await c.Run(host, starter.Shortcut.Clone());
                Console.WriteLine($"  PASS  {c.Name}: {detail}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"  FAIL  {c.Name}: {ex.Message}");
            }
        }
        return failures;
    }

    private static IEnumerable<Case> Cases()
    {
        yield return new("Unit Converter", async (h, s) =>
        {
            h.UI.Enqueue(12).Enqueue("Kilometres to miles");
            await Ok(h, s, ContentValue.Empty);
            var km = h.UI.Shown.Last().AsText();
            h.UI.Enqueue(100).Enqueue("Celsius to Fahrenheit");
            await Ok(h, s, ContentValue.Empty);
            var c = h.UI.Shown.Last().AsText();
            Expect(km == "12 km = 7.46 miles", $"unexpected '{km}'");
            Expect(c.Contains("100 °C = 212"), $"unexpected '{c}'");
            return $"\"{km}\" and \"{c}\"";
        });

        yield return new("Password to Clipboard", async (h, s) =>
        {
            await Ok(h, s, ContentValue.Empty);
            var pw = h.Clipboard.Value.AsText();
            Expect(pw.Length == 20, $"password length {pw.Length}");
            Expect(h.Notifications.Shown.Count == 1, "no notification");
            return $"fake clipboard got a {pw.Length}-character password, 1 notification";
        });

        yield return new("Clean Link", async (h, s) =>
        {
            await Ok(h, s, ContentValue.Url("https://example.com/page?utm_source=news&id=5&fbclid=abc123"));
            var url = h.Clipboard.Value.AsText();
            Expect(url == "https://example.com/page?id=5", $"got '{url}'");
            return url;
        });

        yield return new("Copy as Plain Text", async (h, s) =>
        {
            h.Clipboard.Value = ContentValue.Text("Hello plain world");
            await Ok(h, s, ContentValue.Empty);
            Expect(h.Clipboard.Value.AsText() == "Hello plain world", "clipboard changed");
            return "clipboard round-trips as text";
        });

        yield return new("Quick Note", async (h, s) =>
        {
            Directory.CreateDirectory(h.Root);
            s.FindAction("addNote01")!.Parameters["folder"] = ParamValue.File(h.Root);
            h.UI.Enqueue("Buy milk");
            await Ok(h, s, ContentValue.Text("ignored default"));
            var file = Directory.GetFiles(h.Root, "*.md").Single();
            var text = await IOFile.ReadAllTextAsync(file);
            Expect(text.Contains("Buy milk"), "note text missing");
            return $"{Path.GetFileName(file)} contains the note";
        });

        yield return new("Rename Photos by Date", async (h, s) =>
        {
            var dir = Dir(h);
            var files = new[] { "IMG_001.jpg", "IMG_002.jpg", "IMG_003.jpg" }.Select(n => Path.Combine(dir, n)).ToArray();
            foreach (var f in files) MakeJpeg(f, 120, 80);
            await Ok(h, s, ContentValue.Images(files));
            var names = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Expect(names.All(n => n!.EndsWith(".jpg") && !n.StartsWith("IMG_")), "not renamed: " + string.Join(", ", names));
            return string.Join(", ", names) + " (no EXIF date, so {taken} fell back to the file date)";
        });

        yield return new("Resize Images", async (h, s) =>
        {
            var dir = Dir(h);
            var files = new[] { "a.jpg", "b.jpg" }.Select(n => Path.Combine(dir, n)).ToArray();
            foreach (var f in files) MakeJpeg(f, 3000, 2000);
            Disable(s, "reveal0001");
            var r = await Ok(h, s, ContentValue.Images(files));
            var outputs = Directory.GetFiles(dir, "*-small.jpg");
            Expect(outputs.Length == 2, $"{outputs.Length} resized files");
            var (w, hh) = Size(outputs[0]);
            Expect(w == 1600 && hh == 1067, $"size {w}x{hh}");
            return $"2 copies at {w}x{hh}, originals kept";
        });

        yield return new("Images to PDF", async (h, s) =>
        {
            var dir = Dir(h);
            var files = new[] { "a.jpg", "b.jpg" }.Select(n => Path.Combine(dir, n)).ToArray();
            foreach (var f in files) MakeJpeg(f, 600, 400);
            Disable(s, "reveal0001");
            await Ok(h, s, ContentValue.Images(files));
            var pdf = Directory.GetFiles(dir, "*.pdf").Single();
            Expect(new FileInfo(pdf).Length > 1000, "pdf too small");
            return $"{Path.GetFileName(pdf)} ({new FileInfo(pdf).Length / 1024} KB)";
        });

        yield return new("Combine PDFs", async (h, s) =>
        {
            var dir = Dir(h);
            var pdfs = new List<string>();
            foreach (var n in new[] { "one", "two" })
            {
                var img = Path.Combine(dir, n + ".jpg");
                MakeJpeg(img, 300, 200);
                var made = await ActionTestHarness.RunInShortcutAsync(h, "pdf.make", ContentValue.Images(new[] { img }),
                    ("name", n), ("saveTo", ParamValue.Choice("next")));
                Expect(made.Status == RunStatus.Succeeded, "pdf.make failed: " + made.ErrorMessage);
                pdfs.Add(made.Output.AsText());
            }
            Disable(s, "reveal0001");
            await Ok(h, s, ContentValue.Files(pdfs));
            var combined = Directory.GetFiles(dir, "Combined*.pdf").Single();
            return $"{Path.GetFileName(combined)} from {pdfs.Count} PDFs";
        });

        yield return new("Organize Downloads", async (h, s) =>
        {
            var dir = Dir(h);
            var step = s.FindAction("folder0001")!;
            step.Parameters["folder"] = ParamValue.Choice("chosen");
            step.Parameters["chosen"] = ParamValue.File(dir);
            MakeJpeg(Path.Combine(dir, "photo.jpg"), 50, 50);
            foreach (var n in new[] { "report.pdf", "notes.txt", "backup.zip" }) await IOFile.WriteAllTextAsync(Path.Combine(dir, n), "x");
            await Ok(h, s, ContentValue.Empty);
            var folders = Directory.GetDirectories(dir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Expect(folders.Length >= 3, "folders: " + string.Join(", ", folders));
            Expect(Directory.GetFiles(dir).Length == 0, "loose files remain");
            return "sorted into " + string.Join(", ", folders);
        });

        yield return new("Tidy Desktop", async (h, s) =>
        {
            var dir = Dir(h);
            var step = s.FindAction("desktop001")!;
            step.Parameters["folder"] = ParamValue.Choice("chosen");
            step.Parameters["chosen"] = ParamValue.File(dir);
            MakeJpeg(Path.Combine(dir, "photo.jpg"), 50, 50);
            await IOFile.WriteAllTextAsync(Path.Combine(dir, "report.pdf"), "x");
            await IOFile.WriteAllTextAsync(Path.Combine(dir, "Game.lnk"), "x");
            await Ok(h, s, ContentValue.Empty);
            var tidy = Path.Combine(dir, "Tidy Desktop");
            var moved = Directory.GetFiles(tidy, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(tidy, p)).OrderBy(x => x).ToArray();
            Expect(IOFile.Exists(Path.Combine(dir, "Game.lnk")), "the shortcut file was moved");
            Expect(moved.Length >= 2, "moved: " + string.Join(", ", moved));
            return $"moved {string.Join(", ", moved)}; Game.lnk left alone (empty kinds are skipped by Continue-on-error)";
        });

        yield return new("Zip & Email", async (h, s) =>
        {
            var dir = Dir(h);
            var f1 = Path.Combine(dir, "a.txt"); var f2 = Path.Combine(dir, "b.txt");
            await IOFile.WriteAllTextAsync(f1, "one"); await IOFile.WriteAllTextAsync(f2, "two");
            Disable(s, "mail000001");
            await Ok(h, s, ContentValue.Files(new[] { f1, f2 }));
            var zip = Directory.GetFiles(dir, "*.zip").Single();
            return $"{Path.GetFileName(zip)} created (email step skipped)";
        });

        yield return new("Remove Photo Location Before Sharing", async (h, s) =>
        {
            var dir = Dir(h);
            var f = Path.Combine(dir, "holiday.jpg");
            MakeJpeg(f, 200, 100);
            Disable(s, "share00001");
            await Ok(h, s, ContentValue.Images(new[] { f }));
            Expect(IOFile.Exists(Path.Combine(dir, "holiday-clean.jpg")), "clean copy missing");
            return "holiday-clean.jpg written next to the original (share step skipped)";
        });

        yield return new("Clipboard to QR Code", async (h, s) =>
        {
            h.Clipboard.Value = ContentValue.Text("https://example.com");
            await Ok(h, s, h.Clipboard.Value);
            Expect(h.UI.Calls.Contains("preview"), "no preview");
            var img = h.UI.Shown.Last().AsText();
            return $"QR image previewed ({Path.GetFileName(img)})";
        });

        yield return new("Text Expansion: Today's Date", async (h, s) =>
        {
            var r = await Ok(h, s, ContentValue.Empty);
            var text = r.Output.AsText();
            Expect(System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}$"), $"got '{text}'");
            return text;
        });

        yield return new("Text Expansion: My Address", async (h, s) =>
        {
            var r = await Ok(h, s, ContentValue.Empty);
            Expect(r.Output.AsText().Contains("Main Street"), "wrong output");
            return "output is the address text";
        });

        yield return new("Weather Now", async (h, s) =>
        {
            var r = await Ok(h, s, ContentValue.Empty);
            return "live Open-Meteo lookup: " + h.UI.Shown.Last().AsText().Replace("\n", " ");
        });
    }

    private static async Task<RunResult> Ok(TestHost h, Shortcut s, ContentValue input)
    {
        var r = await h.Runner.RunAsync(s, input, new RunOptions { Source = RunSource.Editor, RecordHistory = false });
        if (r.Status != RunStatus.Succeeded) throw new Exception($"run {r.Status}: {r.ErrorMessage} (step {r.FailedStepUid})");
        return r;
    }

    private static void Expect(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Disable(Shortcut s, string uid) => s.FindAction(uid)!.Disabled = true;
    private static string Dir(TestHost h) { Directory.CreateDirectory(h.Root); return h.Root; }

    private static (int W, int H) Size(string path)
    {
        using var fs = IOFile.OpenRead(path);
        var f = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (f.PixelWidth, f.PixelHeight);
    }

    private static void MakeJpeg(string path, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
        var px = new byte[w * h * 4];
        Array.Fill(px, (byte)170);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = IOFile.Create(path);
        enc.Save(fs);
    }
}
