using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class IncumbentCalibrationAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrationRejectsBadDevelopmentFitBeforeHoldoutAndBlocksUnqualifiedPublication(bool rejectEveryCandidate)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"calibration-acceptance-{Guid.NewGuid():N}").Options);
        var snapshot = new DatasetSnapshot { DatasetKey = "fixture", Checksum = "fixture", IsSealed = true, ValidationState = DatasetValidationStates.Validated };
        db.DatasetSnapshots.Add(snapshot);
        db.Jurisdictions.AddRange(new Jurisdiction { Id = 1, Code = "US-IN" }, new Jurisdiction { Id = 2, Code = "US-OH" });
        db.ModelParameterSets.Add(new ModelParameterSet { Id = 1, Key = "fixture", Scope = "national", Version = "1", ModelVersionApplicability = "gravity-v1" });
        db.OriginZones.Add(new OriginZone
        {
            StableOriginId = "origin", DatasetSnapshotId = snapshot.Id, RepresentativePoint = new Point(-85, 41) { SRID = 4326 },
            AreaGeometry = new Polygon(new LinearRing([new(-86, 40), new(-84, 40), new(-84, 42), new(-86, 42), new(-86, 40)])) { SRID = 4326 }
        });
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 12, 31);
        for (var id = 1; id <= 4; id++)
        {
            db.CasinoCompetitors.Add(new CasinoCompetitor
            {
                Id = id, StableVenueId = $"venue-{id}", Name = $"Venue {id}", State = id == 3 ? "OH" : "IN",
                JurisdictionId = id == 3 ? 2 : 1, DatasetSnapshotId = snapshot.Id,
                Geom = new Point(-85, 41) { SRID = 4326 },
                Latitude = 41, Longitude = -85, GamingPositions = id * 1_000, SlotOrVltPositions = id * 1_000
            });
            db.CasinoGamingRevenuePeriods.Add(new CasinoGamingRevenuePeriod
            {
                CasinoCompetitorId = id, DatasetSnapshotId = snapshot.Id, PeriodStart = start, PeriodEnd = end,
                ReportedMetricKey = "ggr", ReportedMetricDefinition = "Annual comparable GGR", ReportedAmount = id * 100
            });
        }
        await db.SaveChangesAsync();
        var executor = new CapturingExecutor(db, rejectEveryCandidate);
        var metrics = new ValidationMetricsService();
        var parameterService = new ThrowingParameterService();
        var evaluator = new ValidationEvaluationService(db, metrics, new ComparableMarketModelService(), parameterService);
        var calibration = new IncumbentBacktestCalibrationService(db, new DevelopmentProgramService(db), executor, metrics, evaluator);
        var baseRequest = new GravityModelRunRequest("fixture", "US-IN", Guid.NewGuid(), 41, -85,
            snapshot.Id, snapshot.Id, snapshot.Id, snapshot.Id, snapshot.Id, ["origin"], [1, 2, 3, 4],
            2025, 2025, end, "commercial-casino", GravityDemandSpecifications.AgiShare,
            FacilityAttractionSpecifications.HybridObservedGgr, "inverse-power", "ggr", start, end, 1, null, null, []);
        var request = new IncumbentBacktestCalibrationRequest(
            "fixture", "1", ValidationObjectiveFunctions.Mape, baseRequest,
            [new(1, "training-1", ValidationPartitions.Training, "development"), new(2, "training-2", ValidationPartitions.Training, "development"), new(4, "selection", DemandValidationPartitions.Selection, "selection"), new(3, "holdout", ValidationPartitions.Holdout, "reserved")],
            [new("good", new Dictionary<string, double> { ["gravity.beta"] = 1 }), new("bad", new Dictionary<string, double> { ["gravity.beta"] = 2 })],
            ["gaming-positions"], "{}", 1, "blocked");

        if (rejectEveryCandidate)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => calibration.CalibrateAsync(request));
            Assert.Equal(6, executor.Requests.Count);
            Assert.DoesNotContain(executor.Requests, item => item.ExcludedCompetitorIds!.Contains(3));
            Assert.Empty(await db.ValidationEvaluations.ToArrayAsync());
            return;
        }
        var result = await calibration.CalibrateAsync(request);

        Assert.Equal("good", result.SelectedCandidateKey);
        Assert.False(result.CandidateResults.Single(candidate => candidate.CandidateKey == "bad").DevelopmentAdmission!.Admitted);
        Assert.Equal(7, executor.Requests.Count);
        Assert.Equal(3, executor.Requests.Last().ExcludedCompetitorIds!.Single());
        Assert.Equal("US-OH", executor.Requests.Last().JurisdictionCode);
        Assert.Equal("US-OH", executor.Requests.Last().ImpactGeography!.ScopeCode);
        Assert.Single(executor.Requests, item => item.ExcludedCompetitorIds!.Contains(3));
        Assert.All(executor.Requests, item => Assert.Contains(3, item.ObservedGgrExcludedCompetitorIds!));
        Assert.All(executor.Requests, item => Assert.Contains(4, item.ObservedGgrExcludedCompetitorIds!));
        Assert.Null(result.Evaluation.PublishedParameterSetId);
        Assert.False(result.Evaluation.ReleaseAcceptance.Accepted);
        var stored = await db.ValidationEvaluations.SingleAsync();
        Assert.True(stored.IsImmutable);
        Assert.Contains("releaseAcceptance", stored.InclusionRulesJson);
        Assert.Equal("US-OH", (await db.ValidationCases.SingleAsync(item => item.CaseKey == "holdout")).JurisdictionCode);
    }

    private sealed class CapturingExecutor(AppDbContext db, bool rejectEveryCandidate) : IGravityModelExecutionService
    {
        public List<GravityModelRunRequest> Requests { get; } = [];
        public async Task<GravityModelRunResult> ExecuteAsync(GravityModelRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var target = request.ExcludedCompetitorIds!.Single();
            var beta = (decimal)request.UserOverrides!.Single(item => item.Key == "gravity.beta").Value;
            var selectionFactor = beta == 1 ? (rejectEveryCandidate ? 2m : 1.04m) : 1m;
            var amount = (target == 4 ? selectionFactor : beta) * target * 100;
            var run = new ModelRun { Status = ModelRunStatuses.Finalized, ResolvedInputJson = "{}" };
            db.ModelRuns.Add(run);
            db.ModelRunFacilityResults.Add(new ModelRunFacilityResult { ModelRunId = run.Id, FacilityKey = "proposed", IsProposedFacility = true, StabilizedTotalGgr = amount });
            await db.SaveChangesAsync(cancellationToken);
            return new GravityModelRunResult(run.Id, ModelRunStatuses.Finalized, "zcta", 1, 2, "graph", "auto",
                amount, 0, amount, 0, amount, 0, 0, amount, 0, 0, 0, 0, 0, 0, new Dictionary<string, decimal>(), []);
        }
    }

    private sealed class ThrowingParameterService : IModelParameterSetService
    {
        public Task<ModelParameterSet> CreateVersionAsync(long sourceParameterSetId, string newVersion, string? calibrationNotes = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unqualified calibration must not publish parameters.");
        public Task<ModelParameterSetValue> SetValueAsync(long parameterSetId, string parameterKey, double value, string? provenanceNotes = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unqualified calibration must not publish parameters.");
    }
}
