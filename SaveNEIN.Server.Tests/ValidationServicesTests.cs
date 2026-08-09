using SaveNEIN.Server.Services.Validation;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Tests;

public sealed class ValidationServicesTests
{
    [Fact]
    public void Metrics_CalculateAbsolutePercentageRankAndBiasMeasures()
    {
        var result = new ValidationMetricsService().Calculate(
        [
            new("a", 100, 110),
            new("b", 200, 180),
            new("c", 300, 330)
        ]);

        Assert.Equal(3, result.ObservationCount);
        Assert.Equal(3, result.MapeObservationCount);
        Assert.Equal(20, result.MeanAbsoluteError, 8);
        Assert.Equal(10, result.MeanAbsolutePercentageError!.Value, 8);
        Assert.Equal(20d / 3d, result.Bias, 8);
        Assert.Equal(Math.Sqrt((100d + 400d + 900d) / 3d), result.RootMeanSquaredError, 8);
        Assert.Equal(1, result.SpearmanRankCorrelation!.Value, 8);
    }

    [Fact]
    public void Metrics_ExcludesZeroObservedValuesFromMapeWithoutDroppingOtherMetrics()
    {
        var result = new ValidationMetricsService().Calculate(
        [
            new("zero", 0, 10),
            new("positive", 100, 90)
        ]);

        Assert.Equal(2, result.ObservationCount);
        Assert.Equal(1, result.MapeObservationCount);
        Assert.Equal(10, result.MeanAbsolutePercentageError!.Value, 8);
        Assert.Equal(10, result.MeanAbsoluteError, 8);
    }

    [Fact]
    public void Metrics_UsesAverageRanksForTies()
    {
        var result = new ValidationMetricsService().Calculate(
        [
            new("a", 100, 80),
            new("b", 100, 90),
            new("c", 200, 200)
        ]);

        Assert.True(result.SpearmanRankCorrelation > 0.8);
    }

    [Fact]
    public void ComparableModel_FitsIndependentLogLinearReasonablenessModel()
    {
        var training = Enumerable.Range(1, 8)
            .Select(index =>
            {
                var population = 100_000d * index;
                var positions = 400d + 100d * index;
                var revenue = Math.Exp(4 + 0.45 * Math.Log(population) + 0.25 * Math.Log(positions));
                return new ComparableMarketSample(
                    $"case-{index}",
                    revenue,
                    new Dictionary<string, double>
                    {
                        ["log-accessible-population"] = Math.Log(population),
                        ["log-gaming-positions"] = Math.Log(positions)
                    });
            })
            .ToArray();

        var model = new ComparableMarketModelService().Fit(
            training,
            ["log-accessible-population", "log-gaming-positions"],
            useLogRevenue: true,
            ridgePenalty: 1e-8);
        var prediction = model.Predict(training[3].Predictors);

        Assert.InRange(prediction / training[3].ObservedRevenue, 0.999, 1.001);
        Assert.True(model.Coefficients.ContainsKey("log-accessible-population"));
        Assert.True(model.Coefficients.ContainsKey("log-gaming-positions"));
    }

    [Fact]
    public void ComparableModel_RejectsMissingPredictors()
    {
        var samples = new[]
        {
            new ComparableMarketSample("a", 100, new Dictionary<string, double> { ["population"] = 1 }),
            new ComparableMarketSample("b", 200, new Dictionary<string, double>())
        };

        var error = Assert.Throws<ArgumentException>(() =>
            new ComparableMarketModelService().Fit(samples, ["population"]));

        Assert.Contains("missing finite predictor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalibrationSearch_SelectsLowestTrainingObjectiveWithoutUsingHoldouts()
    {
        var service = new CalibrationSearchService(new ValidationMetricsService());
        var result = service.SelectBest("rmse",
        [
            new CalibrationCandidate(
                "beta-1.4",
                new Dictionary<string, double> { ["gravity.beta"] = 1.4 },
                [new("a", 100, 92), new("b", 200, 215)]),
            new CalibrationCandidate(
                "beta-1.5",
                new Dictionary<string, double> { ["gravity.beta"] = 1.5 },
                [new("a", 100, 101), new("b", 200, 198)])
        ]);

        Assert.Equal("beta-1.5", result.CandidateKey);
        Assert.Equal(1.5, result.Parameters["gravity.beta"]);
    }

    [Theory]
    [InlineData(ValidationObjectiveFunctions.Mae, 11d)]
    [InlineData(ValidationObjectiveFunctions.Mape, 12d)]
    [InlineData(ValidationObjectiveFunctions.Smape, 13d)]
    [InlineData(ValidationObjectiveFunctions.Rmse, 14d)]
    public void IncumbentCalibration_ObjectiveSelectsRequestedTrainingMetric(string objective, double expected)
    {
        var value = new ValidationMetrics(4, 4, 11, 12, 13, 14, 0, 1);

        Assert.Equal(expected, IncumbentBacktestCalibrationService.Objective(objective, value));
    }

    [Fact]
    public void IncumbentCalibration_MapeWithoutEligibleObservationsCannotWin()
    {
        var value = new ValidationMetrics(2, 0, 1, null, 3, 2, 0, null);

        Assert.Equal(
            double.PositiveInfinity,
            IncumbentBacktestCalibrationService.Objective(ValidationObjectiveFunctions.Mape, value));
    }

    [Fact]
    public void BenchmarkOutputReader_ReadsSourceExtractedMonetaryMetricByPath()
    {
        var metric = new BenchmarkOutputReader().ReadMonetaryMetric(
            """{"currency":"USD","stabilizedAnnual":{"grossGamingRevenue":282300000}}""",
            "stabilizedAnnual.grossGamingRevenue");

        Assert.Equal("stabilizedAnnual.grossGamingRevenue", metric.Path);
        Assert.Equal(282_300_000m, metric.Value);
        Assert.Equal("USD", metric.Currency);
    }

    [Theory]
    [InlineData("missing.value")]
    [InlineData("stabilizedAnnual")]
    public void BenchmarkOutputReader_RejectsMissingOrNonNumericMetrics(string path)
    {
        var service = new BenchmarkOutputReader();

        Assert.ThrowsAny<Exception>(() => service.ReadMonetaryMetric(
            """{"currency":"USD","stabilizedAnnual":{"grossGamingRevenue":282300000}}""",
            path));
    }

    [Fact]
    public void SensitivityRunFactory_ReplacesOnlySelectedOverrideAndKeepsBaseRequestImmutable()
    {
        var request = CreateRunRequest(
        [
            new ParameterOverride("gravity.beta", 1.5),
            new ParameterOverride("gravity.alpha", 1.0)
        ]);

        var point = SensitivityRunFactory.WithOverride(request, "gravity.beta", 1.4, "low");

        Assert.Equal(1.5, request.UserOverrides!.Single(item => item.Key == "gravity.beta").Value);
        Assert.Equal(1.4, point.UserOverrides!.Single(item => item.Key == "gravity.beta").Value);
        Assert.Equal(1.0, point.UserOverrides!.Single(item => item.Key == "gravity.alpha").Value);
        Assert.Equal(2, point.UserOverrides!.Count);
        Assert.Contains("gravity.beta low", point.ScenarioName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SensitivityOutputMetrics.StabilizedTotalGgr, 100d)]
    [InlineData(SensitivityOutputMetrics.GrossGamingTax, 20d)]
    [InlineData(SensitivityOutputMetrics.GrossSocialCost, 5d)]
    [InlineData(SensitivityOutputMetrics.NetHostLocalImpact, 30d)]
    public void SensitivityMetricSelection_UsesStoredRunOutputs(string metric, double expected)
    {
        var result = new GravityModelRunResult(
            Guid.NewGuid(), "finalized", "zcta", 1, 1, "graph", "auto",
            100m, 0m, 80m, 0m, 80m, 10m, 10m, 100m, 15m, 20m, 5m, 2d, 30m, 40m,
            new Dictionary<string, decimal>(), []);

        Assert.Equal((decimal)expected, SensitivityAnalysisService.SelectMetric(result, metric));
    }

    private static GravityModelRunRequest CreateRunRequest(IReadOnlyCollection<ParameterOverride> overrides) => new(
        "Base", "US-IN", Guid.NewGuid(), 41, -85,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        ["18003"], [1], 2025, 2025, new DateOnly(2025, 12, 31), "commercial",
        GravityDemandSpecifications.AgiShare, FacilityAttractionSpecifications.Structural, "power",
        "ggr", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
        null, null, null, overrides);
}
