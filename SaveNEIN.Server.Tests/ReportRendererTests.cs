using System.Text;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Services.Reports;

namespace SaveNEIN.Server.Tests;

public sealed class ReportRendererTests
{
    [Fact]
    public void HtmlRenderer_IsDeterministicAndEncodesPresentationText()
    {
        var model = CreateModel();
        var options = new ReportPresentationOptions("Validation <Report>", "Public & Review", 20, "USD");
        var renderer = new HtmlReportRenderer();

        var first = renderer.Render(model, options);
        var second = renderer.Render(model, options);

        Assert.Equal(first, second);
        Assert.Contains("Validation &lt;Report&gt;", first, StringComparison.Ordinal);
        Assert.Contains("Public &amp; Review", first, StringComparison.Ordinal);
        Assert.Contains(model.Identity.ModelRunId.ToString(), first, StringComparison.Ordinal);
        Assert.Contains("One-at-a-time sensitivity", first, StringComparison.Ordinal);
        Assert.Contains("County/parish composition", first, StringComparison.Ordinal);
        Assert.Contains("Test sensitivity", first, StringComparison.Ordinal);
        Assert.DoesNotContain("html2canvas", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PdfRenderer_ProducesServerSidePdfFromReportModel()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pdf = new PdfReportRenderer().Render(CreateModel(), new ReportPresentationOptions());

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void CsvRenderer_ProducesStableMachineReadableTables()
    {
        var renderer = new CsvReportRenderer();
        var model = CreateModel();

        var first = renderer.Render(model);
        var second = renderer.Render(model);

        Assert.Equal(first, second);
        Assert.Contains("revenue,stabilized,total_ggr,1250000,USD", first, StringComparison.Ordinal);
        Assert.Contains("origin,ZCTA:46802,total_proposed_resident_ggr,900000,USD", first, StringComparison.Ordinal);
        Assert.Contains("sensitivity,gravity.beta,low_model_run_id,10000000-0000-0000-0000-000000000001,uuid", first, StringComparison.Ordinal);
    }

    private static CasinoImpactReportModel CreateModel()
    {
        var runId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        return new CasinoImpactReportModel(
            Identity: new ReportIdentity(
                runId,
                "gravity-v1",
                "professional-v1",
                new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 9, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 9, 11, 1, 0, DateTimeKind.Utc),
                "US-IN",
                "Indiana",
                ["graph-hash"],
                ["auto"]),
            Scenario: new ReportScenario(
                "Stored test scenario",
                41.1,
                -85.1,
                "agi-share",
                "observed-ggr",
                "inverse-power",
                "zcta",
                "host-state",
                "US-IN",
                ["national-calibrated-set:test@1"]),
            DevelopmentProgram: new ReportDevelopmentProgram(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                "test-program",
                "1",
                "Test development",
                1_200,
                40,
                10,
                true,
                200,
                100_000,
                4,
                1_000,
                2,
                300_000_000,
                2026,
                new DateOnly(2028, 1, 1),
                3),
            Revenue: new ReportRevenueSummary(2_000_000, 800_000, 100_000, 900_000, 200_000, 150_000, 1_250_000),
            DataSources:
            [
                new ReportDatasetSource(
                    "origin-geography", "default", "origin-geography", "2025", new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 12, 31), "validated", "snapshot-hash", "transform-v1",
                    "Test source", "https://example.test/source", "source-hash")
            ],
            Parameters:
            [
                new ReportParameter(
                    "gravity.beta", "gravity", "Distance decay beta", "exponent", 1.5, 1.5, null, 1.6,
                    1.6, "user-override", 1.4, 1.6, false, null, "Test provenance")
            ],
            Origins:
            [
                new ReportOrigin(
                    "ZCTA:46802", "zcta", "46802", "IN", "003", "23060", 1_500_000,
                    800_000, 100_000, 900_000, 600_000, 200_000, 50_000, 150_000, 1)
            ],
            OriginStates: [new ReportOriginGroup("state", "IN", 800_000, 100_000, 900_000, 1, 1)],
            OriginCounties: [new ReportOriginGroup("county", "IN-003", 800_000, 100_000, 900_000, 1, 1)],
            Facilities:
            [
                new ReportFacility(
                    "scenario:test", "Test development", "scenario", true, 1.2, 0, 900_000, 900_000,
                    100_000, 900_000, 200_000, 150_000, 1_250_000)
            ],
            DemandComponents: [],
            Capacity: new ReportCapacity("scenario:test", 1_250_000, 900_000, 1_500_000, false, false, "within-range", null),
            Ramp:
            [
                new ReportRampYear(2028, 1, "first-full-year", 1, 0.75, 937_500),
                new ReportRampYear(2030, 3, "stabilized", 1, 1, 1_250_000)
            ],
            GeographicAccounting: null,
            SectorDisplacement:
            [
                new ReportSectorDisplacement("restaurants", 1, 500_000, 0.5, 250_000, 150_000, 50_000, 10_000, 2_000, 3)
            ],
            Employment: new ReportEmployment(100, 400, 20, 3, 5, 112, 6_000_000, 1_000_000, 300_000),
            Fiscal: new ReportFiscal(125_000, 50_000, 125_000, 12_000, 5_000, 2_000, 33_000, 108_000, -2_000, "{}"),
            SocialCosts: [new ReportSocialCost("treatment-health", 100_000, 10, 10_000, 100_000, 75_000, 125_000, true, "Test")],
            NetImpact: new ReportNetImpact(
                1_250_000, 800_000, 200_000, 50_000, 100_000, 350_000, 250_000, 7_000_000,
                33_000, 108_000, 100_000, 650_000, 383_000, 458_000, "explicit-cash-flow-bridge-v1"),
            Sensitivity: new ReportSensitivityAnalysis(
                Guid.Parse("12345678-1234-1234-1234-123456789012"),
                "test-sensitivity", "1", "Test sensitivity", "net-host-local-impact", "USD", runId, 383_000,
                [new ReportSensitivityRow(
                    "gravity.beta", 1.4, 1.6, 1.8,
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    360_000, 383_000, 410_000, -23_000, 27_000, 50_000)]),
            Benchmarks: [],
            Warnings: ["Test warning"],
            Limitations: ["Test limitation"]);
    }
}
