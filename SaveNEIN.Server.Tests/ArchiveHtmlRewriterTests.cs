using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class ArchiveHtmlRewriterTests
{
    [Fact]
    public void WebLinksAreRewrittenThroughImmutableCapture()
    {
        var captureId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        const string html = """
            <a href="/facts/details?item=1#evidence" target="_blank">Details</a>
            <a href="https://outside.example/report">Outside</a>
            <a href="#local">Local</a>
            <a href="mailto:test@example.com">Email</a>
            """;

        var rewritten = ArchiveHtmlRewriter.Rewrite(html, new Uri("https://example.com/facts/"), captureId);

        Assert.Contains($"/api/web-archives/captures/{captureId}/singlefile?url=https%3A%2F%2Fexample.com%2Ffacts%2Fdetails%3Fitem%3D1#evidence", rewritten);
        Assert.Contains($"/api/web-archives/captures/{captureId}/singlefile?url=https%3A%2F%2Foutside.example%2Freport", rewritten);
        Assert.Contains("href=\"#local\"", rewritten);
        Assert.Contains("href=\"mailto:test@example.com\"", rewritten);
        Assert.DoesNotContain("target=", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"https://", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingPageDoesNotOfferLiveLink()
    {
        var html = ArchiveHtmlRewriter.MissingLinkedPage("https://example.com/live");

        Assert.Contains("prevented from opening the mutable live website", html);
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinkExtractionResolvesRelativeHttpLinksAndSkipsNonWebSchemes()
    {
        const string html = """
            <a href="/details">Details</a>
            <a href="https://example.com/report.pdf#page=2">Report</a>
            <a href="mailto:test@example.com">Email</a>
            <a href="#local">Local</a>
            """;

        var links = ArchiveHtmlRewriter.ExtractHttpLinks(html, new Uri("https://example.com/facts/"));

        Assert.Equal(["https://example.com/details", "https://example.com/report.pdf#page=2"],
            links.Select(link => link.AbsoluteUri));
    }
}
