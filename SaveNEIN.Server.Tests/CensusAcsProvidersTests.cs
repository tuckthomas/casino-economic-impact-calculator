using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class CensusAcsProvidersTests
{
    [Fact]
    public async Task AgeProvider_ProducesContiguousZctaAgeBinsWithoutTreatingZctaAsZip()
    {
        var headers = new List<string> { "NAME" };
        headers.AddRange(Enumerable.Range(3, 23).Select(index => $"B01001_{index:000}E"));
        headers.AddRange(Enumerable.Range(27, 23).Select(index => $"B01001_{index:000}E"));
        headers.Add("zip code tabulation area");
        var values = new List<string> { "ZCTA5 46802" };
        values.AddRange(Enumerable.Repeat("10", 23));
        values.AddRange(Enumerable.Repeat("20", 23));
        values.Add("46802");
        var handler = new StubHttpHandler(JsonResponse([headers.ToArray(), values.ToArray()]));
        var provider = new CensusAcsAgePopulationProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions { ApiKey = "test-key" }));

        var dataset = await provider.FetchAsync(Request());

        Assert.Equal(DatasetSnapshotKinds.AgePopulation, dataset.DatasetKey);
        Assert.Equal(23, dataset.Rows.Count);
        Assert.All(dataset.Rows, row =>
        {
            Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
            Assert.Equal(30, row.Population);
        });
        Assert.Equal(0, dataset.Rows.First().MinimumAge);
        Assert.Null(dataset.Rows.Last().MaximumAge);
        Assert.DoesNotContain("key=", dataset.Source.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not USPS ZIP Codes", dataset.Source.Notes, StringComparison.Ordinal);
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.Contains("key=test-key", handler.RequestUris.Single().Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgeProvider_UsesOfficialTableSummaryFallbackWithoutApiKey()
    {
        var columns = Enumerable.Range(1, 49)
            .Select(index => $"B01001_E{index:000}")
            .SelectMany(column => new[] { column, column.Replace("_E", "_M", StringComparison.Ordinal) })
            .Prepend("GEO_ID")
            .ToArray();
        var values = Enumerable.Range(1, 49)
            .SelectMany(_ => new[] { "10", "1" })
            .Prepend("860Z200US46802")
            .ToArray();
        var handler = new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                string.Join('|', columns) + "\n" + string.Join('|', values) + "\n",
                Encoding.UTF8,
                "text/plain")
        });
        var provider = new CensusAcsAgePopulationProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions()));

        var dataset = await provider.FetchAsync(Request());

        Assert.Equal(23, dataset.Rows.Count);
        Assert.All(dataset.Rows, row => Assert.Equal(20, row.Population));
        Assert.Contains(dataset.Warnings, warning => warning.Contains("Summary File", StringComparison.Ordinal));
    }

    [Fact]
    public void AgeIngestion_BatchesOnlyAtCompleteOriginYearBoundaries()
    {
        var rows = new[] { "a", "b", "c" }
            .SelectMany(origin => new[]
            {
                new OriginAgeBinImportRow(origin, 2022, 0, 20, 10, "validated"),
                new OriginAgeBinImportRow(origin, 2022, 21, null, 20, "validated")
            })
            .ToArray();

        var batches = ProviderSnapshotIngestionService.CompleteAgeBatches(rows, maximumRows: 3).ToArray();

        Assert.Equal(3, batches.Length);
        Assert.All(batches, batch =>
        {
            Assert.Equal(2, batch.Count);
            Assert.Single(batch.Select(row => (row.StableOriginId, row.ObservationYear)).Distinct());
            Assert.Null(batch.OrderBy(row => row.MinimumAge).Last().MaximumAge);
        });
    }

    [Fact]
    public async Task MedianIncomeProvider_LabelsAcsIncomeAndRejectsSilentAgiSubstitution()
    {
        var handler = new StubHttpHandler(JsonResponse(
        [
            ["NAME", "B19013_001E", "zip code tabulation area"],
            ["ZCTA5 46802", "61234", "46802"]
        ]));
        var provider = new CensusAcsMedianIncomeProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions { ApiKey = "test-key" }));

        var dataset = await provider.FetchAsync(Request());
        var row = Assert.Single(dataset.Rows);

        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.Equal(61_234m, row.MedianHouseholdIncome);
        Assert.Null(row.AdjustedGrossIncome);
        Assert.Contains("not IRS adjusted gross income", row.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MedianIncomeProvider_AppliesTheSameExplicitZctaUniverseAsOriginGeography()
    {
        var handler = new StubHttpHandler(JsonResponse(
        [
            ["NAME", "B19013_001E", "zip code tabulation area"],
            ["ZCTA5 46802", "61234", "46802"],
            ["ZCTA5 99999", "50000", "99999"]
        ]));
        var provider = new CensusAcsMedianIncomeProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions { ApiKey = "test-key" }));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = "46802" }));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.NotEqual(dataset.Source.ContentHash, dataset.ContentChecksum);
    }

    [Fact]
    public async Task Provider_AllowsDocumentedKeylessRequestsWithoutPersistingAKeyParameter()
    {
        var handler = new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "GEO_ID|B19013_E001|B19013_M001\n" +
                "0100000US|75000|-555555555\n" +
                "860Z200US46802|61234|1000\n",
                Encoding.UTF8,
                "text/plain")
        });
        var provider = new CensusAcsMedianIncomeProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions()));

        var dataset = await provider.FetchAsync(Request());

        Assert.Single(dataset.Rows);
        Assert.EndsWith("acsdt5y2024-b19013.dat", handler.RequestUris.Single().AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("Summary File", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MedianIncomeProvider_OmitsUnavailableOfficialEstimateWithoutInventingIncome()
    {
        var handler = new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "GEO_ID|B19013_E001|B19013_M001\n" +
                "860Z200US43007|-666666666|-222222222\n" +
                "860Z200US46802|61234|1000\n",
                Encoding.UTF8,
                "text/plain")
        });
        var provider = new CensusAcsMedianIncomeProvider(
            new HttpClient(handler),
            Options.Create(new CensusAcsProviderOptions()));

        var dataset = await provider.FetchAsync(Request());

        var row = Assert.Single(dataset.Rows);
        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.Contains(dataset.Warnings, warning =>
            warning.Contains("43007", StringComparison.Ordinal) &&
            warning.Contains("no zero or imputed value", StringComparison.Ordinal));
    }

    private static ProviderFetchRequest Request() => new(
        "US-ZCTA",
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    private static HttpResponseMessage JsonResponse(IReadOnlyCollection<string[]> rows) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(rows), Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(response);
        }
    }
}
