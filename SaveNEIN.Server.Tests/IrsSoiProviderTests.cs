using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class IrsSoiProviderTests
{
    [Fact]
    public async Task Provider_ReconcilesExactCodesAndExcludesUnmatchedUspsZipRows()
    {
        var handler = new IrsResponseHandler(BuildWorkbook(), BuildGazetteer());
        var provider = new IrsSoiExactCodeZctaIncomeProvider(
            new HttpClient(handler),
            Options.Create(new IrsSoiProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31)));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal(DatasetSnapshotKinds.Income, dataset.DatasetKey);
        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.Equal(100, row.ReturnCount);
        Assert.Equal(5_000_000m, row.AdjustedGrossIncome);
        Assert.Null(row.InflationAdjustedAdjustedGrossIncome);
        Assert.Null(row.MedianHouseholdIncome);
        Assert.Contains("not a claim", row.Notes);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("1 of 2", StringComparison.Ordinal));
        Assert.Contains(dataset.Warnings, warning => warning.Contains("not treated as identical", StringComparison.Ordinal));
        Assert.Contains(dataset.Warnings, warning => warning.Contains("excluded rows represent 20 returns", StringComparison.Ordinal));
        Assert.Equal("https://www.irs.gov/pub/irs-soi/22zp15in.xlsx", dataset.Source.Url);
        Assert.Equal(64, dataset.ContentChecksum.Length);
        Assert.NotEqual(dataset.Source.ContentHash, dataset.ContentChecksum);
        Assert.Contains(dataset.Warnings, warning => warning.Contains("$5,000,000", StringComparison.Ordinal));
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task Provider_RejectsUnsupportedPeriodBeforeNetworkAccess()
    {
        var handler = new IrsResponseHandler(BuildWorkbook(), BuildGazetteer());
        var provider = new IrsSoiExactCodeZctaIncomeProvider(
            new HttpClient(handler),
            Options.Create(new IrsSoiProviderOptions()));

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2021, 1, 1),
            new DateOnly(2021, 12, 31))));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Provider_BuildsOneReconciledSnapshotAcrossExplicitStateWorkbooks()
    {
        var handler = new MultiStateIrsResponseHandler(
            BuildWorkbook("46802", "99999", 100, 5_000),
            BuildWorkbook("43215", "99998", 200, 12_000),
            BuildGazetteer("46802", "43215"));
        var provider = new IrsSoiExactCodeZctaIncomeProvider(
            new HttpClient(handler),
            Options.Create(new IrsSoiProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-STATES",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            new Dictionary<string, string> { ["state-codes"] = "OH,IN" }));

        Assert.Equal(2, dataset.Rows.Count);
        Assert.Contains(dataset.Rows, row => row.StableOriginId == "USA-ZCTA-46802" && row.AdjustedGrossIncome == 5_000_000m);
        Assert.Contains(dataset.Rows, row => row.StableOriginId == "USA-ZCTA-43215" && row.AdjustedGrossIncome == 12_000_000m);
        Assert.Contains(dataset.Warnings, warning => warning.StartsWith("Indiana:", StringComparison.Ordinal));
        Assert.Contains(dataset.Warnings, warning => warning.StartsWith("Ohio:", StringComparison.Ordinal));
        Assert.Equal("https://www.irs.gov/statistics/soi-tax-stats-individual-income-tax-statistics-2022-zip-code-data-soi", dataset.Source.Url);
        Assert.Contains("US-STATES:IN,OH", dataset.Source.GeographicCoverage, StringComparison.Ordinal);
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Fact]
    public async Task Provider_LimitsRowsAndChecksumToExplicitZctaMarketUniverse()
    {
        var handler = new MultiStateIrsResponseHandler(
            BuildWorkbook("46802", "99999", 100, 5_000),
            BuildWorkbook("43215", "99998", 200, 12_000),
            BuildGazetteer("46802", "43215", "43007"));
        var provider = new IrsSoiExactCodeZctaIncomeProvider(
            new HttpClient(handler),
            Options.Create(new IrsSoiProviderOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-STATES",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            new Dictionary<string, string>
            {
                ["state-codes"] = "OH,IN",
                ["zcta-codes"] = "46802,43007"
            }));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal("USA-ZCTA-46802", row.StableOriginId);
        Assert.NotEqual(dataset.Source.ContentHash, dataset.ContentChecksum);
        Assert.Contains(dataset.Warnings, warning =>
            warning.Contains("43007", StringComparison.Ordinal) &&
            warning.Contains("No missing AGI was replaced", StringComparison.Ordinal));
    }

    private static byte[] BuildWorkbook(
        string matchedZip = "46802",
        string unmatchedZip = "99999",
        long matchedReturns = 100,
        decimal matchedAgiThousands = 5_000)
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbook = new XDocument(
            new XElement(spreadsheet + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", officeRelationships),
                new XElement(spreadsheet + "sheets",
                    new XElement(spreadsheet + "sheet",
                        new XAttribute("name", "Sheet1"),
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
                    Row(spreadsheet, 4,
                        TextCell(spreadsheet, "A4", "ZIPCODE"),
                        TextCell(spreadsheet, "B4", "agi_stub")),
                    Row(spreadsheet, 5,
                        NumberCell(spreadsheet, "A5", "0"),
                        NumberCell(spreadsheet, "C5", "120"),
                        NumberCell(spreadsheet, "S5", "6000")),
                    Row(spreadsheet, 6,
                        NumberCell(spreadsheet, "A6", matchedZip),
                        NumberCell(spreadsheet, "C6", matchedReturns.ToString(CultureInfo.InvariantCulture)),
                        NumberCell(spreadsheet, "S6", matchedAgiThousands.ToString(CultureInfo.InvariantCulture))),
                    Row(spreadsheet, 7,
                        NumberCell(spreadsheet, "A7", matchedZip),
                        TextCell(spreadsheet, "B7", "$1 under $25,000"),
                        NumberCell(spreadsheet, "C7", "50"),
                        NumberCell(spreadsheet, "S7", "1000")),
                    Row(spreadsheet, 8,
                        NumberCell(spreadsheet, "A8", unmatchedZip),
                        NumberCell(spreadsheet, "C8", "20"),
                        NumberCell(spreadsheet, "S8", "1000")))));

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXml(archive, "xl/workbook.xml", workbook);
            WriteXml(archive, "xl/_rels/workbook.xml.rels", relationships);
            WriteXml(archive, "xl/worksheets/sheet1.xml", worksheet);
        }
        return stream.ToArray();
    }

    private static byte[] BuildGazetteer(params string[] codes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("2022_Gaz_zcta_national.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.WriteLine("GEOID\tALAND\tAWATER\tALAND_SQMI\tAWATER_SQMI\tINTPTLAT\tINTPTLONG");
            foreach (var code in codes.Length == 0 ? ["46802"] : codes)
            {
                writer.WriteLine($"{code}\t1\t0\t1\t0\t+41.0\t-85.0");
            }
        }
        return stream.ToArray();
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

    private sealed class IrsResponseHandler(byte[] workbook, byte[] gazetteer) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var content = request.RequestUri!.AbsolutePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? workbook
                : gazetteer;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class MultiStateIrsResponseHandler(
        byte[] indianaWorkbook,
        byte[] ohioWorkbook,
        byte[] gazetteer) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            var content = path.EndsWith("22zp15in.xlsx", StringComparison.OrdinalIgnoreCase)
                ? indianaWorkbook
                : path.EndsWith("22zp36oh.xlsx", StringComparison.OrdinalIgnoreCase)
                    ? ohioWorkbook
                    : gazetteer;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
