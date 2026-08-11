using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class OhioLotteryVideoLotteryProviderTests
{
    [Fact]
    public void FiscalPdfParser_ReconcilesNetWinAndMapsCalendarMonths()
    {
        var report = OhioLotteryVideoLotteryPdfParser.Parse(
            CompleteFiscalReport(),
            2025,
            OhioLotteryFacilityCatalog.Entries[0]);

        Assert.Equal(12, report.Rows.Count);
        Assert.Equal(new DateOnly(2024, 7, 1), new DateOnly(report.Rows[0].CalendarYear, report.Rows[0].CalendarMonth, 1));
        Assert.Equal(new DateOnly(2025, 6, 1), new DateOnly(report.Rows[11].CalendarYear, report.Rows[11].CalendarMonth, 1));
        Assert.True(report.Rows[7].HasSourceFootnote);
        Assert.Equal(1_800m, report.Rows.Sum(row => row.NetWin));
    }

    [Fact]
    public void FiscalPdfParser_RejectsNetWinMismatch()
    {
        var source = CompleteFiscalReport().Replace(
            "July $1,000 $800 $50 $150",
            "July $1,000 $800 $50 $151",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            OhioLotteryVideoLotteryPdfParser.Parse(source, 2025, OhioLotteryFacilityCatalog.Entries[0]));

        Assert.Contains("does not reconcile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinkResolver_SelectsLatestCoverageAndDerivesMissingStatewideReport()
    {
        const string html = """
            <a href="/reports/VLT-BPC-Monthly-Revenue-Report-FY-2026_DEC.pdf">December</a>
            <a href="/reports/VLT-BPC-Monthly-Revenue-Report-FY-2026_MAY.pdf">May</a>
            """;
        var links = OhioLotteryVideoLotteryLinkResolver.ExtractPdfLinks(
            html,
            "https://lottery.example/vlt-revenue");

        var facility = OhioLotteryVideoLotteryLinkResolver.ResolveFacilityReport(
            links,
            OhioLotteryFacilityCatalog.Entries[0],
            2026);
        var statewide = OhioLotteryVideoLotteryLinkResolver.ResolveStatewideReport(links, 2026, facility);

        Assert.EndsWith("_MAY.pdf", facility.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("/VLT-Statewide-Monthly-Revenue-Report-FY-2026_MAY.pdf", statewide.AbsoluteUri, StringComparison.Ordinal);
    }

    private static string CompleteFiscalReport()
    {
        var lines = new List<string>
        {
            "OHIO LOTTERY",
            "Belterra Park Cincinnati",
            "6301 Kellogg Avenue",
            "Cincinnati, OH 45230",
            "VLT RESULTS FOR FISCAL YEAR 2025"
        };
        var months = new[]
        {
            "July", "August", "September", "October", "November", "December",
            "January", "*February", "March", "April", "May", "June"
        };
        lines.AddRange(months.Select(month =>
            $"{month} $1,000 $800 $50 $150 85.00% 1,000 $5 $100 $40 $5 $5"));
        lines.Add("TOTAL $12,000 $9,600 $600 $1,800 $1,200 $480 $60 $60");
        return string.Join('\n', lines);
    }
}
