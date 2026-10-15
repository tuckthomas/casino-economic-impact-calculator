// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json.Serialization;

namespace SaveNEIN.Shared.Modeling;

public sealed record PopulationProjectionRequest(
    [property: JsonPropertyName("observedPopulation")] double ObservedPopulation,
    [property: JsonPropertyName("observationYear")] int ObservationYear,
    [property: JsonPropertyName("scenarioYear")] int ScenarioYear,
    [property: JsonPropertyName("annualGrowthRate")] double AnnualGrowthRate);

public sealed record PopulationProjectionOutput(
    [property: JsonPropertyName("observedPopulation")] double ObservedPopulation,
    [property: JsonPropertyName("projectedPopulation")] double ProjectedPopulation,
    [property: JsonPropertyName("observationYear")] int ObservationYear,
    [property: JsonPropertyName("scenarioYear")] int ScenarioYear,
    [property: JsonPropertyName("projectionYears")] int ProjectionYears,
    [property: JsonPropertyName("annualGrowthRate")] double AnnualGrowthRate,
    [property: JsonPropertyName("methodKey")] string MethodKey);

public static class PopulationProjectionArithmetic
{
    public const string CompoundAnnualGrowthMethod = "compound-annual-growth-v1";
    public const string NoGrowthMethod = "constant-population-v1";

    public static int ResolveScenarioYear(int? populationScenarioYear, int effectiveYear)
    {
        var year = populationScenarioYear ?? effectiveYear;
        if (year is < 1900 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(populationScenarioYear), "Population scenario year must be between 1900 and 2200.");
        return year;
    }

    public static PopulationProjectionOutput Calculate(PopulationProjectionRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!double.IsFinite(input.ObservedPopulation) || input.ObservedPopulation < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Observed population must be finite and nonnegative.");
        if (input.ObservationYear is < 1900 or > 2200 || input.ScenarioYear is < 1900 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(input), "Observation and scenario years must be between 1900 and 2200.");
        if (!double.IsFinite(input.AnnualGrowthRate) || input.AnnualGrowthRate <= -1 || input.AnnualGrowthRate > 1)
            throw new ArgumentOutOfRangeException(nameof(input), "Annual population growth must be finite and greater than -100% and no more than 100%.");

        var years = input.ScenarioYear - input.ObservationYear;
        if (Math.Abs(years) > 100)
            throw new ArgumentOutOfRangeException(nameof(input), "Population projection and backcast horizons cannot exceed 100 years.");
        var projected = input.ObservedPopulation * Math.Pow(1 + input.AnnualGrowthRate, years);
        if (!double.IsFinite(projected) || projected < 0)
            throw new InvalidOperationException("Population projection produced an invalid result.");
        return new(input.ObservedPopulation, projected, input.ObservationYear, input.ScenarioYear, years,
            input.AnnualGrowthRate, input.AnnualGrowthRate == 0 ? NoGrowthMethod : CompoundAnnualGrowthMethod);
    }
}
