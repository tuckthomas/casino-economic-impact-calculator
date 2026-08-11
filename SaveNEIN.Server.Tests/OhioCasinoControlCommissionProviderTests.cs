using System.Net;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class OhioCasinoControlCommissionProviderTests
{
    [Fact]
    public void PdfParser_ReconcilesFacilityMonthlyAnnualAndStatewideValues()
    {
        var pages = CompleteReportPages();

        var rows = OhioCasinoRevenuePdfParser.ParsePageTexts(pages, 2025, 12);

        Assert.Equal(48, rows.Count);
        Assert.Equal(4, rows.Select(row => row.StableVenueId).Distinct().Count());
        Assert.Equal(12, rows.Select(row => row.Month).Distinct().Count());
        Assert.Equal(
            1_872m,
            rows.Sum(row => row.TotalRevenue));
    }

    [Fact]
    public void PdfParser_RejectsStatewideInventoryMismatch()
    {
        var pages = CompleteReportPages();
        pages[0] = pages[0].Replace(
            "January 24 10 20 54 100 10 75% 414 140 14 92%",
            "January 24 10 20 55 100 10 75% 414 140 14 92%",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            OhioCasinoRevenuePdfParser.ParsePageTexts(pages, 2025, 12));

        Assert.Contains("# of Tables", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportCandidates_CoverCurrentAndLegacyOfficialAssetPaths()
    {
        var urls = OhioCasinoControlCommissionRevenueProvider.CandidateReportUris(
            "https://dam.assets.ohio.gov/image/upload/casinocontrol.ohio.gov/revenue-reports",
            2025,
            4);

        Assert.Equal(2, urls.Count);
        Assert.Equal(
            "https://dam.assets.ohio.gov/image/upload/casinocontrol.ohio.gov/revenue-reports/2025/Casino/2025_Ohio_Casino_Monthly_Revenue_Report04.pdf",
            urls[0].AbsoluteUri);
        Assert.Equal(
            "https://dam.assets.ohio.gov/image/upload/casinocontrol.ohio.gov/revenue-reports/2025/2025_Ohio_Casino_Monthly_Revenue_Report04.pdf",
            urls[1].AbsoluteUri);
    }

    [Fact]
    public async Task RevenueProvider_RejectsPartialPeriodBeforeNetworkAccess()
    {
        var handler = new CountingHandler();
        var provider = new OhioCasinoControlCommissionRevenueProvider(
            new HttpClient(handler),
            Options.Create(new OhioCasinoControlCommissionProviderOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-OH",
            new DateOnly(2025, 1, 2),
            new DateOnly(2025, 1, 31))));

        Assert.Equal(0, handler.RequestCount);
    }

    private static List<string> CompleteReportPages()
    {
        var facilityPages = OhioCasinoFacilityCatalog.Entries
            .Select((entry, index) => BuildPage(entry.ReportName, BuildFacilityRows(index + 1)))
            .ToArray();
        var statewideRows = Enumerable.Range(1, 12)
            .Select(month => AggregateMonth(month, Enumerable.Range(1, 4).Select(index => FacilityMonth(index, month))))
            .ToArray();
        return [BuildPage("STATEWIDE", statewideRows), .. facilityPages];
    }

    private static IReadOnlyList<TestRevenueRow> BuildFacilityRows(int facilityIndex) =>
        Enumerable.Range(1, 12).Select(month => FacilityMonth(facilityIndex, month)).ToArray();

    private static TestRevenueRow FacilityMonth(int facilityIndex, int month)
    {
        var tableRevenue = facilityIndex * month;
        var slotRevenue = (facilityIndex + 1) * month;
        return new TestRevenueRow(
            month,
            tableRevenue + slotRevenue,
            facilityIndex,
            facilityIndex * 2,
            11 + facilityIndex,
            tableRevenue * 10,
            tableRevenue,
            101 + facilityIndex,
            slotRevenue * 10,
            slotRevenue);
    }

    private static TestRevenueRow AggregateMonth(int month, IEnumerable<TestRevenueRow> values)
    {
        var rows = values.ToArray();
        return new TestRevenueRow(
            month,
            rows.Sum(row => row.TotalRevenue),
            rows.Sum(row => row.TablePromotional),
            rows.Sum(row => row.SlotPromotional),
            rows.Sum(row => row.TableCount),
            rows.Sum(row => row.TableDrop),
            rows.Sum(row => row.TableRevenue),
            rows.Sum(row => row.SlotCount),
            rows.Sum(row => row.SlotCoinIn),
            rows.Sum(row => row.SlotRevenue));
    }

    private static string BuildPage(string reportName, IReadOnlyList<TestRevenueRow> rows)
    {
        var lines = new List<string>
        {
            $"2025 {reportName} CASINO REVENUE",
            "Month Total Revenue Table Promotional Slot Promotional # of Tables Table Drop Table Revenue Table Payout % # of Slots Slot Coin In Slot Revenue Slot Payout %"
        };
        lines.AddRange(rows.Select(row =>
            $"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(row.Month)} {row.TotalRevenue} {row.TablePromotional} {row.SlotPromotional} {row.TableCount} {row.TableDrop} {row.TableRevenue} 75% {row.SlotCount} {row.SlotCoinIn} {row.SlotRevenue} 92%"));
        lines.Add(
            $"Total {rows.Sum(row => row.TotalRevenue)} {rows.Sum(row => row.TablePromotional)} {rows.Sum(row => row.SlotPromotional)} {rows.Sum(row => row.TableDrop)} {rows.Sum(row => row.TableRevenue)} {rows.Sum(row => row.SlotCoinIn)} {rows.Sum(row => row.SlotRevenue)}");
        return string.Join('\n', lines);
    }

    private sealed record TestRevenueRow(
        int Month,
        decimal TotalRevenue,
        decimal TablePromotional,
        decimal SlotPromotional,
        int TableCount,
        decimal TableDrop,
        decimal TableRevenue,
        int SlotCount,
        decimal SlotCoinIn,
        decimal SlotRevenue);

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
