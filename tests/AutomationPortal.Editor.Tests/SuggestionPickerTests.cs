using AutomationPortal.Editor;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class SuggestionPickerTests
{
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(0, false, false)]
    public void OnlyTheFirstSuggestionIsPrimaryForAutomations(int index, bool forAutomation, bool expected) =>
        Assert.Equal(expected, SuggestionPicker.IsPrimary(index, forAutomation));
}
