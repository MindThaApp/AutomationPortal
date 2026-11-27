using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Actions.Windows.Input;
using NAudio.Wave;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AutomationPortal.Actions.Windows.Media;

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
            catch (Exception inner) { throw new ActionException(Strings.Format("AudioPlayer_Error_Couldn_T_Play_0_Use_A", Path.GetFileName(path)), inner); }
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
        Id = "media.playPause", Title = Strings.Get("PlayPauseAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "VideoPlayPause24",
        Keywords = new[] { "music", "spotify", "video", "pause", "resume", "media key" },
        Summary = Strings.Get("PlayPauseAction_Summary"),
        Description = Strings.Get("PlayPauseAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("PlayPauseAction_Param_Mode_Label"), "toggle", new Choice("toggle", Strings.Get("PlayPauseAction_Param_Mode_Choice_Toggle")), new Choice("play", Strings.Get("PlayPauseAction_Param_Mode_Choice_Play")), new Choice("pause", Strings.Get("PlayPauseAction_Param_Mode_Choice_Pause"))),
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
        Id = "media.next", Title = Strings.Get("SkipForwardAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "NextFrame24",
        Keywords = new[] { "next", "track", "song", "skip", "media key" },
        Summary = Strings.Get("SkipForwardAction_Summary"),
        Description = Strings.Get("SkipForwardAction_Description"),
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
        Id = "media.previous", Title = Strings.Get("SkipBackAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "PreviousFrame24",
        Keywords = new[] { "previous", "track", "song", "back", "media key" },
        Summary = Strings.Get("SkipBackAction_Summary"),
        Description = Strings.Get("SkipBackAction_Description"),
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
        Id = "media.nowPlaying", Title = Strings.Get("GetNowPlayingAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "MusicNote224",
        Keywords = new[] { "song", "artist", "track", "now playing", "music", "album art", "spotify" },
        Summary = Strings.Get("GetNowPlayingAction_Summary"),
        Description = Strings.Get("GetNowPlayingAction_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("GetNowPlayingAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetNowPlayingAction_Param_Info_Label"), "all",
                new Choice("all", Strings.Get("GetNowPlayingAction_Param_Info_Choice_All")), new Choice("title", Strings.Get("GetNowPlayingAction_Param_Info_Choice_Title")), new Choice("artist", Strings.Get("GetNowPlayingAction_Param_Info_Choice_Artist")), new Choice("album", Strings.Get("GetNowPlayingAction_Param_Info_Choice_Album")),
                new Choice("app", Strings.Get("GetNowPlayingAction_Param_Info_Choice_App")), new Choice("status", Strings.Get("GetNowPlayingAction_Param_Info_Choice_Status")), new Choice("thumbnail", Strings.Get("GetNowPlayingAction_Param_Info_Choice_Thumbnail"))),
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
            throw new ActionException(Strings.Get("GetNowPlayingAction_Error_The_Media_App_Didn_T_Say"), ex);
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
        Id = "media.playSound", Title = Strings.Get("PlaySoundAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "Speaker224",
        Keywords = new[] { "beep", "chime", "alert", "audio", "mp3", "wav", "alarm" },
        Summary = Strings.Get("PlaySoundAction_Summary"),
        Description = Strings.Get("PlaySoundAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("source", Strings.Get("PlaySoundAction_Param_Source_Label"), "system", new Choice("system", Strings.Get("PlaySoundAction_Param_Source_Choice_System")), new Choice("file", Strings.Get("PlaySoundAction_Param_Source_Choice_File"))),
            new ParameterDescriptor
            {
                Key = "sound", Label = Strings.Get("PlaySoundAction_Param_Source_Label_2"), Type = ParamType.Choice, Default = ParamValue.Choice(DefaultSound),
                VisibleWhen = new VisibleWhen("source", "system"),
                ChoicesProvider = (_, _) => Task.FromResult<IReadOnlyList<Choice>>(WindowsSounds().Select(f => new Choice(Path.GetFileName(f), Path.GetFileNameWithoutExtension(f))).ToList()),
            },
            ParameterDescriptor.FileParam("file", Strings.Get("PlaySoundAction_Param_File_Label")) with { VisibleWhen = new VisibleWhen("source", "file") },
            ParameterDescriptor.Bool("wait", Strings.Get("PlaySoundAction_Param_Wait_Label"), true),
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
            path = await ctx.GetPathAsync("file") ?? throw new ActionException(Strings.Get("PlaySoundAction_Error_Choose_A_Sound_File_To"));
        }
        else
        {
            var name = await ctx.GetChoiceAsync("sound", DefaultSound);
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", Path.GetFileName(name));
        }
        if (!File.Exists(path)) throw new ActionException(Strings.Format("PlaySoundAction_Error_Couldn_T_Find_The_Sound_0", Path.GetFileName(path)));
        var wait = await ctx.GetBoolAsync("wait", true);
        await AudioPlayer.PlayAsync(AudioPlayer.OpenFile(path), wait, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}
