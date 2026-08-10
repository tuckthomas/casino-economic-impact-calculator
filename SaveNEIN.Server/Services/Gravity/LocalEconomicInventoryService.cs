// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record LocalInventoryWeightResolution(
    IReadOnlyDictionary<string, double> Modifiers,
    string WeightBasis);

public interface ILocalEconomicInventoryWeightService
{
    LocalInventoryWeightResolution Resolve(
        IReadOnlyCollection<LocalEconomicSectorObservation> observations,
        string scopeKind,
        string scopeCode,
        IReadOnlyDictionary<string, double> priors,
        IReadOnlyDictionary<string, double> configuredModifiers,
        bool inventorySnapshotSelected);
}

public sealed class LocalEconomicInventoryWeightService : ILocalEconomicInventoryWeightService
{
    public LocalInventoryWeightResolution Resolve(
        IReadOnlyCollection<LocalEconomicSectorObservation> observations,
        string scopeKind,
        string scopeCode,
        IReadOnlyDictionary<string, double> priors,
        IReadOnlyDictionary<string, double> configuredModifiers,
        bool inventorySnapshotSelected)
    {
        if (!inventorySnapshotSelected)
        {
            return new LocalInventoryWeightResolution(configuredModifiers, "parameterized-prior-modifiers");
        }

        var matching = observations.Where(observation =>
                string.Equals(observation.GeographyType, scopeKind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(observation.GeographyCode, scopeCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matching.Length == 0)
        {
            throw new InvalidOperationException(
                $"The selected local-economic inventory has no observations for impact geography " +
                $"'{scopeKind}:{scopeCode}'.");
        }

        var bySector = matching
            .GroupBy(observation => observation.SectorKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var missingSectors = priors.Keys.Where(sector => !bySector.ContainsKey(sector)).ToArray();
        if (missingSectors.Length > 0)
        {
            throw new InvalidOperationException(
                $"The selected local-economic inventory is missing displacement sector(s) for " +
                $"'{scopeKind}:{scopeCode}': {string.Join(", ", missingSectors)}.");
        }

        var candidates = new (string Key, Func<LocalEconomicSectorObservation, double?> Selector)[]
        {
            ("annual-receipts-or-sales", observation => observation.AnnualReceiptsOrSales is { } value ? Convert.ToDouble(value) : null),
            ("annual-payroll", observation => observation.AnnualPayroll is { } value ? Convert.ToDouble(value) : null),
            ("employment", observation => observation.Employment is { } value ? value : null),
            ("establishments", observation => observation.Establishments is { } value ? value : null)
        };

        foreach (var candidate in candidates)
        {
            var amounts = new Dictionary<string, double>(StringComparer.Ordinal);
            var complete = true;
            foreach (var sector in priors.Keys)
            {
                var values = bySector[sector]
                    .Select(candidate.Selector)
                    .Where(value => value is not null)
                    .Select(value => value!.Value)
                    .ToArray();
                var amount = values.Sum();
                if (values.Length == 0 || !double.IsFinite(amount) || amount <= 0)
                {
                    complete = false;
                    break;
                }
                amounts[sector] = amount;
            }
            if (!complete)
            {
                continue;
            }

            var modifiers = amounts.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var prior = priors[pair.Key];
                    if (!double.IsFinite(prior) || prior <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Displacement prior for sector '{pair.Key}' must be positive when an inventory snapshot is selected.");
                    }
                    return pair.Value / prior;
                },
                StringComparer.Ordinal);
            return new LocalInventoryWeightResolution(
                modifiers,
                $"provider-snapshot:{candidate.Key}:{scopeKind}:{scopeCode}");
        }

        throw new InvalidOperationException(
            "The selected local-economic inventory does not contain one complete positive measure " +
            "(receipts/sales, payroll, employment, or establishments) across all displacement sectors.");
    }
}
