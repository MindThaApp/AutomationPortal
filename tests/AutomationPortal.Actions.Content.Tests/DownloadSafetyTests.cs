using System.Net;
using Flowmate.Actions.Content.Actions;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Content.Tests;

public class DownloadSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public DownloadSafetyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Downloads_get_the_mark_of_the_web_and_never_replace_a_file()
    {
        using var host = new TestHost();
        host.Services.Http = new StubHandler(_ => Kit.Resp(HttpStatusCode.OK, "MZ payload", "application/octet-stream")).Client();
        var existing = Path.Combine(_dir, "x.exe");
        File.WriteAllText(existing, "mine");

        var result = await Kit.Run(host, new DownloadFileAction(), null,
            ("url", ParamValue.Text("https://user:secret@evil.example/files/x.exe?a=1")), ("folder", ParamValue.File(_dir)), ("fileName", ParamValue.Text("x.exe")));

        var path = result.Items.OfType<PathItem>().Single().Path;
        Assert.Equal(Path.Combine(_dir, "x (2).exe"), path);
        Assert.Equal("mine", File.ReadAllText(existing));
        var zone = File.ReadAllText(path + ":Zone.Identifier");
        Assert.Contains("ZoneId=3", zone);
        Assert.Contains("HostUrl=https://evil.example/files/x.exe?a=1", zone);
        Assert.DoesNotContain("secret", zone);
    }

    [Fact]
    public void Zone_identifier_has_no_host_for_non_web_sources()
    {
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\n", HttpHelpers.ZoneIdentifierFor(new Uri("file:///C:/x.exe")));
    }

    [Theory]
    [InlineData("GET", "none", ImportRisk.None)]
    [InlineData("HEAD", "none", ImportRisk.None)]
    [InlineData("GET", "file", ImportRisk.SendsData)]
    [InlineData("POST", "none", ImportRisk.SendsData)]
    public void Get_contents_of_url_flags_sending(string method, string body, ImportRisk expected)
    {
        var a = new ActionInstance { ActionId = "web.fetch" };
        a.Parameters["method"] = ParamValue.Choice(method);
        a.Parameters["bodyType"] = ParamValue.Choice(body);
        Assert.Equal(expected, new GetContentsOfUrlAction().Descriptor.ImportRiskOf(a));
        Assert.Equal(ImportRisk.DownloadsFiles, new DownloadFileAction().Descriptor.ImportRiskOf(new ActionInstance { ActionId = "web.download" }));
    }
}
