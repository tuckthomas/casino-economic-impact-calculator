// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class DemandSpecificationValidationWorkflowTests
{
    [Fact]
    public async Task RunPair_ExecutesBothSpecificationsThroughTheSameAuthoritativeRequest()
    {
        var fake = new CapturingGravityModelExecutionService();
        var service = new DemandSpecificationRunPairService(fake);
        var request = BaseRequest();

        var result = await service.ExecuteAsync(request);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(GravityDemandSpecifications.AgiShare, fake.Requests[0].DemandSpecification);
        Assert.Equal(GravityDemandSpecifications.EligibleAdultPerCapita, fake.Requests[1].DemandSpecification);
        Assert.Equal(request.CandidateLatitude, fake.Requests[0].CandidateLatitude);
        Assert.Equal(request.CandidateLatitude, fake.Requests[1].CandidateLatitude);
        Assert.Equal(request.OriginGeographySnapshotId, fake.Requests[0].OriginGeographySnapshotId);
        Assert.Equal(request.OriginGeographySnapshotId, fake.Requests[1].OriginGeographySnapshotId);
        Assert.Equal(request.CompetitorSnapshotId, fake.Requests[0].CompetitorSnapshotId);
        Assert.Equal(request.CompetitorSnapshotId, fake.Requests[1].CompetitorSnapshotId);
        Assert.NotEqual(result.AgiShare.ModelRunId, result.EligibleAdultPerCapita.ModelRunId);
    }

    [Fact]
    public async Task DemandParameterInitializer_SeedsNonOverrideableGovernedWeightsIdempotently()
    {
        await using var db = CreateDb();

        await DemandModelParameterInitializer.SeedAsync(db);
        await DemandModelParameterInitializer.SeedAsync(db);

        var definitions = await db.ModelParameterDefinitions
            .Where(definition => definition.Key == DemandModelParameterInitializer.AgiShareWeightKey ||
                                 definition.Key == DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey)
            .OrderBy(definition => definition.Key)
            .ToArrayAsync();
        Assert.Equal(2, definitions.Length);
        Assert.All(definitions, definition =>
        {
            Assert.False(definition.IsUserOverridable);
            Assert.Equal(0, definition.ComputationalMinimum);
            Assert.Equal(1, definition.ComputationalMaximum);
            Assert.Contains("validation-published", definition.ProvenanceNotes!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void DemandEnsembleGovernance_UsesConvexCombinationAndRejectsAdditiveWeights()
    {
        var combined = DemandEnsembleGovernance.Combine(
            agiShareDemand: 100,
            eligibleAdultPerCapitaDemand: 300,
            agiShareWeight: 0.25,
            eligibleAdultPerCapitaWeight: 0.75);

        Assert.Equal(250, combined, 8);
        Assert.Throws<InvalidOperationException>(() => DemandEnsembleGovernance.Combine(100, 300, 1, 1));
    }

    [Fact]
    public async Task DemandEnsembleGovernance_RejectsSystemFallbackAndAcceptsValidationPublishedParameterSet()
    {
        await using var db = CreateDb();
        var agiDefinition = WeightDefinition(1, DemandModelParameterInitializer.AgiShareWeightKey);
        var perCapitaDefinition = WeightDefinition(2, DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey);
        var fallback = new[]
        {
            Resolved(agiDefinition, 0.4, "system-fallback"),
            Resolved(perCapitaDefinition, 0.6, "system-fallback")
        };

        var fallbackError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemandEnsembleGovernance.ValidatePublishedParameterResolutionAsync(db, fallback, []));
        Assert.Contains("system-fallback", fallbackError.Message);

        const long parameterSetId = 91;
        db.ValidationEvaluations.Add(new ValidationEvaluation
        {
            EvaluationKey = "demand-eval",
            Version = "1",
            ModelVersion = "gravity-v1",
            ObjectiveFunction = ValidationObjectiveFunctions.Smape,
            Status = ValidationEvaluationStatuses.Finalized,
            PublishedParameterSetId = parameterSetId,
            InclusionRulesJson = "{}",
            SelectedParametersJson = JsonSerializer.Serialize(new
            {
                evaluationKind = "demand-specification-reconciliation",
                ensembleAccepted = true,
                publishedEnsembleParameterSetId = parameterSetId
            }),
            TrainingMetricsJson = "{}",
            HoldoutMetricsJson = "{}",
            BenchmarkMetricsJson = "{}",
            ComparableModelJson = "{}",
            ComparableTrainingMetricsJson = "{}",
            ComparableHoldoutMetricsJson = "{}",
            ComparableBenchmarkMetricsJson = "{}",
            IsImmutable = true,
            FinalizedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var published = new[]
        {
            Resolved(agiDefinition, 0.4, "scenario-preset"),
            Resolved(perCapitaDefinition, 0.6, "scenario-preset")
        };
        await DemandEnsembleGovernance.ValidatePublishedParameterResolutionAsync(
            db,
            published,
            [(parameterSetId, "scenario-preset")]);
    }

    [Fact]
    public async Task Evaluation_PersistsHoldoutSelectedBaseAndOriginReconciliationEvidence()
    {
        await using var db = CreateDb();
        var fixture = await SeedPairedEvaluationFixtureAsync(db);
        var service = new DemandSpecificationValidationEvaluationService(
            db,
            new ValidationMetricsService(),
            new ThrowingParameterSetService());

        var result = await service.FinalizeAsync(new DemandSpecificationValidationEvaluationRequest(
            "paired-demand",
            "1",
            ValidationObjectiveFunctions.Mape,
            fixture.Pairs,
            LargestOriginDifferenceCount: 10));

        Assert.Equal(DemandSpecification.EligibleAdultPerCapita, result.SelectedBaseSpecification);
        Assert.Equal(5, result.SelectedBaseObjectiveValue, 8);
        Assert.NotEmpty(result.LargestOriginDifferences);
        Assert.Contains(result.LargestOriginDifferences, item => item.DistanceBand == "30-60");

        var stored = await db.ValidationEvaluations.SingleAsync(item => item.Id == result.ValidationEvaluationId);
        Assert.Equal(ValidationEvaluationStatuses.Finalized, stored.Status);
        Assert.True(stored.IsImmutable);
        Assert.Contains("demand-specification-reconciliation", stored.SelectedParametersJson);
        Assert.Contains("largestOriginDifferences", stored.InclusionRulesJson);
        Assert.Contains("eligible-adult-per-capita", stored.SelectedParametersJson);
    }

    private static async Task<(IReadOnlyCollection<DemandSpecificationValidationCasePair> Pairs, Guid UnrelatedRunId)>
        SeedPairedEvaluationFixtureAsync(AppDbContext db)
    {
        var pairs = new List<DemandSpecificationValidationCasePair>();
        var snapshotIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var partitions = new[]
        {
            ValidationPartitions.Training,
            ValidationPartitions.Training,
            ValidationPartitions.Holdout
        };
        var agiPredictions = new decimal[] { 80, 80, 60 };
        var perCapitaPredictions = new decimal[] { 90, 90, 95 };
        var programId = Guid.NewGuid();

        for (var index = 0; index < partitions.Length; index++)
        {
            var origin = new OriginZone
            {
                DatasetSnapshotId = snapshotIds[index],
                StableOriginId = $"USA-ZCTA-{46000 + index}",
                OriginType = "zcta",
                GeographyCode = $"{46000 + index}",
                CountryCode = "USA",
                StateOrTerritoryCode = index == 1 ? "MI" : "IN",
                RepresentativePoint = new Point(-85 + index * 0.1, 41 + index * 0.1) { SRID = 4326 },
                AreaGeometry = new Point(-85 + index * 0.1, 41 + index * 0.1) { SRID = 4326 }
            };
            db.OriginZones.Add(origin);
            await db.SaveChangesAsync();

            var incumbentId = 500 + index;
            var candidateLatitude = 41.1 + index * 0.1;
            var candidateLongitude = -85.1 - index * 0.1;
            var agiRun = Run(
                GravityDemandSpecifications.AgiShare,
                programId,
                candidateLatitude,
                candidateLongitude,
                origin.StableOriginId,
                incumbentId);
            var perCapitaRun = Run(
                GravityDemandSpecifications.EligibleAdultPerCapita,
                programId,
                candidateLatitude,
                candidateLongitude,
                origin.StableOriginId,
                incumbentId);
            db.ModelRuns.AddRange(agiRun, perCapitaRun);
            AddRunEvidence(db, agiRun, origin, snapshotIds[index], incumbentId, agiPredictions[index], 100 + index * 20, 40);
            AddRunEvidence(db, perCapitaRun, origin, snapshotIds[index], incumbentId, perCapitaPredictions[index], 120 + index * 20, 40);

            var validationCase = new ValidationCase
            {
                CaseKey = $"case-{index + 1}",
                Name = $"Case {index + 1}",
                MarketCode = index == 2 ? "holdout-market" : "training-market",
                JurisdictionCode = "US-IN",
                CaseKind = ValidationCaseKinds.IncumbentBacktest,
                DatasetPartition = partitions[index],
                HoldoutGroup = partitions[index] == ValidationPartitions.Holdout ? "independent-holdout" : "training",
                TargetCasinoCompetitorId = incumbentId,
                ModelRunId = agiRun.Id,
                ObservedRevenue = 100,
                ObservedMetricKey = "comparable-land-based-gaming-revenue",
                ObservedMetricDefinition = "Comparable annual land-based gaming revenue",
                TrainingPeriodStart = new DateOnly(2025, 1, 1),
                TrainingPeriodEnd = new DateOnly(2025, 12, 31),
                ValidationPeriodStart = new DateOnly(2025, 1, 1),
                ValidationPeriodEnd = new DateOnly(2025, 12, 31),
                InclusionRulesJson = "{}",
                PredictorValuesJson = "{}",
                ExecutionRequestJson = agiRun.ResolvedInputJson
            };
            db.ValidationCases.Add(validationCase);
            await db.SaveChangesAsync();
            pairs.Add(new DemandSpecificationValidationCasePair(
                validationCase.Id,
                agiRun.Id,
                perCapitaRun.Id));
        }

        var unrelated = Run(
            GravityDemandSpecifications.AgiShare,
            Guid.NewGuid(),
            39,
            -77,
            "USA-ZCTA-20001",
            999);
        db.ModelRuns.Add(unrelated);
        await db.SaveChangesAsync();
        return (pairs, unrelated.Id);
    }

    private static void AddRunEvidence(
        AppDbContext db,
        ModelRun run,
        OriginZone origin,
        Guid snapshotId,
        int incumbentId,
        decimal proposedPrediction,
        decimal residentDemand,
        double candidateTravelMinutes)
    {
        db.ModelRunDatasetSnapshotReferences.Add(new ModelRunDatasetSnapshotReference
        {
            ModelRunId = run.Id,
            DatasetSnapshotId = snapshotId,
            Role = DatasetSnapshotRoles.OriginDemographics,
            ReferenceKey = "geography"
        });
        db.ModelRunOriginResults.Add(new ModelRunOriginResult
        {
            ModelRunId = run.Id,
            OriginZoneId = origin.Id,
            DemandSpecification = ReadDemandSpecification(run.ResolvedInputJson),
            ResidentDemand = residentDemand
        });
        db.ModelRunFacilityResults.AddRange(
            new ModelRunFacilityResult
            {
                ModelRunId = run.Id,
                CasinoCompetitorId = incumbentId,
                FacilityKey = $"incumbent-{incumbentId}",
                FacilityKind = FacilityKinds.Incumbent,
                IsProposedFacility = false,
                StabilizedTotalGgr = 50
            },
            new ModelRunFacilityResult
            {
                ModelRunId = run.Id,
                FacilityKey = $"scenario:{run.Id:D}",
                FacilityKind = FacilityKinds.Scenario,
                IsProposedFacility = true,
                StabilizedTotalGgr = proposedPrediction
            });
        db.OriginFacilityTravel.Add(new OriginFacilityTravel
        {
            OriginZoneId = origin.Id,
            ModelRunId = run.Id,
            FacilityKey = $"scenario:{run.Id:D}",
            FacilityKind = FacilityKinds.Scenario,
            FacilityCoordinateHash = $"hash-{run.Id:N}",
            FacilityLatitude = run.CandidateLatitude,
            FacilityLongitude = run.CandidateLongitude,
            RoutingGraphHash = "graph-v1",
            CostingProfile = "auto",
            TravelTimeMinutes = candidateTravelMinutes,
            RoutedDistanceMeters = candidateTravelMinutes * 1_000,
            RouteFound = true
        });
    }

    private static ModelRun Run(
        string demandSpecification,
        Guid developmentProgramId,
        double candidateLatitude,
        double candidateLongitude,
        string originId,
        int incumbentId)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            ScenarioName = "paired demand validation",
            computationalOriginType = "zcta",
            StableOriginIds = new[] { originId },
            CompetitorIds = new[] { incumbentId },
            PopulationObservationYear = 2024,
            IncomeTaxYear = 2022,
            EffectiveOn = new DateOnly(2026, 1, 1),
            FacilityRegime = "commercial-casino",
            DemandSpecification = demandSpecification,
            AttractionSpecification = FacilityAttractionSpecifications.Structural,
            FrictionForm = "inverse-power",
            ObservedMetricKey = "comparable-land-based-gaming-revenue",
            ObservedPeriodStart = new DateOnly(2025, 1, 1),
            ObservedPeriodEnd = new DateOnly(2025, 12, 31),
            CostingProfile = "auto"
        });
        return new ModelRun
        {
            ModelVersion = "gravity-v1",
            Status = ModelRunStatuses.Finalized,
            JurisdictionId = 1,
            DevelopmentProgramId = developmentProgramId,
            CandidateLatitude = candidateLatitude,
            CandidateLongitude = candidateLongitude,
            ResolvedInputJson = inputJson,
            DataSnapshotReferencesJson = "{}",
            FinalizedAtUtc = DateTime.UtcNow,
            IsImmutableForTestOnly = false
        };
    }

    private static string ReadDemandSpecification(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("DemandSpecification").GetString()!;
    }

    private static GravityModelRunRequest BaseRequest() => new(
        ScenarioName: "paired validation",
        JurisdictionCode: "US-IN",
        DevelopmentProgramId: Guid.NewGuid(),
        CandidateLatitude: 41.08,
        CandidateLongitude: -85.14,
        OriginGeographySnapshotId: Guid.NewGuid(),
        AgePopulationSnapshotId: Guid.NewGuid(),
        IncomeSnapshotId: Guid.NewGuid(),
        CompetitorSnapshotId: Guid.NewGuid(),
        ObservedPerformanceSnapshotId: Guid.NewGuid(),
        StableOriginIds: ["USA-ZCTA-46802"],
        CompetitorIds: [1],
        PopulationObservationYear: 2024,
        IncomeTaxYear: 2022,
        EffectiveOn: new DateOnly(2026, 1, 1),
        FacilityRegime: "commercial-casino",
        DemandSpecification: GravityDemandSpecifications.AgiShare,
        AttractionSpecification: FacilityAttractionSpecifications.Structural,
        FrictionForm: "inverse-power",
        ObservedMetricKey: "comparable-land-based-gaming-revenue",
        ObservedPeriodStart: new DateOnly(2025, 1, 1),
        ObservedPeriodEnd: new DateOnly(2025, 12, 31),
        NationalParameterSetId: null,
        JurisdictionParameterSetId: null,
        ScenarioParameterSetId: null,
        UserOverrides: null);

    private static ModelParameterDefinition WeightDefinition(long id, string key) => new()
    {
        Id = id,
        Key = key,
        Category = "demand",
        DisplayName = key,
        TechnicalDescription = "test",
        PlainLanguageDescription = "test",
        Units = "share",
        SystemDefaultValue = 0.5,
        ComputationalMinimum = 0,
        ComputationalMaximum = 1,
        UiExposureLevel = "expert",
        IsUserOverridable = false,
        ModelVersionApplicability = "gravity-v1"
    };

    private static ResolvedModelParameter Resolved(
        ModelParameterDefinition definition,
        double value,
        string sourceLayer) => new(
        definition,
        definition.SystemDefaultValue,
        value,
        null,
        null,
        value,
        sourceLayer,
        false,
        null);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"demand-validation-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private sealed class CapturingGravityModelExecutionService : IGravityModelExecutionService
    {
        public List<GravityModelRunRequest> Requests { get; } = [];

        public Task<GravityModelRunResult> ExecuteAsync(
            GravityModelRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new GravityModelRunResult(
                Guid.NewGuid(),
                ModelRunStatuses.Finalized,
                "zcta",
                1,
                1,
                "graph",
                request.CostingProfile,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, decimal>(),
                []));
        }
    }

    private sealed class ThrowingParameterSetService : IModelParameterSetService
    {
        public Task<ModelParameterSet> CreateVersionAsync(
            long sourceParameterSetId,
            string newVersion,
            string? calibrationNotes = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No ensemble should be published in this test.");

        public Task<ModelParameterSetValue> SetValueAsync(
            long parameterSetId,
            string parameterKey,
            double value,
            string? provenanceNotes = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No ensemble should be published in this test.");
    }
}
