using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Hold.Tests;

internal static class Fixture
{
    public const string ShopifyDoen = "shopify-doen.json";
    public const string JsonLdNaadam = "jsonld-naadam.html";
    public const string JsonLdEdge = "jsonld-edge.html";
    public const string OpenGraphHedley = "opengraph-hedley.html";
    public const string Microdata = "microdata.html";

    public static string Text(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static async Task<IDocument> DocumentAsync(string name) =>
        await new HtmlParser().ParseDocumentAsync(Text(name), CancellationToken.None);

    public static async Task<IDocument> ParseAsync(string html) =>
        await new HtmlParser().ParseDocumentAsync(html, CancellationToken.None);
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(respond(request));

    public static HttpClient Returning(HttpStatusCode status) =>
        new(new StubHandler(_ => new HttpResponseMessage(status)));

    public static HttpClient Serving(string body, string mediaType) =>
        new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        }));
}

internal sealed class StepCollector : IProgress<ScrapeStep>
{
    public List<ScrapeStep> Steps { get; } = [];

    public void Report(ScrapeStep value) => Steps.Add(value);
}

internal sealed class HangingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        throw new UnreachableException();
    }
}
