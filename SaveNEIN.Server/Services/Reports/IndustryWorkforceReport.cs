// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Services.Reports;

public static partial class LegacyEconomicReportDocument
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static string Count(double? value) => value?.ToString("N0", Us) ?? "Not available";
    private static string Money(double? value) => value?.ToString("C0", Us) ?? "Not available";
    private static string Percent(double? value) => value?.ToString("0.0", Us) is { } text ? text + "%" : "Not available";
    private static string Growth(double? value) => value is null ? "Not available" : (value > 0 ? "+" : "") + Percent(value);
    private static double? Share(double? value, double? baseline) => value is not null && baseline > 0 ? value / baseline * 100 : null;
    private static string Survey(SurveyEstimate? value) => value?.Value is null ? "Not available" : Count(value.Value) +
        (value.MarginOfError is null ? " (margin unavailable)" : " +/- " + Count(value.MarginOfError));

    private static void EvidenceTable(ColumnDescriptor column, string[] headers, float[] widths, IEnumerable<string[]> rows)
    {
        column.Item().ShowEntire().Table(table =>
        {
            table.ColumnsDefinition(columns => { foreach (var width in widths) columns.RelativeColumn(width); });
            table.Header(header =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = header.Cell().Element(TableHeader);
                    if (i > 0) cell = cell.AlignRight();
                    cell.Text(headers[i]);
                }
            });
            var index = 0;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = table.Cell().ShowEntire().Background(index % 2 == 0 ? "#FFFFFF" : "#ECEFF1")
                        .BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3.5f).PaddingHorizontal(7);
                    if (i > 0) cell = cell.AlignRight();
                    cell.Text(Clean((row.ElementAtOrDefault(i) ?? "N/A").Replace("Not available", "N/A", StringComparison.Ordinal))).FontSize(9).FontColor(Ink);
                }
                index++;
            }
        });
    }

    private static void EvidenceSource(ColumnDescriptor column, string text) => column.Item().PaddingTop(12)
        .DefaultTextStyle(style => style.FontSize(8).FontColor(Muted)).Text(descriptor => Inline(descriptor, text));

    private static void AddLocalEconomyPages(IDocumentContainer document, LocalEconomyReport? report, string county, byte[]? logo, DateTime date)
    {
        if (report?.Profile is not { } profile) return;
        var total = profile.Industries.FirstOrDefault(row => row.Code == "10");
        var hospitality = profile.Industries.FirstOrDefault(row => row.Code == "72");
        var industries = profile.Industries.Where(row => row.Code is not ("10" or "721" or "722"))
            .OrderByDescending(row => row.Jobs ?? -1).Take(19).ToArray();
        var chunks = industries.Chunk(10).ToArray();
        if (chunks.Length == 0) chunks = [[]];
        for (var index = 0; index < chunks.Length; index++)
        {
            var part = index; var rows = chunks[index];
            AddPage(document, "Industry composition and trends", county, logo, date, true, column =>
            {
                if (part == 0) column.Item().Section("industry");
                Chapter(column, "03", part == 0 ? "Industry composition and trends" : "Industry detail, continued",
                    $"{county} | Private UI-covered workplace jobs | BLS QCEW {profile.IndustryYear} annual averages");
                if (part == 0) column.Item().PaddingBottom(12).Background("#E9E0D0").Padding(10)
                    .Text($"PRIVATE WORKPLACE JOBS: {Count(total?.Jobs)}     |     CHANGE SINCE 2020: {Growth(total?.ChangeFiveYear)}     |     SINCE 2015: {Growth(total?.ChangeTenYear)}")
                    .FontSize(10).SemiBold().FontColor(Navy);
                EvidenceTable(column, ["Industry / NAICS", "Jobs", "Estab.", "Share", "Annual pay", "2020-25", "2015-25", "LQ"],
                    [3.1f, 1, .75f, .7f, 1.05f, .85f, .85f, .6f], rows.Select(row => new[]
                    {
                        row.Name + " / " + row.Code + (row.Suppressed ? " *" : ""), Count(row.Jobs), Count(row.Establishments), Percent(row.Share),
                        Money(row.AnnualPay), Growth(row.ChangeFiveYear), Growth(row.ChangeTenYear), row.LocationQuotient?.ToString("0.00", Us) ?? "N/A"
                    }));
                EvidenceSource(column, $"Source: [BLS QCEW 2025](https://data.bls.gov/cew/data/api/2025/a/area/{profile.CountyFips}.csv), " +
                    $"[2020](https://data.bls.gov/cew/data/api/2020/a/area/{profile.CountyFips}.csv), [2015](https://data.bls.gov/cew/data/api/2015/a/area/{profile.CountyFips}.csv). " +
                    $"U.S. comparison: [QCEW national](https://data.bls.gov/cew/data/api/2025/a/area/US000.csv). Retrieved {profile.RetrievedAtUtc:yyyy-MM-dd}.");
                EvidenceSource(column, "READING THIS EXHIBIT: Shares use private county jobs; LQ compares with private U.S. shares (above 1 = specialization). * Employment/pay suppressed; N/A is not zero. Pay is nominal payroll per average job, not a full-time salary. The 2020 baseline reflects the pandemic; NAICS revisions affect comparability. County totals do not describe drive-time bands.");
            });
        }

        var workforce = profile.Workforce;
        AddPage(document, "Resident workforce and commuting", county, logo, date, false, column =>
        {
            column.Item().Section("workforce");
            Chapter(column, "04.01", "Resident workforce and commuting", "Census ACS 2020-2024 five-year estimates | Residents age 16+ | Counts with 90% sampling margins");
            if (profile.CurrentLaborMarket is { } monthly)
            {
                column.Item().PaddingBottom(12).Background("#E9E0D0").Padding(12).Column(current =>
                {
                    current.Item().Text($"MONTHLY LABOR MARKET / {monthly.Period}").FontSize(8).Bold().FontColor(Navy);
                    current.Item().PaddingTop(5).Text($"{Count(monthly.LaborForce)} labor force | {Count(monthly.Employed)} employed | {Count(monthly.Unemployed)} unemployed | {Percent(monthly.UnemploymentRate)} unemployment")
                        .FontSize(10).SemiBold().FontColor(Navy);
                    current.Item().PaddingTop(4).Text("BLS LAUS, not seasonally adjusted. " + (monthly.Preliminary ? "Preliminary; subject to revision. " : "") + "Separate from the ACS five-year estimates below.").FontSize(8).FontColor(Muted);
                });
            }
            EvidenceTable(column, ["Resident measure", "Estimate +/- margin"], [3, 1.5f], new[]
            {
                new[] { "Population age 16+", Survey(workforce?.Population16Plus) },
                new[] { "Civilian labor force", Survey(workforce?.CivilianLaborForce) },
                new[] { "Employed residents", Survey(workforce?.Employed) },
                new[] { "Unemployed residents", Survey(workforce?.Unemployed) },
                new[] { "Not in the labor force", Survey(workforce?.NotInLaborForce) },
                new[] { "Resident workers", Survey(workforce?.ResidentWorkers) },
                new[] { "Work in home county", Survey(workforce?.WorkInCounty) },
                new[] { "Work elsewhere in home state", Survey(workforce?.WorkElsewhereInState) },
                new[] { "Work outside home state", Survey(workforce?.WorkOutsideState) }
            });
            EvidenceSource(column, $"Sources: [Census ACS B23025](https://data.census.gov/table/ACSDT5Y2024.B23025?g=050XX00US{profile.CountyFips}) and " +
                    $"[B08007](https://data.census.gov/table/ACSDT5Y2024.B08007?g=050XX00US{profile.CountyFips}). " +
                    (profile.CurrentLaborMarket is not null ? $"[BLS LAUS](https://data.bls.gov/timeseries/LAUCN{profile.CountyFips}0000000003). " : "") +
                    $"Public snapshots retrieved {profile.RetrievedAtUtc:yyyy-MM-dd} UTC.");
            Note(column, "Workforce availability", "These are pooled survey estimates, not current monthly unemployment. People outside the labor force are not automatically available for hire. Commuting figures describe where county residents work; they do not measure incoming workers, available recruits or casino patron origins. Employed residents and workplace jobs have different populations and coverage.");
            Note(column, "Sampling and geography", "Margins are the published 90% sampling margins for each estimate, not forecast intervals; nonsampling error is excluded. County estimates are not apportioned into isochrones. Hiring ratios compare these baseline estimates with a user-defined future scenario, not simultaneous measured outcomes.");
        });

        AddPage(document, "Occupational and industry pay", county, logo, date, false, column =>
        {
            Chapter(column, "04.02", "Occupational and industry pay", profile.Occupations.Count > 0
                ? $"May 2025 BLS OEWS | {profile.WageAreaName} | Regional wage benchmarks" : "County industry pay and occupational-data coverage");
            if (profile.Occupations.Count > 0)
            {
                EvidenceTable(column, ["Occupation", "Median hourly", "Mean annual"], [3, 1.1f, 1.1f], profile.Occupations.Take(8).Select(row => new[]
                {
                    row.Name, row.MedianHourly?.ToString("C2", Us) ?? "Not available", Money(row.MeanAnnual)
                }));
                EvidenceSource(column, $"Source: [BLS OEWS May 2025]({profile.WageSourceUrl}). Geography: {profile.WageAreaName}; not county-only estimates. " +
                    "Occupations selected to illustrate hospitality, security and production recruitment comparisons; not an operator's staffing mix.");
            }
            else Note(column, "Regional wages unavailable", "A verified occupational-wage snapshot is not available for this county. Wages from another region have not been substituted. County industry pay remains a separate measure.");
            column.Item().PaddingTop(22).PaddingBottom(8).Text("County industry pay comparison").FontSize(15).Bold().FontColor(Navy);
            EvidenceTable(column, ["County industry", "Annual pay per average job"], [3, 1.8f],
                profile.Industries.Where(row => row.Code is "10" or "31-33" or "72" or "721" or "722")
                    .Select(row => new[] { row.Name, Money(row.AnnualPay) }));
            EvidenceSource(column, $"Source: [BLS QCEW 2025](https://data.bls.gov/cew/data/api/2025/a/area/{profile.CountyFips}.csv). Nominal annual pay; subindustries 721/722 are included in 72.");
            Note(column, "Interpreting wage comparisons", "Hourly medians, annual means and industry-average annual pay measure different things. OEWS annual wages generally annualize hourly rates at 2,080 hours; they are not actual annual earnings for part-time or seasonal workers. QCEW annual pay reflects payroll divided by average jobs, including part-time and seasonal patterns. Neither is a casino pay offer or benefits-inclusive compensation, or establishes whether a casino can recruit or retain staff.");
        });

        var hiring = report.Hiring;
        AddPage(document, "Recruitment scenario", county, logo, date, false, column =>
        {
            Chapter(column, "04.03", "Recruitment scenario", "Editable assumptions | Gross hiring assignments | Excluded from the financial balance");
            var shares = new[] { hiring.PreviouslyUnemployedPercent, hiring.ExistingEmployersPercent, hiring.InCommutersPercent, hiring.NewResidentsPercent };
            var valid = double.IsFinite(hiring.Positions) && hiring.Positions >= 0 && hiring.Positions <= 100000 && hiring.Positions == Math.Truncate(hiring.Positions)
                && double.IsFinite(hiring.FtePerPosition) && hiring.FtePerPosition > 0 && hiring.FtePerPosition <= 1
                && shares.All(share => double.IsFinite(share) && share >= 0 && share <= 100) && Math.Abs(shares.Sum() - 100) < .001;
            if (!valid) { Note(column, "Invalid recruitment assumptions", "No hiring results are reported. Positions must be whole numbers, the FTE factor must be above 0 and at most 1, and recruitment shares must total 100%."); return; }
            column.Item().PaddingBottom(16).Row(row =>
            {
                row.RelativeItem().Element(cell => Metric(cell, "PROPOSED POSITIONS / PEOPLE", Count(hiring.Positions)));
                row.ConstantItem(10);
                row.RelativeItem().Element(cell => Metric(cell, "FULL-TIME EQUIVALENTS", Count(hiring.Positions * hiring.FtePerPosition)));
            });
            var sources = new[] { "Previously unemployed county residents", "Transfers from existing county employers", "Incoming commuters residing elsewhere", "New residents relocating to the county" };
            EvidenceTable(column, ["Recruitment source", "Assumed share", "Hires"], [3, 1.1f, .8f],
                sources.Select((source, index) => new[] { source, Percent(shares[index]), Count(hiring.Positions * shares[index] / 100) }));
            var unemployedHires = hiring.Positions * hiring.PreviouslyUnemployedPercent / 100;
            var relativeUnemployment = Share(unemployedHires, profile.CurrentLaborMarket?.Unemployed ?? workforce?.Unemployed.Value);
            column.Item().PaddingTop(14).Column(comparisons =>
            {
                KeyValue(comparisons, "Positions / civilian labor force", Percent(Share(hiring.Positions, profile.CurrentLaborMarket?.LaborForce ?? workforce?.CivilianLaborForce.Value)));
                KeyValue(comparisons, "Positions / accommodation-food-service jobs", Percent(Share(hiring.Positions, hospitality?.Jobs)));
                KeyValue(comparisons, "Unemployed-resident hires / unemployed estimate", Percent(relativeUnemployment));
            });
            EvidenceSource(column, "Labor-force and unemployment baseline: " + (profile.CurrentLaborMarket is { } labor ? $"BLS LAUS {labor.Period}; not seasonally adjusted" : "ACS 2020-2024") + "; industry jobs: QCEW 2025. These comparisons use different periods and populations for scale, not recruitment probabilities. Counts are rounded independently and may not sum exactly.");
            if (relativeUnemployment > 100) Note(column, "Recruitment constraint", "Assigned unemployed-resident hires exceed the baseline unemployment estimate. This allocation requires additional supporting evidence or revision; it is not a demonstrated labor supply.");
            Note(column, "Assumption basis", "The initial 1,000 positions, 1.00 FTE per position and 25% shares are illustrative editing defaults, not research findings or a verified staffing plan. " +
                (string.IsNullOrWhiteSpace(hiring.SupportingInformation) ? "No supporting information supplied." : "User-supplied supporting information: " + Clean(hiring.SupportingInformation[..Math.Min(1000, hiring.SupportingInformation.Length)])));
            Note(column, "Gross assignments, not net job creation", $"The selected mix assigns {Count(unemployedHires + hiring.Positions * hiring.NewResidentsPercent / 100)} hires to newly employed county residents after relocation. " +
                "Transfers do not by themselves add employed residents and may create replacement demand at existing employers; incoming commuters reside elsewhere. One distinct person per position is assumed. No vacancy, migration, multiplier, wage-benefit or net employment effect is inferred or added to the calculator's financial statements.");
        });
    }
}
