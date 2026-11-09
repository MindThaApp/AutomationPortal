using Flowmate.Actions.Windows.Advanced;
using Flowmate.Actions.Windows.Apps;
using Flowmate.Core;

namespace Flowmate.Actions.Windows.Tests;

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
    public void Run_command_and_open_files_are_flagged()
    {
        Assert.Equal(ImportRisk.RunsPrograms, new RunCommandAction().Descriptor.ImportRiskOf(new ActionInstance()));
        Assert.Equal(ImportRisk.RunsPrograms, new OpenFilesAction().Descriptor.ImportRiskOf(new ActionInstance()));
    }
}
