using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services.Reports;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/model-runs/{modelRunId:guid}/reports")]
public sealed class ModelRunReportsController(
    AppDbContext db,
    IReportArtifactService reportArtifactService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Generate(
        Guid modelRunId,
        [FromBody] ReportPresentationOptions? options,
        CancellationToken cancellationToken)
    {
        var artifact = await reportArtifactService.GetOrCreateAsync(
            modelRunId,
            options ?? new ReportPresentationOptions(),
            cancellationToken);
        return CreatedAtAction(nameof(GetMetadata), new { modelRunId, reportArtifactId = artifact.Id }, Metadata(artifact));
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid modelRunId, CancellationToken cancellationToken) =>
        Ok((await db.ModelRunReportArtifacts.AsNoTracking()
            .Where(artifact => artifact.ModelRunId == modelRunId)
            .OrderByDescending(artifact => artifact.GeneratedAtUtc)
            .ToListAsync(cancellationToken)).Select(Metadata));

    [HttpGet("{reportArtifactId:guid}")]
    public async Task<IActionResult> GetMetadata(
        Guid modelRunId,
        Guid reportArtifactId,
        CancellationToken cancellationToken)
    {
        var artifact = await Find(modelRunId, reportArtifactId, cancellationToken);
        return artifact is null ? NotFound() : Ok(Metadata(artifact));
    }

    [HttpGet("{reportArtifactId:guid}/html")]
    public async Task<IActionResult> GetHtml(Guid modelRunId, Guid reportArtifactId, CancellationToken cancellationToken)
    {
        var artifact = await Find(modelRunId, reportArtifactId, cancellationToken);
        return artifact is null ? NotFound() : Content(artifact.HtmlContent, "text/html", Encoding.UTF8);
    }

    [HttpGet("{reportArtifactId:guid}/pdf")]
    public async Task<IActionResult> GetPdf(Guid modelRunId, Guid reportArtifactId, CancellationToken cancellationToken)
    {
        var artifact = await Find(modelRunId, reportArtifactId, cancellationToken);
        return artifact is null
            ? NotFound()
            : File(artifact.PdfContent, "application/pdf", $"casino-impact-{modelRunId:D}.pdf");
    }

    [HttpGet("{reportArtifactId:guid}/json")]
    public async Task<IActionResult> GetJson(Guid modelRunId, Guid reportArtifactId, CancellationToken cancellationToken)
    {
        var artifact = await Find(modelRunId, reportArtifactId, cancellationToken);
        return artifact is null
            ? NotFound()
            : Content(artifact.ReportModelJson, "application/json", Encoding.UTF8);
    }

    [HttpGet("{reportArtifactId:guid}/csv")]
    public async Task<IActionResult> GetCsv(Guid modelRunId, Guid reportArtifactId, CancellationToken cancellationToken)
    {
        var artifact = await Find(modelRunId, reportArtifactId, cancellationToken);
        return artifact is null
            ? NotFound()
            : File(Encoding.UTF8.GetBytes(artifact.CsvContent), "text/csv", $"casino-impact-{modelRunId:D}.csv");
    }

    private Task<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact?> Find(
        Guid modelRunId,
        Guid reportArtifactId,
        CancellationToken cancellationToken) =>
        db.ModelRunReportArtifacts.AsNoTracking().SingleOrDefaultAsync(
            artifact => artifact.Id == reportArtifactId && artifact.ModelRunId == modelRunId,
            cancellationToken);

    private static object Metadata(SaveNEIN.Server.Data.Entities.ModelRunReportArtifact artifact) => new
    {
        artifact.Id,
        artifact.ModelRunId,
        artifact.TemplateVersion,
        artifact.PresentationOptionsJson,
        artifact.GeneratedAtUtc,
        artifact.ReportModelHash,
        artifact.HtmlContentHash,
        artifact.PdfContentHash,
        artifact.CsvContentHash,
        HtmlUrl = $"api/model-runs/{artifact.ModelRunId:D}/reports/{artifact.Id:D}/html",
        PdfUrl = $"api/model-runs/{artifact.ModelRunId:D}/reports/{artifact.Id:D}/pdf",
        JsonUrl = $"api/model-runs/{artifact.ModelRunId:D}/reports/{artifact.Id:D}/json",
        CsvUrl = $"api/model-runs/{artifact.ModelRunId:D}/reports/{artifact.Id:D}/csv"
    };
}
