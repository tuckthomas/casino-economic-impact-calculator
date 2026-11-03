// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
using System.Globalization;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Controllers;

namespace SaveNEIN.Server.Services.Reports;

/// <summary>Presentation of the browser calculator's supplied results, without recalculating the model.</summary>
public static partial class LegacyEconomicReportDocument
{
    private const string Navy = "#101E32";
    private const string NavyLight = "#1C304B";
    private const string Gold = "#B08D57";
    private const string GoldInk = "#836333";
    private const string Paper = "#F7F5F0";
    private const string Ink = "#202E40";
    private const string Muted = "#627083";
    private const string Rule = "#DCE0E4";
    private const string Red = "#A33338";
    private const string Green = "#216355";

    private sealed record Statement(string Title, List<List<string>> Rows);
    private sealed record Narrative(string Title, List<string> Lines);

    public static byte[] Render(ReportRequest request, byte[]? darkLogo, DateTime generatedAt)
    {
        var county = Clean(request.SubjectCountyName ?? "Selected study area");
        if (!county.EndsWith("County", StringComparison.OrdinalIgnoreCase) && request.SubjectCountyName is not null)
            county += " County";
        var statements = SplitStatements(request.MainTable?.Rows ?? []);
        var sections = SplitNarrative(request.AnalysisText ?? "");
        var map = DecodeMap(request.MapImageBase64);
        var context = request.ScenarioContext ?? [];
        var hasEconomy = request.LocalEconomy?.Profile is not null;
        var statementNumber = hasEconomy ? 5 : 3;
        string Value(string key) => context.GetValueOrDefault(key, "Not supplied");

        var document = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(42);
                page.PageColor(Navy);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontColor(Colors.White));
                page.Content().Column(column =>
                {
                    if (darkLogo is not null) column.Item().Width(210).Height(94).Image(darkLogo).FitArea();
                    else column.Item().Text("SAVE NORTHEAST INDIANA").FontSize(18).Bold();
                    column.Item().PaddingTop(42).Text("ECONOMIC RESEARCH  /  SCENARIO REPORT").FontSize(10).SemiBold().FontColor(Gold);
                    column.Item().PaddingTop(16).Text("Casino Economic\nImpact Analysis").FontSize(40).Bold().LineHeight(1.08f);
                    column.Item().PaddingTop(24).Text(county).FontSize(23).SemiBold();
                    column.Item().PaddingTop(22).Width(92).Height(3).Background(Gold);
                    column.Item().PaddingTop(22).Text("Geographic exposure, public revenue, social costs and business-sales displacement.")
                        .FontSize(14).LineHeight(1.4f).FontColor("#D6DEEA");
                    column.Item().PaddingTop(32).Background(NavyLight).BorderLeft(3).BorderColor(Gold).Padding(18).Column(box =>
                    {
                        box.Item().Text("RESEARCH BASIS").FontSize(9).Bold().FontColor(Gold);
                        box.Item().PaddingTop(8).Text("An adjustable application of Grinols-based social-cost valuations, combined with the selected population, exposure and fiscal assumptions. The full methodology, user adjustments and limitations are documented in this report.")
                            .FontSize(11).LineHeight(1.45f).FontColor("#E3E8F0");
                    });
                    column.Item().PaddingTop(32).Row(row =>
                    {
                        row.RelativeItem().Column(details =>
                        {
                            details.Item().Text("PREPARED BY").FontSize(8).FontColor(Gold).SemiBold();
                            details.Item().PaddingTop(5).Text("Save Northeast Indiana").FontSize(12).SemiBold();
                        });
                        row.RelativeItem().Column(details =>
                        {
                            details.Item().Text("REPORT DATE").FontSize(8).FontColor(Gold).SemiBold();
                            details.Item().PaddingTop(5).Text(generatedAt.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)).FontSize(12);
                        });
                    });
                });
                page.Footer().BorderTop(1).BorderColor(Gold).PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text("SAVENEIN.COM").FontSize(8).FontColor("#D6DEEA");
                    row.AutoItem().Text("RESEARCH-BASED ASSUMPTIONS  |  CONDITIONAL RESULTS").FontSize(7).FontColor("#D6DEEA");
                });
            });

            AddPage(document, "Report overview", county, darkLogo, generatedAt, false, column =>
            {
                column.Item().Section("overview");
                Chapter(column, "01", "Scenario at a glance", "Annual scenario results for the selected inputs. MM denotes millions of U.S. dollars.");
                var cards = new[] {
                    ("ANNUAL CASINO AGR", Value("annualAgr")), ("GAMING-TAX RECEIPTS", Value("taxRevenue")),
                    ("MODELED SOCIAL COSTS", Value("socialCosts")), ("BUSINESS-SALES DISPLACEMENT", Value("salesDisplacement")),
                    ("TAX LESS SOCIAL COSTS", Value("taxSocialBalance")), ("INCLUDING DISPLACED SALES", Value("illustrativeBalance"))
                };
                for (var i = 0; i < cards.Length; i += 2)
                {
                    var first = cards[i]; var second = cards[i + 1];
                    column.Item().PaddingBottom(7).Row(row =>
                    {
                        row.RelativeItem().Element(container => Metric(container, first.Item1, first.Item2));
                        row.ConstantItem(10);
                        row.RelativeItem().Element(container => Metric(container, second.Item1, second.Item2));
                    });
                }
                Note(column, "Reading the balances", "Social costs include public and private burdens. Displaced sales measure business turnover, not government expenditure or net welfare. The final comparison includes different economic measures; it is not a government budget or a complete cost-benefit assessment.");
                column.Item().PaddingTop(12).PaddingBottom(6).Text("Report guide").FontSize(15).Bold().FontColor(Navy);
                ContentsRow(column, "01", "Scenario overview", "overview");
                ContentsRow(column, "02", "Geographic context", "geography");
                if (hasEconomy)
                {
                    ContentsRow(column, "03", "Industry composition and trends", "industry");
                    ContentsRow(column, "04", "Workforce and recruitment", "workforce");
                }
                for (var i = 0; i < statements.Count; i++) ContentsRow(column, $"{StatementScope(statements[i].Title, i) + statementNumber:00}", ShortTitle(statements[i].Title), $"statement-{i}");
                ContentsRow(column, $"{statementNumber + 3:00}", "Social-cost detail", "cost-detail");
                if (sections.Count > 0) ContentsRow(column, $"{statementNumber + 4:00}", "Methodology and written analysis", "analysis-0");
            });

            AddPage(document, "Geographic context", county, darkLogo, generatedAt, false, column =>
            {
                column.Item().Section("geography");
                Chapter(column, "02", "Geographic context", "Casino location and the exposure footprint used in this scenario.");
                if (map is not null) column.Item().Background(Colors.White).Border(1).BorderColor(Rule).Padding(8).Height(250).AlignMiddle().Image(map).FitArea();
                else Note(column, "Map unavailable", "No map image was supplied for this export. Review the selected location in the calculator before using geographic results.");
                column.Item().PaddingTop(10).Text("EXHIBIT 1  |  Selected casino location and impact zones").FontSize(8).Bold().FontColor(Muted);
                var exposure = Value("exposureMethod");
                if (map is not null && (exposure.Contains("60-minute") || exposure.Contains("50-mile")))
                {
                    var bands = exposure.Contains("60-minute") ? new[] { "0-15 minutes", "15-30 minutes", "30-60 minutes" }
                        : new[] { "0-10 miles", "10-20 miles", "20-50 miles" };
                    var colors = new[] { "#3B82F6", "#EF4444", "#F59E0B" };
                    column.Item().PaddingTop(8).Row(legend =>
                    {
                        for (var band = 0; band < bands.Length; band++)
                        {
                            var label = bands[band]; var color = colors[band];
                            legend.RelativeItem().Row(item =>
                            {
                                item.ConstantItem(7).AlignMiddle().Height(7).Background(color);
                                item.ConstantItem(6);
                                item.RelativeItem().Text(label).FontSize(8).FontColor(Muted);
                            });
                        }
                    });
                }
                column.Item().PaddingTop(14).Column(settings =>
                {
                    settings.Spacing(0);
                    KeyValue(settings, "Study area", county);
                    KeyValue(settings, "Exposure method", Value("exposureMethod"));
                    KeyValue(settings, "Baseline prevalence", Value("baselineRate"));
                    KeyValue(settings, "Additional baseline increase", Value("baselineIncrease"));
                    KeyValue(settings, "Annual social-cost value per additional case", Value("costPerCase"));
                });
                Note(column, "Geographic interpretation", "The map is a spatial model of exposure, not observed patron behavior. The written analysis explains road-network routing, population allocation, band construction and same-state coverage. Geographic approximations and source-data vintage affect the estimates.");
            });

            AddLocalEconomyPages(document, request.LocalEconomy, county, darkLogo, generatedAt);

            for (var statementIndex = 0; statementIndex < statements.Count; statementIndex++)
            {
                var index = statementIndex;
                var statement = statements[index];
                var scope = StatementScope(statement.Title, index);
                var chunks = StrategicChunks(statement.Rows, 24);
                for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var part = chunkIndex; var rows = chunks[part];
                    AddPage(document, ShortTitle(statement.Title), county, darkLogo, generatedAt, false, column =>
                    {
                        if (part == 0) column.Item().Section($"statement-{index}");
                        Chapter(column, $"{scope + statementNumber:00}", ShortTitle(statement.Title),
                            part == 0 ? StatementDescription(scope) : $"Continued - part {part + 1} of {chunks.Count}. Column headings are repeated; rows stay intact.");
                        if (part > 0) column.Item().PaddingBottom(8).Text($"{county} / {statement.Title}").FontSize(9).SemiBold().FontColor(Muted);
                        column.Item().ShowEntire().Element(container => Ledger(container, rows));
                        if (part == chunks.Count - 1)
                            Note(column, "Basis of comparison", scope == 0
                                ? "The host comparison includes costs borne by residents and businesses as well as public services. Business-sales displacement is shown separately at the selected host share. It is not an expense paid by county government."
                                : scope == 1 ? "Regional receipts are compared with social costs in other assessed same-state counties. The detailed category composition follows in Exhibit 2."
                                : "The consolidated statement includes the full sales-displacement amount once. Its social-cost and turnover measures have different economic meanings; overlap is not quantified.");
                    });
                }
            }

            var costRows = CostRows(request, county);
            var costChunkSize = costRows.Count == 0 ? 12 : (int)Math.Ceiling(costRows.Count / Math.Ceiling(costRows.Count / 12d));
            var costChunks = costRows.Chunk(costChunkSize).ToArray();
            if (costChunks.Length == 0) costChunks = [[]];
            for (var i = 0; i < costChunks.Length; i++)
            {
                var part = i; var rows = costChunks[i];
                AddPage(document, "Social-cost detail", county, darkLogo, generatedAt, true, column =>
                {
                    if (part == 0) column.Item().Section("cost-detail");
                    Chapter(column, $"{statementNumber + 3:00}", "Social-cost detail", part == 0
                        ? "EXHIBIT 2  |  Annual social costs by county and category. Dollar figures in millions (MM)."
                        : $"EXHIBIT 2 (continued)  |  Part {part + 1} of {costChunks.Length}. Each county remains on a single row.");
                    column.Item().ShowEntire().Table(table =>
                    {
                        table.ColumnsDefinition(columns => { columns.RelativeColumn(1.8f); for (var c = 0; c < 7; c++) columns.RelativeColumn(); });
                        table.Header(header =>
                        {
                            foreach (var label in new[] { "County / area", "Public\nhealth", "Social\nservices", "Law\nenforcement", "Civil\nlegal", "Abused\ndollars", "Lost\nemployment", "Total" })
                                header.Cell().Element(TableHeader).Text(label);
                        });
                        for (var r = 0; r < rows.Length; r++)
                        {
                            var row = rows[r];
                            for (var c = 0; c < 8; c++)
                            {
                                var cell = table.Cell().ShowEntire().Background(r % 2 == 0 ? Colors.White : "#ECEFF1")
                                    .BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3.5f).PaddingHorizontal(7);
                                if (c > 0) cell = cell.AlignRight();
                                cell.Text(Clean(row.ElementAtOrDefault(c) ?? "-")).FontSize(9).FontColor(c == 7 ? Navy : Ink).SemiBold();
                            }
                        }
                    });
                    Note(column, "Units and cost incidence", "Dollar values represent annual social costs attributable to modeled additional problem-gambling cases. General taxpayer-services categories and private-sector categories are distinguished in the financial statements. Displayed values are rounded; category sums can differ slightly from rounded totals.");
                });
            }

            if (sections.Count > 0)
            {
                AddPage(document, "Methodology and written analysis", county, darkLogo, generatedAt, false, column =>
                {
                    for (var index = 0; index < sections.Count; index++)
                    {
                        var section = sections[index];
                        var opening = column.Item().Section($"analysis-{index}").PaddingTop(index == 0 ? 0 : 18);
                        opening = section.Lines.FirstOrDefault()?.Length > 2200 ? opening.EnsureSpace(220) : opening.ShowEntire();
                        opening.Column(introduction =>
                        {
                            Chapter(introduction, $"{statementNumber + 4:00}.{index + 1:00}", section.Title,
                                index == 0 ? "Automated analysis of the selected scenario. Research assumptions, settings and interpretation are retained in full." : "");
                            if (section.Lines.Count > 0) NarrativeLine(introduction, section.Lines[0]);
                        });
                        foreach (var line in section.Lines.Skip(1)) NarrativeLine(column, line);
                    }
                });
            }
        });
        return document.GeneratePdf();
    }

    private static void AddPage(IDocumentContainer document, string section, string county, byte[]? logo,
        DateTime date, bool landscape, Action<ColumnDescriptor> content)
    {
        document.Page(page =>
        {
            page.Size(landscape ? PageSizes.Letter.Landscape() : PageSizes.Letter);
            page.Margin(34);
            page.PageColor(Paper);
            page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(10).FontColor(Ink).LineHeight(1.3f));
            page.Header().Background(Navy).PaddingHorizontal(13).PaddingVertical(9).Row(row =>
            {
                if (logo is not null) row.ConstantItem(90).Height(34).Image(logo).FitArea();
                else row.ConstantItem(90).Text("SaveNEIN").Bold().FontColor(Colors.White);
                row.ConstantItem(18);
                row.RelativeItem().AlignMiddle().Column(column =>
                {
                    column.Item().Text("CASINO ECONOMIC IMPACT ANALYSIS").FontSize(7.5f).SemiBold().FontColor("#D8C29D");
                    column.Item().PaddingTop(3).Text(Clean(section)).FontSize(10).SemiBold().FontColor(Colors.White);
                });
                row.ConstantItem(86).AlignRight().AlignMiddle().Text(date.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()).FontSize(8).FontColor("#D8C29D");
            });
            page.Footer().BorderTop(1).BorderColor(Gold).PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"SAVENEIN.COM  /  {county}").FontSize(7.5f).FontColor(Muted);
                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Navy));
                    text.Span("PAGE "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages();
                });
            });
            page.Content().PaddingTop(20).PaddingBottom(14).Column(column => { column.Spacing(0); content(column); });
        });
    }

    private static void Chapter(ColumnDescriptor column, string number, string title, string subtitle)
    {
        column.Item().EnsureSpace(110).PaddingBottom(17).Column(heading =>
        {
            heading.Item().Text($"{number}  /  {title.ToUpperInvariant()}").FontSize(8).Bold().FontColor(GoldInk);
            heading.Item().PaddingTop(8).Text(title).FontSize(24).Bold().LineHeight(1.15f).FontColor(Navy);
            if (!string.IsNullOrWhiteSpace(subtitle)) heading.Item().PaddingTop(8).Text(subtitle).FontSize(9.5f).FontColor(Muted);
        });
    }

    private static void Metric(IContainer container, string label, string value) => container
        .Background(Colors.White).BorderTop(2).BorderColor(Gold).Padding(10).Column(column =>
        {
            column.Item().Text(label).FontSize(7.5f).SemiBold().FontColor(Muted);
            column.Item().PaddingTop(6).Text(value).FontSize(value.Length > 18 ? 15 : 21).Bold().FontColor(value.StartsWith('-') ? Red : Navy);
        });

    private static void ContentsRow(ColumnDescriptor column, string number, string title, string target) => column.Item()
        .SectionLink(target).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(4).Row(row =>
        {
            row.ConstantItem(32).Text(number).FontSize(9).Bold().FontColor(GoldInk);
            row.RelativeItem().Text(title).FontSize(10).SemiBold();
            row.AutoItem().Text(text => text.BeginPageNumberOfSection(target));
        });

    private static void Note(ColumnDescriptor column, string title, string text) => column.Item().PaddingTop(16)
        .ShowEntire().Background("#EAEFF2").BorderLeft(2).BorderColor(Gold).Padding(12).Column(note =>
        {
            note.Item().Text(title.ToUpperInvariant()).FontSize(8).Bold().FontColor(Navy);
            note.Item().PaddingTop(5).Text(text).FontSize(9).LineHeight(1.35f).FontColor(Muted);
        });

    private static void KeyValue(ColumnDescriptor column, string key, string value) => column.Item()
        .BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).Row(row =>
        {
            row.RelativeItem().Text(key).FontSize(9).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(value).FontSize(9).SemiBold().FontColor(Navy);
        });

    private static void Ledger(IContainer container, List<List<string>> rows) => container.Table(table =>
    {
        table.ColumnsDefinition(columns => { columns.RelativeColumn(4.7f); columns.RelativeColumn(); });
        table.Header(header =>
        {
            header.Cell().Element(TableHeader).Text("ANNUAL STATEMENT / ITEM");
            header.Cell().Element(TableHeader).AlignRight().Text("USD");
        });
        var expense = false;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var label = Clean(row.ElementAtOrDefault(0) ?? "");
            var value = Clean(row.ElementAtOrDefault(1) ?? "");
            if (string.IsNullOrWhiteSpace(value))
            {
                if (label.Contains("REVENUE", StringComparison.OrdinalIgnoreCase)) expense = false;
                else if (label.Contains("EXPENSE", StringComparison.OrdinalIgnoreCase) || label.Contains("COST", StringComparison.OrdinalIgnoreCase) || label.Contains("DISPLACEMENT", StringComparison.OrdinalIgnoreCase)) expense = true;
                table.Cell().ColumnSpan(2).Background("#DFE5EB").PaddingVertical(6).PaddingHorizontal(10)
                    .Text(label).FontSize(8).Bold().FontColor(Navy);
                continue;
            }
            var final = label.StartsWith("ILLUSTRATIVE", StringComparison.OrdinalIgnoreCase);
            var subtotal = IsTotal(label);
            var background = final ? Navy : subtotal ? "#E9E0D0" : i % 2 == 0 ? "#FFFFFF" : "#EEF0F1";
            var valueColor = final ? "#FFFFFF" : value.StartsWith('-') ? Red : subtotal ? Navy : expense ? Red : Green;
            table.Cell().ShowEntire().Background(background).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(10)
                .DefaultTextStyle(style => subtotal || final ? style.Bold() : style)
                .Text(label.TrimStart('+', '-', ' ')).FontSize(9).FontColor(final ? "#FFFFFF" : Ink);
            table.Cell().ShowEntire().Background(background).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(10).AlignRight()
                .Text(value).FontSize(9).SemiBold().FontColor(valueColor);
        }
    });

    private static IContainer TableHeader(IContainer container) => container.Background(Navy).PaddingVertical(8).PaddingHorizontal(8)
        .DefaultTextStyle(style => style.FontSize(8).SemiBold().FontColor(Colors.White));

    private static void NarrativeLine(ColumnDescriptor column, string raw)
    {
        var line = Clean(raw); var trimmed = line.Trim();
        if (trimmed.Length == 0) return;
        var bullet = trimmed.StartsWith('*');
        var level = bullet ? Math.Min((line.Length - line.TrimStart().Length) / 2, 4) : 0;
        var content = bullet ? trimmed[1..].Trim() : trimmed;
        var container = column.Item().PaddingBottom(12);
        container = content.Length < 2200 ? container.ShowEntire() : container.EnsureSpace(50);
        container.PaddingLeft(level * 14).Row(row =>
        {
            if (bullet) row.ConstantItem(12).PaddingTop(6).Width(3).Height(3).Background(Gold);
            row.RelativeItem().Text(text => Inline(text, content));
        });
    }

    private static void Inline(TextDescriptor descriptor, string content)
    {
        var bold = false;
        foreach (var part in Regex.Split(content, @"(\*\*|\[[^\]]+\]\(https?://[^)]+\))"))
        {
            if (part == "**") { bold = !bold; continue; }
            var link = Regex.Match(part, @"^\[([^\]]+)\]\((https?://[^)]+)\)$");
            var span = link.Success ? descriptor.Hyperlink(link.Groups[1].Value, link.Groups[2].Value) : descriptor.Span(part);
            if (bold) span.SemiBold().FontColor(Navy);
            if (link.Success) span.FontColor("#315B7B").Underline();
        }
    }

    private static List<Statement> SplitStatements(List<List<string>> rows)
    {
        var result = new List<Statement>(); Statement? current = null;
        foreach (var row in rows)
        {
            var name = row.ElementAtOrDefault(0) ?? "";
            if ((row.ElementAtOrDefault(1) ?? "").Length == 0 &&
                (name.StartsWith("Host-Area", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Regional Revenue", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Consolidated", StringComparison.OrdinalIgnoreCase)))
            {
                current = new Statement(Clean(name), []); result.Add(current);
            }
            else
            {
                if (current is null) { current = new Statement("Financial statement", []); result.Add(current); }
                current.Rows.Add(row);
            }
        }
        return result;
    }

    private static List<List<List<string>>> StrategicChunks(List<List<string>> rows, int limit)
    {
        var chunks = new List<List<List<string>>>(); var offset = 0;
        while (offset < rows.Count)
        {
            var length = Math.Min(limit, rows.Count - offset);
            if (offset + length < rows.Count)
            {
                // Prefer a county boundary over splitting the county's expanded cost group.
                var countyBoundary = Enumerable.Range(1, length - 1).LastOrDefault(index =>
                    Regex.IsMatch((rows[offset + index].FirstOrDefault() ?? "").TrimStart(), @"^[+−-]\s*\w"));
                if (countyBoundary > limit / 2) length = countyBoundary;
                while (length > 8 && ((rows[offset + length - 1].ElementAtOrDefault(1) ?? "").Length == 0 || IsTotal(rows[offset + length].FirstOrDefault() ?? ""))) length--;
                if (rows.Count - offset - length < 4) length -= 4;
            }
            chunks.Add(rows.Skip(offset).Take(length).ToList()); offset += length;
        }
        if (chunks.Count == 0) chunks.Add([]);
        return chunks;
    }

    private static List<Narrative> SplitNarrative(string text)
    {
        var sections = new List<Narrative>(); Narrative? section = null;
        foreach (var raw in text.Split('\n'))
        {
            if (raw.TrimStart().StartsWith("###"))
            {
                var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Clean(raw.Trim().TrimStart('#').Trim()).ToLowerInvariant());
                title = Regex.Replace(title, @"\b(And|Or|Of|The|In|For|To|With|By)\b", match => match.Value.ToLowerInvariant());
                section = new Narrative(title, []); sections.Add(section);
            }
            else if (!string.IsNullOrWhiteSpace(raw))
            {
                if (section is null) { section = new Narrative("Written analysis", []); sections.Add(section); }
                section.Lines.Add(raw);
            }
        }
        return sections;
    }

    private static List<List<string>> CostRows(ReportRequest request, string county)
    {
        var rows = new List<List<string>>();
        if (request.BreakdownTable is { Count: >= 7 } subject)
            rows.Add(new[] { county }.Concat(subject.Take(7).Select(row => row.ElementAtOrDefault(3) ?? "-")).ToList());
        if (request.BreakdownOtherTable is not null) rows.AddRange(request.BreakdownOtherTable.Where(row => row.Count >= 8));
        return rows;
    }

    private static bool IsTotal(string value) => value.Contains("TOTAL", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("HOST TAX", StringComparison.OrdinalIgnoreCase) || value.StartsWith("TAX REVENUE LESS", StringComparison.OrdinalIgnoreCase);
    private static string ShortTitle(string title) => title.StartsWith("Host-Area", StringComparison.OrdinalIgnoreCase) ? "Host-area comparison"
        : title.StartsWith("Regional", StringComparison.OrdinalIgnoreCase) ? "Regional comparison"
        : title.StartsWith("Consolidated", StringComparison.OrdinalIgnoreCase) ? "Consolidated comparison" : title;
    private static int StatementScope(string title, int fallback) => title.StartsWith("Host-Area", StringComparison.OrdinalIgnoreCase) ? 0
        : title.StartsWith("Regional", StringComparison.OrdinalIgnoreCase) ? 1
        : title.StartsWith("Consolidated", StringComparison.OrdinalIgnoreCase) ? 2 : fallback;
    private static string StatementDescription(int index) => index == 0 ? "Host tax receipts, local social costs and the selected host share of displaced business sales."
        : index == 1 ? "Regional tax receipts compared with social costs in other assessed same-state counties."
        : "All modeled gaming-tax receipts compared with assessed social costs and business-sales displacement.";
    private static string Clean(string value) => value.Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2011', '-').Replace('\u2212', '-').Replace('\r', ' ');
    private static byte[]? DecodeMap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var data = value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value;
        try { return Convert.FromBase64String(data); } catch (FormatException) { return null; }
    }
}
