// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record LocalInventoryWeightResolution(
    IReadOnlyDictionary<string, double> Modifiers,
    string WeightBasis);

public sealed record LocalLaborAssumptionResolution(
    double DirectAverageAnnualWage,
    double IndirectAverageAnnualWage,
    double IncumbentAverageAnnualWage,
    string AssumptionBasis,
    IReadOnlyCollection<string> Warnings);

public interface ILocalEconomicInventoryWeightService
{
    LocalInventoryWeightResolution Resolve(
        IReadOnlyCollection<LocalEconomicSectorObservation> observations,
        string scopeKind,
        string scopeCode,
        IReadOnlyDictionary<string, double> priors,
        IReadOnlyDictionary<string, double> configuredModifiers,
        bool inventorySnapshotSelected);

    LocalLaborAssumptionResolution ResolveLaborAssumptions(
        IReadOnlyCollection<LocalEconomicSectorObservation> observations,
        string scopeKind,
        string scopeCode,
        double directAverageAnnualWageFallback,
        double indirectAverageAnnualWageFallback,
        double incumbentAverageAnnualWageFallback,
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

    public LocalLaborAssumptionResolution ResolveLaborAssumptions(
        IReadOnlyCollection<LocalEconomicSectorObservation> observations,
        string scopeKind,
        string scopeCode,
        double directAverageAnnualWageFallback,
        double indirectAverageAnnualWageFallback,
        double incumbentAverageAnnualWageFallback,
        bool inventorySnapshotSelected)
    {
        var fallbackValues = new[]
        {
            directAverageAnnualWageFallback,
            indirectAverageAnnualWageFallback,
            incumbentAverageAnnualWageFallback
        };
        if (fallbackValues.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(directAverageAnnualWageFallback),
                "Labor-assumption fallbacks must be finite and nonnegative.");
        }
        if (!inventorySnapshotSelected)
        {
            return new LocalLaborAssumptionResolution(
                directAverageAnnualWageFallback,
                indirectAverageAnnualWageFallback,
                incumbentAverageAnnualWageFallback,
                "versioned-parameter-fallbacks",
                []);
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

        static double? AverageAnnualWage(
            IReadOnlyCollection<LocalEconomicSectorObservation> source,
            string sectorKey)
        {
            var sector = source.Where(observation =>
                    string.Equals(observation.SectorKey, sectorKey, StringComparison.OrdinalIgnoreCase) &&
                    observation.AnnualPayroll is > 0 && observation.Employment is > 0)
                .ToArray();
            if (sector.Length == 0)
            {
                return null;
            }
            var employment = sector.Sum(observation => observation.Employment!.Value);
            var payroll = sector.Sum(observation => observation.AnnualPayroll!.Value);
            return employment <= 0 || payroll <= 0 ? null : Convert.ToDouble(payroll / employment);
        }

        var casinoWage = AverageAnnualWage(matching, LocalEconomicSectorKeys.CasinoGambling);
        var allIndustriesWage = AverageAnnualWage(matching, LocalEconomicSectorKeys.AllIndustries);
        var warnings = new List<string>();
        if (casinoWage is null)
        {
            warnings.Add(
                $"The selected local-economic inventory has no positive payroll/employment pair for sector " +
                $"'{LocalEconomicSectorKeys.CasinoGambling}' at '{scopeKind}:{scopeCode}'; direct and incumbent " +
                "casino wage assumptions remain at their versioned parameter values.");
        }
        if (allIndustriesWage is null)
        {
            warnings.Add(
                $"The selected local-economic inventory has no positive payroll/employment pair for sector " +
                $"'{LocalEconomicSectorKeys.AllIndustries}' at '{scopeKind}:{scopeCode}'; the indirect/induced " +
                "wage assumption remains at its versioned parameter value.");
        }
        return new LocalLaborAssumptionResolution(
            casinoWage ?? directAverageAnnualWageFallback,
            allIndustriesWage ?? indirectAverageAnnualWageFallback,
            casinoWage ?? incumbentAverageAnnualWageFallback,
            $"provider-snapshot:annual-payroll-per-employee:{scopeKind}:{scopeCode};" +
            $"direct={(casinoWage is null ? "parameter" : LocalEconomicSectorKeys.CasinoGambling)};" +
            $"indirect={(allIndustriesWage is null ? "parameter" : LocalEconomicSectorKeys.AllIndustries)};" +
            $"incumbent={(casinoWage is null ? "parameter" : LocalEconomicSectorKeys.CasinoGambling)}",
            warnings);
    }
}
