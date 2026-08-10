// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record ValidationEvaluationRequest(
    string EvaluationKey,
    string Version,
    string ObjectiveFunction,
    IReadOnlyCollection<Guid> ValidationCaseIds,
    IReadOnlyDictionary<string, double> SelectedParameters,
    IReadOnlyCollection<string> ComparablePredictorKeys,
    string InclusionRulesJson,
    long? SourceParameterSetId = null,
    string? PublishedParameterSetVersion = null,
    double ComparableRidgePenalty = 1e-8);

public sealed record ValidationEvaluationResult(
    Guid ValidationEvaluationId,
    long? PublishedParameterSetId,
    ValidationMetrics TrainingMetrics,
    ValidationMetrics HoldoutMetrics,
    ValidationMetrics? BenchmarkMetrics,
    ValidationMetrics ComparableTrainingMetrics,
    ValidationMetrics ComparableHoldoutMetrics,
    ValidationMetrics? ComparableBenchmarkMetrics,
    ComparableMarketModel ComparableModel);

public interface IValidationEvaluationService
{
    Task<ValidationEvaluationResult> FinalizeAsync(
        ValidationEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationEvaluationService(
    AppDbContext db,
    IValidationMetricsService metricsService,
    IComparableMarketModelService comparableMarketModelService,
    IModelParameterSetService parameterSetService) : IValidationEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ValidationEvaluationResult> FinalizeAsync(
        ValidationEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var ids = request.ValidationCaseIds.Distinct().ToArray();
        var cases = await db.ValidationCases
            .AsNoTracking()
            .Where(validationCase => ids.Contains(validationCase.Id))
            .OrderBy(validationCase => validationCase.CaseKey)
            .ToListAsync(cancellationToken);
        if (cases.Count != ids.Length)
        {
            throw new KeyNotFoundException("One or more validation cases were not found.");
        }
        if (cases.Count(validationCase => validationCase.DatasetPartition == ValidationPartitions.Training) < 2)
        {
            throw new InvalidOperationException("A finalized evaluation requires at least two training cases.");
        }
        if (!cases.Any(validationCase => validationCase.DatasetPartition == ValidationPartitions.Holdout))
        {
            throw new InvalidOperationException("A finalized evaluation requires at least one independent holdout case.");
        }
        if (await db.ValidationEvaluations.AnyAsync(
                evaluation => evaluation.EvaluationKey == request.EvaluationKey.Trim() &&
                              evaluation.Version == request.Version.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Validation evaluation '{request.EvaluationKey}' version '{request.Version}' already exists.");
        }

        var runIds = cases.Select(validationCase => validationCase.ModelRunId).Distinct().ToArray();
        var runs = await db.ModelRuns.AsNoTracking()
            .Where(run => runIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, cancellationToken);
        if (runs.Count != runIds.Length || runs.Values.Any(run => run.Status != ModelRunStatuses.Finalized))
        {
            throw new InvalidOperationException("Every validation case must reference an existing finalized model run.");
        }
        var proposedFacilities = await db.ModelRunFacilityResults.AsNoTracking()
            .Where(result => runIds.Contains(result.ModelRunId) && result.IsProposedFacility)
            .ToListAsync(cancellationToken);
        var duplicateProposedRuns = proposedFacilities
            .GroupBy(result => result.ModelRunId)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        if (proposedFacilities.Count != runIds.Length || duplicateProposedRuns.Length > 0)
        {
            throw new InvalidOperationException("Every validation run must contain exactly one proposed-facility result.");
        }
        var predictionByRun = proposedFacilities.ToDictionary(
            result => result.ModelRunId,
            result => Convert.ToDouble(result.StabilizedTotalGgr));

        var gravityObservations = cases.Select(validationCase => new ValidationObservation(
            validationCase.CaseKey,
            Convert.ToDouble(validationCase.ObservedRevenue),
            predictionByRun[validationCase.ModelRunId])).ToArray();
        var gravityTraining = MetricsForPartition(cases, gravityObservations, ValidationPartitions.Training)!;
        var gravityHoldout = MetricsForPartition(cases, gravityObservations, ValidationPartitions.Holdout)!;
        var gravityBenchmark = MetricsForPartition(cases, gravityObservations, ValidationPartitions.Benchmark);

        var predictorKeys = request.ComparablePredictorKeys
            .Select(key => key.Trim())
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var comparableSamples = cases.Select(validationCase => new ComparableMarketSample(
            validationCase.CaseKey,
            Convert.ToDouble(validationCase.ObservedRevenue),
            DeserializePredictors(validationCase))).ToArray();
        var trainingCaseKeys = cases
            .Where(validationCase => validationCase.DatasetPartition == ValidationPartitions.Training)
            .Select(validationCase => validationCase.CaseKey)
            .ToHashSet(StringComparer.Ordinal);
        var comparableModel = comparableMarketModelService.Fit(
            comparableSamples.Where(sample => trainingCaseKeys.Contains(sample.CaseKey)).ToArray(),
            predictorKeys,
            useLogRevenue: true,
            request.ComparableRidgePenalty);
        var comparableObservations = comparableSamples.Select(sample => new ValidationObservation(
            sample.CaseKey,
            sample.ObservedRevenue,
            comparableModel.Predict(sample.Predictors))).ToArray();
        var comparableTraining = MetricsForPartition(cases, comparableObservations, ValidationPartitions.Training)!;
        var comparableHoldout = MetricsForPartition(cases, comparableObservations, ValidationPartitions.Holdout)!;
        var comparableBenchmark = MetricsForPartition(cases, comparableObservations, ValidationPartitions.Benchmark);

        var evaluation = new ValidationEvaluation
        {
            EvaluationKey = request.EvaluationKey.Trim(),
            Version = request.Version.Trim(),
            ModelVersion = "gravity-v1",
            ObjectiveFunction = request.ObjectiveFunction,
            Status = ValidationEvaluationStatuses.Draft,
            InclusionRulesJson = CanonicalJson(request.InclusionRulesJson),
            SelectedParametersJson = JsonSerializer.Serialize(request.SelectedParameters, JsonOptions),
            TrainingMetricsJson = JsonSerializer.Serialize(gravityTraining, JsonOptions),
            HoldoutMetricsJson = JsonSerializer.Serialize(gravityHoldout, JsonOptions),
            BenchmarkMetricsJson = SerializeNullable(gravityBenchmark),
            ComparableModelJson = JsonSerializer.Serialize(comparableModel, JsonOptions),
            ComparableTrainingMetricsJson = JsonSerializer.Serialize(comparableTraining, JsonOptions),
            ComparableHoldoutMetricsJson = JsonSerializer.Serialize(comparableHoldout, JsonOptions),
            ComparableBenchmarkMetricsJson = SerializeNullable(comparableBenchmark),
            TrainingCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Training),
            HoldoutCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Holdout),
            BenchmarkCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Benchmark)
        };
        db.ValidationEvaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);

        db.ValidationCaseResults.AddRange(BuildResults(
            evaluation.Id, cases, gravityObservations, ValidationPredictionKinds.Gravity));
        db.ValidationCaseResults.AddRange(BuildResults(
            evaluation.Id, cases, comparableObservations, ValidationPredictionKinds.Comparable));
        await db.SaveChangesAsync(cancellationToken);

        long? publishedParameterSetId = null;
        if (request.SelectedParameters.Count > 0)
        {
            var clone = await parameterSetService.CreateVersionAsync(
                request.SourceParameterSetId!.Value,
                request.PublishedParameterSetVersion!,
                $"Validation evaluation {evaluation.EvaluationKey} version {evaluation.Version}; " +
                $"objective={evaluation.ObjectiveFunction}; training={evaluation.TrainingMetricsJson}; " +
                $"holdout={evaluation.HoldoutMetricsJson}.",
                cancellationToken);
            foreach (var (key, value) in request.SelectedParameters.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                await parameterSetService.SetValueAsync(
                    clone.Id,
                    key,
                    value,
                    $"Selected by validation evaluation {evaluation.Id:D}.",
                    cancellationToken);
            }
            clone = await db.ModelParameterSets.SingleAsync(set => set.Id == clone.Id, cancellationToken);
            clone.IsImmutable = true;
            await db.SaveChangesAsync(cancellationToken);
            publishedParameterSetId = clone.Id;
        }

        evaluation.PublishedParameterSetId = publishedParameterSetId;
        evaluation.Status = ValidationEvaluationStatuses.Finalized;
        evaluation.IsImmutable = true;
        evaluation.FinalizedAtUtc = DateTime.UtcNow;
        var benchmarkIds = cases
            .Where(validationCase => validationCase.BenchmarkStudyId.HasValue)
            .Select(validationCase => validationCase.BenchmarkStudyId!.Value)
            .Distinct()
            .ToArray();
        if (benchmarkIds.Length > 0)
        {
            var validatedBenchmarks = await db.BenchmarkStudies
                .Where(study => benchmarkIds.Contains(study.Id))
                .ToListAsync(cancellationToken);
            foreach (var benchmark in validatedBenchmarks)
            {
                benchmark.ValidationState = BenchmarkValidationStates.Validated;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        return new ValidationEvaluationResult(
            evaluation.Id,
            publishedParameterSetId,
            gravityTraining,
            gravityHoldout,
            gravityBenchmark,
            comparableTraining,
            comparableHoldout,
            comparableBenchmark,
            comparableModel);
    }

    private ValidationMetrics? MetricsForPartition(
        IReadOnlyCollection<ValidationCase> cases,
        IReadOnlyCollection<ValidationObservation> observations,
        string partition)
    {
        var keys = cases
            .Where(validationCase => validationCase.DatasetPartition == partition)
            .Select(validationCase => validationCase.CaseKey)
            .ToHashSet(StringComparer.Ordinal);
        var partitionObservations = observations.Where(observation => keys.Contains(observation.CaseKey)).ToArray();
        return partitionObservations.Length == 0 ? null : metricsService.Calculate(partitionObservations);
    }

    private static IReadOnlyCollection<ValidationCaseResult> BuildResults(
        Guid evaluationId,
        IReadOnlyCollection<ValidationCase> cases,
        IReadOnlyCollection<ValidationObservation> observations,
        string predictionKind)
    {
        var byKey = observations.ToDictionary(observation => observation.CaseKey, StringComparer.Ordinal);
        return cases.Select(validationCase =>
        {
            var observation = byKey[validationCase.CaseKey];
            var error = observation.Predicted - observation.Observed;
            var denominator = Math.Abs(observation.Observed) + Math.Abs(observation.Predicted);
            return new ValidationCaseResult
            {
                ValidationEvaluationId = evaluationId,
                ValidationCaseId = validationCase.Id,
                ModelRunId = validationCase.ModelRunId,
                PredictionKind = predictionKind,
                DatasetPartition = validationCase.DatasetPartition,
                ObservedRevenue = Money(observation.Observed),
                PredictedRevenue = Money(observation.Predicted),
                Residual = Money(error),
                AbsolutePercentageError = Math.Abs(observation.Observed) <= 1e-12
                    ? null
                    : Math.Abs(error) / Math.Abs(observation.Observed) * 100d,
                SymmetricAbsolutePercentageError = denominator <= 1e-12
                    ? 0d
                    : 200d * Math.Abs(error) / denominator,
                DiagnosticsJson = JsonSerializer.Serialize(new
                {
                    validationCase.MarketCode,
                    validationCase.HoldoutGroup,
                    validationCase.CaseKind
                }, JsonOptions)
            };
        }).ToArray();
    }

    private static Dictionary<string, double> DeserializePredictors(ValidationCase validationCase)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(
                       validationCase.PredictorValuesJson,
                       JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Validation case '{validationCase.CaseKey}' has invalid predictor JSON.",
                exception);
        }
    }

    private static string CanonicalJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Inclusion rules must be valid JSON.", nameof(json), exception);
        }
    }

    private static string SerializeNullable(ValidationMetrics? metrics) =>
        metrics is null ? "{}" : JsonSerializer.Serialize(metrics, JsonOptions);

    private static decimal Money(double value)
    {
        if (!double.IsFinite(value) || value > (double)decimal.MaxValue || value < (double)decimal.MinValue)
        {
            throw new InvalidOperationException("Validation revenue cannot be represented as decimal currency.");
        }
        return Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateRequest(ValidationEvaluationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EvaluationKey) || string.IsNullOrWhiteSpace(request.Version))
        {
            throw new ArgumentException("Evaluation key and version are required.", nameof(request));
        }
        if (request.ValidationCaseIds.Count < 3 || request.ValidationCaseIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("At least three non-empty validation case IDs are required.", nameof(request));
        }
        if (request.ObjectiveFunction is not (
                ValidationObjectiveFunctions.Mae or ValidationObjectiveFunctions.Mape or
                ValidationObjectiveFunctions.Smape or ValidationObjectiveFunctions.Rmse))
        {
            throw new ArgumentException($"Unsupported objective function '{request.ObjectiveFunction}'.", nameof(request));
        }
        if (request.ComparablePredictorKeys.Count == 0)
        {
            throw new ArgumentException("At least one comparable-market predictor is required.", nameof(request));
        }
        if (request.SelectedParameters.Any(item => !double.IsFinite(item.Value)))
        {
            throw new ArgumentException("Selected calibration parameter values must be finite.", nameof(request));
        }
        var publicationRequested = request.SelectedParameters.Count > 0;
        if (publicationRequested != (request.SourceParameterSetId.HasValue &&
                                     !string.IsNullOrWhiteSpace(request.PublishedParameterSetVersion)))
        {
            throw new ArgumentException(
                "Publishing selected parameters requires both a source parameter set and a new version; omit all three to evaluate without publishing.",
                nameof(request));
        }
    }
}
