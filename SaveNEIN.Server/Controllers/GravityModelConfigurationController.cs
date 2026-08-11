// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/gravity-model-configuration")]
public sealed class GravityModelConfigurationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var jurisdictions = await db.Jurisdictions
            .AsNoTracking()
            .Where(jurisdiction => jurisdiction.IsActive)
            .OrderBy(jurisdiction => jurisdiction.Kind)
            .ThenBy(jurisdiction => jurisdiction.Name)
            .Select(jurisdiction => new
            {
                jurisdiction.Id,
                jurisdiction.Code,
                jurisdiction.Name,
                jurisdiction.Kind,
                jurisdiction.ParentJurisdictionId
            })
            .ToListAsync(cancellationToken);
        var snapshots = await db.DatasetSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.IsSealed &&
                               snapshot.ValidationState != DatasetValidationStates.Rejected)
            .Join(
                db.DataSources.AsNoTracking(),
                snapshot => snapshot.DataSourceId,
                source => source.Id,
                (snapshot, source) => new { Snapshot = snapshot, Source = source })
            .OrderBy(item => item.Snapshot.DatasetKey)
            .ThenByDescending(item => item.Snapshot.PeriodEnd)
            .ThenByDescending(item => item.Snapshot.IngestedAtUtc)
            .Select(item => new
            {
                item.Snapshot.Id,
                item.Snapshot.DatasetKey,
                item.Snapshot.Period,
                item.Snapshot.PeriodStart,
                item.Snapshot.PeriodEnd,
                item.Snapshot.RowCount,
                item.Snapshot.ValidationState,
                item.Snapshot.WarningsJson,
                item.Snapshot.TransformVersion,
                item.Snapshot.Checksum,
                SourceName = item.Source.Name,
                SourcePublisher = item.Source.Publisher,
                SourceVintage = item.Source.VintagePeriod
            })
            .ToListAsync(cancellationToken);
        var programs = await db.DevelopmentPrograms
            .AsNoTracking()
            .OrderByDescending(program => program.CreatedAtUtc)
            .Select(program => new
            {
                program.Id,
                program.StableProgramId,
                program.Version,
                program.Name,
                program.SlotOrVltPositions,
                program.TableGameCount,
                program.HotelRoomCount,
                program.EventCapacity,
                program.CapitalCost,
                program.PlannedOpeningDate,
                program.StabilizedYearNumber,
                program.IsImmutable
            })
            .ToListAsync(cancellationToken);
        var parameterDefinitions = await db.ModelParameterDefinitions
            .AsNoTracking()
            .Where(definition => definition.IsActive && definition.ModelVersionApplicability == "gravity-v1")
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.DisplayName)
            .Select(definition => new
            {
                definition.Key,
                definition.Category,
                definition.DisplayName,
                definition.TechnicalDescription,
                definition.PlainLanguageDescription,
                definition.Units,
                definition.SystemDefaultValue,
                definition.ComputationalMinimum,
                definition.ComputationalMaximum,
                definition.RecommendedMinimum,
                definition.RecommendedMaximum,
                definition.UiStep,
                definition.UiExposureLevel,
                definition.IsUserOverridable,
                definition.IsCalibrated,
                definition.ProvenanceNotes
            })
            .ToListAsync(cancellationToken);
        var parameterSets = await db.ModelParameterSets
            .AsNoTracking()
            .Where(set => set.ModelVersionApplicability == "gravity-v1")
            .OrderBy(set => set.Scope)
            .ThenBy(set => set.Name)
            .Select(set => new
            {
                set.Id,
                set.Key,
                set.Name,
                set.Scope,
                set.JurisdictionId,
                set.MarketCode,
                set.ScenarioKind,
                set.Version,
                set.CalibrationNotes
            })
            .ToListAsync(cancellationToken);
        var parameterSetValues = await db.ModelParameterSetValues
            .AsNoTracking()
            .Join(
                db.ModelParameterDefinitions.AsNoTracking(),
                value => value.ParameterDefinitionId,
                definition => definition.Id,
                (value, definition) => new
                {
                    value.ParameterSetId,
                    ParameterKey = definition.Key,
                    value.Value,
                    value.ProvenanceNotes
                })
            .OrderBy(value => value.ParameterSetId)
            .ThenBy(value => value.ParameterKey)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            ModelVersion = "gravity-v1",
            Jurisdictions = jurisdictions,
            Snapshots = snapshots,
            DevelopmentPrograms = programs,
            ParameterDefinitions = parameterDefinitions,
            ParameterSets = parameterSets,
            ParameterSetValues = parameterSetValues
        });
    }

    [HttpGet("origins")]
    public async Task<IActionResult> GetOrigins(
        Guid datasetSnapshotId,
        string? stateCode = null,
        string? countyCode = null,
        string? msaCode = null,
        int skip = 0,
        int take = 5_000,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 10_000)
        {
            return BadRequest("skip must be nonnegative and take must be between 1 and 10,000.");
        }
        var snapshotValid = await db.DatasetSnapshots.AsNoTracking().AnyAsync(
            snapshot => snapshot.Id == datasetSnapshotId &&
                        snapshot.DatasetKey == DatasetSnapshotKinds.OriginGeography &&
                        snapshot.IsSealed &&
                        snapshot.ValidationState != DatasetValidationStates.Rejected,
            cancellationToken);
        if (!snapshotValid)
        {
            return BadRequest("The selected origin-geography snapshot is not sealed and usable.");
        }

        var query = db.OriginZones.AsNoTracking()
            .Where(origin => origin.DatasetSnapshotId == datasetSnapshotId);
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            query = query.Where(origin => origin.StateOrTerritoryCode == stateCode);
        }
        if (!string.IsNullOrWhiteSpace(countyCode))
        {
            query = query.Where(origin => origin.CountyEquivalentCode == countyCode);
        }
        if (!string.IsNullOrWhiteSpace(msaCode))
        {
            query = query.Where(origin => origin.MetropolitanStatisticalAreaCode == msaCode);
        }
        var total = await query.CountAsync(cancellationToken);
        var origins = await query
            .OrderBy(origin => origin.StateOrTerritoryCode)
            .ThenBy(origin => origin.CountyEquivalentCode)
            .ThenBy(origin => origin.StableOriginId)
            .Skip(skip)
            .Take(take)
            .Select(origin => new
            {
                origin.StableOriginId,
                origin.OriginType,
                origin.GeographyCode,
                origin.StateOrTerritoryCode,
                origin.CountyEquivalentCode,
                origin.MetropolitanStatisticalAreaCode,
                Latitude = origin.RepresentativePoint.Y,
                Longitude = origin.RepresentativePoint.X
            })
            .ToListAsync(cancellationToken);
        return Ok(new { Total = total, Skip = skip, Take = take, Origins = origins });
    }

    [HttpGet("tourism-observations")]
    public async Task<IActionResult> GetTourismObservations(
        Guid datasetSnapshotId,
        CancellationToken cancellationToken) =>
        Ok(await db.TourismMarketObservations
            .AsNoTracking()
            .Where(observation => observation.DatasetSnapshotId == datasetSnapshotId)
            .OrderBy(observation => observation.MarketKey)
            .ThenBy(observation => observation.StableObservationId)
            .Select(observation => new
            {
                observation.StableObservationId,
                observation.MarketKey,
                observation.GeographyType,
                observation.GeographyCode,
                observation.PeriodStart,
                observation.PeriodEnd,
                observation.NormalizedVisitorPersonTrips,
                observation.NormalizationMethod
            })
            .ToListAsync(cancellationToken));

    [HttpGet("traffic-observations")]
    public async Task<IActionResult> GetTrafficObservations(
        Guid datasetSnapshotId,
        CancellationToken cancellationToken) =>
        Ok(await db.TrafficCorridorObservations
            .AsNoTracking()
            .Where(observation => observation.DatasetSnapshotId == datasetSnapshotId)
            .OrderBy(observation => observation.RouteDesignation)
            .ThenBy(observation => observation.StableObservationId)
            .Select(observation => new
            {
                observation.StableObservationId,
                observation.RouteDesignation,
                observation.JurisdictionCode,
                observation.PeriodStart,
                observation.PeriodEnd,
                observation.AnnualAverageDailyTraffic,
                observation.ObservationDays,
                Latitude = observation.CountLocation.Y,
                Longitude = observation.CountLocation.X
            })
            .ToListAsync(cancellationToken));
}
