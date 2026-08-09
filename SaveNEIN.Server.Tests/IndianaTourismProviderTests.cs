using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class IndianaTourismProviderTests
{
    [Fact]
    public async Task Provider_NormalizesPublishedMillionPersonTripsWithoutCallingThemUniqueVisitors()
    {
        const string html = """
            <html><body><ul><li>
            Total Indiana visitor volume grew 1.2% in 2023 from 80.8 to 81.7 million person-trips,
            normalizing growth after two years of strong recovery;
            </li></ul></body></html>
            """;
        var handler = new StringResponseHandler(html);
        var provider = new IndianaDestinationDevelopmentPersonTripsProvider(
            new HttpClient(handler),
            Options.Create(new IndianaTourismProviderOptions()));

        var dataset = await provider.FetchAsync(Request());

        var row = Assert.Single(dataset.Rows);
        Assert.Equal(DatasetSnapshotKinds.Tourism, dataset.DatasetKey);
        Assert.Equal("USA-IN-IDDC-2023-statewide-person-trips", row.StableObservationId);
        Assert.Equal("million-person-trips", row.SourceMetricKind);
        Assert.Equal(81.7m, row.SourceQuantity);
        Assert.Equal(81_700_000m, row.NormalizedVisitorPersonTrips);
        Assert.Contains("no conversion to unique visitors", row.NormalizationMethod, StringComparison.Ordinal);
        Assert.Contains("not unique people", row.Notes, StringComparison.Ordinal);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("not site-addressable", StringComparison.Ordinal));
        Assert.Contains(dataset.Warnings, warning => warning.Contains("Resident-origin overlap", StringComparison.Ordinal));
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task Provider_RejectsUnsupportedYearBeforeNetworkAccess()
    {
        var handler = new StringResponseHandler(string.Empty);
        var provider = new IndianaDestinationDevelopmentPersonTripsProvider(
            new HttpClient(handler),
            Options.Create(new IndianaTourismProviderOptions()));

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31))));

        Assert.Empty(handler.RequestUris);
    }

    private static ProviderFetchRequest Request() => new(
        "US-IN",
        new DateOnly(2023, 1, 1),
        new DateOnly(2023, 12, 31));

    private sealed class StringResponseHandler(string html) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
        }
    }
}
