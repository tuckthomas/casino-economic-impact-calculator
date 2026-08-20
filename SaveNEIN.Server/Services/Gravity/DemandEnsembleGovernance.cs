// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public static class DemandEnsembleGovernance
{
    private const double WeightTolerance = 1e-9;

    public static (double AgiShareWeight, double EligibleAdultPerCapitaWeight) ResolveWeights(
        IReadOnlyDictionary<string, double> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(DemandModelParameterInitializer.AgiShareWeightKey, out var agiWeight) ||
            !parameters.TryGetValue(DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey, out var perCapitaWeight))
        {
            throw new InvalidOperationException("Validated demand-ensemble weight parameters are missing.");
        }
        ValidateWeights(agiWeight, perCapitaWeight);
        return (agiWeight, perCapitaWeight);
    }

    public static double Combine(
        double agiShareDemand,
        double eligibleAdultPerCapitaDemand,
        double agiShareWeight,
        double eligibleAdultPerCapitaWeight)
    {
        OriginDemandService.RequireNonNegativeFinite(agiShareDemand, nameof(agiShareDemand));
        OriginDemandService.RequireNonNegativeFinite(
            eligibleAdultPerCapitaDemand,
            nameof(eligibleAdultPerCapitaDemand));
        ValidateWeights(agiShareWeight, eligibleAdultPerCapitaWeight);
        var demand = agiShareDemand * agiShareWeight +
                     eligibleAdultPerCapitaDemand * eligibleAdultPerCapitaWeight;
        OriginDemandService.RequireFiniteResult(demand, "validated demand ensemble");
        return demand;
    }

    public static async Task ValidatePublishedParameterResolutionAsync(
        AppDbContext db,
        IReadOnlyCollection<ResolvedModelParameter> resolved,
        IReadOnlyCollection<(long Id, string Layer)> selectedSets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(selectedSets);

        var agi = resolved.SingleOrDefault(parameter =>
            parameter.Definition.Key == DemandModelParameterInitializer.AgiShareWeightKey)
            ?? throw new InvalidOperationException("Validated demand-ensemble AGI-share weight was not resolved.");
        var perCapita = resolved.SingleOrDefault(parameter =>
            parameter.Definition.Key == DemandModelParameterInitializer.EligibleAdultPerCapitaWeightKey)
            ?? throw new InvalidOperationException("Validated demand-ensemble eligible-adult weight was not resolved.");
        ValidateWeights(agi.FinalValue, perCapita.FinalValue);

        if (agi.SourceLayer is "system-fallback" or "user-override" ||
            perCapita.SourceLayer is "system-fallback" or "user-override")
        {
            throw new InvalidOperationException(
                "Validated-ensemble execution rejects system-fallback and user-overridden weights; both weights must resolve from one validation-published parameter set.");
        }
        if (!string.Equals(agi.SourceLayer, perCapita.SourceLayer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Validated demand-ensemble weights must resolve from the same parameter-set layer.");
        }

        var sourceSets = selectedSets
            .Where(selection => selection.Layer == agi.SourceLayer)
            .Select(selection => selection.Id)
            .Distinct()
            .ToArray();
        if (sourceSets.Length != 1)
        {
            throw new InvalidOperationException(
                "Validated demand-ensemble weights could not be traced to exactly one selected parameter set.");
        }
        var sourceSetId = sourceSets[0];
        var evaluation = await db.ValidationEvaluations.AsNoTracking()
            .Where(item => item.Status == ValidationEvaluationStatuses.Finalized &&
                           item.IsImmutable &&
                           item.PublishedParameterSetId == sourceSetId)
            .OrderByDescending(item => item.FinalizedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (evaluation is null)
        {
            throw new InvalidOperationException(
                $"Parameter set '{sourceSetId}' was not published by a finalized immutable validation evaluation and cannot enable validated-ensemble demand.");
        }

        using var selectionDocument = JsonDocument.Parse(evaluation.SelectedParametersJson);
        var root = selectionDocument.RootElement;
        if (!TryReadString(root, "evaluationKind", out var kind) ||
            !string.Equals(kind, "demand-specification-reconciliation", StringComparison.Ordinal) ||
            !TryReadBoolean(root, "ensembleAccepted", out var ensembleAccepted) ||
            !ensembleAccepted ||
            !TryReadInt64(root, "publishedEnsembleParameterSetId", out var publishedSetId) ||
            publishedSetId != sourceSetId)
        {
            throw new InvalidOperationException(
                $"Validation evaluation '{evaluation.Id}' did not accept and publish parameter set '{sourceSetId}' as a demand ensemble.");
        }
    }

    public static void ValidateWeights(double agiShareWeight, double eligibleAdultPerCapitaWeight)
    {
        if (!double.IsFinite(agiShareWeight) || !double.IsFinite(eligibleAdultPerCapitaWeight) ||
            agiShareWeight <= 0 || agiShareWeight >= 1 ||
            eligibleAdultPerCapitaWeight <= 0 || eligibleAdultPerCapitaWeight >= 1 ||
            Math.Abs(agiShareWeight + eligibleAdultPerCapitaWeight - 1d) > WeightTolerance)
        {
            throw new InvalidOperationException(
                "Validated demand-ensemble weights must be finite, strictly between zero and one, and sum to 1.0.");
        }
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static bool TryReadBoolean(JsonElement root, string propertyName, out bool value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.Value.GetBoolean();
                return true;
            }
        }
        value = false;
        return false;
    }

    private static bool TryReadInt64(JsonElement root, string propertyName, out long value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.TryGetInt64(out value))
            {
                return true;
            }
        }
        value = 0;
        return false;
    }
}
