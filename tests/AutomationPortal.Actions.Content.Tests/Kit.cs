using System.Net;
using System.Text;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Content.Tests;

internal static class Kit
{
    public static async Task<ContentValue> Run(ShortcutAction action, ContentValue? input = null, params (string Key, ParamValue Value)[] p)
    {
        using var host = new TestHost();
        return await ActionTestHarness.RunAsync(host, action, input ?? ContentValue.Empty, p);
    }

    public static Task<ContentValue> Run(TestHost host, ShortcutAction action, ContentValue? input = null, params (string Key, ParamValue Value)[] p) =>
        ActionTestHarness.RunAsync(host, action, input ?? ContentValue.Empty, p);

    public static ParamValue C(string id) => ParamValue.Choice(id);

    public static ContentValue Texts(params string[] t) => ContentValue.Texts(t);

    public static async Task<ActionException> Fails(Task<ContentValue> work) =>
        await Assert.ThrowsAnyAsync<ActionException>(() => work);

    public static string[] Lines(ContentValue v) => v.AsTexts().ToArray();

    public static DateTimeOffset L(int y, int m, int d, int h = 0, int min = 0, int s = 0)
    {
        var dt = new DateTime(y, m, d, h, min, s, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    /// <summary>Friday 2 October 2026, 09:00 in the machine's time zone.</summary>
    public static DateTimeOffset Now => L(2026, 10, 2, 9, 0);

    public static HttpResponseMessage Resp(HttpStatusCode status, string body, string contentType = "text/plain") =>
        new(status) { Content = Typed(new StringContent(body, Encoding.UTF8), contentType) };

    private static StringContent Typed(StringContent content, string contentType)
    {
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    public static HttpResponseMessage Json(string body) => Resp(HttpStatusCode.OK, body, "application/json");
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        return _respond(request);
    }

    public HttpClient Client() => new(this);
}
