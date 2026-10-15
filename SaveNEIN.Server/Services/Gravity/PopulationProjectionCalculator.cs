// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Gravity;

public sealed record PopulationProjectionInput(
    double ObservedPopulation,
    int ObservationYear,
    int ScenarioYear,
    double AnnualGrowthRate);

public sealed record PopulationProjectionResult(
    double ObservedPopulation,
    double ProjectedPopulation,
    int ObservationYear,
    int ScenarioYear,
    int ProjectionYears,
    double AnnualGrowthRate,
    string MethodKey);

public static class PopulationProjectionCalculator
{
    public const string CompoundAnnualGrowthMethod = SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.CompoundAnnualGrowthMethod;
    public const string NoGrowthMethod = SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.NoGrowthMethod;

    public static PopulationProjectionResult Calculate(PopulationProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.Calculate(new(
            input.ObservedPopulation, input.ObservationYear, input.ScenarioYear, input.AnnualGrowthRate));
        return new(result.ObservedPopulation, result.ProjectedPopulation, result.ObservationYear,
            result.ScenarioYear, result.ProjectionYears, result.AnnualGrowthRate, result.MethodKey);
    }
}
