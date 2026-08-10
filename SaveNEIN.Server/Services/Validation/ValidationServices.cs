// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Validation;

public sealed record ValidationObservation(string CaseKey, double Observed, double Predicted);

public sealed record ValidationMetrics(
    int ObservationCount,
    int MapeObservationCount,
    double MeanAbsoluteError,
    double? MeanAbsolutePercentageError,
    double SymmetricMeanAbsolutePercentageError,
    double RootMeanSquaredError,
    double Bias,
    double? SpearmanRankCorrelation);

public interface IValidationMetricsService
{
    ValidationMetrics Calculate(IReadOnlyCollection<ValidationObservation> observations);
}

public sealed class ValidationMetricsService : IValidationMetricsService
{
    public ValidationMetrics Calculate(IReadOnlyCollection<ValidationObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            throw new ArgumentException("At least one validation observation is required.", nameof(observations));
        }

        var sample = observations.ToArray();
        if (sample.Any(observation =>
                string.IsNullOrWhiteSpace(observation.CaseKey) ||
                !double.IsFinite(observation.Observed) || observation.Observed < 0 ||
                !double.IsFinite(observation.Predicted) || observation.Predicted < 0))
        {
            throw new ArgumentException(
                "Validation observations require a case key and finite, nonnegative observed and predicted revenue.",
                nameof(observations));
        }
        if (sample.Select(observation => observation.CaseKey).Distinct(StringComparer.Ordinal).Count() != sample.Length)
        {
            throw new ArgumentException("Validation case keys must be unique within an evaluation.", nameof(observations));
        }

        var errors = sample.Select(observation => observation.Predicted - observation.Observed).ToArray();
        var absoluteErrors = errors.Select(Math.Abs).ToArray();
        var percentageErrors = sample
            .Where(observation => Math.Abs(observation.Observed) > 1e-12)
            .Select(observation => Math.Abs(observation.Predicted - observation.Observed) /
                                   Math.Abs(observation.Observed) * 100d)
            .ToArray();
        var smape = sample.Average(observation =>
        {
            var denominator = Math.Abs(observation.Observed) + Math.Abs(observation.Predicted);
            return denominator <= 1e-12
                ? 0d
                : 200d * Math.Abs(observation.Predicted - observation.Observed) / denominator;
        });

        return new ValidationMetrics(
            sample.Length,
            percentageErrors.Length,
            absoluteErrors.Average(),
            percentageErrors.Length == 0 ? null : percentageErrors.Average(),
            smape,
            Math.Sqrt(errors.Average(error => error * error)),
            errors.Average(),
            sample.Length < 2
                ? null
                : PearsonCorrelation(
                    Rank(sample.Select(observation => observation.Observed).ToArray()),
                    Rank(sample.Select(observation => observation.Predicted).ToArray())));
    }

    private static double[] Rank(IReadOnlyList<double> values)
    {
        var indexed = values
            .Select((value, index) => (value, index))
            .OrderBy(item => item.value)
            .ToArray();
        var ranks = new double[values.Count];
        for (var start = 0; start < indexed.Length;)
        {
            var end = start + 1;
            while (end < indexed.Length && indexed[end].value.Equals(indexed[start].value))
            {
                end++;
            }
            var averageRank = ((start + 1) + end) / 2d;
            for (var index = start; index < end; index++)
            {
                ranks[indexed[index].index] = averageRank;
            }
            start = end;
        }
        return ranks;
    }

    private static double? PearsonCorrelation(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var leftMean = left.Average();
        var rightMean = right.Average();
        var numerator = 0d;
        var leftSquares = 0d;
        var rightSquares = 0d;
        for (var index = 0; index < left.Count; index++)
        {
            var leftDelta = left[index] - leftMean;
            var rightDelta = right[index] - rightMean;
            numerator += leftDelta * rightDelta;
            leftSquares += leftDelta * leftDelta;
            rightSquares += rightDelta * rightDelta;
        }
        var denominator = Math.Sqrt(leftSquares * rightSquares);
        return denominator <= 1e-12 ? null : numerator / denominator;
    }
}

public sealed record CalibrationCandidate(
    string CandidateKey,
    IReadOnlyDictionary<string, double> Parameters,
    IReadOnlyCollection<ValidationObservation> TrainingObservations);

public sealed record CalibrationSelection(
    string CandidateKey,
    IReadOnlyDictionary<string, double> Parameters,
    ValidationMetrics Metrics,
    double ObjectiveValue);

public interface ICalibrationSearchService
{
    CalibrationSelection SelectBest(
        string objectiveFunction,
        IReadOnlyCollection<CalibrationCandidate> candidates);
}

public sealed class CalibrationSearchService(IValidationMetricsService metricsService) : ICalibrationSearchService
{
    public CalibrationSelection SelectBest(
        string objectiveFunction,
        IReadOnlyCollection<CalibrationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one calibration candidate is required.", nameof(candidates));
        }

        return candidates
            .Select(candidate =>
            {
                if (string.IsNullOrWhiteSpace(candidate.CandidateKey) || candidate.Parameters.Count == 0)
                {
                    throw new ArgumentException(
                        "Each calibration candidate requires a key and at least one parameter.",
                        nameof(candidates));
                }
                var metrics = metricsService.Calculate(candidate.TrainingObservations);
                var objective = objectiveFunction switch
                {
                    "mae" => metrics.MeanAbsoluteError,
                    "mape" => metrics.MeanAbsolutePercentageError ?? double.PositiveInfinity,
                    "smape" => metrics.SymmetricMeanAbsolutePercentageError,
                    "rmse" => metrics.RootMeanSquaredError,
                    _ => throw new ArgumentException(
                        $"Unsupported calibration objective '{objectiveFunction}'.",
                        nameof(objectiveFunction))
                };
                return new CalibrationSelection(candidate.CandidateKey, candidate.Parameters, metrics, objective);
            })
            .OrderBy(selection => selection.ObjectiveValue)
            .ThenBy(selection => selection.CandidateKey, StringComparer.Ordinal)
            .First();
    }
}

public sealed record ComparableMarketSample(
    string CaseKey,
    double ObservedRevenue,
    IReadOnlyDictionary<string, double> Predictors);

public sealed record ComparableMarketModel(
    bool UsesLogRevenue,
    double Intercept,
    IReadOnlyDictionary<string, double> Coefficients,
    IReadOnlyDictionary<string, double> PredictorMeans,
    IReadOnlyDictionary<string, double> PredictorScales,
    double RidgePenalty)
{
    public double Predict(IReadOnlyDictionary<string, double> predictors)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        var fitted = Intercept;
        foreach (var (key, coefficient) in Coefficients)
        {
            if (!predictors.TryGetValue(key, out var value) || !double.IsFinite(value))
            {
                throw new ArgumentException($"Comparable-market prediction is missing finite predictor '{key}'.", nameof(predictors));
            }
            fitted += coefficient * ((value - PredictorMeans[key]) / PredictorScales[key]);
        }
        var prediction = UsesLogRevenue ? Math.Exp(fitted) : fitted;
        return Math.Max(0d, prediction);
    }
}

public interface IComparableMarketModelService
{
    ComparableMarketModel Fit(
        IReadOnlyCollection<ComparableMarketSample> trainingSample,
        IReadOnlyCollection<string> predictorKeys,
        bool useLogRevenue = true,
        double ridgePenalty = 1e-8);
}

public sealed class ComparableMarketModelService : IComparableMarketModelService
{
    public ComparableMarketModel Fit(
        IReadOnlyCollection<ComparableMarketSample> trainingSample,
        IReadOnlyCollection<string> predictorKeys,
        bool useLogRevenue = true,
        double ridgePenalty = 1e-8)
    {
        ArgumentNullException.ThrowIfNull(trainingSample);
        ArgumentNullException.ThrowIfNull(predictorKeys);
        if (trainingSample.Count < 2)
        {
            throw new ArgumentException("Comparable-market fitting requires at least two training cases.", nameof(trainingSample));
        }
        if (!double.IsFinite(ridgePenalty) || ridgePenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ridgePenalty), "Ridge penalty must be finite and nonnegative.");
        }

        var keys = predictorKeys
            .Select(key => key.Trim())
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            throw new ArgumentException("At least one predictor key is required.", nameof(predictorKeys));
        }
        var samples = trainingSample.ToArray();
        foreach (var sample in samples)
        {
            if (string.IsNullOrWhiteSpace(sample.CaseKey) ||
                !double.IsFinite(sample.ObservedRevenue) ||
                sample.ObservedRevenue < 0 ||
                useLogRevenue && sample.ObservedRevenue <= 0)
            {
                throw new ArgumentException(
                    "Comparable-market samples require a key and valid observed revenue (strictly positive for log models).",
                    nameof(trainingSample));
            }
            foreach (var key in keys)
            {
                if (!sample.Predictors.TryGetValue(key, out var value) || !double.IsFinite(value))
                {
                    throw new ArgumentException(
                        $"Comparable-market sample '{sample.CaseKey}' is missing finite predictor '{key}'.",
                        nameof(trainingSample));
                }
            }
        }

        var means = keys.ToDictionary(
            key => key,
            key => samples.Average(sample => sample.Predictors[key]),
            StringComparer.Ordinal);
        var scales = keys.ToDictionary(
            key => key,
            key =>
            {
                var variance = samples.Average(sample =>
                {
                    var delta = sample.Predictors[key] - means[key];
                    return delta * delta;
                });
                var scale = Math.Sqrt(variance);
                return scale <= 1e-12 ? 1d : scale;
            },
            StringComparer.Ordinal);

        var columnCount = keys.Length + 1;
        var normal = new double[columnCount, columnCount];
        var right = new double[columnCount];
        foreach (var sample in samples)
        {
            var row = new double[columnCount];
            row[0] = 1d;
            for (var index = 0; index < keys.Length; index++)
            {
                row[index + 1] = (sample.Predictors[keys[index]] - means[keys[index]]) / scales[keys[index]];
            }
            var target = useLogRevenue ? Math.Log(sample.ObservedRevenue) : sample.ObservedRevenue;
            for (var left = 0; left < columnCount; left++)
            {
                right[left] += row[left] * target;
                for (var top = 0; top < columnCount; top++)
                {
                    normal[left, top] += row[left] * row[top];
                }
            }
        }
        for (var index = 1; index < columnCount; index++)
        {
            normal[index, index] += ridgePenalty;
        }

        var solution = Solve(normal, right);
        var coefficients = keys
            .Select((key, index) => (key, value: solution[index + 1]))
            .ToDictionary(item => item.key, item => item.value, StringComparer.Ordinal);
        return new ComparableMarketModel(
            useLogRevenue,
            solution[0],
            coefficients,
            means,
            scales,
            ridgePenalty);
    }

    private static double[] Solve(double[,] matrix, double[] right)
    {
        var size = right.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }
            augmented[row, size] = right[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var pivotRow = pivot;
            for (var row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[pivotRow, pivot]))
                {
                    pivotRow = row;
                }
            }
            if (Math.Abs(augmented[pivotRow, pivot]) <= 1e-12)
            {
                throw new InvalidOperationException(
                    "Comparable-market design matrix is singular; add cases, remove collinear predictors, or use a positive ridge penalty.");
            }
            if (pivotRow != pivot)
            {
                for (var column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[pivotRow, column]) =
                        (augmented[pivotRow, column], augmented[pivot, column]);
                }
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }
            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                {
                    continue;
                }
                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        return Enumerable.Range(0, size).Select(index => augmented[index, size]).ToArray();
    }
}
