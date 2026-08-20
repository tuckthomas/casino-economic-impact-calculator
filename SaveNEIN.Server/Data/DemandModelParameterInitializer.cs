// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Data;

/// <summary>
/// Seeds parameter definitions that are intentionally unusable from their system
/// fallback in validated-ensemble mode. The gravity executor separately requires
/// the resolved weights to originate from a parameter set published by a finalized
/// demand-specification validation evaluation.
/// </summary>
public static class DemandModelParameterInitializer
{
    public const string AgiShareWeightKey = "demand.ensemble_agi_share_weight";
    public const string EligibleAdultPerCapitaWeightKey = "demand.ensemble_eligible_adult_per_capita_weight";

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var seeds = new[]
        {
            Definition(
                AgiShareWeightKey,
                "Validated ensemble AGI-share weight",
                1d,
                "Weight assigned to the AGI-share resident-demand specification inside a validation-published convex demand ensemble."),
            Definition(
                EligibleAdultPerCapitaWeightKey,
                "Validated ensemble eligible-adult weight",
                0d,
                "Weight assigned to the eligible-adult per-capita resident-demand specification inside a validation-published convex demand ensemble.")
        };

        var existing = await db.ModelParameterDefinitions
            .Where(definition => definition.Key == AgiShareWeightKey ||
                                 definition.Key == EligibleAdultPerCapitaWeightKey)
            .ToDictionaryAsync(definition => definition.Key, StringComparer.Ordinal, cancellationToken);

        foreach (var seed in seeds)
        {
            if (!existing.TryGetValue(seed.Key, out var stored))
            {
                db.ModelParameterDefinitions.Add(seed);
                continue;
            }

            stored.Category = seed.Category;
            stored.DisplayName = seed.DisplayName;
            stored.TechnicalDescription = seed.TechnicalDescription;
            stored.PlainLanguageDescription = seed.PlainLanguageDescription;
            stored.Units = seed.Units;
            stored.DataType = seed.DataType;
            stored.SystemDefaultValue = seed.SystemDefaultValue;
            stored.ComputationalMinimum = seed.ComputationalMinimum;
            stored.ComputationalMaximum = seed.ComputationalMaximum;
            stored.RecommendedMinimum = seed.RecommendedMinimum;
            stored.RecommendedMaximum = seed.RecommendedMaximum;
            stored.UiStep = seed.UiStep;
            stored.UiExposureLevel = seed.UiExposureLevel;
            stored.IsUserOverridable = seed.IsUserOverridable;
            stored.ModelVersionApplicability = seed.ModelVersionApplicability;
            stored.ProvenanceNotes = seed.ProvenanceNotes;
            stored.IsCalibrated = seed.IsCalibrated;
            stored.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ModelParameterDefinition Definition(
        string key,
        string displayName,
        double systemDefault,
        string description) => new()
        {
            Key = key,
            Category = "demand",
            DisplayName = displayName,
            TechnicalDescription = description +
                " The two ensemble weights must be finite, nonnegative, sum to 1.0, and resolve from the same parameter set published by a finalized demand-specification validation evaluation.",
            PlainLanguageDescription = description,
            Units = "share",
            DataType = "number",
            SystemDefaultValue = systemDefault,
            ComputationalMinimum = 0,
            ComputationalMaximum = 1,
            RecommendedMinimum = 0,
            RecommendedMaximum = 1,
            UiStep = 0.01,
            UiExposureLevel = "expert",
            IsUserOverridable = false,
            ModelVersionApplicability = "gravity-v1",
            ProvenanceNotes =
                "The system fallback exists only so parameter resolution remains total. Validated-ensemble execution rejects system fallback, user override, mixed-layer weights, and any parameter set not published by a finalized demand-specification validation evaluation.",
            IsCalibrated = false,
            IsActive = true
        };
}
