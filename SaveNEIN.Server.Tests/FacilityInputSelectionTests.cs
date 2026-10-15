// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class FacilityInputSelectionTests
{
    private static DevelopmentProgram Program() => new() { SlotOrVltPositions = 1000, GamingFloorSquareFeet = 50000 };
    private static CasinoCompetitor Competitor(string key, int? floor) => new()
    {
        StableVenueId = key, SlotOrVltPositions = 500, GamingFloorSquareFeet = floor, TableGameCount = 0
    };

    [Fact]
    public void OneMissingCompetitorDisablesTheInputForTheWholePhysicalComparison()
    {
        var available = FacilityInputSelection.Availability(Program(), [Competitor("known", 40000), Competitor("unknown", null)], "structural");
        var floor = Assert.Single(available, item => item.Key == "gaming-floor-square-feet");
        Assert.False(floor.Available);
        Assert.Equal(["unknown"], floor.MissingFacilityKeys);
        Assert.Contains("No reference value", floor.UnavailableReason);
        Assert.Throws<ArgumentException>(() => FacilityInputSelection.Resolve([floor.Key], available));
        Assert.DoesNotContain(floor.Key, FacilityInputSelection.Resolve(null, available));
        Assert.Contains("gaming-positions", FacilityInputSelection.Resolve(null, available));
    }

    [Fact]
    public void ActualZeroIsAvailableAndAddingMissingDataEnablesAnInput()
    {
        var competitor = Competitor("known-zero", 0);
        Assert.True(FacilityInputSelection.Availability(Program(), [competitor], "structural").Single(item => item.Key == "gaming-floor-square-feet").Available);
        competitor.GamingFloorSquareFeet = null;
        Assert.False(FacilityInputSelection.Availability(Program(), [competitor], "structural").Single(item => item.Key == "gaming-floor-square-feet").Available);
        competitor.GamingFloorSquareFeet = 100;
        Assert.True(FacilityInputSelection.Availability(Program(), [competitor], "structural").Single(item => item.Key == "gaming-floor-square-feet").Available);
    }

    [Fact]
    public void ObservedIncumbentsDoNotRequireUnusedPhysicalInputsButProposalStillDoes()
    {
        var available = FacilityInputSelection.Availability(Program(), [Competitor("unknown", null)], "observed-ggr");
        Assert.True(available.Single(item => item.Key == "gaming-floor-square-feet").Available);
        Assert.False(available.Single(item => item.Key == "capital-cost").Available);
        Assert.False(available.Single(item => item.Key == "highway-access").Available);
    }

    [Fact]
    public void SelectionRejectsUnknownDuplicateAndUnavailableKeysButAllowsExplicitNeutralAttraction()
    {
        var available = FacilityInputSelection.Availability(Program(), [Competitor("known", null)], "structural");
        foreach (string[] keys in new[] { new[] { "gaming-positions", "gaming-positions" }, ["made-up"], ["gaming-floor-square-feet"] })
            Assert.Throws<ArgumentException>(() => FacilityInputSelection.Resolve(keys, available));
        Assert.Empty(FacilityInputSelection.Resolve([], available));
    }

    [Fact]
    public void DeselectedAndZeroWeightTermsCannotChangeAttractionOrTriggerImputation()
    {
        FacilityFeatureTerm[] terms = [new("gaming-positions", 500, 1000, 1), new("gaming-floor-square-feet", null, 75000, 1), new("hotel-rooms", null, 200, 0)];
        var selected = FacilityInputSelection.ActiveTerms(terms, ["gaming-positions", "hotel-rooms"]);
        Assert.Single(selected);
        var result = new FacilityAttractivenessService().CalculateStructural(new("site", selected, MissingFacilityAttributeBehavior.Reject));
        Assert.Empty(result.Warnings);
        Assert.Equal(501d / 1001, result.NormalizedAttraction, 12);
        Assert.Throws<InvalidOperationException>(() => new FacilityAttractivenessService().CalculateStructural(new("site",
            FacilityInputSelection.ActiveTerms(terms, ["gaming-floor-square-feet"]), MissingFacilityAttributeBehavior.Reject)));
        Assert.Empty(FacilityInputSelection.ActiveTerms(terms, ["hotel-rooms"]));
        Assert.Equal(1d, FacilityInputSelection.Calculate(new FacilityAttractivenessService(), "site", []));
    }

    [Fact]
    public void CatalogUsesTheSameMeasuredValuesForAvailabilityAndCalculation()
    {
        var parameters = FacilityInputSelection.Definitions.SelectMany(item => item.ParameterKeys).Distinct().ToDictionary(key => key, _ => 1d);
        var site = Competitor("site", 34567);
        var terms = GravityModelExecutionService.FacilityFeatures(site, parameters);
        Assert.Equal(34567, terms.Single(item => item.Key == "gaming-floor-square-feet").Value);
        Assert.Equal(500, terms.Single(item => item.Key == "gaming-positions").Value);
        Assert.All(FacilityInputSelection.Availability(Program(), [site], "structural"), input =>
            Assert.Equal(terms.Single(term => term.Key == input.Key).Value.HasValue && input.Key is not ("capital-cost" or "highway-access"), input.Available));
    }
}
