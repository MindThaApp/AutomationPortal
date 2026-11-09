using AutomationPortal.App.Pages;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

/// <summary>"Test now" only makes the trigger's input a sample; the prompt must not promise more than that.</summary>
public sealed class TestNowPromptTests
{
    [Fact]
    public void The_prompt_says_steps_run_for_real_and_names_the_ones_that_delete()
    {
        var actions = new ActionRegistry();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.Transfer).Assembly);
        var shortcut = new Shortcut { Name = "Tidy" };
        shortcut.Actions.Add(ActionInstance.Create("file.delete"));

        var text = AutomationsPage.TestPromptText(shortcut, actions);
        Assert.Contains("every step runs for real", text);
        Assert.Contains("Delete Files", text);

        Assert.DoesNotContain("change or delete", AutomationsPage.TestPromptText(new Shortcut { Name = "Quiet" }, actions));
    }
}
