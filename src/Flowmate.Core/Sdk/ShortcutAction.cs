namespace Flowmate.Core;

/// <summary>
/// Base class for every action. Must be public, non-abstract and have a public parameterless constructor so
/// <see cref="ActionRegistry.RegisterAssembly"/> can find it. Keep <see cref="Descriptor"/> in a static readonly field.
/// </summary>
public abstract class ShortcutAction
{
    public abstract ActionDescriptor Descriptor { get; }

    /// <summary>Does the work. Return <see cref="ContentValue.Empty"/> for "no output". Throw <see cref="ActionException"/> for expected failures.</summary>
    public abstract Task<ContentValue> RunAsync(ActionContext ctx);
}

/// <summary>An expected failure with a message that is safe to show: say what happened and what to do next.</summary>
public class ActionException : Exception
{
    public ActionException(string message) : base(message) { }
    public ActionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The user cancelled a prompt. Ends the run quietly (status Cancelled, no error shown).</summary>
public sealed class UserCancelledException : Exception
{
    public UserCancelledException() : base("Cancelled.") { }
    public UserCancelledException(string message) : base(message) { }
}

/// <summary>Thrown by Stop actions to end the current shortcut. Engine-internal but public so custom flow actions can use it.</summary>
public sealed class StopShortcutException : Exception
{
    public StopShortcutException() : base("Stopped.") { }
    public StopShortcutException(ContentValue output) : base("Stopped with output.") { Output = output; HasOutput = true; }
    public ContentValue Output { get; } = ContentValue.Empty;
    public bool HasOutput { get; }
}

/// <summary>Base for the control-flow actions (If, Menu, Repeat…). The engine runs them; <see cref="RunAsync"/> is never called.</summary>
public abstract class FlowControlAction : ShortcutAction
{
    public sealed override Task<ContentValue> RunAsync(ActionContext ctx) =>
        throw new InvalidOperationException("Control-flow actions are run by the shortcut engine.");
}
