using System.Text.Json;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public class MultimarketAttractionProtocolTests
{
    private static MultimarketCalibrationProtocol Read(string json) =>
        JsonSerializer.Deserialize<MultimarketCalibrationProtocol>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void ExistingProtocolsRetainHybridAttraction()
    {
        var protocol = Read("{}");
        Assert.Equal(FacilityAttractionSpecifications.HybridObservedGgr, protocol.AttractionSpecification);
        MultimarketRevenueCalibration.ValidateAttractionProtocol(protocol);
    }

    [Fact]
    public void StructuralModeAcceptsPositiveDeclaredOffsets()
    {
        MultimarketRevenueCalibration.ValidateAttractionProtocol(Read(
            "{\"attractionSpecification\":\"structural\",\"gamingPositionsOffset\":[100,500,2000]}"));
    }

    [Theory]
    [InlineData("{\"gamingPositionsOffset\":[0]}")]
    [InlineData("{\"gamingPositionsOffset\":[-1]}")]
    [InlineData("{\"gamingPositionsOffset\":[]}")]
    [InlineData("{\"attractionSpecification\":\"observed-ggr\"}")]
    [InlineData("{\"demandSpecification\":\"eligible-adult-per-capita\"}")]
    [InlineData("{\"demandSpecification\":\"unknown\"}")]
    [InlineData("{\"minimumDemandScale\":0}")]
    [InlineData("{\"minimumDemandScale\":1,\"maximumDemandScale\":0.1}")]
    [InlineData("{\"fitDemandScaleByJurisdiction\":true}")]
    [InlineData("{\"travelTimeRegularizationMinutes\":[0]}")]
    [InlineData("{\"travelTimeRegularizationMinutes\":[]}")]
    [InlineData("{\"incomeElasticity\":[]}")]
    [InlineData("{\"incomeElasticity\":[1]}")]
    [InlineData("{\"incomeElasticity\":[6]}")]
    [InlineData("{\"incomeAdjustmentMinimum\":0}")]
    [InlineData("{\"incomeAdjustmentMinimum\":2,\"incomeAdjustmentMaximum\":1}")]
    [InlineData("{\"referenceIncome\":-1}")]
    [InlineData("{\"referenceIncome\":1000001}")]
    [InlineData("{\"referenceIncome\":60000}")]
    [InlineData("{\"observedGgrExcludedVenueIds\":[\"\"]}")]
    [InlineData("{\"observedGgrExcludedVenueIds\":[\"reserved\",\"reserved\"]}")]
    [InlineData("{\"observedGgrExcludedVenueIds\":[\"training-property\"],\"training\":[\"training-property\"]}")]
    [InlineData("{\"frictionForm\":\"unknown\"}")]
    [InlineData("{\"frictionForm\":\"exponential\",\"beta\":[1.5]}")]
    [InlineData("{\"frictionForm\":\"exponential\",\"beta\":[1.5],\"exponentialLambda\":[-0.1]}")]
    [InlineData("{\"frictionForm\":\"exponential\",\"beta\":[1.5],\"exponentialLambda\":[2]}")]
    [InlineData("{\"frictionForm\":\"exponential\",\"beta\":[1.5,2],\"exponentialLambda\":[0.03]}")]
    [InlineData("{\"frictionForm\":\"exponential\",\"beta\":[1.5],\"exponentialLambda\":[0.03],\"travelTimeRegularizationMinutes\":[1,5]}")]
    [InlineData("{\"exponentialLambda\":[0.03]}")]
    [InlineData("{\"selectionPolicyVersion\":\"gravity-development-selection-v1\",\"training\":[\"t1\",\"t2\"],\"selection\":[\"s1\"]}")]
    public void RejectsInvalidSpecificationBeforeAnyDatabaseOrRoutingWork(string json)
    {
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateAttractionProtocol(Read(json)));
    }

    [Fact]
    public void IncomeResolutionUsesTheServerMetricAndRejectsMissingValues()
    {
        var period = new OriginZoneIncomePeriod
        {
            MedianHouseholdIncome = 60_000, AdjustedGrossIncome = 1_000_000,
            InflationAdjustedAdjustedGrossIncome = 1_200_000, ReturnCount = 20
        };
        Assert.Equal(60_000, GravityModelExecutionService.ResolveIncomeMetric(period, "origin"));
        period.MedianHouseholdIncome = null;
        Assert.Equal(60_000, GravityModelExecutionService.ResolveIncomeMetric(period, "origin"));
        period.InflationAdjustedAdjustedGrossIncome = null;
        Assert.Equal(50_000, GravityModelExecutionService.ResolveIncomeMetric(period, "origin"));
        period.ReturnCount = 0;
        Assert.Throws<InvalidOperationException>(() => GravityModelExecutionService.ResolveIncomeMetric(period, "origin"));
    }

    [Fact]
    public void FixedReferencePreservesTheSameOriginBudgetAcrossDifferentCandidateMarkets()
    {
        var demand = new OriginDemandService();
        var firstReference = GravityModelExecutionService.ResolveReferenceIncome(60_000, [30_000, 60_000]);
        var secondReference = GravityModelExecutionService.ResolveReferenceIncome(60_000, [60_000, 120_000]);
        var first = demand.CalculatePerCapita(new("shared-origin", 1000, 370, 75_000, firstReference, 1, 0.5, 1.5));
        var second = demand.CalculatePerCapita(new("shared-origin", 1000, 370, 75_000, secondReference, 1, 0.5, 1.5));
        Assert.Equal(462_500, first.Demand);
        Assert.Equal(first.Demand, second.Demand);
        Assert.NotEqual(GravityModelExecutionService.ResolveReferenceIncome(0, [30_000, 60_000]),
            GravityModelExecutionService.ResolveReferenceIncome(0, [60_000, 120_000]));
    }

    [Fact]
    public void FinalHoldoutRejectsTargetJurisdictionSpendingAndChangingIncomeReferences()
    {
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateFinalHoldoutBudget(Read("{\"fitDemandScaleByJurisdiction\":true}")));
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateFinalHoldoutBudget(Read(
            "{\"demandSpecification\":\"eligible-adult-per-capita\",\"incomeElasticity\":[0,1]}")));
        MultimarketRevenueCalibration.ValidateFinalHoldoutBudget(Read(
            "{\"demandSpecification\":\"eligible-adult-per-capita\",\"incomeElasticity\":[1],\"referenceIncome\":60000}"));
        MultimarketRevenueCalibration.ValidateFinalHoldoutBudget(Read(
            "{\"demandSpecification\":\"eligible-adult-per-capita\",\"incomeElasticity\":[0]}"));
        MultimarketRevenueCalibration.ValidateFinalHoldoutBudget(Read("{}"));
    }

    [Fact]
    public void ReservesUnscoredRevenueIdentitiesAsWellAsSelectionAndFinalHoldoutTargets()
    {
        var protocol = Read("{\"training\":[\"training\"],\"selection\":[\"selection\"],\"holdout\":{\"market\":[\"holdout\"]},\"observedGgrExcludedVenueIds\":[\"reserved\"]}");
        var inventory = new Dictionary<string, int> { ["training"] = 1, ["selection"] = 2, ["holdout"] = 3, ["reserved"] = 4 };
        var excluded = MultimarketRevenueCalibration.BuildObservedGgrExclusions(protocol, inventory);
        Assert.Equal(new[] { 2, 3, 4 }, excluded.Order().ToArray());
        Assert.DoesNotContain(1, excluded);
        Assert.Equal(4, inventory.Count);
    }

    [Fact]
    public void ExponentialComparisonUsesTheServerLambdaInPerMinuteUnits()
    {
        var protocol = Read("{\"frictionForm\":\"exponential\",\"beta\":[1.5],\"exponentialLambda\":[0.005,0.03,0.1]}");
        MultimarketRevenueCalibration.ValidateAttractionProtocol(protocol);
        var decay = MultimarketRevenueCalibration.ResolveActiveTravelDecay(protocol, 1.5, 0.03);
        Assert.Equal(0.03, decay);
        var allocation = GravityModelService.AllocateTargetDemand(new("origin", 1000, 0,
            [new("target", 1, 10, true), new("peer", 1, 40, true)]),
            new(1, TravelFrictionForm.Exponential, decay, 1), "target");
        Assert.Equal(1000 / (1 + Math.Exp(-0.03 * 30)), allocation, 8);
        Assert.Equal(1.5, MultimarketRevenueCalibration.ResolveActiveTravelDecay(Read("{}"), 1.5, 0.03));
    }
}
