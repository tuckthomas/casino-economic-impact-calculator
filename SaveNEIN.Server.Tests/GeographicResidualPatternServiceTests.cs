using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class GeographicResidualPatternServiceTests
{
    [Fact]
    public void Calculate_AggregatesDirectionalResidualsByPartitionAndGeography()
    {
        var observations = new[]
        {
            new GeographicResidualObservation(
                "a", ValidationPartitions.Training, "market-north", "US-IN", "north-group", 100, 120),
            new GeographicResidualObservation(
                "b", ValidationPartitions.Training, "market-north", "US-IN", "north-group", 200, 180),
            new GeographicResidualObservation(
                "c", ValidationPartitions.Training, "market-central", "US-IN", "central-group", 300, 330),
            new GeographicResidualObservation(
                "d", ValidationPartitions.Holdout, "market-border", "US-OH", "border-group", 400, 360)
        };

        var patterns = new GeographicResidualPatternService(new ValidationMetricsService())
            .Calculate(observations);

        var north = Assert.Single(patterns, pattern =>
            pattern.DatasetPartition == ValidationPartitions.Training &&
            pattern.GeographyKind == ValidationGeographyKinds.Market &&
            pattern.GeographyCode == "market-north");
        Assert.Equal(2, north.ObservationCount);
        Assert.Equal(300, north.ObservedRevenue, 8);
        Assert.Equal(300, north.PredictedRevenue, 8);
        Assert.Equal(0, north.Residual, 8);
        Assert.Equal(20, north.MeanAbsoluteError, 8);
        Assert.Equal(1, north.OverpredictionCount);
        Assert.Equal(1, north.UnderpredictionCount);
        Assert.Equal(0, north.ExactPredictionCount);

        var trainingMarkets = patterns.Where(pattern =>
            pattern.DatasetPartition == ValidationPartitions.Training &&
            pattern.GeographyKind == ValidationGeographyKinds.Market).ToArray();
        Assert.Equal(
            observations.Where(item => item.DatasetPartition == ValidationPartitions.Training)
                .Sum(item => item.Observed),
            trainingMarkets.Sum(pattern => pattern.ObservedRevenue),
            8);
        Assert.Contains(patterns, pattern =>
            pattern.GeographyKind == ValidationGeographyKinds.Jurisdiction &&
            pattern.GeographyCode == "US-IN");
        Assert.Contains(patterns, pattern =>
            pattern.GeographyKind == ValidationGeographyKinds.HoldoutGroup &&
            pattern.GeographyCode == "border-group");
    }

    [Fact]
    public async Task FinalizeAsync_PersistsGravityAndComparableGeographicPatterns()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"geographic-residuals-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var cases = new[]
        {
            CreateCase("train-a", ValidationPartitions.Training, "market-a", "US-IN", "training-market", 100, 120, 1),
            CreateCase("train-b", ValidationPartitions.Training, "market-a", "US-IN", "training-market", 200, 180, 2),
            CreateCase("holdout", ValidationPartitions.Holdout, "market-b", "US-OH", "holdout-market", 300, 330, 3)
        };
        foreach (var validationCase in cases)
        {
            db.ModelRuns.Add(new ModelRun
            {
                Id = validationCase.ModelRunId,
                Status = ModelRunStatuses.Finalized
            });
            db.ModelRunFacilityResults.Add(new ModelRunFacilityResult
            {
                ModelRunId = validationCase.ModelRunId,
                FacilityKey = $"proposed-{validationCase.CaseKey}",
                IsProposedFacility = true,
                StabilizedTotalGgr = validationCase.CaseKey switch
                {
                    "train-a" => 120,
                    "train-b" => 180,
                    _ => 330
                }
            });
        }
        db.ValidationCases.AddRange(cases);
        await db.SaveChangesAsync();

        var metrics = new ValidationMetricsService();
        var service = new ValidationEvaluationService(
            db,
            metrics,
            new ComparableMarketModelService(),
            new ModelParameterSetService(db),
            new GeographicResidualPatternService(metrics));
        var result = await service.FinalizeAsync(new ValidationEvaluationRequest(
            "geographic-pattern-test",
            "1.0.0",
            ValidationObjectiveFunctions.Smape,
            cases.Select(item => item.Id).ToArray(),
            new Dictionary<string, double>(),
            ["scale"],
            "{}"));

        Assert.Equal(6, result.GravityGeographicResidualPatterns.Count);
        Assert.Equal(6, result.ComparableGeographicResidualPatterns.Count);
        Assert.Equal(12, await db.ValidationGeographicResidualPatterns.CountAsync());
        var gravityMarket = await db.ValidationGeographicResidualPatterns.SingleAsync(pattern =>
            pattern.ValidationEvaluationId == result.ValidationEvaluationId &&
            pattern.PredictionKind == ValidationPredictionKinds.Gravity &&
            pattern.DatasetPartition == ValidationPartitions.Training &&
            pattern.GeographyKind == ValidationGeographyKinds.Market &&
            pattern.GeographyCode == "market-a");
        Assert.Equal(300m, gravityMarket.ObservedRevenue);
        Assert.Equal(300m, gravityMarket.PredictedRevenue);
        Assert.Equal(1, gravityMarket.OverpredictionCount);
        Assert.Equal(1, gravityMarket.UnderpredictionCount);
    }

    private static ValidationCase CreateCase(
        string key,
        string partition,
        string market,
        string jurisdiction,
        string holdoutGroup,
        decimal observed,
        decimal predicted,
        double scale) => new()
        {
            Id = Guid.NewGuid(),
            CaseKey = key,
            Name = key,
            MarketCode = market,
            JurisdictionCode = jurisdiction,
            DatasetPartition = partition,
            HoldoutGroup = holdoutGroup,
            ModelRunId = Guid.NewGuid(),
            ObservedRevenue = observed,
            ObservedMetricKey = "ggr",
            ObservedMetricDefinition = "Test GGR",
            TrainingPeriodStart = new DateOnly(2025, 1, 1),
            TrainingPeriodEnd = new DateOnly(2025, 12, 31),
            PredictorValuesJson = $$"""{"scale":{{scale}}}""",
            InclusionRulesJson = "{}",
            ExecutionRequestJson = $$"""{"predicted":{{predicted}}}"""
        };
}
