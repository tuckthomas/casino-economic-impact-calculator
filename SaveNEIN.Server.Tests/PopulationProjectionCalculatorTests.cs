using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class PopulationProjectionCalculatorTests
{
    [Fact]
    public void PopulationScenarioYearIsIndependentOfFiscalYearAndKeepsLegacyFallback()
    {
        Assert.Equal(2030, SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.ResolveScenarioYear(2030, 2026));
        Assert.Equal(2026, SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.ResolveScenarioYear(null, 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveNEIN.Shared.Modeling.PopulationProjectionArithmetic.ResolveScenarioYear(2201, 2026));
    }

    [Theory]
    [InlineData(2020, 2026, 0.01)]
    [InlineData(2020, 2030, 0)]
    [InlineData(2026, 2020, -0.01)]
    public void ServerAndWebAssemblyShareProjectionAndConserveOriginTotals(int baselineYear, int targetYear, double growth)
    {
        var server = PopulationProjectionCalculator.Calculate(new(5, baselineYear, targetYear, growth));
        var browser = SaveNEIN.Client.Services.EconomicModelInterop.ProjectPopulation(new(5, baselineYear, targetYear, growth));
        var multiplier = SaveNEIN.Client.Services.EconomicModelInterop.ProjectPopulation(new(1, baselineYear, targetYear, growth)).ProjectedPopulation;
        Assert.Equal(server.ProjectedPopulation, browser.ProjectedPopulation);
        Assert.Equal(server.MethodKey, browser.MethodKey);
        Assert.Equal(server.ProjectedPopulation, Enumerable.Repeat(multiplier, 5).Sum(), 12);
    }

    [Fact]
    public void Calculate_CompoundsObservedPopulationToScenarioYear()
    {
        var result = PopulationProjectionCalculator.Calculate(new PopulationProjectionInput(
            100_000,
            2022,
            2027,
            0.01));

        Assert.Equal(105_101.00501, result.ProjectedPopulation, 8);
        Assert.Equal(5, result.ProjectionYears);
        Assert.Equal(PopulationProjectionCalculator.CompoundAnnualGrowthMethod, result.MethodKey);
    }

    [Fact]
    public void Calculate_PreservesObservedPopulationUnderExplicitNoGrowthDefault()
    {
        var result = PopulationProjectionCalculator.Calculate(new PopulationProjectionInput(
            125_000,
            2022,
            2030,
            0));

        Assert.Equal(125_000, result.ProjectedPopulation);
        Assert.Equal(PopulationProjectionCalculator.NoGrowthMethod, result.MethodKey);
    }

    [Fact]
    public void Calculate_SupportsExplicitBackcastWithoutChangingTheFormula()
    {
        var result = PopulationProjectionCalculator.Calculate(new PopulationProjectionInput(
            110_408.08032,
            2027,
            2022,
            0.02));

        Assert.Equal(100_000, result.ProjectedPopulation, 6);
        Assert.Equal(-5, result.ProjectionYears);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.01)]
    public void Calculate_RejectsUnsafeGrowthRates(double annualGrowthRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PopulationProjectionCalculator.Calculate(
            new PopulationProjectionInput(100, 2022, 2030, annualGrowthRate)));
    }
}
