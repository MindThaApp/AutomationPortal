using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Actions.Content.Tests;

public class OpenUrlsRiskTests
{
    private static ImportRisk Risk(ParamValue? urls)
    {
        var a = new ActionInstance { ActionId = "web.open" };
        if (urls is not null) a.Parameters["urls"] = urls;
        return new OpenUrlsAction().Descriptor.ImportRiskOf(a);
    }

    [Fact]
    public void Open_urls_sends_data_when_the_address_comes_from_the_input_or_a_variable()
    {
        Assert.Equal(ImportRisk.None, Risk(ParamValue.Text("https://example.com")));
        Assert.Equal(ImportRisk.None, Risk(new AskValue()));
        Assert.Equal(ImportRisk.SendsData, Risk(null));
        Assert.Equal(ImportRisk.SendsData, Risk(ParamValue.Text("")));
        Assert.Equal(ImportRisk.SendsData, Risk(ParamValue.Text("https://example.com/?q=", VarRef.Named("Clip"))));
        Assert.Equal(ImportRisk.SendsData, Risk(ParamValue.Var(VarRef.Named("Url"))));
    }
}
