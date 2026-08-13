// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services.Gravity;
using System.Text.Json;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/gravity-model-runs")]
public sealed class GravityModelRunsController(
    AppDbContext db,
    IGravityModelExecutionService executionService,
    IOriginSummaryService originSummaryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100)
        {
            return BadRequest("take must be between 1 and 100.");
        }
        var runs = await db.ModelRuns
            .AsNoTracking()
            .OrderByDescending(run => run.CreatedAtUtc)
            .Take(take)
            .Select(run => new
            {
                run.Id,
                run.ModelVersion,
                run.Status,
                run.JurisdictionId,
                run.DevelopmentProgramId,
                run.CandidateLatitude,
                run.CandidateLongitude,
                run.CreatedAtUtc,
                run.FinalizedAtUtc,
                run.WarningSummary,
                run.ResolvedInputJson
            })
            .ToListAsync(cancellationToken);
        return Ok(runs.Select(run => new
        {
            run.Id,
            run.ModelVersion,
            run.Status,
            run.JurisdictionId,
            run.DevelopmentProgramId,
            run.CandidateLatitude,
            run.CandidateLongitude,
            run.CreatedAtUtc,
            run.FinalizedAtUtc,
            run.WarningSummary,
            ScenarioName = ReadScenarioName(run.ResolvedInputJson)
        }));
    }

    [HttpPost]
    public async Task<ActionResult<GravityModelRunResult>> Execute(
        [FromBody] GravityModelRunRequest request,
        CancellationToken cancellationToken)
    {
        var result = await executionService.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { modelRunId = result.ModelRunId }, result);
    }

    [HttpGet("{modelRunId:guid}")]
    public async Task<IActionResult> Get(Guid modelRunId, CancellationToken cancellationToken)
    {
        var run = await db.ModelRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == modelRunId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var facilities = await db.ModelRunFacilityResults
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .OrderByDescending(result => result.IsProposedFacility)
            .ThenBy(result => result.FacilityKey)
            .Select(result => new
            {
                result.FacilityKey,
                result.FacilityKind,
                result.IsProposedFacility,
                result.NormalizedAttraction,
                result.BaselineResidentGgr,
                result.WithProjectResidentGgr,
                result.ChangeInResidentGgr,
                result.InducedResidentGgr,
                result.TotalWithProjectResidentGgr,
                result.TourismGgr,
                result.TrafficGgr,
                result.StabilizedTotalGgr
            })
            .ToListAsync(cancellationToken);
        var originSummary = await db.ModelRunOriginResults
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                OriginCount = group.Count(),
                ResidentDemand = group.Sum(result => result.ResidentDemand),
                InducedResidentDemand = group.Sum(result => result.InducedResidentDemand),
                ProposedResidentGgr = group.Sum(result => result.ProposedResidentGgr),
                ProposedInducedResidentGgr = group.Sum(result => result.ProposedInducedResidentGgr),
                TotalProposedResidentGgr = group.Sum(result => result.TotalProposedResidentGgr),
                HostJurisdictionCapture = group.Sum(result => result.HostJurisdictionCapture),
                ExternalJurisdictionCapture = group.Sum(result => result.ExternalJurisdictionCapture),
                TribalOrOtherJurisdictionCapture = group.Sum(result => result.TribalOrOtherJurisdictionCapture),
                OutsideOptionCapture = group.Sum(result => result.OutsideOptionCapture)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var demandComponents = await db.ModelRunDemandComponents
            .AsNoTracking()
            .Where(component => component.ModelRunId == modelRunId)
            .OrderBy(component => component.ComponentType)
            .ThenBy(component => component.SourceRecordKey)
            .ToListAsync(cancellationToken);
        var capacity = await db.ModelRunCapacityDiagnostics
            .AsNoTracking()
            .SingleOrDefaultAsync(diagnostic => diagnostic.ModelRunId == modelRunId, cancellationToken);
        var ramp = await db.ModelRunRampResults
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .OrderBy(result => result.CalendarYear)
            .ToListAsync(cancellationToken);
        var geographicAccounting = await db.ModelRunGeographicAccounting
            .AsNoTracking()
            .SingleOrDefaultAsync(result => result.ModelRunId == modelRunId, cancellationToken);
        var sectorDisplacement = await db.ModelRunSectorDisplacement
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .OrderByDescending(result => result.DisplacedSales)
            .ThenBy(result => result.SectorKey)
            .ToListAsync(cancellationToken);
        var employment = await db.ModelRunEmploymentImpacts
            .AsNoTracking()
            .SingleOrDefaultAsync(result => result.ModelRunId == modelRunId, cancellationToken);
        var fiscal = await db.ModelRunFiscalImpacts
            .AsNoTracking()
            .SingleOrDefaultAsync(result => result.ModelRunId == modelRunId, cancellationToken);
        var socialCosts = await db.ModelRunSocialCosts
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .OrderByDescending(result => result.AnnualCost)
            .ThenBy(result => result.DomainKey)
            .ToListAsync(cancellationToken);
        var netImpact = await db.ModelRunNetImpacts
            .AsNoTracking()
            .SingleOrDefaultAsync(result => result.ModelRunId == modelRunId, cancellationToken);

        return Ok(new
        {
            run.Id,
            run.ModelVersion,
            run.Status,
            run.JurisdictionId,
            run.DevelopmentProgramId,
            run.CandidateLatitude,
            run.CandidateLongitude,
            run.CreatedAtUtc,
            run.FinalizedAtUtc,
            run.ExecutionDuration,
            run.WarningSummary,
            run.ErrorSummary,
            run.ResolvedInputJson,
            OriginSummary = originSummary,
            Facilities = facilities,
            DemandComponents = demandComponents,
            Capacity = capacity,
            Ramp = ramp,
            GeographicAccounting = geographicAccounting,
            SectorDisplacement = sectorDisplacement,
            Employment = employment,
            Fiscal = fiscal,
            SocialCosts = socialCosts,
            NetImpact = netImpact
        });
    }

    [HttpGet("{modelRunId:guid}/origins")]
    public async Task<IActionResult> GetOrigins(
        Guid modelRunId,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 1_000)
        {
            return BadRequest("skip must be non-negative and take must be between 1 and 1000.");
        }
        if (!await db.ModelRuns.AsNoTracking().AnyAsync(run => run.Id == modelRunId, cancellationToken))
        {
            return NotFound();
        }

        var total = await db.ModelRunOriginResults
            .AsNoTracking()
            .CountAsync(result => result.ModelRunId == modelRunId, cancellationToken);
        var origins = await db.ModelRunOriginResults
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .Join(
                db.OriginZones.AsNoTracking(),
                result => result.OriginZoneId,
                origin => origin.Id,
                (result, origin) => new
                {
                    origin.StableOriginId,
                    origin.OriginType,
                    origin.GeographyCode,
                    origin.StateOrTerritoryCode,
                    origin.CountyEquivalentCode,
                    result.DemandSpecification,
                    result.ResidentDemand,
                    result.BaselineLogAccessibility,
                    result.WithProjectLogAccessibility,
                    result.InducedResidentDemand,
                    result.InducedOutsideOptionGgr,
                    result.BaselineOutsideShare,
                    result.WithProjectOutsideShare,
                    result.ProposedResidentGgr,
                    result.ProposedInducedResidentGgr,
                    result.TotalProposedResidentGgr,
                    result.HostJurisdictionCapture,
                    result.ExternalJurisdictionCapture,
                    result.TribalOrOtherJurisdictionCapture,
                    result.OutsideOptionCapture
                })
            .OrderByDescending(result => result.ProposedResidentGgr)
            .ThenBy(result => result.StableOriginId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return Ok(new { Total = total, Skip = skip, Take = take, Origins = origins });
    }

    [HttpGet("{modelRunId:guid}/origin-summaries")]
    public async Task<IActionResult> GetOriginSummary(
        Guid modelRunId,
        string dimension = OriginSummaryDimensions.Zcta,
        int top = 25,
        decimal minimumShare = 0.001m,
        CancellationToken cancellationToken = default)
    {
        if (!OriginSummaryDimensions.Supported.Contains(dimension))
        {
            return BadRequest(
                $"Unsupported dimension '{dimension}'. Supported dimensions: {string.Join(", ", OriginSummaryDimensions.Supported.Order())}.");
        }
        if (top is < 1 or > 100)
        {
            return BadRequest("top must be between 1 and 100.");
        }
        if (minimumShare is < 0 or > 1)
        {
            return BadRequest("minimumShare must be between 0 and 1.");
        }

        var runContext = await db.ModelRuns
            .AsNoTracking()
            .Where(run => run.Id == modelRunId)
            .Join(
                db.Jurisdictions.AsNoTracking(),
                run => run.JurisdictionId,
                jurisdiction => jurisdiction.Id,
                (run, jurisdiction) => new { Run = run, Jurisdiction = jurisdiction })
            .SingleOrDefaultAsync(cancellationToken);
        if (runContext is null)
        {
            return NotFound();
        }

        var sourceRows = await db.ModelRunOriginResults
            .AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .Join(
                db.OriginZones.AsNoTracking(),
                result => result.OriginZoneId,
                origin => origin.Id,
                (result, origin) => new OriginSummaryMaterializedRow(
                    new OriginSummarySourceRow(
                        origin.Id,
                        origin.StableOriginId,
                        origin.OriginType,
                        origin.GeographyCode,
                        origin.CountryCode,
                        origin.StateOrTerritoryCode,
                        origin.CountyEquivalentCode,
                        origin.MetropolitanStatisticalAreaCode,
                        origin.CombinedStatisticalAreaCode,
                        result.ResidentDemand,
                        result.InducedResidentDemand,
                        result.ProposedResidentGgr,
                        result.ProposedInducedResidentGgr,
                        result.TotalProposedResidentGgr,
                        result.HostJurisdictionCapture,
                        result.ExternalJurisdictionCapture,
                        result.TribalOrOtherJurisdictionCapture,
                        result.OutsideOptionCapture),
                    origin.AreaGeometry))
            .ToListAsync(cancellationToken);
        if (sourceRows.Count == 0)
        {
            return Conflict("The stored run does not contain origin results to summarize.");
        }
        if (string.Equals(dimension, OriginSummaryDimensions.Zcta, StringComparison.OrdinalIgnoreCase) &&
            sourceRows.Any(row => !string.Equals(row.Source.OriginType, "zcta", StringComparison.OrdinalIgnoreCase) &&
                                  !string.Equals(row.Source.OriginType, "zip-compatible", StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(
                "A ZIP/ZCTA summary cannot be synthesized from this run's non-ZCTA computational origins without a versioned crosswalk.");
        }

        var accounting = await db.ModelRunGeographicAccounting
            .AsNoTracking()
            .SingleOrDefaultAsync(result => result.ModelRunId == modelRunId, cancellationToken);
        var localStableOriginIds = ReadStableOriginIds(accounting?.LocalOriginIdsJson);
        var inJurisdictionOriginZoneIds = sourceRows
            .Where(row => localStableOriginIds.Contains(row.Source.StableOriginId))
            .Select(row => row.Source.OriginZoneId)
            .ToHashSet();

        var candidate = new Point(runContext.Run.CandidateLongitude, runContext.Run.CandidateLatitude) { SRID = 4326 };
        var hostOrigin = sourceRows
            .Where(row => SafelyCovers(row.AreaGeometry, candidate))
            .OrderBy(row => row.AreaGeometry.Area)
            .ThenBy(row => row.Source.StableOriginId, StringComparer.Ordinal)
            .Select(row => row.Source)
            .FirstOrDefault();
        var jurisdictionStateCode = string.Equals(runContext.Jurisdiction.Kind, "state", StringComparison.OrdinalIgnoreCase)
            ? runContext.Jurisdiction.Code.Split('-', StringSplitOptions.RemoveEmptyEntries)[^1]
            : null;
        var hostCountryCode = hostOrigin?.CountryCode ??
                              (runContext.Jurisdiction.Code.StartsWith("US", StringComparison.OrdinalIgnoreCase)
                                  ? "USA"
                                  : null);
        var context = new OriginSummaryContext(
            hostCountryCode,
            hostOrigin?.StateOrTerritoryCode ?? jurisdictionStateCode,
            hostOrigin?.CountyEquivalentCode,
            hostOrigin?.MetropolitanStatisticalAreaCode,
            hostOrigin?.CombinedStatisticalAreaCode,
            inJurisdictionOriginZoneIds);
        return Ok(originSummaryService.Summarize(
            sourceRows.Select(row => row.Source).ToArray(),
            context,
            new OriginSummaryOptions(dimension, top, minimumShare)));
    }

    [HttpGet("comparison")]
    public async Task<IActionResult> Compare(
        [FromQuery] Guid[] modelRunIds,
        CancellationToken cancellationToken)
    {
        var ids = modelRunIds.Distinct().ToArray();
        if (ids.Length is < 2 or > 10)
        {
            return BadRequest("Select between 2 and 10 distinct model runs.");
        }
        var runs = await db.ModelRuns.AsNoTracking()
            .Where(run => ids.Contains(run.Id))
            .ToListAsync(cancellationToken);
        if (runs.Count != ids.Length)
        {
            return NotFound("One or more model runs were not found.");
        }
        var proposedFacilities = await db.ModelRunFacilityResults.AsNoTracking()
            .Where(result => ids.Contains(result.ModelRunId) && result.IsProposedFacility)
            .ToDictionaryAsync(result => result.ModelRunId, cancellationToken);
        var employment = await db.ModelRunEmploymentImpacts.AsNoTracking()
            .Where(result => ids.Contains(result.ModelRunId))
            .ToDictionaryAsync(result => result.ModelRunId, cancellationToken);
        var fiscal = await db.ModelRunFiscalImpacts.AsNoTracking()
            .Where(result => ids.Contains(result.ModelRunId))
            .ToDictionaryAsync(result => result.ModelRunId, cancellationToken);
        var social = await db.ModelRunSocialCosts.AsNoTracking()
            .Where(result => ids.Contains(result.ModelRunId))
            .GroupBy(result => result.ModelRunId)
            .Select(group => new { ModelRunId = group.Key, AnnualCost = group.Sum(result => result.AnnualCost) })
            .ToDictionaryAsync(result => result.ModelRunId, result => result.AnnualCost, cancellationToken);
        var net = await db.ModelRunNetImpacts.AsNoTracking()
            .Where(result => ids.Contains(result.ModelRunId))
            .ToDictionaryAsync(result => result.ModelRunId, cancellationToken);

        return Ok(ids.Select(id =>
        {
            var run = runs.Single(item => item.Id == id);
            proposedFacilities.TryGetValue(id, out var facility);
            employment.TryGetValue(id, out var jobs);
            fiscal.TryGetValue(id, out var publicRevenue);
            social.TryGetValue(id, out var socialCost);
            net.TryGetValue(id, out var netImpact);
            return new
            {
                run.Id,
                ScenarioName = ReadScenarioName(run.ResolvedInputJson),
                run.Status,
                run.CreatedAtUtc,
                run.CandidateLatitude,
                run.CandidateLongitude,
                facility?.TotalWithProjectResidentGgr,
                facility?.TourismGgr,
                facility?.TrafficGgr,
                facility?.StabilizedTotalGgr,
                jobs?.NetPermanentJobs,
                publicRevenue?.GrossGamingTax,
                GrossSocialCost = socialCost,
                netImpact?.LocalDiscretionaryDisplacement,
                netImpact?.NetHostLocalImpact,
                netImpact?.NetHostStateImpact,
                run.WarningSummary
            };
        }));
    }

    [HttpGet("{modelRunId:guid}/origins/{stableOriginId}/allocations")]
    public async Task<IActionResult> GetOriginAllocations(
        Guid modelRunId,
        string stableOriginId,
        CancellationToken cancellationToken)
    {
        var allocations = await db.ModelRunOriginFacilityAllocations
            .AsNoTracking()
            .Where(allocation => allocation.ModelRunId == modelRunId)
            .Join(
                db.OriginZones.AsNoTracking().Where(origin => origin.StableOriginId == stableOriginId),
                allocation => allocation.OriginZoneId,
                origin => origin.Id,
                (allocation, _) => new
                {
                    allocation.MarketState,
                    allocation.FacilityKey,
                    allocation.IsProposedFacility,
                    allocation.CaptureSourceCategory,
                    allocation.NetworkTravelTimeMinutes,
                    allocation.RoutedDistanceMeters,
                    allocation.NormalizedAttraction,
                    allocation.OriginFacilityModifier,
                    allocation.LogWeight,
                    allocation.Share,
                    allocation.AllocatedResidentGgr,
                    allocation.AllocatedInducedResidentGgr
                })
            .OrderBy(allocation => allocation.MarketState)
            .ThenByDescending(allocation => allocation.Share)
            .ToListAsync(cancellationToken);
        return allocations.Count == 0 ? NotFound() : Ok(allocations);
    }

    private static string ReadScenarioName(string resolvedInputJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(resolvedInputJson);
            return document.RootElement.TryGetProperty("ScenarioName", out var pascalCase)
                ? pascalCase.GetString() ?? "Unnamed scenario"
                : document.RootElement.TryGetProperty("scenarioName", out var camelCase)
                    ? camelCase.GetString() ?? "Unnamed scenario"
                    : "Unnamed scenario";
        }
        catch (System.Text.Json.JsonException)
        {
            return "Unnamed scenario";
        }
    }

    private static IReadOnlySet<string> ReadStableOriginIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static bool SafelyCovers(Geometry geometry, Point candidate)
    {
        try
        {
            return geometry.Covers(candidate);
        }
        catch (TopologyException)
        {
            return false;
        }
    }

    private sealed record OriginSummaryMaterializedRow(
        OriginSummarySourceRow Source,
        Geometry AreaGeometry);
}
