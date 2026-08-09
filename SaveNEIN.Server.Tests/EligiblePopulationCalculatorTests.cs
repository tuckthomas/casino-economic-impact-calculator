using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class EligiblePopulationCalculatorTests
{
    [Fact]
    public void Calculate_SumsExactLegalAgeBoundaryWithoutInterpolation()
    {
        var result = EligiblePopulationCalculator.Calculate(
            [
                new AgeBinValue(0, 17, 180),
                new AgeBinValue(18, 20, 30),
                new AgeBinValue(21, 64, 440),
                new AgeBinValue(65, null, 100)
            ],
            legalGamingAge: 21);

        Assert.Equal(540, result.Population);
        Assert.False(result.UsedInterpolation);
        Assert.Equal("none", result.InterpolationMethod);
    }

    [Fact]
    public void Calculate_InterpolatesOnlyTheCutAgeBin()
    {
        var result = EligiblePopulationCalculator.Calculate(
            [
                new AgeBinValue(0, 17, 180),
                new AgeBinValue(18, 24, 70),
                new AgeBinValue(25, 64, 400),
                new AgeBinValue(65, null, 100)
            ],
            legalGamingAge: 21);

        Assert.Equal(540, result.Population, precision: 8);
        Assert.True(result.UsedInterpolation);
        Assert.Equal("uniform-within-bin", result.InterpolationMethod);
    }

    [Fact]
    public void Calculate_RejectsGapsOrOverlapsInsteadOfSilentlyUndercounting()
    {
        Assert.Throws<InvalidOperationException>(() => EligiblePopulationCalculator.Calculate(
            [
                new AgeBinValue(0, 17, 180),
                new AgeBinValue(19, 64, 460),
                new AgeBinValue(65, null, 100)
            ],
            legalGamingAge: 21));
    }

    [Fact]
    public void Calculate_RejectsLegalAgeCutInsideOpenEndedBin()
    {
        Assert.Throws<InvalidOperationException>(() => EligiblePopulationCalculator.Calculate(
            [new AgeBinValue(18, null, 500)],
            legalGamingAge: 21));
    }
}
