using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class IndianaProviderAdaptersTests
{
    [Fact]
    public async Task GamingCommissionProvider_PreservesCasinoWinAndTaxableBaseAsDistinctMetrics()
    {
        var handler = new ByteResponseHandler(BuildIgcWorkbook());
        var provider = new IndianaGamingCommissionMonthlyRevenueProvider(
            new HttpClient(handler),
            Options.Create(new IndianaGamingCommissionProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 12, 31)));

        Assert.Equal(DatasetSnapshotKinds.ObservedPerformance, dataset.DatasetKey);
        Assert.Equal(6, dataset.Rows.Count);
        var ameristar = dataset.Rows.Where(row => row.StableVenueId == "USA-IN-IGC-ameristar-casino").ToArray();
        Assert.Equal(3, ameristar.Length);
        Assert.Equal(11_795_140.78m, ameristar.Single(row => row.ReportedMetricKey == "casino-win").ReportedAmount);
        Assert.Equal(10_248_996.79m, ameristar.Single(row => row.ReportedMetricKey == "taxable-gaming-revenue").ReportedAmount);
        Assert.Equal(11_795_140.78m, ameristar.Single(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue).ReportedAmount);
        Assert.Contains("intentionally distinct", ameristar.Single(row => row.ReportedMetricKey == "taxable-gaming-revenue").Notes);
        Assert.Contains(dataset.Rows, row => row.StableVenueId == "USA-IN-IGC-horseshoe-indianapolis");
        Assert.Equal("https://www.in.gov/igc/files/reports/2025/2025-12-Revenue.xlsx", dataset.Source.Url);
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GamingCommissionProvider_RejectsPartialMonthBeforeNetworkAccess()
    {
        var handler = new ByteResponseHandler(BuildIgcWorkbook());
        var provider = new IndianaGamingCommissionMonthlyRevenueProvider(
            new HttpClient(handler),
            Options.Create(new IndianaGamingCommissionProviderOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 12, 30))));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GamingCommissionProvider_IngestsCompleteYearAsMonthlySeries()
    {
        var handler = new ByteResponseHandler(BuildIgcWorkbook());
        var provider = new IndianaGamingCommissionMonthlyRevenueProvider(
            new HttpClient(handler),
            Options.Create(new IndianaGamingCommissionProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));

        Assert.Equal("2025", dataset.Period);
        Assert.Equal(72, dataset.Rows.Count);
        Assert.Equal(12, handler.RequestUris.Count);
        Assert.Equal(12, dataset.Rows.Select(row => row.PeriodStart).Distinct().Count());
        Assert.All(dataset.Rows, row => Assert.Equal("monthly", row.PeriodGranularity));
        Assert.Equal("https://www.in.gov/igc/publications/monthly-revenue/", dataset.Source.Url);
        Assert.Contains("2025-01-Revenue.xlsx", dataset.Source.Notes);
        Assert.Contains("2025-12-Revenue.xlsx", dataset.Source.Notes);
    }

    [Fact]
    public async Task GamingCommissionFacilityProvider_JoinsOfficialLocationsToMonthlyUnitCounts()
    {
        var handler = new IgcResponseHandler(BuildIgcWorkbook(), BuildIgcLocationsHtml(), BuildIgcAnnualReportPdf());
        var provider = new IndianaGamingCommissionFacilityInventoryProvider(
            new HttpClient(handler),
            Options.Create(new IndianaGamingCommissionProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 12, 31)));

        Assert.Equal(DatasetSnapshotKinds.Competitors, dataset.DatasetKey);
        Assert.Equal(13, dataset.Rows.Count);
        Assert.Equal("igc-facilities-units-annual-attributes-employment-v4", dataset.TransformVersion);
        Assert.NotEqual(dataset.Source.ContentHash, dataset.ContentChecksum);
        var ameristar = dataset.Rows.Single(row => row.StableVenueId == "USA-IN-IGC-ameristar-casino");
        Assert.Equal(980, ameristar.SlotOrVltPositions);
        Assert.Equal(30, ameristar.TableGameCount);
        Assert.True(ameristar.HasHotel);
        Assert.Equal(288, ameristar.HotelRoomCount);
        Assert.Equal(59_460, ameristar.GamingFloorSquareFeet);
        Assert.Equal(6, ameristar.FoodBeverageVenueCount);
        Assert.Equal(645, ameristar.ReportedEmployment);
        Assert.Equal(10_112, dataset.Rows.Sum(row => row.ReportedEmployment));
        Assert.Equal(1_651, dataset.Rows.Single(row => row.StableVenueId == "USA-IN-IGC-french-lick-resort").ReportedEmployment);
        Assert.Equal(1_409, dataset.Rows.Single(row => row.StableVenueId == "USA-IN-IGC-hard-rock-casino-northern-indiana").ReportedEmployment);
        Assert.Contains("777 Ameristar Drive", ameristar.Notes);
        var hoosierPark = dataset.Rows.Single(row => row.StableVenueId == "USA-IN-IGC-harrahs-hoosier-park");
        Assert.True(hoosierPark.HasRacetrack);
        Assert.Equal("racino", hoosierPark.VenueType);
        Assert.False(hoosierPark.HasHotel);
        Assert.Equal(0, hoosierPark.HotelRoomCount);
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GamingCommissionFacilityProvider_UsesDecemberInventoryForAnnualCompositeRequest()
    {
        var handler = new IgcResponseHandler(BuildIgcWorkbook(), BuildIgcLocationsHtml(), BuildIgcAnnualReportPdf());
        var provider = new IndianaGamingCommissionFacilityInventoryProvider(
            new HttpClient(handler),
            Options.Create(new IndianaGamingCommissionProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));

        Assert.Equal("2025", dataset.Period);
        Assert.Equal("2025-12", dataset.Source.VintagePeriod);
        Assert.Contains("2025-12 month-end", dataset.Source.Notes, StringComparison.Ordinal);
        Assert.Contains(handler.RequestUris, uri =>
            uri.AbsolutePath.EndsWith("/2025/2025-12-Revenue.xlsx", StringComparison.Ordinal));
        Assert.Contains(handler.RequestUris, uri =>
            uri.AbsolutePath.EndsWith("/FY2025-Annual.pdf", StringComparison.Ordinal));
        Assert.Equal(13, dataset.Rows.Count);
    }

    [Fact]
    public void GamingCommissionAnnualFacilityParser_RejectsIncompleteInventory()
    {
        var partial = new[]
        {
            "Indiana Gaming Commission Annual Report 2025 AMERISTAR CASINO " +
            "Gaming Space: 59,460 Square Feet Restaurants: 6 Hotel: 288 Rooms"
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            IndianaGamingCommissionAnnualFacilityParser.ParsePageTexts(partial, 2025));

        Assert.Contains("did not reconcile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndotProvider_ParsesPublishedAadtAndTransformsUtmZone16Coordinates()
    {
        var archive = BuildIndotArchive();
        var handler = new ByteResponseHandler(archive);
        var provider = new IndianaDepartmentOfTransportationAadtProvider(
            new HttpClient(handler),
            Options.Create(new IndianaDepartmentOfTransportationProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new Dictionary<string, string> { ["site-numbers"] = "970200" }));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal(DatasetSnapshotKinds.Traffic, dataset.DatasetKey);
        Assert.Equal("USA-IN-INDOT-AADT-ff07fe1e-ff59-4856-9948-4801a6287a8d", row.StableObservationId);
        Assert.Equal("10000000640000001", row.RouteDesignation);
        Assert.Equal(57_429, row.AnnualAverageDailyTraffic);
        Assert.InRange(row.Latitude, 39.99, 40.01);
        Assert.InRange(row.Longitude, -87.01, -86.99);
        Assert.Equal(366, row.ObservationDays);
        Assert.Contains("site 970200", row.Notes);
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public void Nad83UtmZone16Converter_MapsCentralMeridianReferencePoint()
    {
        var (latitude, longitude) = Nad83UtmZone16N.ToGeographic(500_000, 4_427_757.218);

        Assert.InRange(latitude, 39.99999, 40.00001);
        Assert.InRange(longitude, -87.00001, -86.99999);
    }

    [Fact]
    public async Task IndotProvider_UsesUniqueGlobalIdsWhenPublishedEventIdsRepeat()
    {
        var archive = BuildIndotArchiveWithRepeatedEventIds();
        var provider = new IndianaDepartmentOfTransportationAadtProvider(
            new HttpClient(new ByteResponseHandler(archive)),
            Options.Create(new IndianaDepartmentOfTransportationProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31)));

        Assert.Equal(2, dataset.Rows.Count);
        Assert.Equal(2, dataset.Rows.Select(row => row.StableObservationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(dataset.Rows, row => row.StableObservationId.EndsWith("11111111-1111-1111-1111-111111111111", StringComparison.Ordinal));
        Assert.Contains(dataset.Rows, row => row.StableObservationId.EndsWith("22222222-2222-2222-2222-222222222222", StringComparison.Ordinal));
    }

    private static byte[] BuildIgcWorkbook()
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbook = new XDocument(
            new XElement(spreadsheet + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", officeRelationships),
                new XElement(spreadsheet + "sheets",
                    new XElement(spreadsheet + "sheet",
                        new XAttribute("name", "1 Tax Summary"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(officeRelationships + "id", "rId1")))));
        var relationships = new XDocument(
            new XElement(packageRelationships + "Relationships",
                new XElement(packageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));
        var worksheet = new XDocument(
            new XElement(spreadsheet + "worksheet",
                new XElement(spreadsheet + "sheetData",
                    Row(spreadsheet, 21,
                        TextCell(spreadsheet, "C21", "Win"),
                        TextCell(spreadsheet, "D21", "Free Play"),
                        TextCell(spreadsheet, "E21", "Other *"),
                        TextCell(spreadsheet, "F21", "Taxable AGR")),
                    Row(spreadsheet, 22,
                        TextCell(spreadsheet, "A22", "Ameristar Casino"),
                        NumberCell(spreadsheet, "C22", "11795140.78"),
                        NumberCell(spreadsheet, "F22", "10248996.79")),
                    Row(spreadsheet, 23,
                        TextCell(spreadsheet, "A23", "Indiana Grand**"),
                        NumberCell(spreadsheet, "C23", "28561954.73"),
                        NumberCell(spreadsheet, "F23", "24497058.82")),
                    Row(spreadsheet, 24, TextCell(spreadsheet, "A24", "TOTAL")),
                    Row(spreadsheet, 38,
                        TextCell(spreadsheet, "A38", "WAGERING TAX"),
                        TextCell(spreadsheet, "B38", "No. of Table Games"),
                        TextCell(spreadsheet, "D38", "No. of EGD/Slots"),
                        TextCell(spreadsheet, "F38", "AGR")),
                    FacilityFixtures.Select((fixture, index) => Row(
                        spreadsheet,
                        39 + index,
                        TextCell(spreadsheet, $"A{39 + index}", fixture.ReportName),
                        NumberCell(spreadsheet, $"B{39 + index}", fixture.TableGames.ToString()),
                        NumberCell(spreadsheet, $"D{39 + index}", fixture.Slots.ToString()),
                        NumberCell(spreadsheet, $"F{39 + index}", "1000000"))),
                    Row(spreadsheet, 39 + FacilityFixtures.Length, TextCell(spreadsheet, $"A{39 + FacilityFixtures.Length}", "TOTAL")))));

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXml(archive, "xl/workbook.xml", workbook);
            WriteXml(archive, "xl/_rels/workbook.xml.rels", relationships);
            WriteXml(archive, "xl/worksheets/sheet1.xml", worksheet);
        }
        return stream.ToArray();
    }

    private static string BuildIgcLocationsHtml() =>
        "<html><body><table><tbody>" +
        string.Concat(FacilityFixtures.Select(fixture =>
            $"<tr><td>{WebUtility.HtmlEncode(fixture.PageName)}</td>" +
            $"<td>{WebUtility.HtmlEncode(fixture.Address)}</td><td>555-0100</td>" +
            $"<td><a href=\"{fixture.Url}\">property</a></td></tr>")) +
        "</tbody></table></body></html>";

    private static byte[] BuildIgcAnnualReportPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document =>
        {
            foreach (var fixture in AnnualFacilityFixtures)
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(24);
                    page.Content().Text(
                        $"{fixture.Title}\nIndiana Gaming Commission Annual Report 2025\n" +
                        $"Gaming Space: {fixture.GamingFloorSquareFeet:N0} Square Feet\n" +
                        $"Restaurants: {fixture.RestaurantCount}\n" +
                        $"Hotel: {(fixture.HotelRoomCount is null ? "N/A" : $"{fixture.HotelRoomCount:N0} rooms")}\n" +
                        $"Total Employment: {fixture.TotalEmployment:N0}");
                });
            }
        }).GeneratePdf();
    }

    private static byte[] BuildIndotArchive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "savenein-indot-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "AADT 2025");
            var factory = new GeometryFactory(new PrecisionModel(), 26916);
            var geometry = factory.CreateLineString(
            [
                new Coordinate(500_000, 4_427_757.218),
                new Coordinate(500_100, 4_427_757.218)
            ]);
            var attributes = new AttributesTable
            {
                { "ROUTE_ID", "10000000640000001" },
                { "TRAFFIC_SE", "122_0640_0000_121.703" },
                { "SITE_NO", "970200" },
                { "AADT", 57_429d },
                { "HPMS_YEAR", "2024" },
                { "FROM_DATE", "20190617" },
                { "EVENT_ID", "{FF07FE1E-FF59-4856-9948-4801A6287A8D}" },
                { "COMMENT_", "official frozen-fixture shape" }
            };
            var feature = new Feature(geometry, attributes);
            var writer = new ShapefileDataWriter(basePath, factory)
            {
                Header = ShapefileDataWriter.GetHeader(feature, 1)
            };
            writer.Write([feature]);

            using var archiveStream = new MemoryStream();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "AADT 2025.*"))
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

    private static byte[] BuildIndotArchiveWithRepeatedEventIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "savenein-indot-globalid-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "AADT 2025");
            var factory = new GeometryFactory(new PrecisionModel(), 26916);
            Feature Feature(string globalId, double northing, string site) => new(
                factory.CreateLineString(
                [
                    new Coordinate(500_000, northing),
                    new Coordinate(500_100, northing)
                ]),
                new AttributesTable
                {
                    { "ROUTE_ID", "10000000640000001" },
                    { "TRAFFIC_SE", $"section-{site}" },
                    { "SITE_NO", site },
                    { "AADT", 57_429d },
                    { "HPMS_YEAR", "2024" },
                    { "FROM_DATE", "20190617" },
                    { "EVENT_ID", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}" },
                    { "GLOBALID_1", $"{{{globalId}}}" },
                    { "COMMENT_", "official frozen-fixture shape" }
                });
            var features = new[]
            {
                Feature("11111111-1111-1111-1111-111111111111", 4_427_757.218, "970200"),
                Feature("22222222-2222-2222-2222-222222222222", 4_428_757.218, "970201")
            };
            var writer = new ShapefileDataWriter(basePath, factory)
            {
                Header = ShapefileDataWriter.GetHeader(features[0], features.Length)
            };
            writer.Write(features);

            using var archiveStream = new MemoryStream();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "AADT 2025.*"))
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

    private static XElement Row(XNamespace ns, int rowNumber, params XElement[] cells) =>
        new(ns + "row", new XAttribute("r", rowNumber), cells);

    private static XElement TextCell(XNamespace ns, string reference, string value) =>
        new(ns + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", new XElement(ns + "t", value)));

    private static XElement NumberCell(XNamespace ns, string reference, string value) =>
        new(ns + "c", new XAttribute("r", reference), new XElement(ns + "v", value));

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private sealed class ByteResponseHandler(byte[] content) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class IgcResponseHandler(byte[] workbook, string locationsHtml, byte[] annualReportPdf) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new ByteArrayContent(workbook)
                : request.RequestUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? new ByteArrayContent(annualReportPdf)
                    : new StringContent(locationsHtml, Encoding.UTF8, "text/html");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static readonly FacilityFixture[] FacilityFixtures =
    [
        new("Ameristar Casino", "Ameristar Casino East Chicago", "777 Ameristar Drive, East Chicago, IN 46312", "https://example.test/ameristar", 30, 980),
        new("Bally's Evansville", "Bally's Evansville", "421 NW Riverside Dr., Evansville, IN 47708", "https://example.test/ballys", 36, 936),
        new("Belterra Casino", "Belterra Casino", "777 Belterra Drive, Florence, IN 47020", "https://example.test/belterra", 25, 821),
        new("Blue Chip Casino", "Blue Chip Casino", "777 Blue Chip Drive, Michigan City, IN 46360", "https://example.test/blue-chip", 22, 1303),
        new("Caesars Southern Indiana", "Caesars Southern", "11999 Casino Center Dr. SE, Elizabeth, IN 47117", "https://example.test/caesars", 89, 968),
        new("French Lick Resort", "French Lick Resort & Casino", "8670 West State Road 56, French Lick, IN 47432", "https://example.test/french-lick", 29, 669),
        new("Hard Rock Casino Northern Indiana", "Hard Rock Casino Northern Indiana", "5400 West 29th Ave., Gary, IN 46406", "https://example.test/hard-rock", 76, 1755),
        new("Harrah's Hoosier Park", "Harrah's Hoosier Park Casino", "4500 Dan Patch Cir, Anderson, IN 46013", "https://example.test/hoosier-park", 40, 1249),
        new("Hollywood Lawrenceburg", "Hollywood Casino", "777 Hollywood Blvd., Lawrenceburg, IN 47025", "https://example.test/hollywood", 38, 969),
        new("Horseshoe Hammond", "Horseshoe Hammond Casino", "777 Casino Center Drive, Hammond, IN 46320", "https://example.test/hammond", 75, 1562),
        new("Horseshoe Indianapolis", "Horseshoe Indianapolis", "4300 N. Michigan Road, Shelbyville, IN 46176", "https://example.test/indianapolis", 84, 1501),
        new("Rising Star Casino", "Rising Star Casino", "777 Rising Star Drive, Rising Sun, IN 47040", "https://example.test/rising-star", 16, 629),
        new("Terre Haute Casino", "Terre Haute Casino Resort", "4500 East Margaret Drive, Terre Haute, IN 47803", "https://example.test/terre-haute", 38, 1039)
    ];

    private static readonly AnnualFacilityFixture[] AnnualFacilityFixtures =
    [
        new("AMERISTAR CASINO", 59_460, 6, 288, 645),
        new("BALLY'S EVANSVILLE", 46_265, 3, 338, 555),
        new("BELTERRA CASINO", 70_232, 5, 608, 568),
        new("BLUE CHIP CASINO", 65_375, 3, 486, 556),
        new("CAESARS SOUTHERN INDIANA", 74_421, 5, 503, 958),
        new("FRENCH LICK RESORT CASINO", 49_719, 11, 756, 1_651),
        new("HARD ROCK NORTHERN INDIANA", 77_118, 5, null, 1_409),
        new("HARRAH'S HOOSIER PARK CASINO", 86_136, 3, null, 641),
        new("HOLLYWOOD CASINO", 167_000, 4, 295, 655),
        new("HORSESHOE CASINO HAMMOND", 108_000, 3, null, 812),
        new("HORSESHOE INDIANAPOLIS", 106_700, 4, null, 838),
        new("RISING STAR CASINO", 40_000, 2, 294, 286),
        new("TERRE HAUTE CASINO RESORT", 76_726, 9, 122, 538)
    ];

    private sealed record FacilityFixture(
        string ReportName,
        string PageName,
        string Address,
        string Url,
        int TableGames,
        int Slots);

    private sealed record AnnualFacilityFixture(
        string Title,
        int GamingFloorSquareFeet,
        int RestaurantCount,
        int? HotelRoomCount,
        int TotalEmployment);
}
