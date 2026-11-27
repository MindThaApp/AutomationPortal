namespace AutomationPortal.Core;

/// <summary>
/// Test-harness switch: when on, the app never takes focus or comes to the foreground. Every explicit
/// activation goes through <see cref="Run"/>, which does nothing while the switch is on.
/// </summary>
public static class WindowActivation
{
    public const string Flag = "--no-activate";
    public const string EnvVar = "AUTOMATIONPORTAL_NO_ACTIVATE";

    /// <summary>True when windows must not be activated.</summary>
    public static bool Suppressed { get; private set; }

    public static void Set(bool suppressed) => Suppressed = suppressed;

    /// <summary>Parses the switch from the command line or the environment value.</summary>
    public static bool Parse(IEnumerable<string> args, string? envValue) =>
        args.Any(a => a.Equals(Flag, StringComparison.OrdinalIgnoreCase)) || envValue == "1";

    /// <summary>Runs an activation action unless activation is suppressed. Returns whether it ran.</summary>
    public static bool Run(Action activate)
    {
        if (Suppressed) return false;
        activate();
        return true;
    }

    /// <summary>Evaluates an activation call, or returns <paramref name="whenSuppressed"/> without calling it.</summary>
    public static T Run<T>(Func<T> activate, T whenSuppressed) => Suppressed ? whenSuppressed : activate();
}
