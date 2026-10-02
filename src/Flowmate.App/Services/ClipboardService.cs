using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Flowmate.Core;

namespace Flowmate.App.Services;

/// <summary>
/// Clipboard on the UI thread. Text, pictures (saved as PNG in the temp folder) and files. Another app holding
/// the clipboard is normal on Windows, so every call retries a few times before giving up.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private const int Attempts = 10;
    private const int DelayMs = 60;
    private const int CantOpen = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN

    private readonly IUiThread _ui;
    private readonly FlowmatePaths _paths;

    public ClipboardService(IUiThread ui, FlowmatePaths paths) { _ui = ui; _paths = paths; }

    private async Task<T> RetryAsync<T>(Func<T> work)
    {
        for (var i = 1; ; i++)
        {
            try { return await _ui.InvokeAsync(work).ConfigureAwait(false); }
            catch (COMException ex) when (ex.HResult == CantOpen && i < Attempts) { await Task.Delay(DelayMs).ConfigureAwait(false); }
            catch (COMException ex) when (ex.HResult == CantOpen)
            { throw new ActionException("Another app is using the clipboard right now. Wait a moment and try again."); }
        }
    }

    public Task<ContentValue> GetAsync() => RetryAsync(() =>
    {
        if (Clipboard.ContainsFileDropList())
        {
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count > 0) return ContentValue.Files(paths);
        }
        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image is not null)
            {
                var folder = Path.Combine(_paths.TempRoot, "clipboard");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = File.Create(file)) encoder.Save(fs);
                return ContentValue.Image(file, image.PixelWidth, image.PixelHeight);
            }
        }
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (text.Length > 0) return ContentValue.Text(text);
        }
        return ContentValue.Empty;
    });

    public Task SetAsync(ContentValue value) => RetryAsync(() =>
    {
        if (value.IsEmpty) { Clipboard.Clear(); return true; }
        var data = new DataObject();
        var images = value.Items.OfType<ImageItem>().ToList();
        var files = value.Items.OfType<PathItem>().Where(p => p.Exists).ToList();
        if (files.Count > 0 && files.Count == value.Count)
        {
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(files.Select(f => f.Path).ToArray());
            data.SetFileDropList(list);
            if (images.Count == 1 && TryLoad(images[0].Path) is { } bmp) data.SetImage(bmp);
        }
        else if (images.Count == 1 && value.Count == 1 && TryLoad(images[0].Path) is { } single)
            data.SetImage(single);
        else
            data.SetText(value.AsText());
        Clipboard.SetDataObject(data, true);
        return true;
    });

    public Task ClearAsync() => RetryAsync(() => { Clipboard.Clear(); return true; });

    /// <summary>Copies every format that can be read (text, rich text, HTML, files, pictures, app formats) into a detached DataObject.</summary>
    public Task<object?> SnapshotAsync() => RetryAsync<object?>(() =>
    {
        var source = Clipboard.GetDataObject();
        if (source is null) return null;
        var copy = new DataObject();
        var any = false;
        foreach (var format in source.GetFormats(false))
        {
            try
            {
                var data = source.GetData(format, false);
                if (data is null) continue;
                if (data is System.IO.MemoryStream ms) data = new System.IO.MemoryStream(ms.ToArray());
                copy.SetData(format, data, false);
                any = true;
            }
            catch (Exception) { }
        }
        return any ? copy : null;
    });

    public Task RestoreAsync(object? snapshot) => RetryAsync(() =>
    {
        if (snapshot is DataObject data) Clipboard.SetDataObject(data, true);
        else Clipboard.Clear();
        return true;
    });

    private static BitmapSource? TryLoad(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
