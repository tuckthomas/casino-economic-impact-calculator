using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/casino-competitors")]
public sealed class CasinoCompetitorsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetActive(
        Guid? datasetSnapshotId,
        CancellationToken cancellationToken)
    {
        var snapshot = datasetSnapshotId.HasValue
            ? await db.DatasetSnapshots.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == datasetSnapshotId.Value &&
                        item.DatasetKey == DatasetSnapshotKinds.Competitors &&
                        item.IsSealed &&
                        item.ValidationState != DatasetValidationStates.Pending &&
                        item.ValidationState != DatasetValidationStates.Rejected,
                cancellationToken)
            : await db.DatasetSnapshots.AsNoTracking()
                .Where(item => item.DatasetKey == DatasetSnapshotKinds.Competitors &&
                               item.IsSealed &&
                               item.ValidationState != DatasetValidationStates.Pending &&
                               item.ValidationState != DatasetValidationStates.Rejected)
                .OrderByDescending(item => item.IngestedAtUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null && datasetSnapshotId.HasValue)
        {
            return NotFound($"Usable competitor snapshot '{datasetSnapshotId}' was not found.");
        }
        if (snapshot is null)
        {
            return Ok(new { DatasetSnapshotId = (Guid?)null, SnapshotPeriod = (string?)null, Competitors = Array.Empty<object>() });
        }

        var competitors = await db.CasinoCompetitors
            .AsNoTracking()
            .Where(competitor => competitor.DatasetSnapshotId == snapshot.Id && competitor.IsActive)
            .Select(competitor => new
            {
                competitor.Id,
                competitor.StableVenueId,
                competitor.Name,
                competitor.VenueType,
                competitor.FacilityRegime,
                competitor.OperatorName,
                competitor.Latitude,
                competitor.Longitude
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            DatasetSnapshotId = snapshot.Id,
            SnapshotPeriod = snapshot.Period,
            Competitors = competitors
        });
    }
}
