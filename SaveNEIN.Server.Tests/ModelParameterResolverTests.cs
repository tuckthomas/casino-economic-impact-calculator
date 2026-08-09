using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class ModelParameterResolverTests
{
    [Fact]
    public void Resolve_AppliesAllLayersInCanonicalPrecedenceOrder()
    {
        var definition = Definition("gravity.beta", systemDefault: 1.0, calibrated: true);
        var result = ModelParameterResolver.Resolve(
            [definition],
            [
                Value(10, definition.Id, 1.4),
                Value(20, definition.Id, 1.5),
                Value(30, definition.Id, 1.6)
            ],
            new ParameterResolutionRequest(
                "gravity-v1",
                NationalParameterSetId: 10,
                JurisdictionParameterSetId: 20,
                ScenarioParameterSetId: 30,
                UserOverrides: [new ParameterOverride("gravity.beta", 1.7)]));

        var parameter = Assert.Single(result);
        Assert.Equal(1.0, parameter.SystemFallbackValue);
        Assert.Equal(1.5, parameter.DefaultValue);
        Assert.Equal(1.6, parameter.ScenarioValue);
        Assert.Equal(1.7, parameter.UserOverrideValue);
        Assert.Equal(1.7, parameter.FinalValue);
        Assert.Equal("user-override", parameter.SourceLayer);
    }

    [Fact]
    public void Resolve_RejectsUnknownOverride()
    {
        var definition = Definition("gravity.beta", 1.5);

        var exception = Assert.Throws<KeyNotFoundException>(() => ModelParameterResolver.Resolve(
            [definition],
            [],
            new ParameterResolutionRequest(
                "gravity-v1", null, null, null,
                [new ParameterOverride("gravity.typo", 2)])));

        Assert.Contains("gravity.typo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsComputationallyUnsafeValue()
    {
        var definition = Definition("gravity.beta", 1.5);
        definition.ComputationalMinimum = 0.1;
        definition.ComputationalMaximum = 10;

        Assert.Throws<ArgumentOutOfRangeException>(() => ModelParameterResolver.Resolve(
            [definition],
            [],
            new ParameterResolutionRequest(
                "gravity-v1", null, null, null,
                [new ParameterOverride("gravity.beta", 0)])));
    }

    [Fact]
    public void Resolve_WarnsButPreservesRecommendedRangeOverride()
    {
        var definition = Definition("gravity.beta", 1.5);
        definition.ComputationalMinimum = 0.1;
        definition.ComputationalMaximum = 10;
        definition.RecommendedMinimum = 1.4;
        definition.RecommendedMaximum = 1.6;

        var parameter = Assert.Single(ModelParameterResolver.Resolve(
            [definition],
            [],
            new ParameterResolutionRequest(
                "gravity-v1", null, null, null,
                [new ParameterOverride("gravity.beta", 2.2)])));

        Assert.Equal(2.2, parameter.FinalValue);
        Assert.True(parameter.IsOutsideRecommendedRange);
        Assert.Contains("outside", parameter.WarningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DisclosesUncalibratedPrior()
    {
        var definition = Definition("gravity.alpha", 1.0, calibrated: false);

        var parameter = Assert.Single(ModelParameterResolver.Resolve(
            [definition],
            [],
            new ParameterResolutionRequest("gravity-v1", null, null, null, null)));

        Assert.Contains("calibration", parameter.WarningText, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelParameterDefinition Definition(
        string key,
        double systemDefault,
        bool calibrated = true) => new()
        {
            Id = 1,
            Key = key,
            Category = "test",
            DisplayName = key,
            TechnicalDescription = "test",
            PlainLanguageDescription = "test",
            Units = "unit",
            SystemDefaultValue = systemDefault,
            IsUserOverridable = true,
            ModelVersionApplicability = "gravity-v1",
            IsCalibrated = calibrated
        };

    private static ModelParameterSetValue Value(long setId, long definitionId, double value) => new()
    {
        ParameterSetId = setId,
        ParameterDefinitionId = definitionId,
        Value = value
    };
}
