using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using AutomationPortal.Triggers;

namespace AutomationPortal.Host.Wpf;

/// <summary>The copy trigger's and text expansion's clipboard access through the WPF clipboard (runs on the triggers' STA thread).</summary>
public sealed class WpfTriggerClipboard : ITriggerClipboard
{
    private static T Retry<T>(Func<T> f, T fallback)
    {
        for (var i = 0; i < 6; i++)
        {
            try { return f(); }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }
        return fallback;
    }

    public ClipboardSnapshot Read(string imageFolder, bool readImage)
    {
        var excluded = Retry(() => Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing"), false);
        if (excluded) return new ClipboardSnapshot(null, Array.Empty<string>(), null, 0, true);

        var files = Retry(() => Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToList() : new List<string>(), new List<string>());
        string? text = null;
        if (files.Count == 0) text = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null);
        string? image = null;
        if (readImage && files.Count == 0 && text is null)
        {
            image = Retry(() =>
            {
                if (!Clipboard.ContainsImage()) return null;
                var bmp = Clipboard.GetImage();
                if (bmp is null) return null;
                Directory.CreateDirectory(imageFolder);
                var path = Path.Combine(imageFolder, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(path);
                enc.Save(fs);
                return path;
            }, null);
        }
        return new ClipboardSnapshot(text, files, image, 0, false);
    }

    public string? ReadText() => Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null);

    public object? SetTextTemporarily(string text)
    {
        var previous = Retry(Snapshot, null);
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        Retry(() => { Clipboard.SetDataObject(data, true); return true; }, false);
        return previous;
    }

    public void Restore(object? previous) =>
        Retry(() =>
        {
            if (previous is IDataObject data) Clipboard.SetDataObject(data, true); else Clipboard.Clear();
            return true;
        }, false);

    private static IDataObject? Snapshot()
    {
        var current = Clipboard.GetDataObject();
        if (current is null) return null;
        var copy = new DataObject();
        var any = false;
        foreach (var format in current.GetFormats(false))
        {
            try
            {
                var data = current.GetData(format, false);
                if (data is null) continue;
                copy.SetData(format, data, false);
                any = true;
            }
            catch { }
        }
        return any ? copy : null;
    }
}
