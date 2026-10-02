using Flowmate.Core;
using Xunit;

namespace Flowmate.Core.Tests;

public class PluralAgreementTests
{
    [Theory]
    [InlineData("Every 1 minutes", "Every 1 minute")]
    [InlineData("every 1 hours", "every 1 hour")]
    [InlineData("1 seconds", "1 second")]
    [InlineData("Repeat 1 times", "Repeat 1 time")]
    [InlineData("Get up to 1 items from the feed", "Get up to 1 item from the feed")]
    [InlineData("Every 30 minutes", "Every 30 minutes")]
    [InlineData("Every 21 minutes", "Every 21 minutes")]
    [InlineData("Every 1.5 hours", "Every 1.5 hours")]
    [InlineData("Every 11 minutes", "Every 11 minutes")]
    [InlineData("1 retries", "1 retry")]
    [InlineData("1 minute", "1 minute")]
    [InlineData("In 1 days or 1 hours", "In 1 day or 1 hour")]
    [InlineData("Version 1 status", "Version 1 status")]
    public void OneTakesASingularUnit(string input, string expected) => Assert.Equal(expected, PluralAgreement.Fix(input));

    [Theory]
    [InlineData(" minutes before an event in", " minute before an event in")]
    [InlineData("times", "time")]
    [InlineData(" hours.", " hour.")]
    [InlineData(" and stuff", " and stuff")]
    public void LeadingUnitCanBeMadeSingular(string input, string expected) => Assert.Equal(expected, PluralAgreement.SingularLeadingUnit(input));

    [Fact]
    public void SingularKeepsCaseAndLeavesUnknownWordsAlone()
    {
        Assert.Equal("Minute", PluralAgreement.Singular("Minutes"));
        Assert.Equal("press", PluralAgreement.Singular("press"));
        Assert.Equal("Tuesdays", PluralAgreement.Singular("Tuesdays"));
    }

    [Fact]
    public void OnlyTheNumberOneCounts()
    {
        Assert.True(PluralAgreement.IsOne(ParamValue.Number(1)));
        Assert.True(PluralAgreement.IsOne(ParamValue.Text("1")));
        Assert.False(PluralAgreement.IsOne(ParamValue.Number(1.5)));
        Assert.False(PluralAgreement.IsOne(ParamValue.Number(2)));
        Assert.False(PluralAgreement.IsOne(null));
    }
}
