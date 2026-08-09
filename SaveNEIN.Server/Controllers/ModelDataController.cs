using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

public sealed record SealDatasetSnapshotBody(
    string ValidationState,
    IReadOnlyCollection<string>? Warnings,
    IReadOnlyCollection<string>? Errors);

[ApiController]
[Route("api/model-data")]
public sealed class ModelDataController(
    AppDbContext db,
    IDataSnapshotService snapshots,
    IModelDataIngestionService ingestion) : ControllerBase
{
    [HttpPost("sources")]
    public async Task<IActionResult> RegisterSource(
        [FromBody] RegisterDataSourceRequest request,
        CancellationToken cancellationToken)
    {
        var source = await snapshots.RegisterSourceAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetSource), new { dataSourceId = source.Id }, source);
    }

    [HttpGet("sources/{dataSourceId:long}")]
    public async Task<IActionResult> GetSource(long dataSourceId, CancellationToken cancellationToken)
    {
        var source = await db.DataSources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == dataSourceId, cancellationToken);
        return source is null ? NotFound() : Ok(source);
    }

    [HttpPost("snapshots")]
    public async Task<IActionResult> BeginSnapshot(
        [FromBody] BeginDatasetSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.BeginSnapshotAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetSnapshot), new { datasetSnapshotId = snapshot.Id }, snapshot);
    }

    [HttpGet("snapshots/{datasetSnapshotId:guid}")]
    public async Task<IActionResult> GetSnapshot(Guid datasetSnapshotId, CancellationToken cancellationToken)
    {
        var snapshot = await db.DatasetSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == datasetSnapshotId, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }
        var counts = await ingestion.GetRowCountsAsync(datasetSnapshotId, cancellationToken);
        return Ok(new { Snapshot = snapshot, RowCounts = counts });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/origins")]
    public async Task<IActionResult> AppendOrigins(
        Guid datasetSnapshotId,
        [FromBody] IReadOnlyCollection<OriginZoneImportRow> rows,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendOriginsAsync(datasetSnapshotId, rows, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/age-bins")]
    public async Task<IActionResult> AppendAgeBins(
        Guid datasetSnapshotId,
        [FromBody] OriginAgeBinImportRequest request,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendAgeBinsAsync(datasetSnapshotId, request, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/income-periods")]
    public async Task<IActionResult> AppendIncome(
        Guid datasetSnapshotId,
        [FromBody] OriginIncomeImportRequest request,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendIncomeAsync(datasetSnapshotId, request, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/competitors")]
    public async Task<IActionResult> AppendCompetitors(
        Guid datasetSnapshotId,
        [FromBody] IReadOnlyCollection<CasinoCompetitorImportRow> rows,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendCompetitorsAsync(datasetSnapshotId, rows, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/competitor-history")]
    public async Task<IActionResult> AppendCompetitorHistory(
        Guid datasetSnapshotId,
        [FromBody] CasinoCompetitorHistoryImportRequest request,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendCompetitorHistoryAsync(datasetSnapshotId, request, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/gaming-revenue")]
    public async Task<IActionResult> AppendGamingRevenue(
        Guid datasetSnapshotId,
        [FromBody] CasinoGamingRevenueImportRequest request,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendGamingRevenueAsync(datasetSnapshotId, request, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/seal")]
    public async Task<IActionResult> SealSnapshot(
        Guid datasetSnapshotId,
        [FromBody] SealDatasetSnapshotBody body,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.SealSnapshotAsync(
            new SealDatasetSnapshotRequest(datasetSnapshotId, body.ValidationState, body.Warnings, body.Errors),
            cancellationToken);
        var counts = await ingestion.GetRowCountsAsync(datasetSnapshotId, cancellationToken);
        return Ok(new { Snapshot = snapshot, RowCounts = counts });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/tourism-observations")]
    public async Task<IActionResult> AppendTourismObservations(
        Guid datasetSnapshotId,
        [FromBody] IReadOnlyCollection<TourismMarketObservationImportRow> rows,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendTourismObservationsAsync(datasetSnapshotId, rows, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/traffic-observations")]
    public async Task<IActionResult> AppendTrafficObservations(
        Guid datasetSnapshotId,
        [FromBody] IReadOnlyCollection<TrafficCorridorObservationImportRow> rows,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendTrafficObservationsAsync(datasetSnapshotId, rows, cancellationToken);
        return Ok(new { Inserted = inserted });
    }

    [HttpPost("snapshots/{datasetSnapshotId:guid}/local-economic-sector-observations")]
    public async Task<IActionResult> AppendLocalEconomicSectorObservations(
        Guid datasetSnapshotId,
        [FromBody] IReadOnlyCollection<LocalEconomicSectorObservationImportRow> rows,
        CancellationToken cancellationToken)
    {
        var inserted = await ingestion.AppendLocalEconomicSectorObservationsAsync(
            datasetSnapshotId,
            rows,
            cancellationToken);
        return Ok(new { Inserted = inserted });
    }
}
