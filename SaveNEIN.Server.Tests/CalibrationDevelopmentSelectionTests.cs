using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public class CalibrationDevelopmentSelectionTests
{
    [Fact]
    public void WapeShareUsesPredictionWeightsRatherThanUnweightedRatios()
    {
        var rows = new[] { (Observed: 100d, UnitPrediction: 10_000d), (Observed: 2d, UnitPrediction: 100d), (Observed: 3d, UnitPrediction: 100d) };
        var share = CalibrationDevelopmentSelection.FitTrainingWapeScale(rows);
        Assert.Equal(0.01, share, 12);
        var selectedLoss = rows.Sum(row => Math.Abs(share * row.UnitPrediction - row.Observed));
        var oldMedianLoss = rows.Sum(row => Math.Abs(0.02 * row.UnitPrediction - row.Observed));
        Assert.True(selectedLoss < oldMedianLoss);
    }

    [Fact]
    public void ExcellentSelectionCannotAdmitBadTraining()
    {
        var report = CalibrationDevelopmentSelection.Evaluate([
            new("t1", "training", "Chicago", "IL", 100, 300),
            new("t2", "training", "Chicago", "IL", 100, 300),
            new("s1", "selection", "StLouis", "MO", 100, 100)]);
        Assert.False(report.Admitted);
        Assert.Contains(report.Failures, failure => failure.StartsWith("training: MAPE", StringComparison.Ordinal));
    }

    [Fact]
    public void OppositeMarketBiasesCannotCancelIntoAnAcceptableOverallFit()
    {
        var report = CalibrationDevelopmentSelection.Evaluate([
            new("t1", "training", "Chicago", "IL", 100, 112),
            new("t2", "training", "StLouis", "MO", 100, 88),
            new("s1", "selection", "Chicago", "IL", 100, 100)]);
        Assert.False(report.Admitted);
        Assert.Contains(report.Failures, failure => failure.Contains("market:Chicago", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnAttemptToIncludeTheHoldoutInSelection()
    {
        Assert.False(CalibrationDevelopmentSelection.Evaluate([
            new("t1", "training", "Chicago", "IL", 100, 100),
            new("t2", "holdout", "Chicago", "IL", 100, 100)]).Admitted);
    }

    [Fact]
    public void AdmitsAccurateTrainingAndSelectionAcrossMarkets()
    {
        Assert.True(CalibrationDevelopmentSelection.Evaluate([
            new("t1", "training", "Chicago", "IL", 100, 101),
            new("t2", "training", "StLouis", "MO", 100, 99),
            new("s1", "selection", "Chicago", "IL", 100, 100)]).Admitted);
    }
}
