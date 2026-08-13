// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class GravityModelRunsControllerTests
{
    [Fact]
    public async Task GetOriginSummary_UsesCandidateContainingOriginAndPersistedJurisdictionSet()
    {
        await using var db = CreateContext();
        var runId = Guid.NewGuid();
        db.Jurisdictions.Add(new Jurisdiction { Id = 1, Code = "US-IN", Name = "Indiana", Kind = "state" });
        db.ModelRuns.Add(new ModelRun
        {
            Id = runId,
            ModelVersion = "test",
            Status = ModelRunStatuses.Finalized,
            JurisdictionId = 1,
            CandidateLatitude = 1,
            CandidateLongitude = 1,
            ResolvedInputJson = "{}",
            DataSnapshotReferencesJson = "{}"
        });
        db.OriginZones.AddRange(
            Origin(1, "host", "IN", "001", Box(0, 0, 2, 2), "11111", "222"),
            Origin(2, "other", "OH", "039", Box(3, 3, 4, 4), "33333", "444"));
        db.ModelRunOriginResults.AddRange(Result(runId, 1, 75), Result(runId, 2, 25));
        db.ModelRunGeographicAccounting.Add(new ModelRunGeographicAccounting
        {
            ModelRunId = runId,
            ScopeKind = ImpactScopeKinds.HostCounty,
            ScopeCode = "001",
            LocalOriginCount = 1,
            LocalOriginIdsJson = "[\"origin-host\"]"
        });
        await db.SaveChangesAsync();
        var controller = new GravityModelRunsController(db, new StubExecutionService(), new OriginSummaryService());

        var hostResult = Assert.IsType<OkObjectResult>(await controller.GetOriginSummary(
            runId,
            OriginSummaryDimensions.HostRegion,
            top: 10,
            minimumShare: 0));
        var hostSummary = Assert.IsType<OriginSummaryResult>(hostResult.Value);
        Assert.Equal(75m, hostSummary.Rows.Single(row => row.Key == "host-region:host-county").TotalProposedResidentGgr);
        Assert.Equal(25m, hostSummary.Rows.Single(row => row.Key == "host-region:out-of-state").TotalProposedResidentGgr);

        var jurisdictionResult = Assert.IsType<OkObjectResult>(await controller.GetOriginSummary(
            runId,
            OriginSummaryDimensions.Jurisdiction,
            top: 10,
            minimumShare: 0));
        var jurisdictionSummary = Assert.IsType<OriginSummaryResult>(jurisdictionResult.Value);
        Assert.Equal(75m, jurisdictionSummary.Rows.Single(row => row.Key == "jurisdiction:in").TotalProposedResidentGgr);
        Assert.Equal(25m, jurisdictionSummary.Rows.Single(row => row.Key == "jurisdiction:out").TotalProposedResidentGgr);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"origin-summary-controller-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static OriginZone Origin(
        long id,
        string code,
        string state,
        string county,
        Polygon polygon,
        string msa,
        string csa) => new()
    {
        Id = id,
        DatasetSnapshotId = Guid.NewGuid(),
        StableOriginId = $"origin-{code}",
        OriginType = "zcta",
        GeographyCode = code,
        CountryCode = "USA",
        StateOrTerritoryCode = state,
        CountyEquivalentCode = county,
        MetropolitanStatisticalAreaCode = msa,
        CombinedStatisticalAreaCode = csa,
        RepresentativePoint = polygon.Centroid,
        AreaGeometry = polygon
    };

    private static ModelRunOriginResult Result(Guid runId, long originId, decimal ggr) => new()
    {
        ModelRunId = runId,
        OriginZoneId = originId,
        DemandSpecification = GravityDemandSpecifications.AgiShare,
        ResidentDemand = ggr * 2,
        ProposedResidentGgr = ggr,
        TotalProposedResidentGgr = ggr,
        OutsideOptionCapture = ggr
    };

    private static Polygon Box(double minimumX, double minimumY, double maximumX, double maximumY)
    {
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        return geometryFactory.CreatePolygon(
        [
            new Coordinate(minimumX, minimumY),
            new Coordinate(maximumX, minimumY),
            new Coordinate(maximumX, maximumY),
            new Coordinate(minimumX, maximumY),
            new Coordinate(minimumX, minimumY)
        ]);
    }

    private sealed class StubExecutionService : IGravityModelExecutionService
    {
        public Task<GravityModelRunResult> ExecuteAsync(
            GravityModelRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
