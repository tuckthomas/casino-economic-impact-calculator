using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class CensusCountyBusinessPatternsProviderTests
{
    [Fact]
    public async Task Provider_ProducesOfficialPayrollEmploymentInventoryWithoutInventingSales()
    {
        var handler = new ZipResponseHandler(CountyArchive(
        [
            Row("18", "003", "------", 10_000, 400_000, 1_000),
            Row("18", "003", "44----", 2_000, 80_000, 200),
            Row("18", "003", "71----", 500, 15_000, 50),
            Row("18", "003", "72----", 1_500, 45_000, 150),
            Row("18", "003", "713290", 100, 4_000, 2),
            Row("18", "003", "721120", 300, 12_000, 3),
            Row("18", "005", "72----", 999, 999, 999)
        ]));
        var provider = new CensusCountyBusinessPatternsProvider(
            new HttpClient(handler),
            Options.Create(new CensusCountyBusinessPatternsProviderOptions()));

        var dataset = await provider.FetchAsync(Request("county", "18003", ImpactScopeKinds.HostCounty, "18003"));

        Assert.Equal(DatasetSnapshotKinds.LocalEconomicInventory, dataset.DatasetKey);
        Assert.Equal(5, dataset.Rows.Count);
        Assert.All(dataset.Rows, row =>
        {
            Assert.Equal(ImpactScopeKinds.HostCounty, row.GeographyType);
            Assert.Equal("18003", row.GeographyCode);
            Assert.Null(row.AnnualReceiptsOrSales);
        });
        var restaurant = dataset.Rows.Single(row => row.SectorKey == DisplacementSectorKeys.RestaurantHospitality);
        Assert.Equal(1_500, restaurant.Employment);
        Assert.Equal(45_000_000m, restaurant.AnnualPayroll);
        var casino = dataset.Rows.Single(row => row.SectorKey == LocalEconomicSectorKeys.CasinoGambling);
        Assert.Equal(400, casino.Employment);
        Assert.Equal(16_000_000m, casino.AnnualPayroll);
        Assert.Equal(["713290", "721120"], casino.NaicsCodes);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("does not publish receipts", StringComparison.Ordinal));
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.EndsWith("/2023/cbp23co.zip", handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_RejectsMissingRequiredDisplacementSectorInsteadOfSynthesizingIt()
    {
        var handler = new ZipResponseHandler(CountyArchive(
        [
            Row("18", "003", "44----", 2_000, 80_000, 200),
            Row("18", "003", "72----", 1_500, 45_000, 150)
        ]));
        var provider = new CensusCountyBusinessPatternsProvider(
            new HttpClient(handler),
            Options.Create(new CensusCountyBusinessPatternsProviderOptions()));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.FetchAsync(Request("county", "18003", ImpactScopeKinds.HostCounty, "18003")));

        Assert.Contains(DisplacementSectorKeys.ArtsEntertainmentRecreation, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("state", "18", ImpactScopeKinds.HostMunicipality, "1840788", ImpactScopeKinds.HostState)]
    [InlineData("county", "18003", ImpactScopeKinds.MetropolitanArea, "23060", ImpactScopeKinds.HostCounty)]
    public async Task Provider_RejectsRelabelingSourceGeographyAsAnotherScope(
        string sourceGeography,
        string sourceFips,
        string scopeKind,
        string scopeCode,
        string requiredScopeKind)
    {
        var handler = new ZipResponseHandler([]);
        var provider = new CensusCountyBusinessPatternsProvider(
            new HttpClient(handler),
            Options.Create(new CensusCountyBusinessPatternsProviderOptions()));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.FetchAsync(Request(sourceGeography, sourceFips, scopeKind, scopeCode)));

        Assert.Contains($"must use impact scope '{requiredScopeKind}'", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.RequestUri);
    }

    private static ProviderFetchRequest Request(
        string sourceGeography,
        string sourceFips,
        string scopeKind,
        string scopeCode) => new(
        "US-CBP",
        new DateOnly(2023, 1, 1),
        new DateOnly(2023, 12, 31),
        new Dictionary<string, string>
        {
            ["source-geography"] = sourceGeography,
            ["source-fips"] = sourceFips,
            ["impact-scope-kind"] = scopeKind,
            ["impact-scope-code"] = scopeCode
        });

    private static string Row(string state, string county, string naics, long employment, long payrollThousands, long establishments) =>
        $"\"{state}\",\"{county}\",\"{naics}\",\"G\",{employment},\"G\",0,\"G\",{payrollThousands},{establishments}";

    private static byte[] CountyArchive(IReadOnlyCollection<string> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("cbp23co.txt");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.WriteLine("\"fipstate\",\"fipscty\",\"naics\",\"emp_nf\",\"emp\",\"qp1_nf\",\"qp1\",\"ap_nf\",\"ap\",\"est\"");
            foreach (var row in rows)
            {
                writer.WriteLine(row);
            }
        }
        return output.ToArray();
    }

    private sealed class ZipResponseHandler(byte[] bytes) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }
}
