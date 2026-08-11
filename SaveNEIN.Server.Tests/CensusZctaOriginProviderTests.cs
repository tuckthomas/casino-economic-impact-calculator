using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Options;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class CensusZctaOriginProviderTests
{
    [Fact]
    public async Task Provider_EmitsOnlyExplicitCensusZctaOrigins()
    {
        var handler = new ByteResponseHandler(BuildArchive(), BuildCountyRelationships());
        var provider = new CensusZctaOriginProvider(
            new HttpClient(handler),
            Options.Create(new CensusZctaOriginProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = "46802" }));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal(DatasetSnapshotKinds.OriginGeography, dataset.DatasetKey);
        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.Equal("zcta", row.OriginType);
        Assert.Equal("46802", row.GeographyCode);
        Assert.Equal("USA", row.CountryCode);
        Assert.Equal("IN", row.StateOrTerritoryCode);
        Assert.Equal("18003", row.CountyEquivalentCode);
        Assert.Equal(41.10, row.RepresentativeLatitude, 5);
        Assert.Equal(-85.10, row.RepresentativeLongitude, 5);
        Assert.StartsWith("POLYGON", row.AreaWkt, StringComparison.Ordinal);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("do not make Census ZCTAs equivalent to USPS ZIP Codes", StringComparison.Ordinal));
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task Provider_PrefiltersCandidateStudyRegionWithBroadHaversineRadius()
    {
        var handler = new ByteResponseHandler(BuildArchive(), BuildCountyRelationships());
        var provider = new CensusZctaOriginProvider(
            new HttpClient(handler),
            Options.Create(new CensusZctaOriginProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string>
            {
                ["center-latitude"] = "41.10",
                ["center-longitude"] = "-85.10",
                ["radius-miles"] = "25"
            }));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal("46802", row.GeographyCode);
        Assert.Equal("IN", row.StateOrTerritoryCode);
        Assert.Contains("25.0-mile representative-point radius", dataset.Source.GeographicCoverage, StringComparison.Ordinal);
        Assert.Contains(dataset.Warnings, warning =>
            warning.Contains("broad representative-point Haversine radius", StringComparison.Ordinal) &&
            warning.Contains("persisted Valhalla travel times", StringComparison.Ordinal));
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task Provider_RequiresConfiguredMarketUniverseBeforeNetworkAccess()
    {
        var handler = new ByteResponseHandler(BuildArchive(), BuildCountyRelationships());
        var provider = new CensusZctaOriginProvider(
            new HttpClient(handler),
            Options.Create(new CensusZctaOriginProviderOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31))));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Provider_RejectsMixedExplicitAndRadialMarketUniverseBeforeNetworkAccess()
    {
        var handler = new ByteResponseHandler(BuildArchive(), BuildCountyRelationships());
        var provider = new CensusZctaOriginProvider(
            new HttpClient(handler),
            Options.Create(new CensusZctaOriginProviderOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string>
            {
                ["zcta-codes"] = "46802",
                ["center-latitude"] = "41.10",
                ["center-longitude"] = "-85.10",
                ["radius-miles"] = "25"
            })));

        Assert.Empty(handler.RequestUris);
    }

    private static byte[] BuildArchive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "savenein-zcta-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "cb_2020_us_zcta520_500k");
            var factory = new GeometryFactory(new PrecisionModel(), 4326);
            var features = new[]
            {
                Feature(factory, "46802", -85.20, 41.00, -85.00, 41.20),
                Feature(factory, "99999", -84.20, 40.00, -84.00, 40.20)
            };
            var writer = new ShapefileDataWriter(basePath, factory)
            {
                Header = ShapefileDataWriter.GetHeader(features[0], features.Length)
            };
            writer.Write(features);

            using var archiveStream = new MemoryStream();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "cb_2020_us_zcta520_500k.*"))
                {
                    var entry = archive.CreateEntry(Path.GetFileName(path));
                    using var source = File.OpenRead(path);
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
            }
            return archiveStream.ToArray();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildCountyRelationships() => System.Text.Encoding.UTF8.GetBytes(
        "GEOID_ZCTA5_20|GEOID_COUNTY_20|AREALAND_PART|AREAWATER_PART\n" +
        "46802|18069|100|0\n" +
        "46802|18003|900|1\n" +
        "99999|39049|500|0\n");

    private static Feature Feature(
        GeometryFactory factory,
        string code,
        double west,
        double south,
        double east,
        double north)
    {
        var polygon = factory.CreatePolygon(
        [
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south)
        ]);
        var attributes = new AttributesTable
        {
            { "ZCTA5CE20", code },
            { "GEOID20", code }
        };
        return new Feature(polygon, attributes);
    }

    private sealed class ByteResponseHandler(byte[] archive, byte[] relationships) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? archive
                        : relationships)
            });
        }
    }
}
