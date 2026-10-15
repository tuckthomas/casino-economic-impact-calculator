// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Validation;

public sealed record DevelopmentRevenueObservation(
    string CaseKey, string Partition, string Market, string Jurisdiction, double Observed, double Predicted);

public sealed record DevelopmentFitReport(bool Admitted, IReadOnlyList<string> Failures);

/// <summary>Admission is separate from ranking; a good selection score cannot excuse a bad training or geographic fit.</summary>
public static class CalibrationDevelopmentSelection
{
    public const string PolicyVersion = "gravity-development-selection-v1";

    public static double FitTrainingWapeScale(IEnumerable<(double Observed, double UnitPrediction)> observations,
        double minimum = 0.001, double maximum = 0.03, bool constrainPropertyAndBias = false)
        => FitTrainingWapeScaleWithOffsets(observations.Select(row => (row.Observed, row.UnitPrediction, 0d)),
            minimum, maximum, constrainPropertyAndBias);

    public static double FitTrainingWapeScaleWithOffsets(
        IEnumerable<(double Observed, double UnitPrediction, double FixedPrediction)> observations,
        double minimum, double maximum, bool constrainPropertyAndBias = false)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum <= 0 || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        var rows = observations.Select(row =>
        {
            if (!double.IsFinite(row.Observed) || row.Observed <= 0 ||
                !double.IsFinite(row.UnitPrediction) || row.UnitPrediction <= 0 ||
                !double.IsFinite(row.FixedPrediction) || row.FixedPrediction < 0)
                throw new ArgumentException("Training share fitting requires positive finite observations and unit predictions.", nameof(observations));
            var ratio = (row.Observed - row.FixedPrediction) / row.UnitPrediction;
            if (!double.IsFinite(ratio)) throw new ArgumentException("Training fit residual is not finite.", nameof(observations));
            return (Ratio: ratio, Weight: row.UnitPrediction, row.Observed, Fixed: row.FixedPrediction);
        }).OrderBy(row => row.Ratio).ToArray();
        if (rows.Length == 0) throw new ArgumentException("Training observations are required.", nameof(observations));
        var lower = minimum;
        var upper = maximum;
        if (constrainPropertyAndBias)
        {
            // Necessary training constraints, using training labels only.
            // If infeasible, retain an unrestricted diagnostic fit; admission
            // will reject it rather than fabricating a successful solution.
            lower = Math.Max(lower, rows.Max(row => (row.Observed * 0.60000000001 - row.Fixed) / row.Weight));
            upper = Math.Min(upper, rows.Min(row => (row.Observed * 1.39999999999 - row.Fixed) / row.Weight));
            var normalization = rows.Max(row => Math.Max(Math.Max(row.Observed, row.Fixed), row.Weight));
            var observedSum = rows.Sum(row => row.Observed / normalization);
            var fixedSum = rows.Sum(row => row.Fixed / normalization);
            var unitSum = rows.Sum(row => row.Weight / normalization);
            lower = Math.Max(lower, (observedSum * 0.90000000001 - fixedSum) / unitSum);
            upper = Math.Min(upper, (observedSum * 1.09999999999 - fixedSum) / unitSum);
            if (lower > upper || !double.IsFinite(lower) || !double.IsFinite(upper)) { lower = minimum; upper = maximum; }
        }
        // Minimizing sum |s*u+v-y| uses the weighted median of (y-v)/u
        // with weights u; v is an independently predicted, fixed demand layer.
        // Scale weights before summation to avoid overflow for large income masses.
        var largest = rows.Max(row => row.Weight);
        var halfway = rows.Sum(row => row.Weight / largest) / 2;
        var cumulative = 0d;
        foreach (var row in rows)
        {
            cumulative += row.Weight / largest;
            if (cumulative >= halfway) return Math.Clamp(row.Ratio, lower, upper);
        }
        throw new InvalidOperationException("Training weighted median could not be resolved.");
    }

    public static DevelopmentFitReport Evaluate(IReadOnlyCollection<DevelopmentRevenueObservation> observations)
    {
        var failures = new List<string>();
        if (observations.Count == 0 || observations.Any(row =>
            string.IsNullOrWhiteSpace(row.CaseKey) || string.IsNullOrWhiteSpace(row.Market) ||
            string.IsNullOrWhiteSpace(row.Jurisdiction) || row.Partition is not ("training" or "selection") ||
            !double.IsFinite(row.Observed) || row.Observed <= 0 || !double.IsFinite(row.Predicted) || row.Predicted < 0) ||
            observations.Select(row => row.CaseKey).Distinct(StringComparer.Ordinal).Count() != observations.Count)
            return new(false, ["Invalid or duplicate development observations; holdouts are not allowed in candidate admission."]);
        foreach (var partition in new[] { "training", "selection" })
        {
            var rows = observations.Where(row => row.Partition == partition).ToArray();
            if (rows.Length < (partition == "training" ? 2 : 1))
            {
                failures.Add($"{partition}: insufficient properties.");
                continue;
            }
            var apes = rows.Select(row => 100 * Math.Abs(row.Predicted - row.Observed) / row.Observed).ToArray();
            var (wape, bias) = Residuals(rows);
            if (apes.Average() > 20) failures.Add($"{partition}: MAPE exceeds 20%.");
            if (wape > 15) failures.Add($"{partition}: WAPE exceeds 15%.");
            if (Math.Abs(bias) > 5) failures.Add($"{partition}: absolute normalized bias exceeds 5%.");
            if (apes.Any(value => value > 40)) failures.Add($"{partition}: property error exceeds 40%.");
            if (apes.Count(value => value <= 30) / (double)apes.Length < 0.9)
                failures.Add($"{partition}: fewer than 90% of properties are within 30% error.");
            foreach (var group in rows.GroupBy(row => "market:" + row.Market)
                         .Concat(rows.GroupBy(row => "jurisdiction:" + row.Jurisdiction)))
            {
                var residual = Residuals(group);
                if (residual.Wape > 20) failures.Add($"{partition}/{group.Key}: WAPE exceeds 20%.");
                if (Math.Abs(residual.Bias) > 10) failures.Add($"{partition}/{group.Key}: absolute normalized bias exceeds 10%.");
            }
        }
        return new(failures.Count == 0, failures);
    }

    private static (double Wape, double Bias) Residuals(IEnumerable<DevelopmentRevenueObservation> observations)
    {
        var rows = observations.ToArray();
        var scale = rows.Max(row => row.Observed);
        var denominator = rows.Sum(row => row.Observed / scale);
        return (100 * rows.Sum(row => Math.Abs(row.Predicted / scale - row.Observed / scale)) / denominator,
            100 * rows.Sum(row => row.Predicted / scale - row.Observed / scale) / denominator);
    }
}
