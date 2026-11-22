using AutomationPortal.Actions.Windows.Advanced;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Windows.Tests;

public class ImportRiskTests
{
    private static ImportRisk OpenAppRisk(ParamValue? app)
    {
        var a = new ActionInstance { ActionId = "app.open" };
        if (app is not null) a.Parameters["app"] = app;
        return new OpenAppAction().Descriptor.ImportRiskOf(a);
    }

    [Fact]
    public void Open_app_is_flagged_unless_it_is_a_store_app_or_a_system_program()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.Equal(ImportRisk.None, OpenAppRisk(ParamValue.App(notepad, "Notepad")));
        Assert.Equal(ImportRisk.None, OpenAppRisk(ParamValue.App("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator")));
        Assert.Equal(ImportRisk.None, OpenAppRisk(null));
        Assert.Equal(ImportRisk.RunsPrograms, OpenAppRisk(ParamValue.App(@"C:\Users\Public\x.exe", "Notepad")));
        Assert.Equal(ImportRisk.RunsPrograms, OpenAppRisk(ParamValue.App(@"\\attacker.example\s\x.exe", "Notepad")));
        Assert.Equal(ImportRisk.RunsPrograms, OpenAppRisk(ParamValue.App("ms-msdt:/id x", "Help")));
    }

    [Fact]
    public void Dictate_counts_as_sending_data()
    {
        var a = new ActionInstance { ActionId = "media.dictate" };
        Assert.Equal(ImportRisk.SendsData, new AutomationPortal.Actions.Windows.Media.DictateTextAction().Descriptor.ImportRiskOf(a));
    }

    [Fact]
    public void Quit_app_is_destructive_only_when_forced()
    {
        var d = new QuitAppAction().Descriptor;
        var a = new ActionInstance { ActionId = "app.quit" };
        Assert.False(d.IsDestructiveFor(a));
        a.Parameters["force"] = ParamValue.Bool(false);
        Assert.False(d.IsDestructiveFor(a));
        a.Parameters["force"] = ParamValue.Bool(true);
        Assert.True(d.IsDestructiveFor(a));
    }

    private static AppCandidate Proc(string name, string? path = null) => new(name, path, null, null, false);

    [Fact]
    public void Force_stop_matches_only_the_exact_program()
    {
        var code = new AppItem(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", "Visual Studio Code");
        Assert.True(AppMatcher.MatchesForForceStop(code, Proc("Code", @"c:\users\me\appdata\local\programs\microsoft vs code\code.exe")));
        // Same program name in another folder, or a match by display name only: never force-stopped.
        Assert.False(AppMatcher.MatchesForForceStop(code, Proc("Code", @"C:\Other\Code.exe")));
        Assert.False(AppMatcher.MatchesForForceStop(code, Proc("Code", null)));
        Assert.False(AppMatcher.MatchesForForceStop(new AppItem("x", "Python"), Proc("Python", @"C:\Python\python.exe")));

        // Store apps: the exact package and publisher only.
        var calc = new AppItem("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator");
        Assert.True(AppMatcher.MatchesForForceStop(calc, Proc("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\CalculatorApp.exe")));
        Assert.False(AppMatcher.MatchesForForceStop(calc, Proc("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator.Fake_1.0.0.0_x64__evilpub\CalculatorApp.exe")));
        Assert.False(AppMatcher.MatchesForForceStop(calc, Proc("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_1.0.0.0_x64__evilpub\CalculatorApp.exe")));

        // Get Front App on a Store window gives the frame host: forcing it would take down every Store app window.
        var host = Path.Combine(Environment.SystemDirectory, "ApplicationFrameHost.exe");
        Assert.False(AppMatcher.MatchesForForceStop(new AppItem(host, "Application Frame Host", host), Proc("ApplicationFrameHost", host)));
        Assert.False(AppMatcher.MatchesForForceStop(new AppItem("explorer", "File Explorer"), Proc("explorer", @"C:\Windows\explorer.exe")));

        // Typed by hand: the exact process name.
        Assert.True(AppMatcher.MatchesForForceStop(new AppItem("notepad.exe", "Notepad"), Proc("notepad", @"C:\Windows\System32\notepad.exe")));
        Assert.False(AppMatcher.MatchesForForceStop(new AppItem("notepad.exe", "Notepad"), Proc("notepad2")));
    }

    [Fact]
    public void Run_command_and_open_files_are_flagged()
    {
        Assert.Equal(ImportRisk.RunsPrograms, new RunCommandAction().Descriptor.ImportRiskOf(new ActionInstance()));
        Assert.Equal(ImportRisk.RunsPrograms, new OpenFilesAction().Descriptor.ImportRiskOf(new ActionInstance()));
    }
}
