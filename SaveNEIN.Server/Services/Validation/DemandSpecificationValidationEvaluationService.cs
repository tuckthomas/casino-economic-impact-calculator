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

public sealed record DemandSpecificationValidationCasePair(
    Guid ValidationCaseId,
    Guid AgiShareModelRunId,
    Guid EligibleAdultPerCapitaModelRunId);

public sealed record DemandSpecificationEnsembleCandidate(
    double AgiShareWeight,
    double EligibleAdultPerCapitaWeight,
    long SourceParameterSetId,
    string PublishedParameterSetVersion);

public sealed record DemandSpecificationValidationEvaluationRequest(
    string EvaluationKey,
    string Version,
    string ObjectiveFunction,
    IReadOnlyCollection<DemandSpecificationValidationCasePair> Pairs,
    DemandSpecificationEnsembleCandidate? EnsembleCandidate = null,
    int LargestOriginDifferenceCount = 25);

public sealed record DemandSpecificationMetricBundle(
    ValidationMetrics? AgiShare,
    ValidationMetrics? EligibleAdultPerCapita,
    ValidationMetrics? Ensemble);

public sealed record PersistedDemandOriginDifference(
    string CaseKey,
    string OriginKey,
    string StateOrTerritory,
    string DistanceBand,
    decimal AgiShareDemand,
    decimal EligibleAdultPerCapitaDemand,
    decimal SignedDifference,
    decimal AbsoluteDifference);

public sealed record DemandSpecificationValidationEvaluationResult(
    Guid ValidationEvaluationId,
    DemandSpecification SelectedBaseSpecification,
    string ObjectiveFunction,
    double SelectedBaseSelectionObjectiveValue,
    DemandSpecificationMetricBundle TrainingMetrics,
    DemandSpecificationMetricBundle SelectionMetrics,
    DemandSpecificationMetricBundle HoldoutMetrics,
    DemandSpecificationMetricBundle BenchmarkMetrics,
    long? PublishedEnsembleParameterSetId,
    bool EnsembleAcceptedOnSelection,
    string? EnsembleDecision,
    IReadOnlyList<PersistedDemandOriginDifference> LargestOriginDifferences);

public interface IDemandSpecificationValidationEvaluationService
{
    Task<DemandSpecificationValidationEvaluationResult> FinalizeAsync(
        DemandSpecificationValidationEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Scores paired authoritative gravity runs that differ in resident-demand
/// specification. Model/specification selection occurs only on the explicit
/// selection partition; the independent holdout is never used to choose the base
/// specification or ensemble weights. Observed revenue is read only from persisted
/// validation cases and never enters model execution.
/// </summary>
public sealed class DemandSpecificationValidationEvaluationService(
    AppDbContext db,
    IValidationMetricsService metricsService,
    IModelParameterSetService parameterSetService) : IDemandSpecificationValidationEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> DemandSpecificParameterKeys = new(StringComparer.Ordinal)
    {
        "demand.gaming_income_share",
        "demand.base_ggr_per_eligible_adult",
        "demand.income_elasticity",
        "demand.income_adjustment_minimum",
        "demand.income_adjustment_maximum",
        DemandModelParameterInitializer.AgiShareWeightKey,
        DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey
    };

    public async Task<DemandSpecificationValidationEvaluationResult> FinalizeAsync(
        DemandSpecificationValidationEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var evaluationKey = request.EvaluationKey.Trim();
        var version = request.Version.Trim();
        var objective = NormalizeObjective(request.ObjectiveFunction);
        if (await db.ValidationEvaluations.AsNoTracking().AnyAsync(
                evaluation => evaluation.EvaluationKey == evaluationKey && evaluation.Version == version,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Validation evaluation '{evaluationKey}' version '{version}' already exists.");
        }

        var pairs = request.Pairs.OrderBy(pair => pair.ValidationCaseId).ToArray();
        var caseIds = pairs.Select(pair => pair.ValidationCaseId).ToArray();
        var cases = await db.ValidationCases.AsNoTracking()
            .Where(validationCase => caseIds.Contains(validationCase.Id))
            .OrderBy(validationCase => validationCase.CaseKey)
            .ToArrayAsync(cancellationToken);
        if (cases.Length != caseIds.Length)
        {
            throw new KeyNotFoundException("One or more demand-specification validation cases were not found.");
        }
        if (!cases.Any(item => item.DatasetPartition == ValidationPartitions.Training))
        {
            throw new InvalidOperationException(
                "Demand-specification evaluation requires at least one training/calibration case.");
        }
        if (!cases.Any(item => item.DatasetPartition == DemandValidationPartitions.Selection))
        {
            throw new InvalidOperationException(
                "Demand-specification evaluation requires an explicit selection partition separate from the final holdout.");
        }
        if (!cases.Any(item => item.DatasetPartition == ValidationPartitions.Holdout))
        {
            throw new InvalidOperationException(
                "Demand-specification evaluation requires at least one untouched independent holdout case.");
        }
        var unsupportedPartitions = cases
            .Where(item => item.DatasetPartition is not (
                ValidationPartitions.Training or
                DemandValidationPartitions.Selection or
                ValidationPartitions.Holdout or
                ValidationPartitions.Benchmark))
            .Select(item => item.DatasetPartition)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupportedPartitions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Demand-specification evaluation contains unsupported partition(s): {string.Join(", ", unsupportedPartitions)}.");
        }

        var pairRunIds = pairs
            .SelectMany(pair => new[] { pair.AgiShareModelRunId, pair.EligibleAdultPerCapitaModelRunId })
            .Distinct()
            .ToArray();
        var referenceRunIds = cases.Select(validationCase => validationCase.ModelRunId).Distinct().ToArray();
        var contextRunIds = pairRunIds.Concat(referenceRunIds).Distinct().ToArray();
        var runs = await db.ModelRuns.AsNoTracking()
            .Where(run => contextRunIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, cancellationToken);
        if (runs.Count != contextRunIds.Length ||
            contextRunIds.Any(runId => runs[runId].Status != ModelRunStatuses.Finalized))
        {
            throw new InvalidOperationException(
                "Every paired and reference validation run must exist and be finalized.");
        }

        var proposedResults = await db.ModelRunFacilityResults.AsNoTracking()
            .Where(result => pairRunIds.Contains(result.ModelRunId) && result.IsProposedFacility)
            .ToArrayAsync(cancellationToken);
        var proposedByRun = proposedResults.GroupBy(result => result.ModelRunId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (pairRunIds.Any(runId => !proposedByRun.TryGetValue(runId, out var rows) || rows.Length != 1))
        {
            throw new InvalidOperationException(
                "Every paired demand-specification run must contain exactly one proposed-facility result.");
        }

        var originRows = await db.ModelRunOriginResults.AsNoTracking()
            .Where(result => contextRunIds.Contains(result.ModelRunId))
            .Join(
                db.OriginZones.AsNoTracking(),
                result => result.OriginZoneId,
                origin => origin.Id,
                (result, origin) => new PersistedOriginRow(
                    result.ModelRunId,
                    result.OriginZoneId,
                    origin.StableOriginId,
                    origin.StateOrTerritoryCode ?? string.Empty,
                    result.ResidentDemand))
            .ToArrayAsync(cancellationToken);
        var originsByRun = originRows.GroupBy(row => row.ModelRunId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.OriginKey, StringComparer.Ordinal).ToArray());
        RequireRunCoverage(contextRunIds, originsByRun.Keys, "origin results");

        var scenarioRoutes = await db.OriginFacilityTravel.AsNoTracking()
            .Where(route => route.ModelRunId.HasValue &&
                            contextRunIds.Contains(route.ModelRunId.Value) &&
                            route.FacilityKind == FacilityKinds.Scenario)
            .ToArrayAsync(cancellationToken);
        var routeByRunAndOrigin = scenarioRoutes
            .GroupBy(route => (RunId: route.ModelRunId!.Value, route.OriginZoneId))
            .ToDictionary(group => group.Key, group => group.Single());

        var parameterDefinitions = await db.ModelParameterDefinitions.AsNoTracking()
            .ToDictionaryAsync(definition => definition.Id, definition => definition.Key, cancellationToken);
        var parameterRows = await db.ModelRunParameterValues.AsNoTracking()
            .Where(value => contextRunIds.Contains(value.ModelRunId))
            .ToArrayAsync(cancellationToken);
        var parametersByRun = parameterRows.GroupBy(value => value.ModelRunId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, double>)group.ToDictionary(
                    value => parameterDefinitions[value.ParameterDefinitionId],
                    value => value.FinalValue,
                    StringComparer.Ordinal));

        var snapshotRows = await db.ModelRunDatasetSnapshotReferences.AsNoTracking()
            .Where(reference => contextRunIds.Contains(reference.ModelRunId))
            .ToArrayAsync(cancellationToken);
        var snapshotsByRun = snapshotRows.GroupBy(reference => reference.ModelRunId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, Guid>)group.ToDictionary(
                    reference => $"{reference.Role}:{reference.ReferenceKey}",
                    reference => reference.DatasetSnapshotId,
                    StringComparer.Ordinal));
        RequireRunCoverage(contextRunIds, snapshotsByRun.Keys, "dataset snapshot references");

        var incumbentRows = await db.ModelRunFacilityResults.AsNoTracking()
            .Where(result => contextRunIds.Contains(result.ModelRunId) && !result.IsProposedFacility)
            .ToArrayAsync(cancellationToken);
        var incumbentIdsByRun = incumbentRows.GroupBy(result => result.ModelRunId)
            .ToDictionary(
                group => group.Key,
                group => group.Where(result => result.CasinoCompetitorId.HasValue)
                    .Select(result => result.CasinoCompetitorId!.Value)
                    .Order()
                    .ToArray());
        RequireRunCoverage(contextRunIds, incumbentIdsByRun.Keys, "incumbent facility results");

        var caseById = cases.ToDictionary(item => item.Id);
        var predictions = new List<PairedCasePrediction>(pairs.Length);
        var originDifferences = new List<PersistedDemandOriginDifference>();
        foreach (var pair in pairs)
        {
            var validationCase = caseById[pair.ValidationCaseId];
            var referenceRun = runs[validationCase.ModelRunId];
            var agiRun = runs[pair.AgiShareModelRunId];
            var perCapitaRun = runs[pair.EligibleAdultPerCapitaModelRunId];
            RequireSpecification(agiRun, GravityDemandSpecifications.AgiShare);
            RequireSpecification(perCapitaRun, GravityDemandSpecifications.EligibleAdultPerCapita);
            ValidatePairCompatibility(
                validationCase,
                referenceRun,
                agiRun,
                perCapitaRun,
                snapshotsByRun,
                incumbentIdsByRun,
                parametersByRun,
                originsByRun);

            var agiOrigins = originsByRun[pair.AgiShareModelRunId]
                .ToDictionary(row => row.OriginKey, StringComparer.Ordinal);
            var perCapitaOrigins = originsByRun[pair.EligibleAdultPerCapitaModelRunId]
                .ToDictionary(row => row.OriginKey, StringComparer.Ordinal);
            foreach (var originKey in agiOrigins.Keys.Order(StringComparer.Ordinal))
            {
                var agi = agiOrigins[originKey];
                var perCapita = perCapitaOrigins[originKey];
                var referenceRoute = RequireScenarioRoute(
                    routeByRunAndOrigin,
                    referenceRun.Id,
                    agi.OriginZoneId,
                    validationCase.CaseKey);
                var agiRoute = RequireScenarioRoute(
                    routeByRunAndOrigin,
                    pair.AgiShareModelRunId,
                    agi.OriginZoneId,
                    validationCase.CaseKey);
                var perCapitaRoute = RequireScenarioRoute(
                    routeByRunAndOrigin,
                    pair.EligibleAdultPerCapitaModelRunId,
                    perCapita.OriginZoneId,
                    validationCase.CaseKey);
                RequireEquivalentRoute(validationCase.CaseKey, originKey, referenceRoute, agiRoute);
                RequireEquivalentRoute(validationCase.CaseKey, originKey, referenceRoute, perCapitaRoute);

                var signed = perCapita.ResidentDemand - agi.ResidentDemand;
                originDifferences.Add(new PersistedDemandOriginDifference(
                    validationCase.CaseKey,
                    originKey,
                    agi.StateOrTerritory,
                    DistanceBand(agiRoute.TravelTimeMinutes, agiRoute.RouteFound),
                    agi.ResidentDemand,
                    perCapita.ResidentDemand,
                    signed,
                    Math.Abs(signed)));
            }

            predictions.Add(new PairedCasePrediction(
                validationCase.Id,
                validationCase.CaseKey,
                validationCase.DatasetPartition,
                Convert.ToDouble(validationCase.ObservedRevenue),
                Convert.ToDouble(proposedByRun[pair.AgiShareModelRunId][0].StabilizedTotalGgr),
                Convert.ToDouble(proposedByRun[pair.EligibleAdultPerCapitaModelRunId][0].StabilizedTotalGgr),
                pair.AgiShareModelRunId,
                pair.EligibleAdultPerCapitaModelRunId));
        }

        var agiObservations = predictions.Select(item => new ValidationObservation(
            item.CaseKey, item.Observed, item.AgiSharePrediction)).ToArray();
        var perCapitaObservations = predictions.Select(item => new ValidationObservation(
            item.CaseKey, item.Observed, item.EligibleAdultPerCapitaPrediction)).ToArray();

        var training = new DemandSpecificationMetricBundle(
            MetricsForPartition(cases, agiObservations, ValidationPartitions.Training),
            MetricsForPartition(cases, perCapitaObservations, ValidationPartitions.Training),
            null);
        var agiSelection = MetricsForPartition(cases, agiObservations, DemandValidationPartitions.Selection)!;
        var perCapitaSelection = MetricsForPartition(cases, perCapitaObservations, DemandValidationPartitions.Selection)!;
        var selection = new DemandSpecificationMetricBundle(agiSelection, perCapitaSelection, null);
        var holdout = new DemandSpecificationMetricBundle(
            MetricsForPartition(cases, agiObservations, ValidationPartitions.Holdout),
            MetricsForPartition(cases, perCapitaObservations, ValidationPartitions.Holdout),
            null);
        var benchmark = new DemandSpecificationMetricBundle(
            MetricsForPartition(cases, agiObservations, ValidationPartitions.Benchmark),
            MetricsForPartition(cases, perCapitaObservations, ValidationPartitions.Benchmark),
            null);

        var agiSelectionObjective = ObjectiveValue(agiSelection, objective);
        var perCapitaSelectionObjective = ObjectiveValue(perCapitaSelection, objective);
        var selectedBase = agiSelectionObjective <= perCapitaSelectionObjective
            ? DemandSpecification.AgiShare
            : DemandSpecification.EligibleAdultPerCapita;
        var selectedBaseSelectionObjective = Math.Min(agiSelectionObjective, perCapitaSelectionObjective);

        ValidationMetrics? ensembleTraining = null;
        ValidationMetrics? ensembleSelection = null;
        ValidationMetrics? ensembleHoldout = null;
        ValidationMetrics? ensembleBenchmark = null;
        long? publishedParameterSetId = null;
        var ensembleAcceptedOnSelection = false;
        string? ensembleDecision = null;
        if (request.EnsembleCandidate is { } candidate)
        {
            ValidateEnsembleCandidate(candidate);
            var ensembleObservations = predictions.Select(item => new ValidationObservation(
                item.CaseKey,
                item.Observed,
                item.AgiSharePrediction * candidate.AgiShareWeight +
                item.EligibleAdultPerCapitaPrediction * candidate.EligibleAdultPerCapitaWeight)).ToArray();
            ensembleTraining = MetricsForPartition(cases, ensembleObservations, ValidationPartitions.Training);
            ensembleSelection = MetricsForPartition(cases, ensembleObservations, DemandValidationPartitions.Selection)!;
            ensembleHoldout = MetricsForPartition(cases, ensembleObservations, ValidationPartitions.Holdout);
            ensembleBenchmark = MetricsForPartition(cases, ensembleObservations, ValidationPartitions.Benchmark);
            var ensembleSelectionObjective = ObjectiveValue(ensembleSelection, objective);
            ensembleAcceptedOnSelection = ensembleSelectionObjective <= selectedBaseSelectionObjective + 1e-12;
            ensembleDecision = ensembleAcceptedOnSelection
                ? $"Accepted on selection partition: {objective} {ensembleSelectionObjective:G6} was no worse than primitive-base {selectedBaseSelectionObjective:G6}. Independent holdout was not consulted for selection."
                : $"Rejected on selection partition: {objective} {ensembleSelectionObjective:G6} exceeded primitive-base {selectedBaseSelectionObjective:G6}. Independent holdout was not consulted for selection.";

            if (ensembleAcceptedOnSelection)
            {
                var clone = await parameterSetService.CreateVersionAsync(
                    candidate.SourceParameterSetId,
                    candidate.PublishedParameterSetVersion,
                    $"[validated-demand-ensemble] Demand-specification evaluation {evaluationKey} version {version}; " +
                    $"selection-objective={objective}; selection={ensembleSelectionObjective:G17}; " +
                    $"primitive-base={selectedBase}:{selectedBaseSelectionObjective:G17}; final holdout retained independently.",
                    cancellationToken);
                await parameterSetService.SetValueAsync(
                    clone.Id,
                    DemandModelParameterInitializer.AgiShareWeightKey,
                    candidate.AgiShareWeight,
                    $"Selected on demand validation partition by evaluation {evaluationKey} {version}; holdout not used for selection.",
                    cancellationToken);
                await parameterSetService.SetValueAsync(
                    clone.Id,
                    DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey,
                    candidate.EligibleAdultPerCapitaWeight,
                    $"Selected on demand validation partition by evaluation {evaluationKey} {version}; holdout not used for selection.",
                    cancellationToken);
                clone = await db.ModelParameterSets.SingleAsync(set => set.Id == clone.Id, cancellationToken);
                clone.IsImmutable = true;
                await db.SaveChangesAsync(cancellationToken);
                publishedParameterSetId = clone.Id;
            }
        }

        training = training with { Ensemble = ensembleTraining };
        selection = selection with { Ensemble = ensembleSelection };
        holdout = holdout with { Ensemble = ensembleHoldout };
        benchmark = benchmark with { Ensemble = ensembleBenchmark };
        var largestDifferences = originDifferences
            .OrderByDescending(item => item.AbsoluteDifference)
            .ThenBy(item => item.CaseKey, StringComparer.Ordinal)
            .ThenBy(item => item.OriginKey, StringComparer.Ordinal)
            .Take(request.LargestOriginDifferenceCount)
            .ToArray();

        var evaluation = new ValidationEvaluation
        {
            EvaluationKey = evaluationKey,
            Version = version,
            ModelVersion = "gravity-v1",
            ObjectiveFunction = objective,
            Status = ValidationEvaluationStatuses.Finalized,
            PublishedParameterSetId = publishedParameterSetId,
            InclusionRulesJson = JsonSerializer.Serialize(new
            {
                evaluationKind = "demand-specification-reconciliation",
                partitionGovernance = new
                {
                    training = "May be used for underlying gravity/demand calibration.",
                    selection = "Used to select resident-demand specification and optional ensemble. Must be separate from final holdout.",
                    holdout = "Final independent check. Never used to choose specification or ensemble weights.",
                    benchmark = "Optional external benchmark partition."
                },
                rule = "Each case compares two finalized authoritative gravity runs against the case's persisted reference run. Site, program, snapshots, incumbent field, computational origins, shared parameters, execution fingerprint, and candidate routes must reconcile before observed revenue is scored.",
                pairs = predictions.Select(item => new
                {
                    item.ValidationCaseId,
                    item.CaseKey,
                    item.DatasetPartition,
                    referenceModelRunId = caseById[item.ValidationCaseId].ModelRunId,
                    item.AgiShareModelRunId,
                    item.EligibleAdultPerCapitaModelRunId,
                    item.Observed,
                    item.AgiSharePrediction,
                    item.EligibleAdultPerCapitaPrediction
                }),
                demandReconciliationByCase = originDifferences
                    .GroupBy(item => item.CaseKey, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        caseKey = group.Key,
                        agiShareTotal = group.Sum(item => item.AgiShareDemand),
                        eligibleAdultPerCapitaTotal = group.Sum(item => item.EligibleAdultPerCapitaDemand),
                        byState = group.GroupBy(item => item.StateOrTerritory, StringComparer.Ordinal)
                            .OrderBy(state => state.Key, StringComparer.Ordinal)
                            .Select(state => new
                            {
                                stateOrTerritory = state.Key,
                                agiShare = state.Sum(item => item.AgiShareDemand),
                                eligibleAdultPerCapita = state.Sum(item => item.EligibleAdultPerCapitaDemand)
                            }),
                        byDistanceBand = group.GroupBy(item => item.DistanceBand, StringComparer.Ordinal)
                            .OrderBy(band => band.Key, StringComparer.Ordinal)
                            .Select(band => new
                            {
                                distanceBand = band.Key,
                                agiShare = band.Sum(item => item.AgiShareDemand),
                                eligibleAdultPerCapita = band.Sum(item => item.EligibleAdultPerCapitaDemand)
                            })
                    }),
                largestOriginDifferences = largestDifferences
            }, JsonOptions),
            SelectedParametersJson = JsonSerializer.Serialize(new
            {
                evaluationKind = "demand-specification-reconciliation",
                selectedBaseSpecification = selectedBase == DemandSpecification.AgiShare
                    ? GravityDemandSpecifications.AgiShare
                    : GravityDemandSpecifications.EligibleAdultPerCapita,
                selectionObjectiveFunction = objective,
                selectedBaseSelectionObjectiveValue = selectedBaseSelectionObjective,
                selectionMetrics = selection,
                ensembleCandidate = request.EnsembleCandidate,
                ensembleAcceptedOnSelection,
                ensembleDecision,
                publishedEnsembleParameterSetId = publishedParameterSetId,
                holdoutWasUsedForSelection = false
            }, JsonOptions),
            TrainingMetricsJson = JsonSerializer.Serialize(training, JsonOptions),
            HoldoutMetricsJson = JsonSerializer.Serialize(holdout, JsonOptions),
            BenchmarkMetricsJson = JsonSerializer.Serialize(benchmark, JsonOptions),
            ComparableModelJson = "{}",
            ComparableTrainingMetricsJson = "{}",
            ComparableHoldoutMetricsJson = "{}",
            ComparableBenchmarkMetricsJson = "{}",
            TrainingCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Training),
            HoldoutCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Holdout),
            BenchmarkCaseCount = cases.Count(item => item.DatasetPartition == ValidationPartitions.Benchmark),
            FinalizedAtUtc = DateTime.UtcNow,
            IsImmutable = true
        };
        db.ValidationEvaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);

        return new DemandSpecificationValidationEvaluationResult(
            evaluation.Id,
            selectedBase,
            objective,
            selectedBaseSelectionObjective,
            training,
            selection,
            holdout,
            benchmark,
            publishedParameterSetId,
            ensembleAcceptedOnSelection,
            ensembleDecision,
            largestDifferences);
    }

    private ValidationMetrics? MetricsForPartition(
        IReadOnlyCollection<ValidationCase> cases,
        IReadOnlyCollection<ValidationObservation> observations,
        string partition)
    {
        var keys = cases.Where(validationCase => validationCase.DatasetPartition == partition)
            .Select(validationCase => validationCase.CaseKey)
            .ToHashSet(StringComparer.Ordinal);
        var selected = observations.Where(observation => keys.Contains(observation.CaseKey)).ToArray();
        return selected.Length == 0 ? null : metricsService.Calculate(selected);
    }

    private static void ValidatePairCompatibility(
        ValidationCase validationCase,
        ModelRun referenceRun,
        ModelRun agiRun,
        ModelRun perCapitaRun,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>> snapshotsByRun,
        IReadOnlyDictionary<Guid, int[]> incumbentIdsByRun,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, double>> parametersByRun,
        IReadOnlyDictionary<Guid, PersistedOriginRow[]> originsByRun)
    {
        RequireSameRunContext(validationCase.CaseKey, referenceRun, agiRun, "AGI-share");
        RequireSameRunContext(validationCase.CaseKey, referenceRun, perCapitaRun, "eligible-adult-per-capita");
        RequireSameRunContext(validationCase.CaseKey, agiRun, perCapitaRun, "paired specifications");

        RequireSameDictionary(validationCase.CaseKey, snapshotsByRun[referenceRun.Id], snapshotsByRun[agiRun.Id],
            "reference vs AGI-share dataset snapshots");
        RequireSameDictionary(validationCase.CaseKey, snapshotsByRun[referenceRun.Id], snapshotsByRun[perCapitaRun.Id],
            "reference vs eligible-adult dataset snapshots");
        RequireSameSequence(validationCase.CaseKey, incumbentIdsByRun[referenceRun.Id], incumbentIdsByRun[agiRun.Id],
            "reference vs AGI-share incumbent field");
        RequireSameSequence(validationCase.CaseKey, incumbentIdsByRun[referenceRun.Id], incumbentIdsByRun[perCapitaRun.Id],
            "reference vs eligible-adult incumbent field");

        var referenceOrigins = originsByRun[referenceRun.Id].Select(row => row.OriginKey).ToArray();
        var agiOrigins = originsByRun[agiRun.Id].Select(row => row.OriginKey).ToArray();
        var perCapitaOrigins = originsByRun[perCapitaRun.Id].Select(row => row.OriginKey).ToArray();
        RequireSameSequence(validationCase.CaseKey, referenceOrigins, agiOrigins, "reference vs AGI-share origins");
        RequireSameSequence(validationCase.CaseKey, referenceOrigins, perCapitaOrigins, "reference vs eligible-adult origins");

        var referenceShared = SharedParameters(parametersByRun.GetValueOrDefault(
            referenceRun.Id,
            new Dictionary<string, double>()));
        var agiShared = SharedParameters(parametersByRun.GetValueOrDefault(
            agiRun.Id,
            new Dictionary<string, double>()));
        var perCapitaShared = SharedParameters(parametersByRun.GetValueOrDefault(
            perCapitaRun.Id,
            new Dictionary<string, double>()));
        RequireSameParameterSequence(validationCase.CaseKey, referenceShared, agiShared, "reference vs AGI-share parameters");
        RequireSameParameterSequence(validationCase.CaseKey, referenceShared, perCapitaShared, "reference vs eligible-adult parameters");

        var referenceFingerprint = ReadExecutionFingerprint(referenceRun.ResolvedInputJson);
        var agiFingerprint = ReadExecutionFingerprint(agiRun.ResolvedInputJson);
        var perCapitaFingerprint = ReadExecutionFingerprint(perCapitaRun.ResolvedInputJson);
        if (referenceFingerprint != agiFingerprint || referenceFingerprint != perCapitaFingerprint)
        {
            throw new InvalidOperationException(
                $"Validation case '{validationCase.CaseKey}' has an execution fingerprint that does not match both demand-specification runs.");
        }
    }

    private static KeyValuePair<string, double>[] SharedParameters(IReadOnlyDictionary<string, double> parameters) =>
        parameters.Where(item => !DemandSpecificParameterKeys.Contains(item.Key))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

    private static void RequireSameParameterSequence(
        string caseKey,
        IReadOnlyList<KeyValuePair<string, double>> expected,
        IReadOnlyList<KeyValuePair<string, double>> actual,
        string label)
    {
        if (expected.Count != actual.Count || expected.Where((item, index) =>
                item.Key != actual[index].Key || item.Value != actual[index].Value).Any())
        {
            throw new InvalidOperationException($"Validation case '{caseKey}' does not reconcile {label}.");
        }
    }

    private static void RequireSameRunContext(string caseKey, ModelRun expected, ModelRun actual, string label)
    {
        if (expected.ModelVersion != actual.ModelVersion ||
            expected.JurisdictionId != actual.JurisdictionId ||
            expected.DevelopmentProgramId != actual.DevelopmentProgramId ||
            expected.CandidateLatitude != actual.CandidateLatitude ||
            expected.CandidateLongitude != actual.CandidateLongitude)
        {
            throw new InvalidOperationException(
                $"Validation case '{caseKey}' {label} run does not match the required model/site/program context.");
        }
    }

    private static void RequireSameDictionary<TKey, TValue>(
        string caseKey,
        IReadOnlyDictionary<TKey, TValue> expected,
        IReadOnlyDictionary<TKey, TValue> actual,
        string label)
        where TKey : notnull
        where TValue : IEquatable<TValue>
    {
        if (expected.Count != actual.Count ||
            expected.Any(item => !actual.TryGetValue(item.Key, out var value) || !item.Value.Equals(value)))
        {
            throw new InvalidOperationException($"Validation case '{caseKey}' does not reconcile {label}.");
        }
    }

    private static void RequireSameSequence<T>(
        string caseKey,
        IReadOnlyCollection<T> expected,
        IReadOnlyCollection<T> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"Validation case '{caseKey}' does not reconcile {label}.");
        }
    }

    private static OriginFacilityTravel RequireScenarioRoute(
        IReadOnlyDictionary<(Guid RunId, long OriginZoneId), OriginFacilityTravel> routes,
        Guid runId,
        long originZoneId,
        string caseKey) =>
        routes.TryGetValue((runId, originZoneId), out var route)
            ? route
            : throw new InvalidOperationException(
                $"Validation case '{caseKey}' is missing a persisted candidate route for run '{runId}' and origin '{originZoneId}'.");

    private static void RequireEquivalentRoute(
        string caseKey,
        string originKey,
        OriginFacilityTravel expected,
        OriginFacilityTravel actual)
    {
        if (expected.RouteFound != actual.RouteFound ||
            expected.RoutingGraphHash != actual.RoutingGraphHash ||
            expected.CostingProfile != actual.CostingProfile ||
            !NullableDoubleEqual(expected.TravelTimeMinutes, actual.TravelTimeMinutes) ||
            !NullableDoubleEqual(expected.RoutedDistanceMeters, actual.RoutedDistanceMeters))
        {
            throw new InvalidOperationException(
                $"Validation case '{caseKey}' has different candidate-route evidence across demand specifications for origin '{originKey}'.");
        }
    }

    private static bool NullableDoubleEqual(double? left, double? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || Math.Abs(left.Value - right!.Value) <= 1e-9);

    private static void RequireRunCoverage(
        IReadOnlyCollection<Guid> runIds,
        IEnumerable<Guid> coveredRunIds,
        string label)
    {
        var covered = coveredRunIds.ToHashSet();
        var missing = runIds.Where(runId => !covered.Contains(runId)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Demand-specification validation is missing persisted {label} for run(s): {string.Join(", ", missing)}.");
        }
    }

    private static ExecutionFingerprint ReadExecutionFingerprint(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ExecutionFingerprint(
            ReadString(root, "FacilityRegime"),
            ReadString(root, "AttractionSpecification"),
            ReadString(root, "FrictionForm"),
            ReadString(root, "ObservedMetricKey"),
            ReadString(root, "ObservedPeriodStart"),
            ReadString(root, "ObservedPeriodEnd"),
            ReadString(root, "CostingProfile"),
            ReadString(root, "EffectiveOn"),
            ReadString(root, "PopulationObservationYear"),
            ReadString(root, "IncomeTaxYear"),
            ReadString(root, "computationalOriginType"));
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }
        return string.Empty;
    }

    private static void RequireSpecification(ModelRun run, string expected)
    {
        using var document = JsonDocument.Parse(run.ResolvedInputJson);
        var actual = ReadString(document.RootElement, "DemandSpecification");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Run '{run.Id}' uses demand specification '{actual}', expected '{expected}'.");
        }
    }

    private static string DistanceBand(double? minutes, bool routeFound)
    {
        if (!routeFound || minutes is null)
        {
            return "unroutable";
        }
        return minutes.Value switch
        {
            < 30 => "0-30",
            < 60 => "30-60",
            < 90 => "60-90",
            < 120 => "90-120",
            _ => "120+"
        };
    }

    private static void ValidateEnsembleCandidate(DemandSpecificationEnsembleCandidate candidate)
    {
        if (!double.IsFinite(candidate.AgiShareWeight) ||
            !double.IsFinite(candidate.EligibleAdultPerCapitaWeight) ||
            candidate.AgiShareWeight <= 0 || candidate.AgiShareWeight >= 1 ||
            candidate.EligibleAdultPerCapitaWeight <= 0 || candidate.EligibleAdultPerCapitaWeight >= 1 ||
            Math.Abs(candidate.AgiShareWeight + candidate.EligibleAdultPerCapitaWeight - 1d) > 1e-9)
        {
            throw new ArgumentException(
                "A publishable demand ensemble requires two finite positive weights below 1.0 that sum to 1.0.",
                nameof(candidate));
        }
        if (candidate.SourceParameterSetId <= 0 || string.IsNullOrWhiteSpace(candidate.PublishedParameterSetVersion))
        {
            throw new ArgumentException(
                "A publishable demand ensemble requires a source parameter set and non-empty new version.",
                nameof(candidate));
        }
    }

    private static string NormalizeObjective(string value) => value?.Trim().ToLowerInvariant() switch
    {
        ValidationObjectiveFunctions.Mae => ValidationObjectiveFunctions.Mae,
        ValidationObjectiveFunctions.Mape => ValidationObjectiveFunctions.Mape,
        ValidationObjectiveFunctions.Smape => ValidationObjectiveFunctions.Smape,
        ValidationObjectiveFunctions.Rmse => ValidationObjectiveFunctions.Rmse,
        _ => throw new ArgumentException($"Unsupported validation objective '{value}'.", nameof(value))
    };

    internal static double ObjectiveValue(ValidationMetrics metrics, string objective) => objective switch
    {
        ValidationObjectiveFunctions.Mae => metrics.MeanAbsoluteError,
        ValidationObjectiveFunctions.Mape => metrics.MeanAbsolutePercentageError ?? double.PositiveInfinity,
        ValidationObjectiveFunctions.Smape => metrics.SymmetricMeanAbsolutePercentageError,
        ValidationObjectiveFunctions.Rmse => metrics.RootMeanSquaredError,
        _ => throw new ArgumentException($"Unsupported validation objective '{objective}'.", nameof(objective))
    };

    private static void ValidateRequest(DemandSpecificationValidationEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pairs);
        if (string.IsNullOrWhiteSpace(request.EvaluationKey) || string.IsNullOrWhiteSpace(request.Version))
        {
            throw new ArgumentException("Demand-specification evaluation requires a key and version.", nameof(request));
        }
        if (request.Pairs.Count == 0)
        {
            throw new ArgumentException("At least one paired validation case is required.", nameof(request));
        }
        if (request.Pairs.Select(pair => pair.ValidationCaseId).Distinct().Count() != request.Pairs.Count)
        {
            throw new ArgumentException(
                "A validation case may appear only once in a demand-specification evaluation.",
                nameof(request));
        }
        if (request.Pairs.Any(pair => pair.AgiShareModelRunId == pair.EligibleAdultPerCapitaModelRunId))
        {
            throw new ArgumentException("The two demand specifications must reference distinct model runs.", nameof(request));
        }
        if (request.LargestOriginDifferenceCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Largest-origin difference count must be between 1 and 1,000.");
        }
    }

    private sealed record PairedCasePrediction(
        Guid ValidationCaseId,
        string CaseKey,
        string DatasetPartition,
        double Observed,
        double AgiSharePrediction,
        double EligibleAdultPerCapitaPrediction,
        Guid AgiShareModelRunId,
        Guid EligibleAdultPerCapitaModelRunId);

    private sealed record PersistedOriginRow(
        Guid ModelRunId,
        long OriginZoneId,
        string OriginKey,
        string StateOrTerritory,
        decimal ResidentDemand);

    private sealed record ExecutionFingerprint(
        string FacilityRegime,
        string AttractionSpecification,
        string FrictionForm,
        string ObservedMetricKey,
        string ObservedPeriodStart,
        string ObservedPeriodEnd,
        string CostingProfile,
        string EffectiveOn,
        string PopulationObservationYear,
        string IncomeTaxYear,
        string ComputationalOriginType);
}
