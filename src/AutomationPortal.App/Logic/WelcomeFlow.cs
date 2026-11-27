using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The steps of the first-run welcome and where the person is in them. No UI, so it can be tested.</summary>
public sealed class WelcomeFlow
{
    public const int StepCount = 4;
    public const int HelloStep = 0, LanguageStep = 1, StartersStep = 2, TryItStep = 3;

    public const string WhatsSentUrl = "https://mindthaapp.github.io/automation-portal/privacy.html#crash-reports";

    /// <summary>The colour keys of the six hero squares (note, convert, link, text, folder, weather): keys the category palette knows, one per colour.</summary>
    public static readonly IReadOnlyList<string> HeroColours = new[] { "blue", "purple", "teal", "pink", "green", "orange" };

    public int Current { get; private set; }

    public bool CanGoBack => Current > 0;

    public bool IsLast => Current == StepCount - 1;

    /// <summary>Moves on one step. False on the last step, where the primary button finishes instead.</summary>
    public bool Next()
    {
        if (IsLast) return false;
        Current++;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        Current--;
        return true;
    }

    /// <summary>The automation id a UI Automation harness finds a step by: WelcomeStep1 to WelcomeStep4.</summary>
    public static string StepAutomationId(int step) => "WelcomeStep" + (step + 1);

    /// <summary>Crash reports are on until the person unticks them, so Skip keeps them on.</summary>
    public static bool CrashReportsDefault(AppSettings settings) => settings.SendCrashReports;

    /// <summary>
    /// Remembers the crash-report choice as soon as it is made: saves it, then lets the reporter start or stop.
    /// The same call runs again when the window closes, however it closes.
    /// </summary>
    public static void SaveCrashChoice(bool on, Action<bool> saveSetting, Action<bool>? applyToReporter)
    {
        saveSetting(on);
        applyToReporter?.Invoke(on);
    }
}

/// <summary>The sample the welcome's "Try it" step runs: it changes the case of a line of text typed into the welcome and nothing else.</summary>
public static class TrySample
{
    public const string ActionId = "text.case";
    public const string DefaultText = "hello, this is my first shortcut";
    public const int MaxLength = 60;

    public static Shortcut Create(string? text)
    {
        var line = (text ?? "").Trim();
        if (line.Length == 0) line = DefaultText;
        if (line.Length > MaxLength) line = line[..MaxLength];
        var b = new ShortcutBuilder(Strings.Get("Welcome2_TrySampleName"), "TextCaseTitle24", "violet");
        b.Shortcut.Id = SampleShortcut.SampleId;
        b.Add(ActionId, ("text", ParamValue.Text(line)), ("mode", ParamValue.Choice("upper")));
        return b.Build();
    }
}
