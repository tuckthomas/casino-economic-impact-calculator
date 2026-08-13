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
    public const string CompoundAnnualGrowthMethod = "compound-annual-growth-v1";
    public const string NoGrowthMethod = "constant-population-v1";

    public static PopulationProjectionResult Calculate(PopulationProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!double.IsFinite(input.ObservedPopulation) || input.ObservedPopulation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Observed population must be finite and nonnegative.");
        }
        if (input.ObservationYear is < 1900 or > 2200 || input.ScenarioYear is < 1900 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Observation and scenario years must be between 1900 and 2200.");
        }
        if (!double.IsFinite(input.AnnualGrowthRate) || input.AnnualGrowthRate <= -1 || input.AnnualGrowthRate > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Annual population growth must be finite and greater than -100% and no more than 100%.");
        }

        var projectionYears = input.ScenarioYear - input.ObservationYear;
        if (Math.Abs(projectionYears) > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Population projection and backcast horizons cannot exceed 100 years.");
        }

        var projectedPopulation = input.ObservedPopulation * Math.Pow(1 + input.AnnualGrowthRate, projectionYears);
        if (!double.IsFinite(projectedPopulation) || projectedPopulation < 0)
        {
            throw new InvalidOperationException("Population projection produced an invalid result.");
        }

        return new PopulationProjectionResult(
            input.ObservedPopulation,
            projectedPopulation,
            input.ObservationYear,
            input.ScenarioYear,
            projectionYears,
            input.AnnualGrowthRate,
            input.AnnualGrowthRate == 0 ? NoGrowthMethod : CompoundAnnualGrowthMethod);
    }
}
