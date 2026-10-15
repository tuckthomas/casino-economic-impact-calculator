// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/gravity-model-configuration/facility-input-availability")]
public sealed class FacilityInputAvailabilityController(AppDbContext db, ICompetitiveUniverseService universe) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Get([FromBody] FacilityInputAvailabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.StableOriginIds is not { Length: > 0 } || request.StableOriginIds.Distinct(StringComparer.Ordinal).Count() != request.StableOriginIds.Length ||
            !double.IsFinite(request.CompetitorPrefilterMiles) || request.CompetitorPrefilterMiles is <= 0 or > 2000)
            return BadRequest("Select a complete, unique origin set and a positive competitor radius.");
        var snapshots = await db.DatasetSnapshots.AsNoTracking().Where(item =>
            (item.Id == request.CompetitorSnapshotId || item.Id == request.OriginGeographySnapshotId) &&
            item.IsSealed && item.ValidationState != DatasetValidationStates.Rejected).ToArrayAsync(cancellationToken);
        if (!snapshots.Any(item => item.Id == request.CompetitorSnapshotId && item.DatasetKey == DatasetSnapshotKinds.Competitors) ||
            !snapshots.Any(item => item.Id == request.OriginGeographySnapshotId && item.DatasetKey == DatasetSnapshotKinds.OriginGeography))
            return BadRequest("Select sealed, usable origin and competitor data.");
        var program = await db.DevelopmentPrograms.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.DevelopmentProgramId, cancellationToken);
        if (program is null) return BadRequest("Select an available development program.");
        var origins = await db.OriginZones.AsNoTracking().Where(item => item.DatasetSnapshotId == request.OriginGeographySnapshotId &&
            request.StableOriginIds.Contains(item.StableOriginId)).ToArrayAsync(cancellationToken);
        if (origins.Length != request.StableOriginIds.Length) return BadRequest("The selected origins are absent from this snapshot.");
        var selected = await universe.SelectAsync(request.CompetitorSnapshotId, origins, request.EffectiveOn,
            request.CompetitorPrefilterMiles, request.CompetitorIds ?? [], cancellationToken);
        var competitors = selected.Where(item => !(request.ExcludedCompetitorIds ?? []).Contains(item.Id)).ToArray();
        if (competitors.Length == 0) return BadRequest("No competitors are available for this study area.");
        try { return Ok(FacilityInputSelection.Availability(program, competitors, request.AttractionSpecification)); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
    }
}
