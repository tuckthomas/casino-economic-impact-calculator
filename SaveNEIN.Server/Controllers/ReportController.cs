using Microsoft.AspNetCore.Mvc;
using SaveNEIN.Server.Services.Reports;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController(IWebHostEnvironment env) : ControllerBase
    {
        [HttpPost("generate")]
        [RequestSizeLimit(12_000_000)]
        public async Task<IActionResult> GenerateReport([FromBody] ReportRequest request)
        {
            try
            {
                async Task<byte[]?> ReadLogo(string name)
                {
                    var roots = new[] { env.WebRootPath, Path.Combine(env.ContentRootPath, "wwwroot"),
                        Path.Combine(env.ContentRootPath, "..", "SaveNEIN.Client", "wwwroot") };
                    foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
                    {
                        var path = Path.Combine(root!, "assets", name);
                        if (System.IO.File.Exists(path)) return await System.IO.File.ReadAllBytesAsync(path);
                    }
                    return null;
                }
                var darkLogo = await ReadLogo("SaveNEIN_Dark_Mode.png") ?? await ReadLogo("SAVENEIN.png");
                var bytes = LegacyEconomicReportDocument.Render(request, darkLogo, DateTime.Now);
                return File(bytes, "application/pdf", $"SaveNEIN_Report_{DateTime.Now:yyyyMMdd}.pdf");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PDF Generation Error: {ex}");
                return StatusCode(500, new { error = "The report could not be generated. Please try again." });
            }
        }
    }

    public class ReportRequest
    {
        public string? SubjectCountyName { get; set; }
        public Dictionary<string, string>? ScenarioContext { get; set; }
        public LocalEconomyReport? LocalEconomy { get; set; }
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
