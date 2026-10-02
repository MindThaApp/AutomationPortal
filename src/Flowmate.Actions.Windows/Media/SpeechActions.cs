using System.Runtime.InteropServices;
using System.Speech.Recognition;
using Flowmate.Actions.Windows.Devices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace Flowmate.Actions.Windows.Media;

public sealed class SpeakTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.speak", Title = "Speak Text", Category = ActionCategories.SoundAndMedia, Icon = "Megaphone24",
        Keywords = new[] { "read aloud", "say", "voice", "text to speech", "narrate", "announce" },
        Summary = "Speak {text}",
        Description = "Reads text out loud with one of the voices installed on your PC. It can wait until it has finished speaking, or carry on straight away.",
        InputKinds = new[] { ContentKind.Text }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("text", "Text") with { IsInput = true, Required = true },
            new ParameterDescriptor
            {
                Key = "voice", Label = "Voice", Type = ParamType.Choice, Default = ParamValue.Choice("default"),
                ChoicesProvider = (_, _) => Task.FromResult<IReadOnlyList<Choice>>(
                    new[] { new Choice("default", "Default voice") }
                        .Concat(SpeechSynthesizer.AllVoices.Select(v => new Choice(v.Id, $"{v.DisplayName} ({v.Language})"))).ToList()),
            },
            ParameterDescriptor.Number("rate", "Speed (1 = normal)", 1, 0.5, 3, "x") with { Step = 0.1 },
            ParameterDescriptor.Bool("wait", "Wait until it has finished speaking", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException("There's nothing to say. Enter some text first.");
        var voiceId = await ctx.GetChoiceAsync("voice", "default");
        var rate = Math.Clamp(await ctx.GetNumberAsync("rate", 1), 0.5, 3);
        var wait = await ctx.GetBoolAsync("wait", true);

        var ms = await SpeechService.SynthesizeAsync(text, voiceId, rate, ctx.CancellationToken).ConfigureAwait(false);
        await AudioPlayer.PlayAsync(new WaveFileReader(ms), wait, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

internal static class SpeechService
{
    /// <summary>Turns text into a WAV in memory with a Windows voice ("default" or empty = the default voice).</summary>
    public static async Task<MemoryStream> SynthesizeAsync(string text, string voiceId, double rate, CancellationToken ct)
    {
        using var synth = new SpeechSynthesizer();
        if (voiceId != "default" && !string.IsNullOrWhiteSpace(voiceId))
        {
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId)
                        ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName.Equals(voiceId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ActionException($"Couldn't find the voice '{voiceId}'. Choose a voice that is installed on this PC.");
            synth.Voice = voice;
        }
        synth.Options.SpeakingRate = rate;

        var ms = new MemoryStream();
        try
        {
            using var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(ct).ConfigureAwait(false);
            await stream.AsStreamForRead().CopyToAsync(ms, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new ActionException("Windows couldn't turn that text into speech. Try a different voice.", ex);
        }
        ms.Position = 0;
        return ms;
    }
}

public sealed class DictateTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.dictate", Title = "Dictate Text", Category = ActionCategories.SoundAndMedia, Icon = "MicSparkle24",
        Keywords = new[] { "speech to text", "voice", "listen", "microphone", "talk", "transcribe" },
        Summary = "Listen and turn my speech into text",
        Description = "Listens through the microphone and turns what you say into text. It stops when you pause for a few seconds, or after the maximum time. It uses speech recognition on your PC, and Windows online speech recognition as a backup.",
        OutputKind = ContentKind.Text, OutputName = "Dictated Text", NeedsInternet = false,
        Parameters = new[]
        {
            ParameterDescriptor.Number("silence", "Stop after a pause of", 2, 1, 10, "seconds"),
            ParameterDescriptor.Duration("maxDuration", "Never listen longer than", 30),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var silence = TimeSpan.FromSeconds(Math.Clamp(await ctx.GetNumberAsync("silence", 2), 1, 10));
        var max = await ctx.GetDurationAsync("maxDuration", TimeSpan.FromSeconds(30));
        if (max <= TimeSpan.Zero || max > TimeSpan.FromMinutes(10)) max = TimeSpan.FromSeconds(30);
        string text;
        try
        {
            text = await Task.Run(() => DictateLocal(silence, max, ctx.CancellationToken), ctx.CancellationToken).ConfigureAwait(false);
        }
        catch (NoLocalRecognizerException)
        {
            text = await DictateOnlineAsync(silence, max, ctx.CancellationToken).ConfigureAwait(false);
        }
        text = text.Trim();
        if (text.Length == 0) throw new ActionException("I didn't hear anything. Check your microphone is on and not muted, then try again.");
        return ContentValue.Text(char.ToUpper(text[0]) + text[1..]);
    }

    private sealed class NoLocalRecognizerException : Exception { }

    private static string DictateLocal(TimeSpan silence, TimeSpan max, CancellationToken ct)
    {
        if (SpeechRecognitionEngine.InstalledRecognizers().Count == 0) throw new NoLocalRecognizerException();
        var parts = new List<string>();
        var lastHeard = DateTime.UtcNow;
        var heardAnything = false;
        try
        {
            using var engine = new SpeechRecognitionEngine();
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, e) =>
            {
                lock (parts) { parts.Add(e.Result.Text); lastHeard = DateTime.UtcNow; heardAnything = true; }
            };
            engine.RecognizeAsync(RecognizeMode.Multiple);
            var started = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(100);
                var now = DateTime.UtcNow;
                if (now - started > max) break;
                bool heard; DateTime last;
                lock (parts) { heard = heardAnything; last = lastHeard; }
                if (heard && now - last > silence) break;
                if (!heard && now - started > silence + TimeSpan.FromSeconds(8)) break;
            }
            engine.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException ex)
        {
            throw new ActionException("Flowmate couldn't use the microphone. Check one is plugged in, not muted, and allowed under Settings > Privacy & security > Microphone (including for desktop apps).", ex);
        }
        ct.ThrowIfCancellationRequested();
        lock (parts) return string.Join(" ", parts);
    }

    private static async Task<string> DictateOnlineAsync(TimeSpan silence, TimeSpan max, CancellationToken ct)
    {
        try
        {
            using var recognizer = new global::Windows.Media.SpeechRecognition.SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
                throw new ActionException("Windows couldn't start speech recognition. Turn on Settings > Privacy & security > Speech > Online speech recognition and try again.");
            recognizer.Timeouts.EndSilenceTimeout = silence;
            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(max);
            var result = await recognizer.RecognizeAsync().AsTask(cts.Token).ConfigureAwait(false);
            return result.Status == SpeechRecognitionResultStatus.Success ? result.Text : "";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ""; }
        catch (COMException ex) when ((uint)ex.HResult == 0x80045509 || (uint)ex.HResult == 0x80070005)
        {
            throw new ActionException("Windows needs permission to listen. Turn on Settings > Privacy & security > Speech > Online speech recognition, and allow microphone access for desktop apps.", ex);
        }
        catch (COMException ex)
        {
            throw new ActionException("Speech recognition isn't available on this PC. Check your microphone and the Speech settings in Windows.", ex);
        }
    }
}

public sealed class RecordAudioAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.record", Title = "Record Audio", Category = ActionCategories.SoundAndMedia, Icon = "Record24",
        Keywords = new[] { "microphone", "voice memo", "sound recording", "wav", "capture audio" },
        Summary = "Record audio {mode}",
        Description = "Records sound from the microphone into a WAV file, either for a set time or until you tell it to stop.",
        OutputKind = ContentKind.File, OutputName = "Recording",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", "Record", "time", new Choice("time", "for a set time"), new Choice("ask", "until I click Stop")),
            ParameterDescriptor.Duration("duration", "For", 10) with { VisibleWhen = new VisibleWhen("mode", "time") },
            AudioService.DeviceParam(NAudio.CoreAudioApi.DataFlow.Capture),
            ParameterDescriptor.FolderParam("saveTo", "Also save a copy in this folder"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var untilStopped = await ctx.GetChoiceAsync("mode", "time") == "ask";
        var duration = await ctx.GetDurationAsync("duration", TimeSpan.FromSeconds(10));
        if (!untilStopped && (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(4)))
            throw new ActionException("Choose a recording time between 1 second and 4 hours.");
        var deviceId = await ctx.GetChoiceAsync("device", "default");
        var saveTo = await ctx.GetPathAsync("saveTo");
        var path = ctx.NewTempPath($"Recording {DateTime.Now:yyyy-MM-dd HH.mm.ss}.wav");

        MMDevice device = await Task.Run(() => AudioService.Open(NAudio.CoreAudioApi.DataFlow.Capture, deviceId), ct).ConfigureAwait(false);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WasapiCapture? capture = null;
        WaveFileWriter? writer = null;
        try
        {
            capture = new WasapiCapture(device);
            var format = capture.WaveFormat;
            var isFloat = format.BitsPerSample == 32 && (format.Encoding == WaveFormatEncoding.IeeeFloat ||
                          format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            writer = new WaveFileWriter(path, isFloat ? new WaveFormat(format.SampleRate, 16, format.Channels) : format);
            var w = writer;
            capture.DataAvailable += (_, e) =>
            {
                if (!isFloat) { w.Write(e.Buffer, 0, e.BytesRecorded); return; }
                var samples = e.BytesRecorded / 4;
                var pcm = new byte[samples * 2];
                for (var i = 0; i < samples; i++)
                {
                    var s = (short)Math.Clamp(BitConverter.ToSingle(e.Buffer, i * 4) * 32767f, short.MinValue, short.MaxValue);
                    pcm[2 * i] = (byte)(s & 0xFF);
                    pcm[2 * i + 1] = (byte)(s >> 8);
                }
                w.Write(pcm, 0, pcm.Length);
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception is not null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };

            try { capture.StartRecording(); }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                throw new ActionException("Flowmate couldn't use the microphone. Check one is plugged in, not muted, and allowed under Settings > Privacy & security > Microphone (including for desktop apps).", ex);
            }

            try
            {
                if (untilStopped)
                    await ctx.Services.UserInteraction.ShowAlertAsync("Recording audio", "Flowmate is recording from the microphone. Click OK when you want to stop.", false, ct).ConfigureAwait(false);
                else
                    await Task.Delay(duration, ct).ConfigureAwait(false);
            }
            finally
            {
                capture.StopRecording();
                try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception) { }
            }
        }
        finally
        {
            writer?.Dispose();
            capture?.Dispose();
            device.Dispose();
        }
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(saveTo))
        {
            Directory.CreateDirectory(saveTo);
            File.Copy(path, Path.Combine(saveTo, Path.GetFileName(path)), overwrite: false);
        }
        return ContentValue.File(path);
    }
}

