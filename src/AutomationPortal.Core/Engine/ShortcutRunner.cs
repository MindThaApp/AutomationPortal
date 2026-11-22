using System.Diagnostics;

namespace AutomationPortal.Core;

/// <summary>Runs shortcuts (SPEC §3.7). One instance per app; thread-safe.</summary>
public sealed class ShortcutRunner : IShortcutRunner
{
    public const int MaxDepth = 16;
    public const int MaxConcurrentRuns = 8;
    public const int MaxLoopIterations = 100_000;
    public const int MaxLoggedSteps = 500;

    /// <summary>How long a top-level run waits for one of the <see cref="MaxConcurrentRuns"/> slots before giving up.</summary>
    public TimeSpan SlotWaitTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private readonly ActionRegistry _registry;
    private readonly AutomationPortalServices _services;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRuns, MaxConcurrentRuns);

    /// <summary>Also registers itself as <c>services.Runner</c> when none is set (used by Run Shortcut).</summary>
    public ShortcutRunner(ActionRegistry registry, AutomationPortalServices services)
    {
        _registry = registry;
        _services = services;
        if (!services.HasRunner) services.Runner = this;
    }

    private sealed class RunState
    {
        public required RunScope Scope;
        public readonly List<StepLog> Steps = new();
        public bool Truncated;
    }

    private sealed class StepFailedException : Exception
    {
        public StepFailedException(string uid, string message, Exception? inner) : base(message, inner) { Uid = uid; }
        public string Uid { get; }
    }

    /// <summary>Asks the person before a run for an AI caller. Null when they said yes; otherwise why it didn't run.</summary>
    private async Task<string?> AskForAiCallerAsync(Shortcut shortcut, CallerInfo caller, CancellationToken ct)
    {
        if (_services.UserInteractionOrNull is not { } ui) return "Not run: an AI assistant asked to run it and nobody could be asked here.";
        var who = caller.SafeAppName ?? "An AI assistant";
        var effect = ShortcutEffects.Of(shortcut, _registry);
        var message = $"{who} wants to run \"{shortcut.Name}\". It says it is an AI assistant.\n\n{ShortcutEffects.Badge(effect)}. " +
                      ShortcutTools.DryRun(shortcut, null, Array.Empty<string>(), _registry).Describe() + "\n\nRun it?";
        try { return await ui.ShowAlertAsync("Run this shortcut?", message, true, ct).ConfigureAwait(false) ? null : "Not run: you chose Don't run"; }
        catch (UserCancelledException) { return "Not run: you chose Don't run"; }
    }

    public Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null)
    {
        var shortcut = _services.LibraryOrNull?.GetById(shortcutId);
        if (shortcut is null)
        {
            return Task.FromResult(new RunResult
            {
                ShortcutId = shortcutId, Status = RunStatus.Failed, Started = DateTimeOffset.Now,
                Source = options?.Source ?? RunSource.Library,
                ErrorMessage = Strings.Get("ShortcutRunner_ThatShortcutDoesnT"),
            });
        }
        return RunAsync(shortcut, input, options);
    }

    public async Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null)
    {
        options ??= new RunOptions();
        if (options.Depth == 0 && options.Preview is null && options.Caller is { IsAi: true } caller
            && await AskForAiCallerAsync(shortcut, caller, options.CancellationToken).ConfigureAwait(false) is { } notRun)
            return new RunResult
            {
                RunId = options.RunId ?? "", ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = options.Source,
                Status = RunStatus.Cancelled, ErrorMessage = notRun, Started = DateTimeOffset.Now,
            };
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        var scope = new RunScope(_services, shortcut.Name, input, options, shortcut.Id);
        var rs = new RunState { Scope = scope };
        var status = RunStatus.Succeeded;
        var output = ContentValue.Empty;
        string? error = null, failedUid = null;
        var slotTaken = false;

        try
        {
            if (options.Depth > MaxDepth)
                throw new ActionException(Strings.Format("ShortcutRunner_ShortcutsAreNestedToo", MaxDepth));

            if (options.Depth == 0)
            {
                if (!await _slots.WaitAsync(SlotWaitTimeout, options.CancellationToken).ConfigureAwait(false))
                    throw new ActionException(Strings.Format("ShortcutRunner_TooManyShortcutsAre", MaxConcurrentRuns));
                slotTaken = true;
            }

            IReadOnlyList<FlowNode> tree;
            try { tree = FlowTree.Parse(shortcut.Actions); }
            catch (FlowStructureException ex) { throw new StepFailedException(ex.Uid ?? "", ex.Message, ex); }

            // A preview keeps every file and folder picked while it runs; the real run right after uses them once they are checked.
            if (options.Preview is { } plan && options.Depth == 0) plan.Picks = new PreviewPicks(shortcut.Id, LinkPolicy.StepsHash(shortcut));
            else if (options.PreviewPicks is { } picks) scope.ReplayPicks = await ClaimPreviewPicksAsync(shortcut, picks, options, scope).ConfigureAwait(false);

            var current = await ApplyWhenNoInputAsync(shortcut, input, scope).ConfigureAwait(false);
            // A preview keeps what it was given to work on, so a real run right after can use the same files.
            if (options.Preview is { } previewPlan && current is not null && !ReferenceEquals(current, input)) previewPlan.PickedInput = current;
            if (current is null) { status = RunStatus.Stopped; }
            else
            {
                // Files asked for or taken from the clipboard become the Shortcut Input, not only the first step's input.
                scope.ShortcutInput = current;
                if (options.SeedVariables is { } vars)
                    foreach (var (name, value) in vars) scope.Variables[name] = value;
                if (options.SeedOutputs is { } seeds)
                    foreach (var (uid, value) in seeds) scope.Outputs[uid] = value;
                if (options.StartAtUid is { } startUid)
                {
                    var idx = tree.ToList().FindIndex(n => Contains(n, startUid));
                    if (idx > 0)
                    {
                        if (options.SeedOutputs is not null && options.SeedOutputs.TryGetValue(StartUidOf(tree[idx - 1]), out var prev)) current = prev;
                        tree = tree.Skip(idx).ToList();
                    }
                }
                output = await ExecBlockAsync(tree, current, rs).ConfigureAwait(false);
            }
        }
        catch (StopShortcutException stop)
        {
            if (stop.HasOutput) output = stop.Output; else status = RunStatus.Stopped;
        }
        catch (UserCancelledException) { status = RunStatus.Cancelled; }
        catch (OperationCanceledException) when (options.CancellationToken.IsCancellationRequested) { status = RunStatus.Cancelled; }
        catch (StepFailedException ex) { status = RunStatus.Failed; error = ex.Message; failedUid = ex.Uid; }
        catch (ActionException ex) { status = RunStatus.Failed; error = ex.Message; }
        catch (Exception ex) { status = RunStatus.Failed; error = Strings.Format("ShortcutRunner_SomethingWentWrong", ex.Message); }
        finally
        {
            if (slotTaken) _slots.Release();
            if (!options.KeepTemp) scope.CleanupTemp();
            try { scope.SealJournal(); } catch { /* the journal must never break a run */ }
        }

        var result = new RunResult
        {
            RunId = scope.RunId, TempFolder = options.KeepTemp ? scope.CreatedTempFolder : null, ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = options.Source,
            Status = status, Output = output, ErrorMessage = error, FailedStepUid = failedUid,
            Started = started, Duration = sw.Elapsed, Steps = rs.Steps.ToList(), StepsTruncated = rs.Truncated,
        };

        if (options.RecordHistory && options.Depth == 0 && options.Source != RunSource.Nested && _services.HistoryOrNull is { } history)
        {
            try { history.Add(result.ToRecord()); } catch { /* history must never break a run */ }
        }
        return result;
    }

    private static string StartUidOf(FlowNode n) => n switch
    {
        ActionNode a => a.Action.Uid,
        IfNode i => i.Start.Uid,
        MenuNode m => m.Start.Uid,
        RepeatNode r => r.Start.Uid,
        _ => "",
    };

    private static bool Contains(FlowNode n, string uid) => n switch
    {
        ActionNode a => a.Action.Uid == uid,
        IfNode i => i.Start.Uid == uid || i.Then.Concat(i.Else).Any(c => Contains(c, uid)),
        MenuNode m => m.Start.Uid == uid || m.Branches.Any(b => b.Marker.Uid == uid || b.Body.Any(c => Contains(c, uid))),
        RepeatNode r => r.Start.Uid == uid || r.Body.Any(c => Contains(c, uid)),
        _ => false,
    };

    /// <summary>
    /// The picks from a preview that this run may use: only for a top-level library or editor run (never a scheduled, triggered or
    /// nested one, never one that starts part-way), only for the very shortcut and steps that were previewed, and only once. When
    /// a picked file or folder is gone or changed since, the person is told and asked again; with nobody to ask, the run stops.
    /// Each pick is checked again at the moment it is used (<see cref="PreviewPicks.TryTake"/>), so a change made later is caught too.
    /// </summary>
    private async Task<PreviewPicks?> ClaimPreviewPicksAsync(Shortcut shortcut, PreviewPicks picks, RunOptions options, RunScope scope)
    {
        if (options.Depth != 0 || options.StartAtUid is not null || options.Source is not (RunSource.Library or RunSource.Editor)) return null;
        if (!picks.TryClaim(shortcut.Id, LinkPolicy.StepsHash(shortcut))) return null;
        var changed = picks.ChangedItems();
        if (changed.Count == 0) return picks;
        await PreviewPicks.ConfirmAskAgainAsync(_services, shortcut.Name, PreviewPicks.ChangedText(changed), "Choose the files again.", nothingRanYet: true, scope.Token).ConfigureAwait(false);
        return null;
    }

    /// <summary>Returns the effective input, or null when the shortcut should stop because it needs input.</summary>
    private async Task<ContentValue?> ApplyWhenNoInputAsync(Shortcut shortcut, ContentValue input, RunScope scope)
    {
        if (!input.IsEmpty || shortcut.WhenNoInput == WhenNoInput.ContinueWithNothing) return input;
        if (shortcut.WhenNoInput is WhenNoInput.AskForFiles or WhenNoInput.UseClipboard
            && scope.ReplayPicks is { } replay && replay.TryTake(PreviewPicks.InputKey, out var kept))
        {
            if (shortcut.WhenNoInput == WhenNoInput.UseClipboard)
            {
                // The preview's clipboard content is used only while the clipboard still holds the very same content.
                var now = await _services.Clipboard.GetAsync().ConfigureAwait(false);
                if (kept.Changed.Count == 0 && kept.ContentStamp is { } before && PreviewPicks.ContentStampOf(now) == before) return kept.Value;
                var what = kept.Changed.Count > 0 ? PreviewPicks.ChangedText(kept.Changed) : PreviewPicks.ClipboardChangedText;
                await PreviewPicks.ConfirmAskAgainAsync(_services, shortcut.Name, what, "Run it with what's on the clipboard now?", nothingRanYet: true, scope.Token).ConfigureAwait(false);
                return now;
            }
            if (kept.Changed.Count == 0) return kept.Value;
            await PreviewPicks.ConfirmAskAgainAsync(_services, shortcut.Name, PreviewPicks.ChangedText(kept.Changed), "Choose the files again.", nothingRanYet: true, scope.Token).ConfigureAwait(false);
        }
        var keep = scope.Options.Depth == 0 ? scope.Options.Preview?.Picks : null;
        switch (shortcut.WhenNoInput)
        {
            case WhenNoInput.AskForFiles:
                {
                    var picked = await _services.UserInteraction.PickFilesAsync($"Choose the files for \"{shortcut.Name}\"", true, null, scope.Token).ConfigureAwait(false);
                    keep?.Record(PreviewPicks.InputKey, picked);
                    return picked;
                }
            case WhenNoInput.UseClipboard:
                {
                    var clip = await _services.Clipboard.GetAsync().ConfigureAwait(false);
                    keep?.Record(PreviewPicks.InputKey, clip, PreviewPicks.ContentStampOf(clip));
                    return clip;
                }
            case WhenNoInput.StopAndSay:
                if (_services.UserInteractionOrNull is { } ui)
                    await ui.ShowAlertAsync(shortcut.Name, "This shortcut needs something to work on. Select something first, then run it again.", false, scope.Token).ConfigureAwait(false);
                return null;
            default: return input;
        }
    }

    // ---- block execution ---------------------------------------------------------------------

    private async Task<ContentValue> ExecBlockAsync(IReadOnlyList<FlowNode> nodes, ContentValue input, RunState rs)
    {
        var current = input;
        foreach (var node in nodes)
        {
            rs.Scope.Token.ThrowIfCancellationRequested();
            switch (node)
            {
                case ActionNode an:
                    current = await ExecActionAsync(an.Action, current, rs).ConfigureAwait(false);
                    break;
                case IfNode n:
                    current = await ExecIfAsync(n, current, rs).ConfigureAwait(false);
                    break;
                case MenuNode n:
                    current = await ExecMenuAsync(n, current, rs).ConfigureAwait(false);
                    break;
                case RepeatNode n:
                    current = await ExecRepeatAsync(n, current, rs).ConfigureAwait(false);
                    break;
            }
        }
        return current;
    }

    private Task<ContentValue> ExecActionAsync(ActionInstance a, ContentValue input, RunState rs)
    {
        if (a.Disabled || a.ActionId == FlowIds.Comment) return Task.FromResult(input);
        var action = _registry.Get(a.ActionId);
        if (rs.Scope.Options.Preview is { } plan) return PreviewActionAsync(a, action, input, rs, plan);
        return ExecStepAsync(a, input, rs, ctx =>
        {
            if (action is null) throw new ActionException(Strings.Format("ShortcutRunner_ThisStepUsesAn", a.ActionId));
            return action.RunAsync(ctx);
        });
    }

    /// <summary>
    /// Preview mode: a step that only reads runs for real, a step that can plan says what it would do, and anything else is not
    /// run at all (it is listed instead, and later steps get nothing from it).
    /// </summary>
    private Task<ContentValue> PreviewActionAsync(ActionInstance a, ShortcutAction? action, ContentValue input, RunState rs, FileChangePlan plan)
    {
        var title = _registry.Describe(a.ActionId)?.Title ?? a.ActionId;
        if (action is IPlansFileChanges planner) return ExecStepAsync(a, input, rs, ctx => planner.PlanAsync(ctx, plan));
        if (action is not null && FilePreview.ReadOnlyActionIds.Contains(a.ActionId)) return ExecStepAsync(a, input, rs, action.RunAsync);
        plan.Add(new PlannedChange(FilePreview.DoesNotChangeFiles(a.ActionId) ? PlannedChangeKind.NotRun : PlannedChangeKind.CantPreview, title));
        rs.Scope.Outputs[a.Uid] = ContentValue.Empty;
        return Task.FromResult(ContentValue.Empty);
    }

    private async Task<ContentValue> ExecIfAsync(IfNode n, ContentValue input, RunState rs)
    {
        if (n.Start.Disabled) return input;
        var decision = await ExecStepAsync(n.Start, input, rs, async ctx =>
            ContentValue.Bool((await ctx.GetConditionsAsync("conditions").ConfigureAwait(false)).Evaluate())).ConfigureAwait(false);
        var branch = decision.AsBool() ? n.Then : n.Else;
        var result = await ExecBlockAsync(branch, input, rs).ConfigureAwait(false);
        rs.Scope.Outputs[n.Start.Uid] = result;
        return result;
    }

    private async Task<ContentValue> ExecMenuAsync(MenuNode n, ContentValue input, RunState rs)
    {
        if (n.Start.Disabled || n.Branches.Count == 0) return input;
        if (rs.Scope.Options.Preview is { } plan)
        {
            // A preview never asks; what the chosen branch would do can't be known.
            plan.Add(new PlannedChange(PlannedChangeKind.CantPreview, _registry.Describe(n.Start.ActionId)?.Title ?? "Choose from Menu", Note: "it asks you to choose"));
            return ContentValue.Empty;
        }
        var labels = n.Branches.Select(b => b.Label).ToList();
        var chosen = await ExecStepAsync(n.Start, input, rs, async ctx =>
        {
            var prompt = await ctx.GetTextAsync("prompt").ConfigureAwait(false);
            var idx = await ctx.Services.UserInteraction.ChooseFromMenuAsync(prompt, labels, ctx.CancellationToken).ConfigureAwait(false);
            return ContentValue.Number(idx);
        }).ConfigureAwait(false);
        if (chosen.AsNumber() is not { } i || i < 0 || i >= n.Branches.Count) return input;
        var branch = n.Branches[(int)i];
        rs.Scope.MenuStack.Push(branch.Label);
        try
        {
            var result = await ExecBlockAsync(branch.Body, input, rs).ConfigureAwait(false);
            rs.Scope.Outputs[n.Start.Uid] = result;
            return result;
        }
        finally { rs.Scope.MenuStack.Pop(); }
    }

    private async Task<ContentValue> ExecRepeatAsync(RepeatNode n, ContentValue input, RunState rs)
    {
        if (n.Start.Disabled) return input;
        var scope = rs.Scope;
        var results = new List<ContentValue>();

        if (!n.Each)
        {
            var countValue = await ExecStepAsync(n.Start, input, rs, async ctx =>
            {
                var c = await ctx.GetNumberAsync("count", 1).ConfigureAwait(false);
                if (c > MaxLoopIterations)
                    throw new ActionException($"That would repeat more than {MaxLoopIterations:N0} times. Choose a smaller number.");
                return ContentValue.Number(Math.Max(0, Math.Floor(c)));
            }).ConfigureAwait(false);
            var count = (int)(countValue.AsNumber() ?? 0);
            for (var i = 1; i <= count; i++)
            {
                scope.Token.ThrowIfCancellationRequested();
                Observe(o => o.OnCount(n.Start.Uid, i, count), scope);
                scope.RepeatStack.Push((ContentValue.Number(i), i));
                try { results.Add(await ExecBlockAsync(n.Body, input, rs).ConfigureAwait(false)); }
                finally { scope.RepeatStack.Pop(); }
            }
        }
        else
        {
            var items = await ExecStepAsync(n.Start, input, rs, async ctx =>
            {
                var list = await ctx.GetContentAsync("items").ConfigureAwait(false);
                if (list.Count > MaxLoopIterations)
                    throw new ActionException($"That list has more than {MaxLoopIterations:N0} items. Filter it first.");
                return list;
            }).ConfigureAwait(false);
            for (var i = 0; i < items.Count; i++)
            {
                scope.Token.ThrowIfCancellationRequested();
                var number = i + 1;
                Observe(o => o.OnCount(n.Start.Uid, number, items.Count), scope);
                var item = ContentValue.From(items[i]);
                scope.RepeatStack.Push((item, i + 1));
                try { results.Add(await ExecBlockAsync(n.Body, item, rs).ConfigureAwait(false)); }
                finally { scope.RepeatStack.Pop(); }
            }
        }

        var output = ContentValue.Concat(results.ToArray());
        scope.Outputs[n.Start.Uid] = output;
        return output;
    }

    // ---- single step -------------------------------------------------------------------------

    private async Task<ContentValue> ExecStepAsync(ActionInstance a, ContentValue input, RunState rs, Func<ActionContext, Task<ContentValue>> body)
    {
        var scope = rs.Scope;
        var descriptor = _registry.Describe(a.ActionId) ?? UnknownDescriptor(a.ActionId);
        var attempts = a.OnError.Mode == ErrorMode.Retry && scope.Options.Preview is null ? 1 + Math.Max(0, a.OnError.Retries) : 1;
        if (scope.Options.BeforeStep is { } before)
        {
            await before(new BeforeStepInfo(a.Uid, a.ActionId, descriptor.Title)).ConfigureAwait(false);
            scope.Token.ThrowIfCancellationRequested();
        }
        Observe(o => o.OnStepStarted(a.Uid), scope);

        for (var attempt = 1; ; attempt++)
        {
            scope.Token.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var ctx = new ActionContext(scope, a, descriptor, input);
                var output = await body(ctx).ConfigureAwait(false);
                output ??= ContentValue.Empty;
                scope.Outputs[a.Uid] = output;
                if (descriptor.SensitiveOutput) scope.SensitiveOutputs[a.Uid] = 0;
                Log(rs, a, descriptor, sw.Elapsed, output, null);
                Observe(o => o.OnStepFinished(a.Uid, output, sw.Elapsed, null), scope);
                return output;
            }
            catch (StopShortcutException) { throw; }
            catch (UserCancelledException) { throw; }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var message = Friendly(ex, descriptor);
                if (attempt < attempts)
                {
                    scope.Observer?.OnLog($"{descriptor.Title} failed ({message}). Trying again ({attempt}/{attempts - 1})…");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, a.OnError.RetryDelaySeconds)), scope.Token).ConfigureAwait(false);
                    continue;
                }
                Log(rs, a, descriptor, sw.Elapsed, ContentValue.Empty, message);
                Observe(o => o.OnStepFinished(a.Uid, ContentValue.Empty, sw.Elapsed, message), scope);
                if (a.OnError.Mode == ErrorMode.Continue)
                {
                    scope.Outputs[a.Uid] = ContentValue.Empty;
                    return ContentValue.Empty;
                }
                throw new StepFailedException(a.Uid, message, ex);
            }
        }
    }

    private static ActionDescriptor UnknownDescriptor(string id) =>
        new() { Id = id, Title = id, Category = ActionCategories.Scripting };

    private static void Observe(Action<IRunObserver> call, RunScope scope)
    {
        if (scope.Observer is not { } o) return;
        try { call(o); } catch { /* observers must not break runs */ }
    }

    private static void Log(RunState rs, ActionInstance a, ActionDescriptor d, TimeSpan time, ContentValue output, string? error)
    {
        lock (rs.Steps)
        {
            if (rs.Steps.Count >= MaxLoggedSteps) { rs.Truncated = true; return; }
            rs.Steps.Add(new StepLog
            {
                Uid = a.Uid, ActionId = a.ActionId, Title = d.Title, DurationMs = time.TotalMilliseconds,
                OutputPreview = d.SensitiveOutput && !output.IsEmpty ? "(hidden)" : ContentFormat.Truncate(output.AsText(), 200), Error = error,
            });
        }
    }

    private static string Friendly(Exception ex, ActionDescriptor d) => ex switch
    {
        ActionException => ex.Message,
        HttpRequestException => $"Couldn't reach the internet for '{d.Title}'. Check your connection and try again.",
        TaskCanceledException or TimeoutException or OperationCanceledException => $"'{d.Title}' took too long and was stopped. Try again.",
        FileNotFoundException f => $"Couldn't find the file '{Path.GetFileName(f.FileName ?? "")}'. Check the file still exists.",
        DirectoryNotFoundException => "Couldn't find that folder. Check the folder still exists.",
        UnauthorizedAccessException => "Automation Portal isn't allowed to use that file or folder. Check it isn't read-only or open elsewhere.",
        IOException => $"There was a problem with a file or folder: {ex.Message}",
        _ => $"Something went wrong in '{d.Title}': {ex.Message}",
    };
}
