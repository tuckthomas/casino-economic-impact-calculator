using System.Text;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class PennsylvaniaGamingRevenueProviderTests
{
    private static string Table(string operatorHeader, string yearHeader, int multiplier = 1)
    {
        var html = new StringBuilder($"<table><tr><th>{operatorHeader}</th><th>{yearHeader}</th><th>2024</th><th>% Change</th></tr>");
        foreach (var item in PennsylvaniaGamingFacilityProvider.Catalog)
            html.Append($"<tr><td><strong>{item.Name}</strong></td><td>${100 * multiplier}</td><td>$999</td><td>0%</td></tr>");
        return html.Append($"<tr><td>Statewide Total</td><td>${1700 * multiplier}</td><td>$123</td><td>0%</td></tr></table>").ToString();
    }

    private static string Report => Table("Retail Slot Machine Revenue by Operator", "Slot Machine Revenue 2025") +
        Table("Retail Table Games Revenue by Operator", "2025 Table Games Revenue", 2);

    [Fact]
    public void UsesOnlyTheCorrectYearAndRetailTablesWithStableInventoryIdentities()
    {
        var html = Report + "<table><tr><th>iGaming Revenue</th><th>2025</th></tr><tr><td>Parx Casino</td><td>$999999999</td></tr></table>";
        var capture = PennsylvaniaGamingRevenueProvider.Capture(Encoding.UTF8.GetBytes(html));
        var comparable = capture.Rows.Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue).ToArray();
        Assert.Equal(17, comparable.Length);
        Assert.All(comparable, row => { Assert.Equal(300, row.ReportedAmount); Assert.Equal(new DateOnly(2025, 1, 1), row.PeriodStart); Assert.Equal(new DateOnly(2025, 12, 31), row.PeriodEnd); });
        Assert.Equal(5100, comparable.Sum(row => row.ReportedAmount));
        Assert.Contains(comparable, row => row.StableVenueId == "USA-PA-PGCB-rivers-pittsburgh");
        Assert.NotEqual(capture.ContentChecksum, PennsylvaniaGamingRevenueProvider.Capture(Encoding.UTF8.GetBytes(html + " ")).ContentChecksum);
    }

    [Theory]
    [InlineData("Slot Machine Revenue 2025", "Slot Machine Revenue 2026")]
    [InlineData("2025 Table Games Revenue", "2026 Table Games Revenue")]
    [InlineData("$100</td>", "$100.5</td>")]
    [InlineData("$100</td>", "-$100</td>")]
    [InlineData("$1700</td>", "$1800</td>")]
    [InlineData("RIVERS CASINO PITTSBURGH", "UNKNOWN OPERATOR")]
    public void RejectsWrongYearMalformedAmountsUnknownVenuesAndUnreconciledTotals(string from, string to) =>
        Assert.Throws<InvalidDataException>(() => PennsylvaniaGamingRevenueProvider.ParseAnnualRevenue(Report.Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void RejectsDuplicatedRetailTable() => Assert.Throws<InvalidDataException>(() =>
        PennsylvaniaGamingRevenueProvider.ParseAnnualRevenue(Report + Table("Retail Slot Machine Revenue by Operator", "Slot Machine Revenue 2025")));

    [Fact]
    public void RejectsUnreconciledDuplicateOperatorEvenWhenTotalsMatch() => Assert.Throws<InvalidDataException>(() =>
        PennsylvaniaGamingRevenueProvider.ParseAnnualRevenue(Report.Replace("RIVERS CASINO PITTSBURGH", "PARX CASINO", StringComparison.Ordinal)));

    [Fact]
    public void AllowsOnlyPublishedWholeDollarRoundingDifferences()
    {
        Assert.Equal(68, PennsylvaniaGamingRevenueProvider.ParseAnnualRevenue(Report.Replace("$1700</td>", "$1701</td>", StringComparison.Ordinal)).Count);
        Assert.Throws<InvalidDataException>(() => PennsylvaniaGamingRevenueProvider.ParseAnnualRevenue(Report.Replace("$1700</td>", "$1710</td>", StringComparison.Ordinal)));
    }

    [Fact]
    public void RejectsAnUnsupportedCapturePeriodBeforeFetching() => Assert.Throws<ArgumentException>(() =>
        PennsylvaniaGamingRevenueProvider.ValidateRequest(new("US-PA", new(2025, 7, 1), new(2026, 6, 30))));
}
