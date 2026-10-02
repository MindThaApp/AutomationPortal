using System.Diagnostics;

namespace Flowmate.Core;

/// <summary>Runs shortcuts (SPEC §3.7). One instance per app; thread-safe.</summary>
public sealed class ShortcutRunner : IShortcutRunner
{
    public const int MaxDepth = 16;
    public const int MaxConcurrentRuns = 8;
    public const int MaxLoopIterations = 100_000;
    public const int MaxLoggedSteps = 500;

    private readonly ActionRegistry _registry;
    private readonly FlowmateServices _services;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRuns, MaxConcurrentRuns);

    /// <summary>Also registers itself as <c>services.Runner</c> when none is set (used by Run Shortcut).</summary>
    public ShortcutRunner(ActionRegistry registry, FlowmateServices services)
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

    public Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null)
    {
        var shortcut = _services.LibraryOrNull?.GetById(shortcutId);
        if (shortcut is null)
        {
            return Task.FromResult(new RunResult
            {
                ShortcutId = shortcutId, Status = RunStatus.Failed, Started = DateTimeOffset.Now,
                Source = options?.Source ?? RunSource.Library,
                ErrorMessage = "That shortcut doesn't exist any more. It may have been deleted.",
            });
        }
        return RunAsync(shortcut, input, options);
    }

    public async Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null)
    {
        options ??= new RunOptions();
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
                throw new ActionException($"Shortcuts are nested too deeply (more than {MaxDepth} levels). Check whether a shortcut runs itself.");

            if (options.Depth == 0)
            {
                await _slots.WaitAsync(options.CancellationToken).ConfigureAwait(false);
                slotTaken = true;
            }

            IReadOnlyList<FlowNode> tree;
            try { tree = FlowTree.Parse(shortcut.Actions); }
            catch (FlowStructureException ex) { throw new StepFailedException(ex.Uid ?? "", ex.Message, ex); }

            var current = await ApplyWhenNoInputAsync(shortcut, input, scope).ConfigureAwait(false);
            if (current is null) { status = RunStatus.Stopped; }
            else
            {
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
        catch (Exception ex) { status = RunStatus.Failed; error = $"Something went wrong: {ex.Message}"; }
        finally
        {
            if (slotTaken) _slots.Release();
            scope.CleanupTemp();
        }

        var result = new RunResult
        {
            RunId = scope.RunId, ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = options.Source,
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

    /// <summary>Returns the effective input, or null when the shortcut should stop because it needs input.</summary>
    private async Task<ContentValue?> ApplyWhenNoInputAsync(Shortcut shortcut, ContentValue input, RunScope scope)
    {
        if (!input.IsEmpty || shortcut.WhenNoInput == WhenNoInput.ContinueWithNothing) return input;
        switch (shortcut.WhenNoInput)
        {
            case WhenNoInput.AskForFiles:
                return await _services.UserInteraction.PickFilesAsync($"Choose the files for \"{shortcut.Name}\"", true, null, scope.Token).ConfigureAwait(false);
            case WhenNoInput.UseClipboard:
                return await _services.Clipboard.GetAsync().ConfigureAwait(false);
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
        return ExecStepAsync(a, input, rs, ctx =>
        {
            if (action is null) throw new ActionException($"This step uses an action Flowmate doesn't know ('{a.ActionId}'). Delete it or update Flowmate.");
            return action.RunAsync(ctx);
        });
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
        var attempts = a.OnError.Mode == ErrorMode.Retry ? 1 + Math.Max(0, a.OnError.Retries) : 1;
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
                OutputPreview = ContentFormat.Truncate(output.AsText(), 200), Error = error,
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
        UnauthorizedAccessException => "Flowmate isn't allowed to use that file or folder. Check it isn't read-only or open elsewhere.",
        IOException => $"There was a problem with a file or folder: {ex.Message}",
        _ => $"Something went wrong in '{d.Title}': {ex.Message}",
    };
}
