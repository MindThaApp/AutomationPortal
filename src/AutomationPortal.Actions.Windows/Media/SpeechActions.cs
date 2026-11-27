using System.Runtime.InteropServices;
using System.Speech.Recognition;
using AutomationPortal.Actions.Windows.Devices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace AutomationPortal.Actions.Windows.Media;

public sealed class SpeakTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.speak", Title = Strings.Get("SpeakTextAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "Megaphone24",
        Keywords = new[] { "read aloud", "say", "voice", "text to speech", "narrate", "announce" },
        Summary = Strings.Get("SpeakTextAction_Summary"),
        Description = Strings.Get("SpeakTextAction_Description"),
        InputKinds = new[] { ContentKind.Text }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("text", Strings.Get("SpeakTextAction_Param_Text_Label")) with { IsInput = true, Required = true },
            new ParameterDescriptor
            {
                Key = "voice", Label = Strings.Get("SpeakTextAction_Param_Text_Label_2"), Type = ParamType.Choice, Default = ParamValue.Choice("default"),
                ChoicesProvider = (_, _) => Task.FromResult<IReadOnlyList<Choice>>(
                    new[] { new Choice("default", Strings.Get("SpeakTextAction_Param_Text_Choice_Default")) }
                        .Concat(SpeechSynthesizer.AllVoices.Select(v => new Choice(v.Id, $"{v.DisplayName} ({v.Language})"))).ToList()),
            },
            ParameterDescriptor.Number("rate", Strings.Get("SpeakTextAction_Param_Rate_Label"), 1, 0.5, 3, "x") with { Step = 0.1 },
            ParameterDescriptor.Bool("wait", Strings.Get("SpeakTextAction_Param_Wait_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (string.IsNullOrWhiteSpace(text)) throw new ActionException(Strings.Get("SpeakTextAction_Error_There_S_Nothing_To_Say_Enter"));
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
                        ?? throw new ActionException(Strings.Format("SpeechService_Error_Couldn_T_Find_The_Voice_0", voiceId));
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
            throw new ActionException(Strings.Get("SpeechService_Error_Windows_Couldn_T_Turn_That_Text"), ex);
        }
        ms.Position = 0;
        return ms;
    }
}

public sealed class DictateTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.dictate", Title = Strings.Get("DictateTextAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "MicSparkle24",
        Keywords = new[] { "speech to text", "voice", "listen", "microphone", "talk", "transcribe" },
        Summary = Strings.Get("DictateTextAction_Summary"),
        Description = Strings.Get("DictateTextAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("DictateTextAction_OutputName"), NeedsInternet = false,
        // It listens through the microphone, and Windows online speech recognition may send the audio off this PC.
        ImportRisk = ImportRisk.SendsData,
        Parameters = new[]
        {
            ParameterDescriptor.Number("silence", Strings.Get("DictateTextAction_Param_Silence_Label"), 2, 1, 10, "seconds"),
            ParameterDescriptor.Duration("maxDuration", Strings.Get("DictateTextAction_Param_MaxDuration_Label"), 30),
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
        if (text.Length == 0) throw new ActionException(Strings.Get("DictateTextAction_Error_I_Didn_T_Hear_Anything_Check"));
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
            throw new ActionException(Strings.Get("NoLocalRecognizerException_Error_Automation_Portal_Couldn_T_Use_The"), ex);
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
                throw new ActionException(Strings.Get("NoLocalRecognizerException_Error_Windows_Couldn_T_Start_Speech_Recognition"));
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
            throw new ActionException(Strings.Get("NoLocalRecognizerException_Error_Windows_Needs_Permission_To_Listen"), ex);
        }
        catch (COMException ex)
        {
            throw new ActionException(Strings.Get("NoLocalRecognizerException_Error_Speech_Recognition_Isn_T_Available_On"), ex);
        }
    }
}

public sealed class RecordAudioAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "media.record", Title = Strings.Get("RecordAudioAction_Title"), Category = ActionCategories.SoundAndMedia, Icon = "Record24",
        Keywords = new[] { "microphone", "voice memo", "sound recording", "wav", "capture audio" },
        Summary = Strings.Get("RecordAudioAction_Summary"),
        Description = Strings.Get("RecordAudioAction_Description"),
        OutputKind = ContentKind.File, OutputName = Strings.Get("RecordAudioAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("RecordAudioAction_Param_Mode_Label"), "time", new Choice("time", Strings.Get("RecordAudioAction_Param_Mode_Choice_Time")), new Choice("ask", Strings.Get("RecordAudioAction_Param_Mode_Choice_Ask"))),
            ParameterDescriptor.Duration("duration", Strings.Get("RecordAudioAction_Param_Duration_Label"), 10) with { VisibleWhen = new VisibleWhen("mode", "time") },
            AudioService.DeviceParam(NAudio.CoreAudioApi.DataFlow.Capture),
            ParameterDescriptor.FolderParam("saveTo", Strings.Get("RecordAudioAction_Param_SaveTo_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var untilStopped = await ctx.GetChoiceAsync("mode", "time") == "ask";
        var duration = await ctx.GetDurationAsync("duration", TimeSpan.FromSeconds(10));
        if (!untilStopped && (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(4)))
            throw new ActionException(Strings.Get("RecordAudioAction_Error_Choose_A_Recording_Time_Between"));
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
                throw new ActionException(Strings.Get("RecordAudioAction_Error_Automation_Portal_Couldn_T_Use_The"), ex);
            }

            try
            {
                if (untilStopped)
                    await ctx.Services.UserInteraction.ShowAlertAsync(Strings.Get("RecordAudioAction_Alert_Title"), Strings.Get("RecordAudioAction_Alert_Body"), false, ct).ConfigureAwait(false);
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
            TestRootGuard.Check(saveTo);
            Directory.CreateDirectory(saveTo);
            var copy = Path.Combine(saveTo, Path.GetFileName(path));
            for (var n = 2; File.Exists(copy) || Directory.Exists(copy); n++)
                copy = Path.Combine(saveTo, $"{Path.GetFileNameWithoutExtension(path)} {n}{Path.GetExtension(path)}");
            File.Copy(path, copy, overwrite: false);
            ctx.Journal.Created(copy);
        }
        return ContentValue.File(path);
    }
}

