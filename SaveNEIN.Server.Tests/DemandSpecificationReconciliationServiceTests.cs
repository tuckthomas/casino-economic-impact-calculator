using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class DemandSpecificationReconciliationServiceTests
{
    [Fact]
    public void Reconcile_ComputesBothSpecificationsAndGroupedDifferences()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());

        var result = service.Reconcile(new DemandSpecificationReconciliationRequest(
            [
                Origin(
                    "origin-a",
                    "IN",
                    "0-30",
                    realIncomeMass: 10_000,
                    gamingIncomeShare: 0.01,
                    eligibleAdults: 10,
                    baseSpend: 10,
                    incomeMetric: 50_000,
                    referenceIncome: 50_000,
                    maximumIncomeAdjustment: 2),
                Origin(
                    "origin-b",
                    "MI",
                    "30-60",
                    realIncomeMass: 20_000,
                    gamingIncomeShare: 0.01,
                    eligibleAdults: 10,
                    baseSpend: 10,
                    incomeMetric: 100_000,
                    referenceIncome: 50_000,
                    maximumIncomeAdjustment: 1.5)
            ],
            LargestOriginDifferenceCount: 2));

        Assert.Equal(300, result.AgiShare.TotalDemand, 8);
        Assert.Equal(250, result.EligibleAdultPerCapita.TotalDemand, 8);
        Assert.Equal(100, result.AgiShare.StateTotals["IN"], 8);
        Assert.Equal(200, result.AgiShare.StateTotals["MI"], 8);
        Assert.Equal(100, result.EligibleAdultPerCapita.DistanceBandTotals["0-30"], 8);
        Assert.Equal(150, result.EligibleAdultPerCapita.DistanceBandTotals["30-60"], 8);

        var largest = Assert.Single(result.LargestOriginDifferences.Where(item => item.AbsoluteDifference > 0));
        Assert.Equal("origin-b", largest.OriginKey);
        Assert.Equal(-50, largest.SignedDifference, 8);
        Assert.Equal(50, largest.AbsoluteDifference, 8);
        Assert.Null(result.SelectedBase);
        Assert.Null(result.EnsembleVersion);
    }

    [Fact]
    public void Reconcile_SelectsBaseFromLowerHoldoutObjective()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());

        var result = service.Reconcile(new DemandSpecificationReconciliationRequest(
            [Origin("origin-a", "IN", "0-30", 10_000, 0.01, 10, 10, 50_000, 50_000, 2)],
            ValidationPerformance:
            [
                new DemandSpecificationValidationPerformance(
                    DemandSpecification.AgiShare,
                    Metrics(mape: 18, mae: 20, smape: 17, rmse: 22)),
                new DemandSpecificationValidationPerformance(
                    DemandSpecification.EligibleAdultPerCapita,
                    Metrics(mape: 11, mae: 15, smape: 10, rmse: 16))
            ],
            SelectionObjective: "mape"));

        Assert.NotNull(result.SelectedBase);
        Assert.Equal(DemandSpecification.EligibleAdultPerCapita, result.SelectedBase!.Specification);
        Assert.Equal("mape", result.SelectedBase.ObjectiveFunction);
        Assert.Equal(11, result.SelectedBase.ObjectiveValue, 8);
    }

    [Fact]
    public void Reconcile_ValidatedEnsembleIsAConvexCombinationNotAnAddedDemandPool()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());
        var result = service.Reconcile(new DemandSpecificationReconciliationRequest(
            [
                Origin(
                    "origin-a",
                    "IN",
                    "0-30",
                    realIncomeMass: 10_000,
                    gamingIncomeShare: 0.01,
                    eligibleAdults: 20,
                    baseSpend: 10,
                    incomeMetric: 50_000,
                    referenceIncome: 50_000,
                    maximumIncomeAdjustment: 2)
            ],
            Ensemble: new DemandEnsembleDefinition(
                Version: "demand-ensemble-v1",
                AgiShareWeight: 0.25,
                EligibleAdultPerCapitaWeight: 0.75,
                IsValidated: true)));

        Assert.Equal(100, result.AgiShare.TotalDemand, 8);
        Assert.Equal(200, result.EligibleAdultPerCapita.TotalDemand, 8);
        Assert.Equal("demand-ensemble-v1", result.EnsembleVersion);
        Assert.NotNull(result.EnsembleDemandByOrigin);
        Assert.Equal(175, result.EnsembleDemandByOrigin!["origin-a"], 8);
        Assert.Equal(175, result.EnsembleTotalDemand!.Value, 8);
        Assert.NotEqual(300, result.EnsembleTotalDemand.Value);
    }

    [Fact]
    public void Reconcile_RejectsUnvalidatedEnsemble()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());

        var exception = Assert.Throws<InvalidOperationException>(() => service.Reconcile(
            new DemandSpecificationReconciliationRequest(
                [Origin("origin-a", "IN", "0-30", 10_000, 0.01, 10, 10, 50_000, 50_000, 2)],
                Ensemble: new DemandEnsembleDefinition("draft", 0.5, 0.5, IsValidated: false))));

        Assert.Contains("only after its weights have been validated", exception.Message);
    }

    [Fact]
    public void Reconcile_RejectsEnsembleWeightsThatDoNotSumToOne()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());

        var exception = Assert.Throws<ArgumentException>(() => service.Reconcile(
            new DemandSpecificationReconciliationRequest(
                [Origin("origin-a", "IN", "0-30", 10_000, 0.01, 10, 10, 50_000, 50_000, 2)],
                Ensemble: new DemandEnsembleDefinition("bad-v1", 1, 1, IsValidated: true))));

        Assert.Contains("must sum to 1.0", exception.Message);
    }

    [Fact]
    public void Reconcile_RejectsMismatchedOriginKeysAcrossSpecifications()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());
        var origin = new DemandSpecificationReconciliationOrigin(
            "origin-a",
            "IN",
            "0-30",
            new AgiShareDemandInput("origin-a", 10_000, 0.01),
            new PerCapitaDemandInput("origin-b", 10, 10, 50_000, 50_000, 1, 0.5, 2));

        var exception = Assert.Throws<ArgumentException>(() => service.Reconcile(
            new DemandSpecificationReconciliationRequest([origin])));

        Assert.Contains("must use the same key", exception.Message);
    }

    [Fact]
    public void Reconcile_RequiresBothHoldoutMetricSetsForBaseSelection()
    {
        var service = new DemandSpecificationReconciliationService(new OriginDemandService());

        var exception = Assert.Throws<ArgumentException>(() => service.Reconcile(
            new DemandSpecificationReconciliationRequest(
                [Origin("origin-a", "IN", "0-30", 10_000, 0.01, 10, 10, 50_000, 50_000, 2)],
                ValidationPerformance:
                [
                    new DemandSpecificationValidationPerformance(
                        DemandSpecification.AgiShare,
                        Metrics(mape: 10, mae: 10, smape: 10, rmse: 10))
                ])));

        Assert.Contains("requires holdout performance for both", exception.Message);
    }

    private static DemandSpecificationReconciliationOrigin Origin(
        string originKey,
        string state,
        string distanceBand,
        double realIncomeMass,
        double gamingIncomeShare,
        double eligibleAdults,
        double baseSpend,
        double incomeMetric,
        double referenceIncome,
        double maximumIncomeAdjustment) =>
        new(
            originKey,
            state,
            distanceBand,
            new AgiShareDemandInput(originKey, realIncomeMass, gamingIncomeShare),
            new PerCapitaDemandInput(
                originKey,
                eligibleAdults,
                baseSpend,
                incomeMetric,
                referenceIncome,
                IncomeElasticity: 1,
                MinimumIncomeAdjustment: 0.5,
                MaximumIncomeAdjustment: maximumIncomeAdjustment));

    private static ValidationMetrics Metrics(
        double mape,
        double mae,
        double smape,
        double rmse) =>
        new(
            ObservationCount: 4,
            MapeObservationCount: 4,
            MeanAbsoluteError: mae,
            MeanAbsolutePercentageError: mape,
            SymmetricMeanAbsolutePercentageError: smape,
            RootMeanSquaredError: rmse,
            Bias: 0,
            SpearmanRankCorrelation: 1);
}
