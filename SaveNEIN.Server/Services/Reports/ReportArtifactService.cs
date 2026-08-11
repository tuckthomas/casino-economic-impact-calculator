// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Reports;

public interface IHtmlReportRenderer
{
    string Render(CasinoImpactReportModel model, ReportPresentationOptions options);
}

public interface IPdfReportRenderer
{
    byte[] Render(CasinoImpactReportModel model, ReportPresentationOptions options);
}

public interface ICsvReportRenderer
{
    string Render(CasinoImpactReportModel model);
}

public interface IReportArtifactService
{
    Task<ModelRunReportArtifact> GetOrCreateAsync(
        Guid modelRunId,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class ReportArtifactService(
    AppDbContext db,
    ICasinoImpactReportModelFactory reportModelFactory,
    IHtmlReportRenderer htmlRenderer,
    IPdfReportRenderer pdfRenderer,
    ICsvReportRenderer csvRenderer) : IReportArtifactService
{
    public const string TemplateVersion = "professional-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ModelRunReportArtifact> GetOrCreateAsync(
        Guid modelRunId,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var normalizedOptions = options with
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? null : options.Title.Trim(),
            PreparedFor = string.IsNullOrWhiteSpace(options.PreparedFor) ? null : options.PreparedFor.Trim(),
            CurrencyCode = options.CurrencyCode.Trim().ToUpperInvariant()
        };
        var optionsJson = JsonSerializer.Serialize(normalizedOptions, JsonOptions);
        var optionsHash = Hash(Encoding.UTF8.GetBytes(optionsJson));
        var existing = await db.ModelRunReportArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(artifact => artifact.ModelRunId == modelRunId &&
                                              artifact.TemplateVersion == TemplateVersion &&
                                              artifact.PresentationOptionsHash == optionsHash,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var generatedAtUtc = DateTime.UtcNow;
        var model = await reportModelFactory.BuildAsync(
            modelRunId,
            TemplateVersion,
            generatedAtUtc,
            normalizedOptions,
            cancellationToken);
        var modelJson = JsonSerializer.Serialize(model, JsonOptions);
        var html = htmlRenderer.Render(model, normalizedOptions);
        var pdf = pdfRenderer.Render(model, normalizedOptions);
        var csv = csvRenderer.Render(model);
        var artifact = new ModelRunReportArtifact
        {
            ModelRunId = modelRunId,
            TemplateVersion = TemplateVersion,
            PresentationOptionsJson = optionsJson,
            PresentationOptionsHash = optionsHash,
            ReportModelJson = modelJson,
            ReportModelHash = Hash(Encoding.UTF8.GetBytes(modelJson)),
            HtmlContent = html,
            HtmlContentHash = Hash(Encoding.UTF8.GetBytes(html)),
            PdfContent = pdf,
            PdfContentHash = Hash(pdf),
            CsvContent = csv,
            CsvContentHash = Hash(Encoding.UTF8.GetBytes(csv)),
            GeneratedAtUtc = generatedAtUtc,
            IsImmutable = true
        };
        db.ModelRunReportArtifacts.Add(artifact);
        await db.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateOptions(ReportPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TopOriginCount is < 5 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Top origin count must be between 5 and 100.");
        }
        if (options.Title?.Length > 200 || options.PreparedFor?.Length > 200)
        {
            throw new ArgumentException("Report title and prepared-for label are limited to 200 characters.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.CurrencyCode) || options.CurrencyCode.Trim().Length != 3)
        {
            throw new ArgumentException("A three-letter currency code is required.", nameof(options));
        }
    }
}

public sealed class HtmlReportRenderer : IHtmlReportRenderer
{
    public string Render(CasinoImpactReportModel model, ReportPresentationOptions options)
    {
        var html = new StringBuilder(64_000);
        var title = options.Title ?? $"Casino Revenue and Economic-Impact Analysis — {model.Scenario.Name}";
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(E(title)).Append("</title>")
            .Append("<style>")
            .Append("@page{size:letter;margin:.65in}body{font:15px/1.5 'Public Sans',Arial,sans-serif;color:#172033;margin:0;background:#fff}")
            .Append("main{max-width:1050px;margin:auto;padding:36px}h1{font-size:34px;line-height:1.15;color:#0f2948;margin:0 0 12px}h2{font-size:23px;color:#0f2948;border-bottom:2px solid #cbd5e1;padding-bottom:7px;margin-top:36px}h3{font-size:18px;color:#244d74;margin-top:25px}")
            .Append(".eyebrow{font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:#2c6b8f}.subtitle{font-size:18px;color:#526174}.meta,.note{color:#526174}.metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.metric{border:1px solid #cbd5e1;border-radius:8px;padding:14px}.metric strong{display:block;font-size:20px;color:#0f2948}")
            .Append("table{width:100%;border-collapse:collapse;margin:14px 0 24px;font-size:13px}th{text-align:left;background:#e8eef4;color:#0f2948}th,td{padding:8px;border-bottom:1px solid #dbe2ea;vertical-align:top}.number{text-align:right;font-variant-numeric:tabular-nums}.warning{border-left:4px solid #b45309;background:#fff7ed;padding:9px 12px;margin:8px 0}.repro{background:#eef3f7;padding:14px;border-radius:8px;font-family:ui-monospace,monospace;font-size:12px;overflow-wrap:anywhere}")
            .Append("@media print{main{padding:0}.page-break{break-before:page}a{color:inherit;text-decoration:none}}@media(max-width:700px){main{padding:20px}.metrics{grid-template-columns:1fr}table{display:block;overflow:auto}}")
            .Append("</style></head><body><main>");
        html.Append("<header><div class=\"eyebrow\">Stored model-run report</div><h1>").Append(E(title)).Append("</h1>")
            .Append("<p class=\"subtitle\">Dynamic gravity, revenue, fiscal, employment, displacement, and social-cost analysis</p>");
        if (!string.IsNullOrWhiteSpace(options.PreparedFor))
        {
            html.Append("<p><strong>Prepared for:</strong> ").Append(E(options.PreparedFor)).Append("</p>");
        }
        html.Append("<p class=\"meta\">Run ").Append(model.Identity.ModelRunId).Append(" · ")
            .Append(E(model.Identity.JurisdictionName)).Append(" · generated ")
            .Append(model.Identity.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append("</p></header>");

        Section(html, "Executive summary");
        html.Append("<div class=\"metrics\">");
        Metric(html, "Stabilized GGR", Money(model.Revenue.StabilizedTotalGgr));
        Metric(html, "Resident GGR", Money(model.Revenue.TotalResidentGgr));
        Metric(html, "Tourism + traffic GGR", Money(model.Revenue.TourismGgr + model.Revenue.TrafficGgr));
        Metric(html, "Net permanent jobs", model.Employment?.NetPermanentJobs.ToString("N1", CultureInfo.InvariantCulture) ?? "Not available");
        Metric(html, "Net host-local impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostLocalImpact));
        Metric(html, "Net host-state impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostStateImpact));
        html.Append("</div>");
        if (model.Warnings.Count > 0)
        {
            Subsection(html, "Run warnings");
            foreach (var warning in model.Warnings)
            {
                html.Append("<div class=\"warning\">").Append(E(warning)).Append("</div>");
            }
        }

        Section(html, "Proposed development and site");
        TwoColumnTable(html,
        [
            ("Scenario", model.Scenario.Name),
            ("Site coordinates", $"{model.Scenario.CandidateLatitude:F6}, {model.Scenario.CandidateLongitude:F6}"),
            ("Development program", $"{model.DevelopmentProgram.Name} v{model.DevelopmentProgram.Version}"),
            ("Slots / VLT positions", model.DevelopmentProgram.SlotOrVltPositions.ToString("N0")),
            ("Table games", model.DevelopmentProgram.TableGameCount.ToString("N0")),
            ("Hotel rooms", model.DevelopmentProgram.HotelRoomCount.ToString("N0")),
            ("Demand specification", model.Scenario.DemandSpecification),
            ("Attraction specification", model.Scenario.AttractionSpecification),
            ("Travel friction", model.Scenario.FrictionForm),
            ("Computational origin", model.Scenario.ComputationalOriginType),
            ("Impact geography", $"{model.Scenario.ImpactScopeKind}: {model.Scenario.ImpactScopeCode}")
        ]);

        Section(html, "Gaming revenue projection");
        TwoColumnTable(html,
        [
            ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
            ("Redistributed resident GGR", Money(model.Revenue.RedistributedResidentGgr)),
            ("Accessibility-induced resident GGR", Money(model.Revenue.InducedResidentGgr)),
            ("Tourism GGR", Money(model.Revenue.TourismGgr)),
            ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
            ("Stabilized total GGR", Money(model.Revenue.StabilizedTotalGgr))
        ]);
        if (model.Capacity is not null)
        {
            html.Append("<p class=\"note\">Capacity diagnostic range: ").Append(Money(model.Capacity.PlausibleCapacityMinimum))
                .Append(" to ").Append(Money(model.Capacity.PlausibleCapacityMaximum))
                .Append(". Capacity status: ").Append(E(model.Capacity.Status))
                .Append(". Capacity is diagnostic and did not cap modeled GGR.</p>");
        }
        Table(html, ["Calendar year", "Operating year", "Ramp factor", "Projected GGR", "Stabilized"],
            model.Ramp.Select(row => new[]
            {
                row.CalendarYear.ToString(), row.OperatingYearNumber.ToString(), Percent(row.StabilizationShare),
                Money(row.ProjectedGgr), row.PeriodKind
            }));

        Section(html, "Patron-origin analysis");
        Subsection(html, "State composition");
        Table(html, ["State", "Origins", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.OriginStates.Select(row => new[]
            {
                row.GeographyCode, row.OriginCount.ToString(), Money(row.RedistributedResidentGgr),
                Money(row.InducedResidentGgr), Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));
        Subsection(html, "County/parish composition");
        Table(html, ["County/parish", "Origins", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.OriginCounties.Select(row => new[]
            {
                row.GeographyCode, row.OriginCount.ToString(), Money(row.RedistributedResidentGgr),
                Money(row.InducedResidentGgr), Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));
        Subsection(html, $"Top {Math.Min(options.TopOriginCount, model.Origins.Count)} contributing origins");
        Table(html, ["Origin", "Type", "State", "County", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.Origins.Take(options.TopOriginCount).Select(row => new[]
            {
                row.StableOriginId, row.OriginType, row.StateCode, row.CountyCode ?? "—",
                Money(row.RedistributedResidentGgr), Money(row.InducedResidentGgr),
                Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));

        Section(html, "Competitive impact and cannibalization");
        Table(html, ["Facility", "Kind", "Baseline resident GGR", "With-project resident GGR", "Change", "Stabilized total"],
            model.Facilities.Select(row => new[]
            {
                row.FacilityName, row.IsProposedFacility ? "Proposed" : "Incumbent", Money(row.BaselineResidentGgr),
                Money(row.WithProjectResidentGgr), Money(row.ChangeInResidentGgr), Money(row.StabilizedTotalGgr)
            }));
        if (model.GeographicAccounting is not null)
        {
            Subsection(html, "Geographic accounting bridge");
            TwoColumnTable(html,
            [
                ("Host-jurisdiction cannibalization", Money(model.GeographicAccounting.HostJurisdictionCannibalization)),
                ("Cross-jurisdiction capture", Money(model.GeographicAccounting.CrossJurisdictionCapture)),
                ("Outside / unmodeled leakage capture", Money(model.GeographicAccounting.OutsideOrUnmodeledLeakageCapture)),
                ("Market expansion and imported GGR", Money(model.GeographicAccounting.MarketExpansionAndImportGgr)),
                ("Transfer-effect GGR", Money(model.GeographicAccounting.TransferEffectGgr))
            ]);
        }

        Section(html, "Local spending displacement");
        Table(html, ["Sector", "Weight", "Eligible base", "Coefficient", "Displaced sales", "Fiscal loss", "Jobs displaced"],
            model.SectorDisplacement.Select(row => new[]
            {
                row.SectorKey, Percent(row.NormalizedWeight), Money(row.DisplacementEligibleBase),
                Percent(row.DisplacementCoefficient), Money(row.DisplacedSales),
                Money(row.SalesTaxLoss + row.BusinessIncomeTaxLoss), row.DisplacedJobs.ToString("N2")
            }));

        Section(html, "Employment and fiscal impact");
        if (model.Employment is not null)
        {
            Subsection(html, "Employment");
            TwoColumnTable(html,
            [
                ("Direct casino jobs", model.Employment.DirectCasinoJobs.ToString("N1")),
                ("Indirect and induced jobs", model.Employment.IndirectAndInducedJobs.ToString("N1")),
                ("Displaced-sector jobs", model.Employment.DisplacedSectorJobs.ToString("N1")),
                ("Incumbent casino jobs lost", model.Employment.IncumbentCasinoJobsLost.ToString("N1")),
                ("Net permanent jobs", model.Employment.NetPermanentJobs.ToString("N1")),
                ("Construction job-years", model.Employment.ConstructionJobYears.ToString("N1"))
            ]);
        }
        if (model.Fiscal is not null)
        {
            Subsection(html, "Fiscal bridge");
            TwoColumnTable(html,
            [
                ("Gross gaming tax", Money(model.Fiscal.GrossGamingTax)),
                ("Host-local gross public revenue", Money(model.Fiscal.HostLocalGrossPublicRevenue)),
                ("Host-state gross public revenue", Money(model.Fiscal.HostStateGrossPublicRevenue)),
                ("Displaced local fiscal loss", Money(model.Fiscal.DisplacedLocalFiscalLoss)),
                ("Host incumbent gaming-tax loss", Money(model.Fiscal.HostIncumbentGamingTaxLoss)),
                ("Net host-local fiscal impact", Money(model.Fiscal.NetHostLocalFiscalImpact)),
                ("Net host-state fiscal impact", Money(model.Fiscal.NetHostStateFiscalImpact))
            ]);
        }

        Section(html, "Social and downstream costs");
        Table(html, ["Domain", "Incremental cases", "Per-case cost", "Annual cost", "Low", "High", "Included"],
            model.SocialCosts.Select(row => new[]
            {
                row.DomainKey, row.IncrementalCases.ToString("N2"), Money(row.PerCaseCost), Money(row.AnnualCost),
                Money(row.LowAnnualCost), Money(row.HighAnnualCost), row.Included ? "Yes" : "No"
            }));

        Section(html, "Net economic impact");
        if (model.NetImpact is not null)
        {
            TwoColumnTable(html,
            [
                ("Gross property GGR", Money(model.NetImpact.GrossPropertyGgr)),
                ("Transfer-effect GGR", Money(model.NetImpact.TransferEffectGgr)),
                ("Cross-jurisdiction imported GGR", Money(model.NetImpact.CrossJurisdictionImportedGgr)),
                ("Induced resident GGR", Money(model.NetImpact.InducedResidentGgr)),
                ("Tourism and traffic imported GGR", Money(model.NetImpact.TourismAndTrafficImportGgr)),
                ("Local discretionary displacement", Money(model.NetImpact.LocalDiscretionaryDisplacement)),
                ("Gross social cost", Money(model.NetImpact.GrossSocialCost)),
                ("Net host-local impact", Money(model.NetImpact.NetHostLocalImpact)),
                ("Net host-state impact", Money(model.NetImpact.NetHostStateImpact))
            ]);
        }

        if (model.Sensitivity is not null)
        {
            Section(html, "One-at-a-time sensitivity");
            html.Append("<p class=\"note\">Each point is a complete stored model run. Analysis ")
                .Append(E(model.Sensitivity.Name)).Append(" · ").Append(model.Sensitivity.SensitivityAnalysisId)
                .Append(" · output metric ").Append(E(model.Sensitivity.OutputMetric)).Append(".</p>");
            Table(html,
                ["Parameter", "Low input", "Low result", "Baseline input", "Baseline result", "High input", "High result", "Full range", "Point run IDs"],
                model.Sensitivity.Rows.Select(row => new[]
                {
                    row.ParameterKey,
                    Number(row.LowParameterValue), SensitivityValue(row.LowMetricValue, model.Sensitivity.OutputUnits),
                    Number(row.BaseParameterValue), SensitivityValue(row.BaseMetricValue, model.Sensitivity.OutputUnits),
                    Number(row.HighParameterValue), SensitivityValue(row.HighMetricValue, model.Sensitivity.OutputUnits),
                    SensitivityValue(row.TotalRange, model.Sensitivity.OutputUnits),
                    $"low={row.LowModelRunId:D}; high={row.HighModelRunId:D}"
                }));
        }

        if (model.Benchmarks.Count > 0)
        {
            Section(html, "Benchmark and validation reconciliation");
            Table(html, ["Case", "Market", "Partition", "Observed", "Predicted", "Residual", "Source"],
                model.Benchmarks.Select(row => new[]
                {
                    row.CaseName, row.MarketCode, row.DatasetPartition, Money(row.ObservedRevenue),
                    Money(row.PredictedRevenue), Money(row.Residual), row.ConsultantOrSource ?? "Stored validation case"
                }));
        }

        Section(html, "Methodology, limitations, and disclosure");
        html.Append("<p>The report presents persisted outputs from the finalized gravity run. It does not recompute gravity allocation, revenue, fiscal effects, employment, displacement, or social costs.</p><ul>");
        foreach (var limitation in model.Limitations)
        {
            html.Append("<li>").Append(E(limitation)).Append("</li>");
        }
        html.Append("</ul>");
        Subsection(html, "Model parameters and overrides");
        Table(html, ["Parameter", "Category", "Units", "Default", "Scenario", "User override", "Final", "Source", "Recommended range", "Warning"],
            model.Parameters.Select(row => new[]
            {
                row.DisplayName + " (" + row.Key + ")", row.Category, row.Units,
                Number(row.DefaultValue), NullableNumber(row.ScenarioValue), NullableNumber(row.UserOverrideValue),
                Number(row.FinalValue), row.SourceLayer,
                $"{NullableNumber(row.RecommendedMinimum)} – {NullableNumber(row.RecommendedMaximum)}",
                row.WarningText ?? (row.IsOutsideRecommendedRange ? "Outside recommended range" : "")
            }));
        Subsection(html, "Data sources and vintages");
        Table(html, ["Role", "Dataset", "Period", "Validation", "Transform", "Source", "Checksum"],
            model.DataSources.Select(row => new[]
            {
                row.Role + ":" + row.ReferenceKey, row.DatasetKey, row.Period, row.ValidationState,
                row.TransformVersion, row.SourceName, row.Checksum
            }));

        Section(html, "Reproducibility statement");
        html.Append("<div class=\"repro\">")
            .Append("Model version: ").Append(E(model.Identity.ModelVersion)).Append("<br>")
            .Append("Report template: ").Append(E(model.Identity.TemplateVersion)).Append("<br>")
            .Append("Run UUID: ").Append(model.Identity.ModelRunId).Append("<br>")
            .Append("Jurisdiction: ").Append(E(model.Identity.JurisdictionCode)).Append("<br>")
            .Append("Candidate: ").Append(model.Scenario.CandidateLatitude.ToString("F6", CultureInfo.InvariantCulture)).Append(", ")
            .Append(model.Scenario.CandidateLongitude.ToString("F6", CultureInfo.InvariantCulture)).Append("<br>")
            .Append("Routing graph hash(es): ").Append(E(string.Join(", ", model.Identity.RoutingGraphHashes))).Append("<br>")
            .Append("Costing profile(s): ").Append(E(string.Join(", ", model.Identity.CostingProfiles))).Append("<br>")
            .Append("Development program: ").Append(E($"{model.DevelopmentProgram.StableProgramId}@{model.DevelopmentProgram.Version}"))
            .Append(model.Sensitivity is null
                ? ""
                : $"<br>Sensitivity analysis: {model.Sensitivity.SensitivityAnalysisId:D}")
            .Append("</div></main></body></html>");
        return html.ToString();
    }

    private static void Section(StringBuilder html, string title) => html.Append("<h2>").Append(E(title)).Append("</h2>");
    private static void Subsection(StringBuilder html, string title) => html.Append("<h3>").Append(E(title)).Append("</h3>");
    private static void Metric(StringBuilder html, string label, string value) => html.Append("<div class=\"metric\"><span>").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong></div>");
    private static void TwoColumnTable(StringBuilder html, IEnumerable<(string Label, string Value)> rows) =>
        Table(html, ["Measure", "Value"], rows.Select(row => new[] { row.Label, row.Value }));

    private static void Table(StringBuilder html, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        html.Append("<table><thead><tr>");
        foreach (var header in headers)
        {
            html.Append("<th>").Append(E(header)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            for (var index = 0; index < row.Count; index++)
            {
                html.Append(index > 0 ? "<td class=\"number\">" : "<td>").Append(E(row[index])).Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table>");
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Money(decimal value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    private static string Money(decimal? value) => value?.ToString("C0", CultureInfo.GetCultureInfo("en-US")) ?? "not evaluated";
    private static string Percent(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    private static string NullableNumber(double? value) => value?.ToString("G8", CultureInfo.InvariantCulture) ?? "—";
    private static string SensitivityValue(decimal value, string units) => units == "jobs"
        ? value.ToString("N1", CultureInfo.InvariantCulture)
        : Money(value);
}

public sealed class PdfReportRenderer : IPdfReportRenderer
{
    public byte[] Render(CasinoImpactReportModel model, ReportPresentationOptions options)
    {
        var title = options.Title ?? $"Casino Revenue and Economic-Impact Analysis — {model.Scenario.Name}";
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.6f, Unit.Inch);
                page.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.BlueGrey.Darken4));
                page.Header().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Text(title).SemiBold().FontColor(Colors.Blue.Darken3);
                    row.ConstantItem(150).AlignRight().Text(model.Identity.ModelRunId.ToString()).FontSize(7).FontColor(Colors.Grey.Darken1);
                });
                page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text($"{model.Identity.ModelVersion} · {model.Identity.TemplateVersion}").FontSize(7);
                    row.AutoItem().DefaultTextStyle(style => style.FontSize(7)).Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text(title).FontSize(23).Bold().FontColor(Colors.Blue.Darken3);
                    column.Item().Text($"Stored finalized run {model.Identity.ModelRunId:D} · {model.Identity.JurisdictionName}")
                        .FontSize(11).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(options.PreparedFor))
                    {
                        column.Item().Text($"Prepared for: {options.PreparedFor}").SemiBold();
                    }
                    column.Item().Text($"Generated {model.Identity.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC").FontSize(8);

                    Heading(column, "Executive summary");
                    KeyValues(column,
                    [
                        ("Stabilized GGR", Money(model.Revenue.StabilizedTotalGgr)),
                        ("Resident GGR", Money(model.Revenue.TotalResidentGgr)),
                        ("Tourism + traffic GGR", Money(model.Revenue.TourismGgr + model.Revenue.TrafficGgr)),
                        ("Net permanent jobs", model.Employment?.NetPermanentJobs.ToString("N1") ?? "Not available"),
                        ("Net host-local impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostLocalImpact)),
                        ("Net host-state impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostStateImpact))
                    ]);
                    foreach (var warning in model.Warnings)
                    {
                        column.Item().BorderLeft(3).BorderColor(Colors.Orange.Darken2).Background(Colors.Orange.Lighten5)
                            .Padding(6).Text(warning).FontSize(8);
                    }

                    Heading(column, "Proposed development and site");
                    KeyValues(column,
                    [
                        ("Scenario", model.Scenario.Name),
                        ("Candidate coordinates", $"{model.Scenario.CandidateLatitude:F6}, {model.Scenario.CandidateLongitude:F6}"),
                        ("Development program", $"{model.DevelopmentProgram.Name} v{model.DevelopmentProgram.Version}"),
                        ("Slots / VLT positions", model.DevelopmentProgram.SlotOrVltPositions.ToString("N0")),
                        ("Table games / hotel rooms", $"{model.DevelopmentProgram.TableGameCount:N0} / {model.DevelopmentProgram.HotelRoomCount:N0}"),
                        ("Demand / attraction", $"{model.Scenario.DemandSpecification} / {model.Scenario.AttractionSpecification}"),
                        ("Travel friction", model.Scenario.FrictionForm),
                        ("Origin / report geography", $"{model.Scenario.ComputationalOriginType} / {model.Scenario.ImpactScopeKind}:{model.Scenario.ImpactScopeCode}")
                    ]);

                    Heading(column, "Gaming revenue projection");
                    KeyValues(column,
                    [
                        ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
                        ("Redistributed resident GGR", Money(model.Revenue.RedistributedResidentGgr)),
                        ("Accessibility-induced resident GGR", Money(model.Revenue.InducedResidentGgr)),
                        ("Tourism GGR", Money(model.Revenue.TourismGgr)),
                        ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
                        ("Stabilized total GGR", Money(model.Revenue.StabilizedTotalGgr))
                    ]);
                    SimpleTable(column,
                        ["Year", "Operating year", "Ramp", "Projected GGR"],
                        model.Ramp.Select(row => new[]
                        {
                            row.CalendarYear.ToString(), row.OperatingYearNumber.ToString(),
                            row.StabilizationShare.ToString("P1"), Money(row.ProjectedGgr)
                        }));

                    Heading(column, "Patron-origin analysis");
                    SimpleTable(column,
                        ["State", "Origins", "Resident GGR", "Share"],
                        model.OriginStates.Select(row => new[]
                        {
                            row.GeographyCode, row.OriginCount.ToString(), Money(row.TotalProposedResidentGgr),
                            row.ShareOfProposedResidentGgr.ToString("P1")
                        }));
                    column.Item().Text("County/parish composition").SemiBold();
                    SimpleTable(column,
                        ["County/parish", "Origins", "Resident GGR", "Share"],
                        model.OriginCounties.Select(row => new[]
                        {
                            row.GeographyCode, row.OriginCount.ToString(), Money(row.TotalProposedResidentGgr),
                            row.ShareOfProposedResidentGgr.ToString("P1")
                        }));
                    column.Item().Text($"Top {Math.Min(options.TopOriginCount, model.Origins.Count)} contributing origins").SemiBold();
                    SimpleTable(column,
                        ["Origin", "Type", "State / county", "Resident GGR", "Share"],
                        model.Origins.Take(options.TopOriginCount).Select(row => new[]
                        {
                            row.StableOriginId, row.OriginType, $"{row.StateCode} / {row.CountyCode ?? "—"}",
                            Money(row.TotalProposedResidentGgr), row.ShareOfProposedResidentGgr.ToString("P1")
                        }));

                    Heading(column, "Competitive impact and cannibalization");
                    SimpleTable(column,
                        ["Facility", "Kind", "Baseline", "With project", "Change"],
                        model.Facilities.Select(row => new[]
                        {
                            row.FacilityName, row.IsProposedFacility ? "Proposed" : "Incumbent",
                            Money(row.BaselineResidentGgr), Money(row.WithProjectResidentGgr), Money(row.ChangeInResidentGgr)
                        }));

                    Heading(column, "Local spending displacement");
                    SimpleTable(column,
                        ["Sector", "Weight", "Displaced sales", "Fiscal loss", "Jobs"],
                        model.SectorDisplacement.Select(row => new[]
                        {
                            row.SectorKey, row.NormalizedWeight.ToString("P1"), Money(row.DisplacedSales),
                            Money(row.SalesTaxLoss + row.BusinessIncomeTaxLoss), row.DisplacedJobs.ToString("N2")
                        }));

                    Heading(column, "Employment and fiscal impact");
                    if (model.Employment is not null)
                    {
                        KeyValues(column,
                        [
                            ("Direct casino jobs", model.Employment.DirectCasinoJobs.ToString("N1")),
                            ("Indirect and induced jobs", model.Employment.IndirectAndInducedJobs.ToString("N1")),
                            ("Displaced + incumbent jobs lost", (model.Employment.DisplacedSectorJobs + model.Employment.IncumbentCasinoJobsLost).ToString("N1")),
                            ("Net permanent jobs", model.Employment.NetPermanentJobs.ToString("N1"))
                        ]);
                    }
                    if (model.Fiscal is not null)
                    {
                        KeyValues(column,
                        [
                            ("Gross gaming tax", Money(model.Fiscal.GrossGamingTax)),
                            ("Host-local gross public revenue", Money(model.Fiscal.HostLocalGrossPublicRevenue)),
                            ("Displaced local fiscal loss", Money(model.Fiscal.DisplacedLocalFiscalLoss)),
                            ("Net host-local fiscal impact", Money(model.Fiscal.NetHostLocalFiscalImpact)),
                            ("Net host-state fiscal impact", Money(model.Fiscal.NetHostStateFiscalImpact))
                        ]);
                    }

                    Heading(column, "Social and downstream costs");
                    SimpleTable(column,
                        ["Domain", "Cases", "Annual", "Low", "High"],
                        model.SocialCosts.Select(row => new[]
                        {
                            row.DomainKey, row.IncrementalCases.ToString("N2"), Money(row.AnnualCost),
                            Money(row.LowAnnualCost), Money(row.HighAnnualCost)
                        }));

                    Heading(column, "Net economic impact");
                    if (model.NetImpact is not null)
                    {
                        KeyValues(column,
                        [
                            ("Gross property GGR", Money(model.NetImpact.GrossPropertyGgr)),
                            ("Transfer-effect GGR", Money(model.NetImpact.TransferEffectGgr)),
                            ("Imported + induced + tourism/traffic GGR", Money(model.NetImpact.CrossJurisdictionImportedGgr + model.NetImpact.InducedResidentGgr + model.NetImpact.TourismAndTrafficImportGgr)),
                            ("Local discretionary displacement", Money(model.NetImpact.LocalDiscretionaryDisplacement)),
                            ("Gross social cost", Money(model.NetImpact.GrossSocialCost)),
                            ("Net host-local impact", Money(model.NetImpact.NetHostLocalImpact)),
                            ("Net host-state impact", Money(model.NetImpact.NetHostStateImpact))
                        ]);
                    }

                    if (model.Sensitivity is not null)
                    {
                        Heading(column, "One-at-a-time sensitivity");
                        column.Item().Text($"{model.Sensitivity.Name} · analysis {model.Sensitivity.SensitivityAnalysisId:D} · {model.Sensitivity.OutputMetric}. Every point is a complete stored model run.")
                            .FontSize(8);
                        SimpleTable(column,
                            ["Parameter", "Low input / result", "Base input / result", "High input / result", "Range"],
                            model.Sensitivity.Rows.Select(row => new[]
                            {
                                row.ParameterKey,
                                $"{row.LowParameterValue:G7} / {SensitivityValue(row.LowMetricValue, model.Sensitivity.OutputUnits)}",
                                $"{row.BaseParameterValue:G7} / {SensitivityValue(row.BaseMetricValue, model.Sensitivity.OutputUnits)}",
                                $"{row.HighParameterValue:G7} / {SensitivityValue(row.HighMetricValue, model.Sensitivity.OutputUnits)}",
                                SensitivityValue(row.TotalRange, model.Sensitivity.OutputUnits)
                            }));
                    }

                    if (model.Benchmarks.Count > 0)
                    {
                        Heading(column, "Benchmark reconciliation");
                        SimpleTable(column,
                            ["Case", "Partition", "Observed", "Predicted", "Residual"],
                            model.Benchmarks.Select(row => new[]
                            {
                                row.CaseName, row.DatasetPartition, Money(row.ObservedRevenue),
                                Money(row.PredictedRevenue), Money(row.Residual)
                            }));
                    }

                    Heading(column, "Methodology and limitations");
                    column.Item().Text("All economic values in this report are read from the finalized stored model run; the renderer performs presentation aggregation only and does not execute a second model.");
                    foreach (var limitation in model.Limitations)
                    {
                        column.Item().PaddingLeft(8).Text("• " + limitation).FontSize(8);
                    }

                    Heading(column, "Parameter and override appendix");
                    SimpleTable(column,
                        ["Parameter", "Default", "Override", "Final", "Source / warning"],
                        model.Parameters.Select(row => new[]
                        {
                            row.DisplayName + " (" + row.Key + ")", row.DefaultValue.ToString("G7"),
                            row.UserOverrideValue?.ToString("G7") ?? "—", row.FinalValue.ToString("G7"),
                            row.SourceLayer + (string.IsNullOrWhiteSpace(row.WarningText) ? "" : " · " + row.WarningText)
                        }));

                    Heading(column, "Data and reproducibility appendix");
                    SimpleTable(column,
                        ["Role", "Dataset / period", "Source", "Transform", "Checksum"],
                        model.DataSources.Select(row => new[]
                        {
                            row.Role + ":" + row.ReferenceKey, row.DatasetKey + " / " + row.Period,
                            row.SourceName, row.TransformVersion, row.Checksum
                        }));
                    KeyValues(column,
                    [
                        ("Model version", model.Identity.ModelVersion),
                        ("Report template", model.Identity.TemplateVersion),
                        ("Run UUID", model.Identity.ModelRunId.ToString()),
                        ("Jurisdiction", model.Identity.JurisdictionCode),
                        ("Route graph hash(es)", string.Join(", ", model.Identity.RoutingGraphHashes)),
                        ("Costing profile(s)", string.Join(", ", model.Identity.CostingProfiles)),
                        ("Development program", $"{model.DevelopmentProgram.StableProgramId}@{model.DevelopmentProgram.Version}"),
                        ("Sensitivity analysis", model.Sensitivity?.SensitivityAnalysisId.ToString() ?? "Not included")
                    ]);
                });
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    private static void Heading(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(8).BorderBottom(1.5f).BorderColor(Colors.Blue.Darken2)
            .PaddingBottom(3).Text(title).FontSize(15).Bold().FontColor(Colors.Blue.Darken3);

    private static void KeyValues(ColumnDescriptor column, IReadOnlyCollection<(string Label, string Value)> values) =>
        SimpleTable(column, ["Measure", "Value"], values.Select(value => new[] { value.Label, value.Value }));

    private static void SimpleTable(
        ColumnDescriptor column,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in headers)
                {
                    columns.RelativeColumn();
                }
            });
            table.Header(header =>
            {
                foreach (var value in headers)
                {
                    header.Cell().Element(HeaderCell).Text(value).SemiBold().FontSize(7);
                }
            });
            foreach (var row in rows)
            {
                foreach (var value in row)
                {
                    table.Cell().Element(BodyCell).Text(value).FontSize(7);
                }
            }
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.BlueGrey.Lighten4).BorderBottom(1).BorderColor(Colors.BlueGrey.Lighten2).Padding(4);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);

    private static string Money(decimal value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    private static string SensitivityValue(decimal value, string units) => units == "jobs"
        ? value.ToString("N1", CultureInfo.InvariantCulture)
        : Money(value);
}

public sealed class CsvReportRenderer : ICsvReportRenderer
{
    public string Render(CasinoImpactReportModel model)
    {
        var csv = new StringBuilder(32_000);
        Row(csv, "table", "group", "key", "value", "unit_or_note");
        Row(csv, "identity", "run", "model_run_id", model.Identity.ModelRunId, "uuid");
        Row(csv, "identity", "run", "model_version", model.Identity.ModelVersion, "");
        Row(csv, "identity", "run", "template_version", model.Identity.TemplateVersion, "");
        Row(csv, "scenario", "site", "candidate_latitude", model.Scenario.CandidateLatitude, "degrees");
        Row(csv, "scenario", "site", "candidate_longitude", model.Scenario.CandidateLongitude, "degrees");
        Row(csv, "revenue", "stabilized", "resident_ggr", model.Revenue.TotalResidentGgr, "USD");
        Row(csv, "revenue", "stabilized", "tourism_ggr", model.Revenue.TourismGgr, "USD");
        Row(csv, "revenue", "stabilized", "traffic_ggr", model.Revenue.TrafficGgr, "USD");
        Row(csv, "revenue", "stabilized", "total_ggr", model.Revenue.StabilizedTotalGgr, "USD");
        foreach (var county in model.OriginCounties)
        {
            Row(csv, "origin_county", county.GeographyCode, "origin_count", county.OriginCount, "origins");
            Row(csv, "origin_county", county.GeographyCode, "total_proposed_resident_ggr", county.TotalProposedResidentGgr, "USD");
            Row(csv, "origin_county", county.GeographyCode, "share_of_proposed_resident_ggr", county.ShareOfProposedResidentGgr, "share");
        }
        foreach (var origin in model.Origins)
        {
            Row(csv, "origin", origin.StableOriginId, "state_code", origin.StateCode, origin.OriginType);
            Row(csv, "origin", origin.StableOriginId, "redistributed_resident_ggr", origin.RedistributedResidentGgr, "USD");
            Row(csv, "origin", origin.StableOriginId, "induced_resident_ggr", origin.InducedResidentGgr, "USD");
            Row(csv, "origin", origin.StableOriginId, "total_proposed_resident_ggr", origin.TotalProposedResidentGgr, "USD");
        }
        foreach (var facility in model.Facilities)
        {
            Row(csv, "facility", facility.FacilityKey, "facility_name", facility.FacilityName, facility.FacilityKind);
            Row(csv, "facility", facility.FacilityKey, "baseline_resident_ggr", facility.BaselineResidentGgr, "USD");
            Row(csv, "facility", facility.FacilityKey, "with_project_resident_ggr", facility.WithProjectResidentGgr, "USD");
            Row(csv, "facility", facility.FacilityKey, "change_in_resident_ggr", facility.ChangeInResidentGgr, "USD");
        }
        foreach (var sector in model.SectorDisplacement)
        {
            Row(csv, "displacement", sector.SectorKey, "normalized_weight", sector.NormalizedWeight, "share");
            Row(csv, "displacement", sector.SectorKey, "displaced_sales", sector.DisplacedSales, "USD");
            Row(csv, "displacement", sector.SectorKey, "displaced_jobs", sector.DisplacedJobs, "jobs");
        }
        foreach (var cost in model.SocialCosts)
        {
            Row(csv, "social_cost", cost.DomainKey, "incremental_cases", cost.IncrementalCases, "cases");
            Row(csv, "social_cost", cost.DomainKey, "annual_cost", cost.AnnualCost, "USD");
            Row(csv, "social_cost", cost.DomainKey, "low_annual_cost", cost.LowAnnualCost, "USD");
            Row(csv, "social_cost", cost.DomainKey, "high_annual_cost", cost.HighAnnualCost, "USD");
        }
        if (model.NetImpact is not null)
        {
            Row(csv, "net_impact", "host", "net_host_local_impact", model.NetImpact.NetHostLocalImpact, "USD");
            Row(csv, "net_impact", "host", "net_host_state_impact", model.NetImpact.NetHostStateImpact, "USD");
            Row(csv, "net_impact", "host", "gross_social_cost", model.NetImpact.GrossSocialCost, "USD");
        }
        if (model.Sensitivity is not null)
        {
            Row(csv, "sensitivity", "analysis", "sensitivity_analysis_id", model.Sensitivity.SensitivityAnalysisId, "uuid");
            Row(csv, "sensitivity", "analysis", "baseline_model_run_id", model.Sensitivity.BaselineModelRunId, "uuid");
            Row(csv, "sensitivity", "analysis", "output_metric", model.Sensitivity.OutputMetric, model.Sensitivity.OutputUnits);
            Row(csv, "sensitivity", "analysis", "baseline_metric_value", model.Sensitivity.BaselineMetricValue, model.Sensitivity.OutputUnits);
            foreach (var sensitivity in model.Sensitivity.Rows)
            {
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_parameter_value", sensitivity.LowParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "base_parameter_value", sensitivity.BaseParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_parameter_value", sensitivity.HighParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_model_run_id", sensitivity.LowModelRunId, "uuid");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_model_run_id", sensitivity.HighModelRunId, "uuid");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_metric_value", sensitivity.LowMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "base_metric_value", sensitivity.BaseMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_metric_value", sensitivity.HighMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_delta", sensitivity.LowDelta, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_delta", sensitivity.HighDelta, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "total_range", sensitivity.TotalRange, model.Sensitivity.OutputUnits);
            }
        }
        foreach (var parameter in model.Parameters)
        {
            Row(csv, "parameter", parameter.Key, "default_value", parameter.DefaultValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "scenario_value", parameter.ScenarioValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "user_override_value", parameter.UserOverrideValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "final_value", parameter.FinalValue, parameter.Units);
        }
        foreach (var source in model.DataSources)
        {
            Row(csv, "data_source", source.Role + ":" + source.ReferenceKey, "dataset", source.DatasetKey, source.Period);
            Row(csv, "data_source", source.Role + ":" + source.ReferenceKey, "source_url", source.SourceUrl, source.Checksum);
        }
        return csv.ToString();
    }

    private static void Row(StringBuilder csv, params object?[] values)
    {
        csv.AppendJoin(',', values.Select(value => Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")));
        csv.Append('\n');
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : '"' + value.Replace("\"", "\"\"") + '"';
}
