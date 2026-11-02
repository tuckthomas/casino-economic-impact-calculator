using System.Text;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Services.Reports;

namespace SaveNEIN.Server.Tests;

public sealed class LegacyEconomicReportTests
{
    [Fact]
    public void ReportSupportsMissingOptionalMapAndBrandAssets()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var bytes = LegacyEconomicReportDocument.Render(new ReportRequest
        {
            SubjectCountyName = "Steuben", MapImageBase64 = "not a base64 image",
            AnalysisText = "### Model Scope and Limitations\n* **Research basis:** Grinols-based assumptions remain editable."
        }, null, new DateTime(2026, 11, 2));
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.True(bytes.Length > 10_000);
    }

    [Fact]
    public void LargeRegionalStatementsAndCountyMatricesPaginateWithoutLayoutFailure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var rows = new List<List<string>> { new() { "Regional Revenue vs County Costs", "" }, new() { "REVENUE", "" }, new() { "Regional receipt", "$1.0MM" }, new() { "EXPENSES", "" } };
        var costs = new List<List<string>>();
        for (var county = 1; county <= 40; county++)
        {
            rows.Add([$"+ Test County {county:00}", "$0.6MM"]);
            rows.Add(["Public and private costs for this assessed county", "$0.6MM"]);
            costs.Add([$"Test County {county:00}", "$0.1MM", "$0.1MM", "$0.1MM", "$0.1MM", "$0.1MM", "$0.1MM", "$0.6MM"]);
        }
        rows.Add(["SUBTOTAL: SOCIAL COSTS", "$24.0MM"]);
        rows.Add(["ILLUSTRATIVE BALANCE", "-$23.0MM"]);
        var bytes = LegacyEconomicReportDocument.Render(new ReportRequest
        {
            SubjectCountyName = "Test County", MainTable = new TableData { Rows = rows }, BreakdownOtherTable = costs,
            AnalysisText = "### Social Cost Analysis\n* **Source:** [Research](https://example.com/reference).\n### Substitution Effect Analysis\n* " + string.Join(" ", Enumerable.Repeat("A long narrative remains readable across pages.", 140))
        }, null, new DateTime(2026, 11, 2));
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.True(bytes.Length > 20_000);
    }
}
