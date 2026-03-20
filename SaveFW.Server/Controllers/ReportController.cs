using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace SaveFW.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public ReportController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateReport([FromBody] ReportRequest request)
        {
            // Enable debugging to find layout issues
            QuestPDF.Settings.EnableDebugging = true;

            try
            {
                // Try to find the logo
                byte[]? logoBytes = null;
                var possiblePaths = new[]
                {
                    Path.Combine(_env.ContentRootPath, "..", "SaveFW.Client", "wwwroot", "assets", "SAVEFW.jpg"),
                    Path.Combine(_env.ContentRootPath, "wwwroot", "assets", "SAVEFW.jpg"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "SAVEFW.jpg")
                };

                foreach (var path in possiblePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        try 
                        {
                            logoBytes = await System.IO.File.ReadAllBytesAsync(path);
                            break;
                        } 
                        catch { }
                    }
                }
                
                var brandColor = Color.FromHex("#0f172a");
            
                // Helper: shared footer across all page blocks
                void AddFooter(PageDescriptor page, byte[]? logo)
                {
                    page.Footer()
                        .Row(row => {
                            if (logo != null)
                            {
                                row.RelativeItem().AlignLeft().Height(0.8f, Unit.Centimetre).Image(logo).FitArea();
                            }
                            else
                            {
                                 row.RelativeItem().AlignLeft().Text("SaveFW.org").FontSize(10).FontColor(Colors.Grey.Medium);
                            }
                            
                            row.RelativeItem().AlignRight().Text(x =>
                            {
                                x.Span("Page ");
                                x.CurrentPageNumber();
                                x.Span(" of ");
                                x.TotalPages();
                            });
                        });
                }

                var document = Document.Create(container =>
                {
                    // === Pages 1-3: Cover, TOC, Map (Portrait A4) ===
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Column(col =>
                        {
                            col.Spacing(20);
                            
                            // Cover Page
                            col.Item().PaddingTop(2, Unit.Centimetre).AlignCenter().Text("Net Economic Impact Analysis").FontSize(28).Bold().FontColor(brandColor);
                            col.Item().AlignCenter().Text("Fort Wayne Casino Proposal").FontSize(18).SemiBold().FontColor(Colors.Grey.Darken1);
                            
                            if (logoBytes != null)
                            {
                                col.Item().Height(6, Unit.Centimetre).AlignCenter().Image(logoBytes).FitArea();
                            }
                            
                            col.Item().PaddingTop(4, Unit.Centimetre).AlignCenter().Column(c => 
                            {
                                 c.Item().Text($"Date: {DateTime.Now:MMMM d, yyyy}").FontSize(14);
                                 c.Item().Text("Prepared by: SaveFW Analytics").FontSize(14).Bold();
                            });
                            
                            col.Item().PageBreak();

                            // Table of Contents
                            col.Item().Text("Table of Contents").FontSize(24).Bold().FontColor(brandColor);
                            col.Item().PaddingTop(1, Unit.Centimetre).Column(toc => 
                            {
                                 toc.Spacing(10);
                                 toc.Item().Row(row => { row.RelativeItem().Text("1. Geographic Impact Map").FontSize(14); row.AutoItem().Text("3").FontSize(14); });
                                 toc.Item().Row(row => { row.RelativeItem().Text("2. Net Economic Impact Table").FontSize(14); row.AutoItem().Text("4").FontSize(14); });
                                 toc.Item().Row(row => { row.RelativeItem().Text("3. Detailed Cost Breakdown").FontSize(14); row.AutoItem().Text("5").FontSize(14); });
                                 toc.Item().Row(row => { row.RelativeItem().Text("4. Economic Analysis").FontSize(14); row.AutoItem().Text("6").FontSize(14); });
                            });
                            
                            col.Item().PageBreak();

                            // Map Page
                            col.Item().Text("1. Geographic Impact Map").FontSize(20).Bold().FontColor(brandColor);
                            
                            if (!string.IsNullOrEmpty(request.MapImageBase64))
                            {
                                try 
                                {
                                    var base64Data = request.MapImageBase64;
                                    if (base64Data.Contains(",")) base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
                                    var imageBytes = Convert.FromBase64String(base64Data);
                                    col.Item().PaddingVertical(1, Unit.Centimetre).MaxHeight(18, Unit.Centimetre).Image(imageBytes).FitArea();
                                }
                                catch (Exception)
                                {
                                    col.Item().Text("[Error processing map image]").FontColor(Colors.Red.Medium);
                                }
                            }
                            else 
                            {
                                 col.Item().Text("[Map Image Not Provided]");
                            }
                        });

                        AddFooter(page, logoBytes);
                    });

                    // === Page 4: Net Economic Impact Table (LANDSCAPE A4) ===
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(1.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Column(col =>
                        {
                            col.Spacing(10);

                            col.Item().Text("2. Net Economic Impact Table").FontSize(20).Bold().FontColor(brandColor);
                            
                            if (request.MainTable != null && request.MainTable.Rows != null && request.MainTable.Rows.Count > 0)
                            {
                                col.Item().PaddingTop(10).Table(table =>
                                {
                                    var colCount = request.MainTable.Headers?.Count ?? request.MainTable.Rows[0].Count;
                                    table.ColumnsDefinition(columns =>
                                    {
                                        // First column wider (Labels)
                                        columns.RelativeColumn(2.5f);
                                        for(int i=1; i < colCount; i++) columns.RelativeColumn();
                                    });

                                    // Header
                                    if (request.MainTable.Headers != null)
                                    {
                                        table.Header(header =>
                                        {
                                            foreach (var h in request.MainTable.Headers)
                                            {
                                                header.Cell().Element(CombinedHeaderStyle).Text(h);
                                            }
                                        });
                                    }

                                    // Rows
                                    foreach (var row in request.MainTable.Rows)
                                    {
                                        foreach (var cell in row)
                                        {
                                            bool isTotal = row[0].Contains("Total", StringComparison.OrdinalIgnoreCase) || row[0].Contains("Subtotal", StringComparison.OrdinalIgnoreCase);
                                            
                                            table.Cell().Element(c => CombinedCellStyle(c, isTotal)).Text(cell);
                                        }
                                    }
                                });
                            }
                            else
                            {
                                col.Item().Text("No table data available.");
                            }
                        });

                        AddFooter(page, logoBytes);
                    });

                    // === Pages 5+: Breakdown & Analysis (Portrait A4) ===
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Column(col =>
                        {
                            col.Spacing(20);

                            // Detailed Breakdown (Supplementary)
                            col.Item().Text("3. Detailed Cost Breakdown").FontSize(20).Bold().FontColor(brandColor);
                            col.Item().Text("Supplementary analysis of social costs per problem gambler.").FontSize(10).Italic().FontColor(Colors.Grey.Medium);

                            RenderCombinedBreakdownTable(col, request.BreakdownTable, request.BreakdownOtherTable, request.SubjectCountyName);

                            col.Item().PageBreak();

                            // Analysis Text
                            col.Item().Text("4. Economic Analysis").FontSize(20).Bold().FontColor(brandColor);
                            
                            if (!string.IsNullOrEmpty(request.AnalysisText))
                            {
                                RenderMarkdown(col, request.AnalysisText);
                            }
                        });

                        AddFooter(page, logoBytes);
                    });
                });

                var stream = new MemoryStream();
                document.GeneratePdf(stream);
                stream.Position = 0;

                return File(stream, "application/pdf", $"SaveFW_Report_{DateTime.Now:yyyyMMdd}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PDF Generation Error: {ex}");
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        private void RenderMarkdown(ColumnDescriptor col, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Determine indentation
                int spaces = 0;
                foreach (char c in line)
                {
                    if (c == ' ') spaces++;
                    else break;
                }

                var trimmed = line.Trim();
                if (trimmed.StartsWith("###"))
                {
                    // Section Header
                    col.Item().PaddingTop(10).PaddingBottom(5).Text(trimmed.Substring(3).Trim()).FontSize(14).Bold().Underline().FontColor(Colors.Blue.Darken2);
                }
                else if (trimmed.StartsWith("*"))
                {
                    // Bullet Point
                    int level = spaces / 2;
                    float indent = 10 + (level * 15);

                    col.Item().PaddingLeft(indent).PaddingBottom(2).Row(row =>
                    {
                        row.ConstantItem(15).Text("\u2022"); // Bullet
                        row.RelativeItem().Text(t => ParseInlineMarkdown(t, trimmed.Substring(1).Trim()));
                    });
                }
                else
                {
                    // Paragraph
                    col.Item().PaddingBottom(5).Text(t => ParseInlineMarkdown(t, trimmed));
                }
            }
        }

        private void ParseInlineMarkdown(TextDescriptor text, string content)
        {
            // Simple parser for **bold**
            var parts = content.Split("**");
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1) // Odd index = inside ** **
                {
                    text.Span(parts[i]).Bold();
                }
                else
                {
                    text.Span(parts[i]);
                }
            }
        }

        private void RenderCombinedBreakdownTable(ColumnDescriptor col, List<List<string>>? subjectData, List<List<string>>? otherData, string? subjectCountyName)
        {
            if ((subjectData == null || subjectData.Count == 0) && (otherData == null || otherData.Count == 0))
            {
                col.Item().Text("No data available.");
                return;
            }

            col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.8f); // Impact Area
                    columns.RelativeColumn(1); // Public Health
                    columns.RelativeColumn(1); // Social Services
                    columns.RelativeColumn(1); // Law Enforcement
                    columns.RelativeColumn(1); // Civil Legal
                    columns.RelativeColumn(1); // Abused Dollars
                    columns.RelativeColumn(1); // Lost Employment
                    columns.RelativeColumn(1.2f); // Total
                });

                table.Header(header =>
                {
                    header.Cell().Element(CombinedHeaderStyle).Text("Impact Area");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Pub. Health");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Soc. Svcs");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Law Enf.");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Civil Legal");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Abused $");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Lost Emp.");
                    header.Cell().Element(CombinedHeaderStyle).AlignRight().Text("Total");
                });

                // Row 1: Subject County (Extracted from Category-based rows)
                if (subjectData != null && subjectData.Count >= 7)
                {
                    string name = !string.IsNullOrWhiteSpace(subjectCountyName) ? subjectCountyName : "Subject County";
                    table.Cell().Element(c => CombinedCellStyle(c)).Text(name).Bold();
                    
                    // subjectData indices: 0=PH, 1=SS, 2=Law, 3=Legal, 4=Abused, 5=Emp, 6=Total
                    // Data row structure: [Category, Victims, Per, Total] -> Index 3 is Total Cost
                    for (int i = 0; i < 6; i++) 
                        table.Cell().Element(c => CombinedCellStyle(c)).AlignRight().Text(subjectData[i].Count > 3 ? subjectData[i][3] : "-");
                    
                    // Total Column (Index 6 in data)
                    table.Cell().Element(c => CombinedCellStyle(c, true)).AlignRight().Text(subjectData[6].Count > 3 ? subjectData[6][3] : "-");
                }

                // Other Rows: Regional Spillover (Counties)
                // New format: [Name, PH, SS, Law, Legal, Abused, Emp, Total]
                if (otherData != null)
                {
                    foreach (var row in otherData)
                    {
                        if (row.Count < 8) continue; 
                        
                        table.Cell().Element(c => CombinedCellStyle(c)).Text(row[0]); // Name
                        for (int i = 1; i <= 7; i++)
                        {
                             bool isTotalCol = (i == 7);
                             table.Cell().Element(c => CombinedCellStyle(c, isTotalCol)).AlignRight().Text(row[i]);
                        }
                    }
                }
            });
        }

        static IContainer CombinedHeaderStyle(IContainer container)
        {
            return container.Background(Colors.Blue.Darken3).Padding(5).DefaultTextStyle(x => x.SemiBold().FontColor(Colors.White).FontSize(8));
        }

        static IContainer CombinedCellStyle(IContainer container, bool isTotal = false)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).DefaultTextStyle(x => x.FontSize(9).Weight(isTotal ? FontWeight.Bold : FontWeight.Normal));
        }

        static IContainer HeaderCellStyle(IContainer container)
        {
            return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
        }

        static IContainer CellStyle(IContainer container, bool isTotal = false)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).DefaultTextStyle(x => isTotal ? x.Bold() : x);
        }
    }

    public class ReportRequest
    {
        public string? SubjectCountyName { get; set; }
        public string? MapImageBase64 { get; set; }
        public string? AnalysisText { get; set; }
        public TableData? MainTable { get; set; }
        public List<List<string>>? BreakdownTable { get; set; }
        public List<List<string>>? BreakdownOtherTable { get; set; }
    }

    public class TableData
    {
        public List<string>? Headers { get; set; }
        public List<List<string>>? Rows { get; set; }
    }
}
