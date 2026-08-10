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

public sealed record IncumbentBacktestTarget(
    int CasinoCompetitorId,
    string CaseKey,
    string DatasetPartition,
    string HoldoutGroup);

public sealed record IncumbentCalibrationCandidate(
    string CandidateKey,
    IReadOnlyDictionary<string, double> Parameters);

public sealed record IncumbentBacktestCalibrationRequest(
    string EvaluationKey,
    string Version,
    string ObjectiveFunction,
    GravityModelRunRequest BaseRunRequest,
    IReadOnlyCollection<IncumbentBacktestTarget> Targets,
    IReadOnlyCollection<IncumbentCalibrationCandidate> Candidates,
    IReadOnlyCollection<string> ComparablePredictorKeys,
    string InclusionRulesJson,
    long SourceParameterSetId,
    string PublishedParameterSetVersion,
    double ComparableRidgePenalty = 1e-8,
    double OriginPrefilterMiles = 100);

public sealed record IncumbentCalibrationCandidateResult(
    string CandidateKey,
    IReadOnlyDictionary<string, double> Parameters,
    ValidationMetrics TrainingMetrics,
    double ObjectiveValue,
    IReadOnlyDictionary<string, Guid> ModelRunIds);

public sealed record IncumbentBacktestCalibrationResult(
    string SelectedCandidateKey,
    IReadOnlyDictionary<string, double> SelectedParameters,
    IReadOnlyCollection<IncumbentCalibrationCandidateResult> CandidateResults,
    ValidationEvaluationResult Evaluation);

public interface IIncumbentBacktestCalibrationService
{
    Task<IncumbentBacktestCalibrationResult> CalibrateAsync(
        IncumbentBacktestCalibrationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs leakage-safe incumbent back-tests. Observed target revenue is loaded from a sealed
/// source snapshot, never accepted from the request, and never supplied to model execution;
/// it is used only to score predictions produced with the target excluded.
/// </summary>
public sealed class IncumbentBacktestCalibrationService(
    AppDbContext db,
    IDevelopmentProgramService developmentPrograms,
    IGravityModelExecutionService gravityModel,
    IValidationMetricsService metrics,
    IValidationEvaluationService evaluations) : IIncumbentBacktestCalibrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IncumbentBacktestCalibrationResult> CalibrateAsync(
        IncumbentBacktestCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (await db.ValidationEvaluations.AsNoTracking().AnyAsync(
                evaluation => evaluation.EvaluationKey == request.EvaluationKey.Trim() &&
                              evaluation.Version == request.Version.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Validation evaluation '{request.EvaluationKey}' version '{request.Version}' already exists.");
        }

        await ValidatePersistedInputsAsync(request, cancellationToken);

        var targetIds = request.Targets.Select(target => target.CasinoCompetitorId).ToArray();
        var competitors = await db.CasinoCompetitors
            .AsNoTracking()
            .Where(competitor => targetIds.Contains(competitor.Id) &&
                                 competitor.DatasetSnapshotId == request.BaseRunRequest.CompetitorSnapshotId)
            .ToDictionaryAsync(competitor => competitor.Id, cancellationToken);
        if (competitors.Count != targetIds.Length)
        {
            throw new KeyNotFoundException(
                "One or more back-test targets were not found in the base request's competitor snapshot.");
        }

        var requestedOriginIds = request.BaseRunRequest.StableOriginIds.ToArray();
        var originCandidates = await db.OriginZones.AsNoTracking()
            .Where(origin => origin.DatasetSnapshotId == request.BaseRunRequest.OriginGeographySnapshotId &&
                             requestedOriginIds.Contains(origin.StableOriginId))
            .ToArrayAsync(cancellationToken);
        if (originCandidates.Length != requestedOriginIds.Length)
        {
            throw new KeyNotFoundException(
                "One or more base back-test origins were not found in the origin-geography snapshot.");
        }
        var originIdsByTarget = request.Targets.ToDictionary(
            target => target.CasinoCompetitorId,
            target => (IReadOnlyCollection<string>)originCandidates
                .Where(origin => CompetitiveUniverseService.IsWithinBroadPrefilter(
                    origin.RepresentativePoint.Y,
                    origin.RepresentativePoint.X,
                    competitors[target.CasinoCompetitorId].Latitude,
                    competitors[target.CasinoCompetitorId].Longitude,
                    request.OriginPrefilterMiles))
                .OrderBy(origin => origin.StableOriginId, StringComparer.Ordinal)
                .Select(origin => origin.StableOriginId)
                .ToArray());
        var emptyTarget = request.Targets.FirstOrDefault(target => originIdsByTarget[target.CasinoCompetitorId].Count == 0);
        if (emptyTarget is not null)
        {
            throw new InvalidOperationException(
                $"Back-test target '{emptyTarget.CaseKey}' has no origins inside its broad diagnostic prefilter.");
        }

        var observed = await LoadObservedTargetsAsync(request, competitors, cancellationToken);
        var programs = new Dictionary<int, DevelopmentProgram>();
        foreach (var target in request.Targets.OrderBy(target => target.CaseKey, StringComparer.Ordinal))
        {
            programs[target.CasinoCompetitorId] = await CreateBacktestProgramAsync(
                request,
                competitors[target.CasinoCompetitorId],
                cancellationToken);
        }

        var runResults = new Dictionary<(string CandidateKey, string CaseKey), GravityModelRunResult>();
        var candidateResults = new List<IncumbentCalibrationCandidateResult>();
        var trainingCaseKeys = request.Targets
            .Where(target => target.DatasetPartition == ValidationPartitions.Training)
            .Select(target => target.CaseKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in request.Candidates.OrderBy(candidate => candidate.CandidateKey, StringComparer.Ordinal))
        {
            var modelRunIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var target in request.Targets.OrderBy(target => target.CaseKey, StringComparer.Ordinal))
            {
                var competitor = competitors[target.CasinoCompetitorId];
                var excluded = (request.BaseRunRequest.ExcludedCompetitorIds ?? [])
                    .Append(competitor.Id)
                    .Distinct()
                    .ToArray();
                var mergedOverrides = MergeOverrides(request.BaseRunRequest.UserOverrides, candidate.Parameters);
                var runRequest = request.BaseRunRequest with
                {
                    ScenarioName = $"{request.EvaluationKey} {request.Version}: hold out {competitor.Name}; candidate {candidate.CandidateKey}",
                    DevelopmentProgramId = programs[competitor.Id].Id,
                    CandidateLatitude = competitor.Latitude,
                    CandidateLongitude = competitor.Longitude,
                    StableOriginIds = originIdsByTarget[competitor.Id],
                    UserOverrides = mergedOverrides,
                    CompetitorIds = request.BaseRunRequest.CompetitorIds
                        .Where(competitorId => competitorId != competitor.Id)
                        .ToArray(),
                    ExcludedCompetitorIds = excluded
                };
                var result = await gravityModel.ExecuteAsync(runRequest, cancellationToken);
                runResults[(candidate.CandidateKey, target.CaseKey)] = result;
                modelRunIds[target.CaseKey] = result.ModelRunId;
            }

            var trainingObservations = request.Targets
                .Where(target => trainingCaseKeys.Contains(target.CaseKey))
                .Select(target => new ValidationObservation(
                    target.CaseKey,
                    Convert.ToDouble(observed[target.CasinoCompetitorId].Amount),
                    Convert.ToDouble(runResults[(candidate.CandidateKey, target.CaseKey)].StabilizedTotalGgr)))
                .ToArray();
            var trainingMetrics = metrics.Calculate(trainingObservations);
            candidateResults.Add(new IncumbentCalibrationCandidateResult(
                candidate.CandidateKey,
                candidate.Parameters,
                trainingMetrics,
                Objective(request.ObjectiveFunction, trainingMetrics),
                modelRunIds));
        }

        var selected = candidateResults
            .OrderBy(candidate => candidate.ObjectiveValue)
            .ThenBy(candidate => candidate.CandidateKey, StringComparer.Ordinal)
            .First();
        var selectedCandidate = request.Candidates.Single(candidate => candidate.CandidateKey == selected.CandidateKey);
        var validationCaseIds = new List<Guid>();
        foreach (var target in request.Targets.OrderBy(target => target.CaseKey, StringComparer.Ordinal))
        {
            var competitor = competitors[target.CasinoCompetitorId];
            var result = runResults[(selected.CandidateKey, target.CaseKey)];
            var run = await db.ModelRuns.AsNoTracking().SingleAsync(item => item.Id == result.ModelRunId, cancellationToken);
            var observedTarget = observed[competitor.Id];
            var predictorValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["total-resident-demand"] = Convert.ToDouble(result.TotalResidentDemand),
                ["gaming-positions"] = competitor.GamingPositions ?? competitor.SlotOrVltPositions ?? 0,
                ["table-games"] = competitor.TableGameCount ?? 0,
                ["modeled-incumbent-count"] = result.IncumbentCount
            };
            var validationCase = new ValidationCase
            {
                CaseKey = target.CaseKey.Trim(),
                Name = $"Incumbent holdout: {competitor.Name}",
                MarketCode = target.HoldoutGroup.Trim(),
                JurisdictionCode = request.BaseRunRequest.JurisdictionCode,
                CaseKind = ValidationCaseKinds.IncumbentBacktest,
                DatasetPartition = target.DatasetPartition,
                HoldoutGroup = target.HoldoutGroup.Trim(),
                TargetCasinoCompetitorId = competitor.Id,
                ModelRunId = result.ModelRunId,
                ObservedRevenue = observedTarget.Amount,
                ObservedMetricKey = request.BaseRunRequest.ObservedMetricKey,
                ObservedMetricDefinition = observedTarget.Definition,
                TrainingPeriodStart = request.BaseRunRequest.ObservedPeriodStart,
                TrainingPeriodEnd = request.BaseRunRequest.ObservedPeriodEnd,
                ValidationPeriodStart = request.BaseRunRequest.ObservedPeriodStart,
                ValidationPeriodEnd = request.BaseRunRequest.ObservedPeriodEnd,
                InclusionRulesJson = JsonSerializer.Serialize(new
                {
                    rule = "Target property excluded from the modeled competitive field; observed target revenue came from the sealed observed-performance snapshot and was used only to score the independently produced prediction.",
                    caseKey = target.CaseKey.Trim(),
                    target.CasinoCompetitorId,
                    request.BaseRunRequest.ObservedPerformanceSnapshotId,
                    request.BaseRunRequest.ObservedMetricKey,
                    selectedCandidate.CandidateKey,
                    originPrefilterMiles = request.OriginPrefilterMiles,
                    selectedOriginCount = originIdsByTarget[competitor.Id].Count
                }, JsonOptions),
                PredictorValuesJson = JsonSerializer.Serialize(predictorValues, JsonOptions),
                ExecutionRequestJson = run.ResolvedInputJson,
                Notes = "Observed revenue was source-derived; it was not accepted from the calibration request and was not used as proposed-facility attraction."
            };
            db.ValidationCases.Add(validationCase);
            validationCaseIds.Add(validationCase.Id);
        }
        await db.SaveChangesAsync(cancellationToken);

        var searchEvidence = candidateResults.Select(candidate => new
        {
            candidate.CandidateKey,
            candidate.Parameters,
            candidate.TrainingMetrics,
            candidate.ObjectiveValue
        }).ToArray();
        var evaluation = await evaluations.FinalizeAsync(new ValidationEvaluationRequest(
            request.EvaluationKey,
            request.Version,
            request.ObjectiveFunction,
            validationCaseIds,
            selectedCandidate.Parameters,
            request.ComparablePredictorKeys,
            JsonSerializer.Serialize(new
            {
                suppliedRules = ParseJson(request.InclusionRulesJson),
                selectionRule = "Candidate selected on training partition only; holdout results were not used for selection.",
                candidates = searchEvidence
            }, JsonOptions),
            request.SourceParameterSetId,
            request.PublishedParameterSetVersion,
            request.ComparableRidgePenalty), cancellationToken);

        return new IncumbentBacktestCalibrationResult(
            selected.CandidateKey,
            selectedCandidate.Parameters,
            candidateResults,
            evaluation);
    }

    private async Task<Dictionary<int, ObservedTarget>> LoadObservedTargetsAsync(
        IncumbentBacktestCalibrationRequest request,
        IReadOnlyDictionary<int, CasinoCompetitor> competitors,
        CancellationToken cancellationToken)
    {
        var targetIds = competitors.Keys.ToArray();
        var periods = await db.CasinoGamingRevenuePeriods.AsNoTracking()
            .Where(period => targetIds.Contains(period.CasinoCompetitorId) &&
                             period.DatasetSnapshotId == request.BaseRunRequest.ObservedPerformanceSnapshotId &&
                             period.ReportedMetricKey == request.BaseRunRequest.ObservedMetricKey &&
                             period.PeriodStart >= request.BaseRunRequest.ObservedPeriodStart &&
                             period.PeriodEnd <= request.BaseRunRequest.ObservedPeriodEnd)
            .OrderBy(period => period.CasinoCompetitorId)
            .ThenBy(period => period.PeriodStart)
            .ToListAsync(cancellationToken);
        var result = new Dictionary<int, ObservedTarget>();
        foreach (var competitor in competitors.Values)
        {
            var competitorPeriods = periods.Where(period => period.CasinoCompetitorId == competitor.Id).ToArray();
            RequireContinuousCoverage(competitor, competitorPeriods, request.BaseRunRequest);
            var definitions = competitorPeriods.Select(period => period.ReportedMetricDefinition)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (definitions.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Observed target periods for '{competitor.Name}' do not share one metric definition.");
            }
            result[competitor.Id] = new ObservedTarget(
                competitorPeriods.Sum(period => period.InflationAdjustedAmount ?? period.ReportedAmount),
                definitions[0]);
        }
        return result;
    }

    private async Task ValidatePersistedInputsAsync(
        IncumbentBacktestCalibrationRequest request,
        CancellationToken cancellationToken)
    {
        var caseKeys = request.Targets.Select(target => target.CaseKey.Trim()).ToArray();
        var existingCaseKeys = await db.ValidationCases.AsNoTracking()
            .Where(validationCase => caseKeys.Contains(validationCase.CaseKey))
            .Select(validationCase => validationCase.CaseKey)
            .ToArrayAsync(cancellationToken);
        if (existingCaseKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"Validation case key(s) already exist: {string.Join(", ", existingCaseKeys.Order(StringComparer.Ordinal))}.");
        }

        var sourceParameterSet = await db.ModelParameterSets.AsNoTracking()
            .SingleOrDefaultAsync(set => set.Id == request.SourceParameterSetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Source parameter set '{request.SourceParameterSetId}' was not found.");
        if (!string.Equals(sourceParameterSet.Scope, "national", StringComparison.OrdinalIgnoreCase) ||
            !ModelParameterResolver.AppliesToModel(sourceParameterSet.ModelVersionApplicability, "gravity-v1"))
        {
            throw new InvalidOperationException(
                $"Source parameter set '{sourceParameterSet.Key}' must have national scope and apply to gravity-v1.");
        }
        if (await db.ModelParameterSets.AsNoTracking().AnyAsync(
                set => set.Key == sourceParameterSet.Key &&
                       set.Version == request.PublishedParameterSetVersion.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Parameter set '{sourceParameterSet.Key}' already has version '{request.PublishedParameterSetVersion.Trim()}'.");
        }

        var requiredSnapshotIds = new[]
        {
            request.BaseRunRequest.OriginGeographySnapshotId,
            request.BaseRunRequest.AgePopulationSnapshotId,
            request.BaseRunRequest.IncomeSnapshotId,
            request.BaseRunRequest.CompetitorSnapshotId,
            request.BaseRunRequest.ObservedPerformanceSnapshotId
        };
        var snapshots = await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => requiredSnapshotIds.Contains(snapshot.Id))
            .ToDictionaryAsync(snapshot => snapshot.Id, cancellationToken);
        foreach (var snapshotId in requiredSnapshotIds.Distinct())
        {
            if (!snapshots.TryGetValue(snapshotId, out var snapshot))
            {
                throw new KeyNotFoundException($"Required dataset snapshot '{snapshotId:D}' was not found.");
            }
            if (!snapshot.IsSealed || snapshot.ValidationState is not
                (DatasetValidationStates.Validated or DatasetValidationStates.Warning))
            {
                throw new InvalidOperationException(
                    $"Required dataset snapshot '{snapshot.DatasetKey}' must be sealed and validated or warning-qualified.");
            }
        }
    }

    private async Task<DevelopmentProgram> CreateBacktestProgramAsync(
        IncumbentBacktestCalibrationRequest request,
        CasinoCompetitor competitor,
        CancellationToken cancellationToken)
    {
        var stableId = TrimTo(
            $"incumbent-backtest-{Slug(request.EvaluationKey)}-{Slug(request.Version)}-{competitor.Id}",
            160);
        var version = TrimTo($"{Slug(request.Version)}-{competitor.Id}", 40);
        return await developmentPrograms.CreateAsync(new DevelopmentProgramDefinition(
            stableId,
            version,
            $"Back-test physical program for {competitor.Name}",
            competitor.SlotOrVltPositions ?? competitor.GamingPositions ?? 1,
            competitor.TableGameCount ?? 0,
            competitor.PokerTableCount ?? 0,
            competitor.HasSportsbook == true,
            competitor.HotelRoomCount ?? 250,
            competitor.GamingFloorSquareFeet ?? 0,
            competitor.FoodBeverageVenueCount ?? 0,
            competitor.EventCapacity ?? 1_000,
            competitor.HasResortAmenities == true ? 1 : 0,
            competitor.DevelopmentCost,
            competitor.DevelopmentCostDollarYear,
            null,
            3,
            "Created from the held-out facility snapshot. Missing hotel/event features use neutral reference values; " +
            "observed target revenue is intentionally excluded from the proposed-facility attraction input."), cancellationToken);
    }

    private static IReadOnlyCollection<ParameterOverride> MergeOverrides(
        IReadOnlyCollection<ParameterOverride>? baseOverrides,
        IReadOnlyDictionary<string, double> candidateParameters)
    {
        var merged = (baseOverrides ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, value) in candidateParameters)
        {
            merged[key] = value;
        }
        return merged.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ParameterOverride(item.Key, item.Value)).ToArray();
    }

    private static void RequireContinuousCoverage(
        CasinoCompetitor competitor,
        IReadOnlyCollection<CasinoGamingRevenuePeriod> periods,
        GravityModelRunRequest request)
    {
        var cursor = request.ObservedPeriodStart;
        foreach (var period in periods.OrderBy(period => period.PeriodStart))
        {
            if (period.PeriodStart != cursor || period.PeriodEnd < period.PeriodStart)
            {
                throw new InvalidOperationException(
                    $"Observed target revenue for '{competitor.Name}' does not continuously cover " +
                    $"{request.ObservedPeriodStart:yyyy-MM-dd} through {request.ObservedPeriodEnd:yyyy-MM-dd}.");
            }
            cursor = period.PeriodEnd.AddDays(1);
        }
        if (cursor != request.ObservedPeriodEnd.AddDays(1))
        {
            throw new InvalidOperationException(
                $"Observed target revenue for '{competitor.Name}' is incomplete for the requested back-test period.");
        }
    }

    internal static double Objective(string objectiveFunction, ValidationMetrics value) => objectiveFunction switch
    {
        ValidationObjectiveFunctions.Mae => value.MeanAbsoluteError,
        ValidationObjectiveFunctions.Mape => value.MeanAbsolutePercentageError ?? double.PositiveInfinity,
        ValidationObjectiveFunctions.Smape => value.SymmetricMeanAbsolutePercentageError,
        ValidationObjectiveFunctions.Rmse => value.RootMeanSquaredError,
        _ => throw new ArgumentException($"Unsupported calibration objective '{objectiveFunction}'.", nameof(objectiveFunction))
    };

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string TrimTo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static void ValidateRequest(IncumbentBacktestCalibrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EvaluationKey) || string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.PublishedParameterSetVersion))
        {
            throw new ArgumentException("Evaluation key, version, and published parameter-set version are required.", nameof(request));
        }
        if (request.Targets.Count < 3 ||
            request.Targets.Count(target => target.DatasetPartition == ValidationPartitions.Training) < 2 ||
            !request.Targets.Any(target => target.DatasetPartition == ValidationPartitions.Holdout))
        {
            throw new ArgumentException("Calibration requires at least two training targets and one independent holdout target.", nameof(request));
        }
        if (request.Targets.Any(target => target.CasinoCompetitorId <= 0 || string.IsNullOrWhiteSpace(target.CaseKey) ||
                                          string.IsNullOrWhiteSpace(target.HoldoutGroup) ||
                                          target.DatasetPartition is not (ValidationPartitions.Training or ValidationPartitions.Holdout)) ||
            request.Targets.Select(target => target.CasinoCompetitorId).Distinct().Count() != request.Targets.Count ||
            request.Targets.Select(target => target.CaseKey).Distinct(StringComparer.Ordinal).Count() != request.Targets.Count)
        {
            throw new ArgumentException("Back-test targets require unique IDs/keys, market groups, and training or holdout partitions.", nameof(request));
        }
        if (request.Candidates.Count == 0 ||
            request.Candidates.Any(candidate => string.IsNullOrWhiteSpace(candidate.CandidateKey) ||
                                                candidate.Parameters.Count == 0 ||
                                                candidate.Parameters.Any(item => string.IsNullOrWhiteSpace(item.Key) || !double.IsFinite(item.Value))) ||
            request.Candidates.Select(candidate => candidate.CandidateKey).Distinct(StringComparer.Ordinal).Count() != request.Candidates.Count)
        {
            throw new ArgumentException("Calibration candidates require unique keys and finite parameter values.", nameof(request));
        }
        if (request.ComparablePredictorKeys.Count == 0 || request.SourceParameterSetId <= 0)
        {
            throw new ArgumentException("Comparable predictors and a source parameter set are required.", nameof(request));
        }
        if (!double.IsFinite(request.OriginPrefilterMiles) || request.OriginPrefilterMiles is <= 0 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The broad diagnostic origin prefilter must be between 0 and 2,000 miles.");
        }
        if (request.BaseRunRequest.AttractionSpecification is not (
                FacilityAttractionSpecifications.ObservedGgr or
                FacilityAttractionSpecifications.HybridObservedGgr))
        {
            throw new ArgumentException(
                "Incumbent back-tests must use observed-GGR or audited hybrid mass for remaining incumbents; target GGR remains excluded.",
                nameof(request));
        }
        _ = Objective(request.ObjectiveFunction, new ValidationMetrics(1, 1, 0, 0, 0, 0, 0, null));
        _ = ParseJson(request.InclusionRulesJson);
    }

    private sealed record ObservedTarget(decimal Amount, string Definition);
}
