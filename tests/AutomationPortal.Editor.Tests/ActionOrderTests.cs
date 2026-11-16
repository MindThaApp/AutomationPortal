using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class ActionOrderTests
{
    [Fact]
    public void Everyday_groups_lead()
    {
        var all = ActionCategories.All.ToList();
        string[] lead = { ActionCategories.FilesAndFolders, ActionCategories.AppsAndWindows, ActionCategories.Text, ActionCategories.Web,
            ActionCategories.Images, ActionCategories.Pdf, ActionCategories.Clipboard, ActionCategories.ShowAndAsk,
            ActionCategories.SoundAndMedia, ActionCategories.DeviceAndSettings };
        Assert.Equal(lead, all.Take(lead.Length));
    }

    [Fact]
    public void Scripting_is_last_among_the_listed_groups_and_logic_groups_follow_everyday_ones()
    {
        var all = ActionCategories.All.ToList();
        Assert.True(all.IndexOf(ActionCategories.Scripting) > all.IndexOf(ActionCategories.Variables));
        Assert.True(all.IndexOf(ActionCategories.Scripting) > all.IndexOf(ActionCategories.Dates));
        var firstLogic = all.FindIndex(ActionCategories.IsLogic);
        Assert.All(all.Skip(firstLogic), c => Assert.True(ActionCategories.IsLogic(c), c));
        Assert.DoesNotContain(all.Take(firstLogic), ActionCategories.IsLogic);
    }

    [Fact]
    public void Lists_group_is_called_lookups() => Assert.Equal("Lists & Lookups", ActionCategories.ListsAndDictionaries);
}
