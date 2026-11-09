using Flowmate.Actions.Windows.Apps;
using Flowmate.Actions.Windows.Input;
using NAudio.Wave;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Flowmate.Actions.Windows.Media;

/// <summary>Plays audio through the default output device.</summary>
internal static class AudioPlayer
{
    /// <summary>Plays the source. With <paramref name="wait"/> false it returns straight away and finishes in the background.</summary>
    public static async Task PlayAsync(WaveStream source, bool wait, CancellationToken ct)
    {
        var output = new WaveOutEvent();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => done.TrySetResult();
        try
        {
            output.Init(source);
            output.Play();
        }
        catch
        {
            output.Dispose();
            source.Dispose();
            throw;
        }

        async Task FinishAsync()
        {
            try { await done.Task.ConfigureAwait(false); }
            finally { output.Dispose(); source.Dispose(); }
        }

        if (!wait) { _ = FinishAsync(); return; }
        using (ct.Register(() => { try { output.Stop(); } catch { } })) await FinishAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    public static WaveStream OpenFile(string path)
    {
        try { return OpenReader(path); }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            try { return new MediaFoundationReader(path); }
            catch (Exception inner) { throw new ActionException($"Couldn't play '{Path.GetFileName(path)}'. Use a normal sound file such as WAV or MP3.", inner); }
        }
    }

    // Same reader choice as NAudio's AudioFileReader (which lives in the NAudio meta-package this library avoids).
    private static WaveStream OpenReader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav")
        {
            WaveStream reader = new WaveFileReader(path);
            if (reader.WaveFormat.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
                reader = new BlockAlignReductionStream(WaveFormatConversionStream.CreatePcmStream(reader));
            return reader;
        }
        if (ext is ".aiff" or ".aif") return new AiffFileReader(path);
        return new MediaFoundationReader(path);
    }
}

internal static class MediaSessions
{
    public static async Task<GlobalSystemMediaTransportControlsSession?> CurrentAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return manager.GetCurrentSession();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return null; }
    }

    /// <summary>Runs the command on the current media app, or presses the media key when the app doesn't answer.</summary>
    public static async Task SendAsync(Func<GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> command, int fallbackKey)
    {
        var session = await CurrentAsync();
        if (session is not null)
        {
            try { if (await command(session)) return; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
        InputSender.TapKey(fallbackKey);
    }
}

public sealed class PlayPauseAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.playPause", Title = "Play/Pause", Category = ActionCategories.SoundAndMedia, Icon = "VideoPlayPause24",
        Keywords = new[] { "music", "spotify", "video", "pause", "resume", "media key" },
        Summary = "{mode} the media that's playing",
        Description = "Plays or pauses the music or video that's playing (Spotify, YouTube in your browser, Media Player…), like the media key on a keyboard.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "Action", "toggle", new Choice("toggle", "Play or pause"), new Choice("play", "Play"), new Choice("pause", "Pause")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var mode = await ctx.GetChoiceAsync("mode", "toggle");
        if (mode == "pause" || mode == "play")
        {
            var session = await MediaSessions.CurrentAsync();
            if (session is not null)
            {
                var status = session.GetPlaybackInfo().PlaybackStatus;
                var playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (playing == (mode == "play")) return ContentValue.Empty;
            }
            else if (mode == "pause") return ContentValue.Empty;
        }
        await MediaSessions.SendAsync(s => s.TryTogglePlayPauseAsync(), 0xB3);
        return ContentValue.Empty;
    }
}

public sealed class SkipForwardAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.next", Title = "Skip Forward", Category = ActionCategories.SoundAndMedia, Icon = "NextFrame24",
        Keywords = new[] { "next", "track", "song", "skip", "media key" },
        Summary = "Skip to the next track",
        Description = "Skips to the next song or video in the media app that's playing.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await MediaSessions.SendAsync(s => s.TrySkipNextAsync(), 0xB0);
        return ContentValue.Empty;
    }
}

public sealed class SkipBackAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.previous", Title = "Skip Back", Category = ActionCategories.SoundAndMedia, Icon = "PreviousFrame24",
        Keywords = new[] { "previous", "track", "song", "back", "media key" },
        Summary = "Go back to the previous track",
        Description = "Goes back to the previous song or video in the media app that's playing.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await MediaSessions.SendAsync(s => s.TrySkipPreviousAsync(), 0xB1);
        return ContentValue.Empty;
    }
}

public sealed class GetNowPlayingAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.nowPlaying", Title = "Get What's Playing", Category = ActionCategories.SoundAndMedia, Icon = "MusicNote224",
        Keywords = new[] { "song", "artist", "track", "now playing", "music", "album art", "spotify" },
        Summary = "Get what's playing: {info}",
        Description = "Gets the title, artist, album and cover picture of the music or video that's playing in any media app. If nothing is playing you get an empty result.",
        OutputKind = ContentKind.Dictionary, OutputName = "Now Playing",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "all",
                new Choice("all", "everything"), new Choice("title", "title"), new Choice("artist", "artist"), new Choice("album", "album"),
                new Choice("app", "app"), new Choice("status", "status (Playing, Paused…)"), new Choice("thumbnail", "cover picture")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var info = await ctx.GetChoiceAsync("info", "all");
        var session = await MediaSessions.CurrentAsync();
        if (session is null)
            return info == "all" ? ContentValue.Dict(("Playing", ContentValue.Bool(false))) : info == "status" ? ContentValue.Text("Nothing playing") : ContentValue.Empty;

        GlobalSystemMediaTransportControlsSessionMediaProperties props;
        try { props = await session.TryGetMediaPropertiesAsync(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            throw new ActionException("The media app didn't say what's playing. Try again in a moment.", ex);
        }
        var status = session.GetPlaybackInfo().PlaybackStatus.ToString();
        var app = await FriendlyAppAsync(session.SourceAppUserModelId, ctx.CancellationToken);

        ContentValue? thumb = null;
        if (info is "all" or "thumbnail") thumb = await SaveThumbnailAsync(ctx, props.Thumbnail);

        switch (info)
        {
            case "title": return ContentValue.Text(props.Title ?? "");
            case "artist": return ContentValue.Text(props.Artist ?? "");
            case "album": return ContentValue.Text(props.AlbumTitle ?? "");
            case "app": return ContentValue.Text(app);
            case "status": return ContentValue.Text(status);
            case "thumbnail": return thumb ?? ContentValue.Empty;
        }
        var timeline = session.GetTimelineProperties();
        var entries = new List<KeyValuePair<string, ContentValue>>
        {
            new("Title", props.Title ?? ""), new("Artist", props.Artist ?? ""), new("Album", props.AlbumTitle ?? ""),
            new("App", app), new("Status", status),
            new("Playing", ContentValue.Bool(status == "Playing")),
            new("Position (seconds)", Math.Round(timeline.Position.TotalSeconds)),
            new("Length (seconds)", Math.Round((timeline.EndTime - timeline.StartTime).TotalSeconds)),
        };
        if (thumb is not null) entries.Add(new("Cover", thumb));
        return ContentValue.Dict(entries);
    }

    private static async Task<string> FriendlyAppAsync(string aumid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        try
        {
            var apps = await InstalledApps.GetInstalledAppsAsync(false, ct).ConfigureAwait(false);
            var hit = apps.FirstOrDefault(a => a.Id.Equals(aumid, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Name;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        var name = aumid.Contains('!') ? aumid[..aumid.IndexOf('!')] : aumid;
        name = Path.GetFileNameWithoutExtension(name);
        return name.Contains('_') ? name[..name.IndexOf('_')] : name;
    }

    private static async Task<ContentValue?> SaveThumbnailAsync(ActionContext ctx, IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0) return null;
            var ext = stream.ContentType switch { "image/png" => ".png", "image/bmp" => ".bmp", "image/gif" => ".gif", _ => ".jpg" };
            var path = ctx.NewTempPath("Cover" + ext);
            await using (var file = File.Create(path))
            using (var input = stream.AsStreamForRead())
                await input.CopyToAsync(file, ctx.CancellationToken).ConfigureAwait(false);
            return ContentValue.Image(path);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException) { return null; }
    }
}

public sealed class PlaySoundAction : ShortcutAction
{
    private const string DefaultSound = "Windows Notify System Generic.wav";

    private static readonly ActionDescriptor D = new()
    {
        Id = "media.playSound", Title = "Play Sound", Category = ActionCategories.SoundAndMedia, Icon = "Speaker224",
        Keywords = new[] { "beep", "chime", "alert", "audio", "mp3", "wav", "alarm" },
        Summary = "Play {sound}",
        Description = "Plays a Windows sound or a sound file (WAV, MP3…). It can wait until the sound has finished, or carry on straight away.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("source", "Sound from", "system", new Choice("system", "a Windows sound"), new Choice("file", "a file")),
            new ParameterDescriptor
            {
                Key = "sound", Label = "Windows sound", Type = ParamType.Choice, Default = ParamValue.Choice(DefaultSound),
                VisibleWhen = new VisibleWhen("source", "system"),
                ChoicesProvider = (_, _) => Task.FromResult<IReadOnlyList<Choice>>(WindowsSounds().Select(f => new Choice(Path.GetFileName(f), Path.GetFileNameWithoutExtension(f))).ToList()),
            },
            ParameterDescriptor.FileParam("file", "Sound file") with { VisibleWhen = new VisibleWhen("source", "file") },
            ParameterDescriptor.Bool("wait", "Wait until it has finished", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    private static IEnumerable<string> WindowsSounds()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<string>();
    }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        string path;
        if (await ctx.GetChoiceAsync("source", "system") == "file")
        {
            path = await ctx.GetPathAsync("file") ?? throw new ActionException("Choose a sound file to play.");
        }
        else
        {
            var name = await ctx.GetChoiceAsync("sound", DefaultSound);
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", Path.GetFileName(name));
        }
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the sound '{Path.GetFileName(path)}'. Check the file still exists.");
        var wait = await ctx.GetBoolAsync("wait", true);
        await AudioPlayer.PlayAsync(AudioPlayer.OpenFile(path), wait, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}
