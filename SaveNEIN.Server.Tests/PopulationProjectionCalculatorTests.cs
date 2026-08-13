using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class PopulationProjectionCalculatorTests
{
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
