// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record DemandSpecificationReconciliationOrigin(
    string OriginKey,
    string StateOrTerritory,
    string DistanceBand,
    AgiShareDemandInput AgiShareInput,
    PerCapitaDemandInput EligibleAdultPerCapitaInput);

public sealed record DemandSpecificationValidationPerformance(
    DemandSpecification Specification,
    ValidationMetrics HoldoutMetrics);

public sealed record DemandEnsembleDefinition(
    string Version,
    double AgiShareWeight,
    double EligibleAdultPerCapitaWeight,
    bool IsValidated);

public sealed record DemandSpecificationAggregate(
    DemandSpecification Specification,
    double TotalDemand,
    IReadOnlyDictionary<string, double> StateTotals,
    IReadOnlyDictionary<string, double> DistanceBandTotals);

public sealed record DemandSpecificationOriginDifference(
    string OriginKey,
    string StateOrTerritory,
    string DistanceBand,
    double AgiShareDemand,
    double EligibleAdultPerCapitaDemand,
    double SignedDifference,
    double AbsoluteDifference);

public sealed record DemandSpecificationSelection(
    DemandSpecification Specification,
    string ObjectiveFunction,
    double ObjectiveValue);

public sealed record DemandSpecificationReconciliationRequest(
    IReadOnlyCollection<DemandSpecificationReconciliationOrigin> Origins,
    IReadOnlyCollection<DemandSpecificationValidationPerformance>? ValidationPerformance = null,
    string SelectionObjective = "mape",
    DemandEnsembleDefinition? Ensemble = null,
    int LargestOriginDifferenceCount = 10);

public sealed record DemandSpecificationReconciliationResult(
    DemandSpecificationAggregate AgiShare,
    DemandSpecificationAggregate EligibleAdultPerCapita,
    IReadOnlyList<DemandSpecificationOriginDifference> LargestOriginDifferences,
    DemandSpecificationSelection? SelectedBase,
    string? EnsembleVersion,
    IReadOnlyDictionary<string, double>? EnsembleDemandByOrigin,
    double? EnsembleTotalDemand);

public interface IDemandSpecificationReconciliationService
{
    DemandSpecificationReconciliationResult Reconcile(DemandSpecificationReconciliationRequest request);
}

/// <summary>
/// Validation-only comparison of alternative resident-demand specifications.
/// This service deliberately does not mutate a model run or combine the two
/// specifications as separate demand pools. A validated ensemble, when used,
/// is a convex combination whose weights must sum to one.
/// </summary>
public sealed class DemandSpecificationReconciliationService(
    IOriginDemandService originDemandService) : IDemandSpecificationReconciliationService
{
    private const double WeightTolerance = 1e-9;

    public DemandSpecificationReconciliationResult Reconcile(
        DemandSpecificationReconciliationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Origins);
        if (request.Origins.Count == 0)
        {
            throw new ArgumentException("At least one origin is required for demand-specification reconciliation.", nameof(request));
        }
        if (request.LargestOriginDifferenceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Largest-origin difference count must be positive.");
        }

        var origins = request.Origins.ToArray();
        ValidateOrigins(origins);

        var evaluated = origins.Select(origin =>
        {
            var agi = originDemandService.CalculateAgiShare(origin.AgiShareInput);
            var perCapita = originDemandService.CalculatePerCapita(origin.EligibleAdultPerCapitaInput);
            return new EvaluatedOrigin(origin, agi, perCapita);
        }).ToArray();

        var agiAggregate = Aggregate(
            DemandSpecification.AgiShare,
            evaluated,
            item => item.AgiShare.Demand);
        var perCapitaAggregate = Aggregate(
            DemandSpecification.EligibleAdultPerCapita,
            evaluated,
            item => item.EligibleAdultPerCapita.Demand);

        var largestDifferences = evaluated
            .Select(item =>
            {
                var signed = item.EligibleAdultPerCapita.Demand - item.AgiShare.Demand;
                return new DemandSpecificationOriginDifference(
                    item.Origin.OriginKey,
                    item.Origin.StateOrTerritory,
                    item.Origin.DistanceBand,
                    item.AgiShare.Demand,
                    item.EligibleAdultPerCapita.Demand,
                    signed,
                    Math.Abs(signed));
            })
            .OrderByDescending(item => item.AbsoluteDifference)
            .ThenBy(item => item.OriginKey, StringComparer.Ordinal)
            .Take(request.LargestOriginDifferenceCount)
            .ToArray();

        var selectedBase = SelectBase(request.ValidationPerformance, request.SelectionObjective);
        var (ensembleVersion, ensembleDemandByOrigin, ensembleTotalDemand) =
            BuildValidatedEnsemble(evaluated, request.Ensemble);

        return new DemandSpecificationReconciliationResult(
            agiAggregate,
            perCapitaAggregate,
            largestDifferences,
            selectedBase,
            ensembleVersion,
            ensembleDemandByOrigin,
            ensembleTotalDemand);
    }

    private static void ValidateOrigins(IReadOnlyCollection<DemandSpecificationReconciliationOrigin> origins)
    {
        if (origins.Select(origin => origin.OriginKey)
            .Distinct(StringComparer.Ordinal)
            .Count() != origins.Count)
        {
            throw new ArgumentException("Origin keys must be unique within a reconciliation request.", nameof(origins));
        }

        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin.OriginKey) ||
                string.IsNullOrWhiteSpace(origin.StateOrTerritory) ||
                string.IsNullOrWhiteSpace(origin.DistanceBand))
            {
                throw new ArgumentException(
                    "Each reconciliation origin requires an origin key, state/territory, and distance band.",
                    nameof(origins));
            }
            if (!StringComparer.Ordinal.Equals(origin.OriginKey, origin.AgiShareInput.OriginKey) ||
                !StringComparer.Ordinal.Equals(origin.OriginKey, origin.EligibleAdultPerCapitaInput.OriginKey))
            {
                throw new ArgumentException(
                    $"Origin '{origin.OriginKey}' must use the same key in both demand-specification inputs.",
                    nameof(origins));
            }
        }
    }

    private static DemandSpecificationAggregate Aggregate(
        DemandSpecification specification,
        IReadOnlyCollection<EvaluatedOrigin> origins,
        Func<EvaluatedOrigin, double> demandSelector)
    {
        var total = origins.Sum(demandSelector);
        OriginDemandService.RequireFiniteResult(total, $"{specification} aggregate demand");

        var stateTotals = origins
            .GroupBy(item => item.Origin.StateOrTerritory, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(demandSelector),
                StringComparer.Ordinal);
        var distanceBandTotals = origins
            .GroupBy(item => item.Origin.DistanceBand, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(demandSelector),
                StringComparer.Ordinal);

        if (stateTotals.Values.Any(value => !double.IsFinite(value)) ||
            distanceBandTotals.Values.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidOperationException($"{specification} grouped demand produced a non-finite result.");
        }

        return new DemandSpecificationAggregate(
            specification,
            total,
            stateTotals,
            distanceBandTotals);
    }

    private static DemandSpecificationSelection? SelectBase(
        IReadOnlyCollection<DemandSpecificationValidationPerformance>? validationPerformance,
        string objectiveFunction)
    {
        if (validationPerformance is null || validationPerformance.Count == 0)
        {
            return null;
        }

        var performance = validationPerformance.ToArray();
        if (performance.Select(item => item.Specification).Distinct().Count() != performance.Length)
        {
            throw new ArgumentException(
                "Validation performance may contain at most one holdout metric set per demand specification.",
                nameof(validationPerformance));
        }
        if (performance.Length != 2 ||
            !performance.Any(item => item.Specification == DemandSpecification.AgiShare) ||
            !performance.Any(item => item.Specification == DemandSpecification.EligibleAdultPerCapita))
        {
            throw new ArgumentException(
                "Base-specification selection requires holdout performance for both demand specifications.",
                nameof(validationPerformance));
        }

        var objective = NormalizeObjective(objectiveFunction);
        var scored = performance.Select(item => new DemandSpecificationSelection(
                item.Specification,
                objective,
                ObjectiveValue(item.HoldoutMetrics, objective)))
            .ToArray();
        if (scored.Any(item => !double.IsFinite(item.ObjectiveValue)))
        {
            throw new InvalidOperationException(
                $"Objective '{objective}' is unavailable or non-finite for one or more demand specifications.");
        }

        return scored
            .OrderBy(item => item.ObjectiveValue)
            .ThenBy(item => item.Specification)
            .First();
    }

    private static (string? Version, IReadOnlyDictionary<string, double>? ByOrigin, double? Total)
        BuildValidatedEnsemble(
            IReadOnlyCollection<EvaluatedOrigin> origins,
            DemandEnsembleDefinition? ensemble)
    {
        if (ensemble is null)
        {
            return (null, null, null);
        }
        if (!ensemble.IsValidated)
        {
            throw new InvalidOperationException(
                "An ensemble may be used only after its weights have been validated and versioned.");
        }
        if (string.IsNullOrWhiteSpace(ensemble.Version))
        {
            throw new ArgumentException("A validated demand ensemble requires a non-empty version.", nameof(ensemble));
        }
        RequireFiniteNonNegativeWeight(ensemble.AgiShareWeight, nameof(ensemble.AgiShareWeight));
        RequireFiniteNonNegativeWeight(
            ensemble.EligibleAdultPerCapitaWeight,
            nameof(ensemble.EligibleAdultPerCapitaWeight));

        var weightSum = ensemble.AgiShareWeight + ensemble.EligibleAdultPerCapitaWeight;
        if (Math.Abs(weightSum - 1d) > WeightTolerance)
        {
            throw new ArgumentException(
                "Validated demand-ensemble weights must sum to 1.0 so alternative specifications are not added as separate demand pools.",
                nameof(ensemble));
        }

        var byOrigin = origins
            .OrderBy(item => item.Origin.OriginKey, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Origin.OriginKey,
                item => item.AgiShare.Demand * ensemble.AgiShareWeight +
                        item.EligibleAdultPerCapita.Demand * ensemble.EligibleAdultPerCapitaWeight,
                StringComparer.Ordinal);
        if (byOrigin.Values.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new InvalidOperationException("Validated demand ensemble produced an invalid origin demand value.");
        }

        var total = byOrigin.Values.Sum();
        OriginDemandService.RequireFiniteResult(total, "validated demand-ensemble total");
        return (ensemble.Version.Trim(), byOrigin, total);
    }

    private static string NormalizeObjective(string objectiveFunction)
    {
        if (string.IsNullOrWhiteSpace(objectiveFunction))
        {
            throw new ArgumentException("A validation selection objective is required.", nameof(objectiveFunction));
        }

        return objectiveFunction.Trim().ToLowerInvariant() switch
        {
            "mae" => "mae",
            "mape" => "mape",
            "smape" => "smape",
            "rmse" => "rmse",
            _ => throw new ArgumentException(
                $"Unsupported validation selection objective '{objectiveFunction}'.",
                nameof(objectiveFunction))
        };
    }

    private static double ObjectiveValue(ValidationMetrics metrics, string objectiveFunction) =>
        objectiveFunction switch
        {
            "mae" => metrics.MeanAbsoluteError,
            "mape" => metrics.MeanAbsolutePercentageError ?? double.PositiveInfinity,
            "smape" => metrics.SymmetricMeanAbsolutePercentageError,
            "rmse" => metrics.RootMeanSquaredError,
            _ => throw new InvalidOperationException($"Unsupported normalized objective '{objectiveFunction}'.")
        };

    private static void RequireFiniteNonNegativeWeight(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A finite, nonnegative ensemble weight is required.");
        }
    }

    private sealed record EvaluatedOrigin(
        DemandSpecificationReconciliationOrigin Origin,
        OriginDemandResult AgiShare,
        OriginDemandResult EligibleAdultPerCapita);
}
