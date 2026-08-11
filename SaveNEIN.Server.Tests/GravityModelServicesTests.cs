using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class GravityModelServicesTests
{
    [Fact]
    public void CompetitiveUniverse_HaversineIsOnlyABroadInclusionPrefilter()
    {
        Assert.True(CompetitiveUniverseService.IsWithinBroadPrefilter(
            41.0793,
            -85.1394,
            41.6764,
            -86.2520,
            100));
        Assert.False(CompetitiveUniverseService.IsWithinBroadPrefilter(
            41.0793,
            -85.1394,
            36.1699,
            -115.1398,
            300));

        Assert.False(CompetitiveUniverseService.IsCasinoFloorSubstitute(new CasinoCompetitor
        {
            VenueType = "sportsbook_only"
        }));
        Assert.True(CompetitiveUniverseService.IsCasinoFloorSubstitute(new CasinoCompetitor
        {
            VenueType = "racino",
            HasSlots = true
        }));
    }

    [Fact]
    public void DevelopmentProgram_RejectsAProgramWithoutGamingCapacity()
    {
        var definition = new DevelopmentProgramDefinition(
            "program-a",
            "1",
            "Program A",
            0,
            0,
            0,
            false,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            3,
            null);

        Assert.Throws<ArgumentException>(() => DevelopmentProgramService.Validate(definition));
    }

    [Fact]
    public void AgiShareDemand_ImplementsCanonicalEquationWithoutSecondIncomeWeight()
    {
        var service = new OriginDemandService();

        var result = service.CalculateAgiShare(new AgiShareDemandInput(
            "origin-a",
            RealIncomeMass: 1_000_000,
            GamingIncomeShare: 0.012,
            OriginAdjustment: 1.1));

        Assert.Equal(13_200, result.Demand, 8);
        Assert.Equal(DemandSpecification.AgiShare, result.Specification);
    }

    [Fact]
    public void PerCapitaDemand_BoundsExtremeIncomeAdjustment()
    {
        var service = new OriginDemandService();

        var result = service.CalculatePerCapita(new PerCapitaDemandInput(
            "origin-b",
            EligibleAdults: 1_000,
            BaseGamingExpenditurePerAdult: 500,
            IncomeMetric: 1_000_000,
            RegionalReferenceIncome: 50_000,
            IncomeElasticity: 2,
            MinimumIncomeAdjustment: 0.5,
            MaximumIncomeAdjustment: 1.5));

        Assert.Equal(750_000, result.Demand, 8);
        Assert.Equal(1.5, result.IncomeAdjustment, 8);
        Assert.True(result.IncomeAdjustmentWasBounded);
    }

    [Fact]
    public void StructuralAttraction_NormalizesReferenceFacilityToOne()
    {
        var service = new FacilityAttractivenessService();

        var result = service.CalculateStructural(new StructuralAttractivenessInput(
            "reference-casino",
            [
                new FacilityFeatureTerm("positions", 2_000, 2_000, 0.6),
                new FacilityFeatureTerm("hotel-rooms", 250, 250, 0.2),
                new FacilityFeatureTerm("interchange-access", 1, 1, 0.1)
            ]));

        Assert.Equal(1, result.NormalizedAttraction, 12);
        Assert.Equal(0, result.LogNormalizedAttraction, 12);
    }

    [Fact]
    public void StructuralAttraction_MissingAttributePolicyIsExplicit()
    {
        var service = new FacilityAttractivenessService();
        var input = new StructuralAttractivenessInput(
            "casino-a",
            [new FacilityFeatureTerm("hotel-rooms", null, 200, 0.25)],
            MissingFacilityAttributeBehavior.UseReferenceValue);

        var result = service.CalculateStructural(input);

        Assert.Equal(1, result.NormalizedAttraction, 12);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void ObservedGgrAttraction_RejectsCircularProposedFacilityMass()
    {
        var service = new FacilityAttractivenessService();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.CalculateObservedGgr(new ObservedGgrAttractivenessInput(
                "proposed",
                StabilizedObservedGgr: 200_000_000,
                ReferenceObservedGgr: 100_000_000,
                IsProposedFacility: true)));

        Assert.Contains("cannot derive competitive mass", exception.Message);
    }

    [Fact]
    public void InversePowerGravity_UsesNetworkTimeAndSharesSumWithOutsideOption()
    {
        var service = new GravityModelService();
        var parameters = new GravityParameters(
            AttractionElasticity: 1,
            FrictionForm: TravelFrictionForm.InversePower,
            TravelTimeDecay: 1.5,
            TravelTimeRegularizationMinutes: 1);

        var result = service.Allocate(
            new GravityOriginInput(
                "origin-a",
                Demand: 10_000,
                OutsideOptionWeight: 0.01,
                Alternatives:
                [
                    new GravityAlternativeInput("near", 1, 20, true),
                    new GravityAlternativeInput("far", 1, 60, true)
                ]),
            parameters);

        Assert.Equal(1, result.ShareSum, 12);
        Assert.Equal(10_000, result.AllocatedDemandSum, 8);
        Assert.True(result.OutsideOptionShare > 0);
        Assert.True(
            result.FacilityAllocations.Single(item => item.FacilityKey == "near").Share >
            result.FacilityAllocations.Single(item => item.FacilityKey == "far").Share);
    }

    [Fact]
    public void ExponentialGravity_ImplementsCanonicalAlternativeFriction()
    {
        var service = new GravityModelService();
        var result = service.Allocate(
            new GravityOriginInput(
                "origin-a",
                1,
                0,
                [
                    new GravityAlternativeInput("a", 1, 10, true),
                    new GravityAlternativeInput("b", 1, 20, true)
                ]),
            new GravityParameters(1, TravelFrictionForm.Exponential, 0.1, 1));

        var actualRatio = result.FacilityAllocations[0].Share / result.FacilityAllocations[1].Share;
        Assert.Equal(Math.Exp(1), actualRatio, 12);
    }

    [Fact]
    public void MissingRoute_IsRejectedByDefault()
    {
        var service = new GravityModelService();

        Assert.Throws<InvalidOperationException>(() => service.Allocate(
            new GravityOriginInput(
                "origin-a",
                100,
                1,
                [new GravityAlternativeInput("unrouted", 1, null, false)]),
            new GravityParameters(1, TravelFrictionForm.InversePower, 1.5, 1)));
    }

    [Fact]
    public void MissingRoute_CanBeExplicitlyExcludedAndAudited()
    {
        var service = new GravityModelService();
        var result = service.Allocate(
            new GravityOriginInput(
                "origin-a",
                100,
                1,
                [new GravityAlternativeInput("unrouted", 1, null, false)]),
            new GravityParameters(
                1,
                TravelFrictionForm.InversePower,
                1.5,
                1,
                MissingRouteBehavior.ExcludeFacility));

        Assert.Equal(1, result.OutsideOptionShare, 12);
        Assert.False(result.FacilityAllocations.Single().RouteIncluded);
    }

    [Theory]
    [InlineData(0.000001, 0.000001, 1_000_000)]
    [InlineData(1_000_000, 100, 0.000001)]
    public void Gravity_RemainsFiniteAtExtremeValidatedInputs(
        double attraction,
        double beta,
        double travelMinutes)
    {
        var service = new GravityModelService();
        var result = service.Allocate(
            new GravityOriginInput(
                "rural-origin",
                1_000,
                0.5,
                [new GravityAlternativeInput("facility", attraction, travelMinutes, true)]),
            new GravityParameters(1, TravelFrictionForm.InversePower, beta, 0.001));

        Assert.True(double.IsFinite(result.ShareSum));
        Assert.Equal(1, result.ShareSum, 12);
        Assert.Equal(1_000, result.AllocatedDemandSum, 8);
    }

    [Fact]
    public void Equilibrium_ReconcilesProposedRevenueToIncumbentAndOutsideCapture()
    {
        var gravity = new GravityModelService();
        var service = new MarketEquilibriumService(gravity);
        var result = service.Calculate(new MarketEquilibriumRequest(
            [
                new EquilibriumOriginInput(
                    "origin-a",
                    Demand: 1_000_000,
                    OutsideOptionWeight: 0.01,
                    Incumbents:
                    [
                        new GravityAlternativeInput(
                            "host-incumbent",
                            1.2,
                            25,
                            true,
                            CaptureSourceCategory: CaptureSourceCategories.HostJurisdictionIncumbent),
                        new GravityAlternativeInput(
                            "external-incumbent",
                            1,
                            35,
                            true,
                            CaptureSourceCategory: CaptureSourceCategories.ExternalCommercialIncumbent)
                    ],
                    ProposedFacility: new GravityAlternativeInput(
                        "proposed",
                        1.4,
                        20,
                        true,
                        IsProposedFacility: true))
            ],
            new GravityParameters(1, TravelFrictionForm.InversePower, 1.5, 1)));

        Assert.Equal(0, result.ConservationResidual, 6);
        Assert.Equal(result.ProposedFacilityDemand, result.ProposedCaptureBySource.Values.Sum(), 6);
        Assert.True(result.ProposedCaptureBySource[CaptureSourceCategories.HostJurisdictionIncumbent] > 0);
        Assert.True(result.ProposedCaptureBySource[CaptureSourceCategories.ExternalCommercialIncumbent] > 0);
        Assert.True(result.ProposedCaptureBySource[CaptureSourceCategories.OutsideOption] > 0);
        Assert.All(
            result.Facilities.Where(facility => !facility.IsProposedFacility),
            incumbent => Assert.True(incumbent.ChangeInAllocatedDemand < 0));
    }

    [Fact]
    public void FacilityWeightOverride_FlowsThroughResolutionAttractionAndGravityAllocation()
    {
        var definition = new ModelParameterDefinition
        {
            Id = 1,
            Key = "facility.gaming_positions_coefficient",
            Category = "facility-attraction",
            DisplayName = "Gaming positions coefficient",
            TechnicalDescription = "test",
            PlainLanguageDescription = "test",
            Units = "coefficient",
            SystemDefaultValue = 0,
            ComputationalMinimum = -10,
            ComputationalMaximum = 10,
            IsUserOverridable = true,
            ModelVersionApplicability = "gravity-v1",
            IsCalibrated = true
        };
        var baselineCoefficient = Assert.Single(ModelParameterResolver.Resolve(
            [definition], [], new ParameterResolutionRequest("gravity-v1", null, null, null, null)));
        var overrideCoefficient = Assert.Single(ModelParameterResolver.Resolve(
            [definition], [], new ParameterResolutionRequest(
                "gravity-v1", null, null, null,
                [new ParameterOverride(definition.Key, 1)])));
        var attractiveness = new FacilityAttractivenessService();
        var baselineAttraction = attractiveness.CalculateStructural(new StructuralAttractivenessInput(
            "proposed", [new FacilityFeatureTerm("positions", 2_000, 1_000, baselineCoefficient.FinalValue)]));
        var overrideAttraction = attractiveness.CalculateStructural(new StructuralAttractivenessInput(
            "proposed", [new FacilityFeatureTerm("positions", 2_000, 1_000, overrideCoefficient.FinalValue)]));
        var gravity = new GravityModelService();
        var parameters = new GravityParameters(1, TravelFrictionForm.InversePower, 1.5, 1);

        var baseline = gravity.Allocate(new GravityOriginInput(
            "origin", 1_000, 0,
            [
                new GravityAlternativeInput("incumbent", 1, 30, true),
                new GravityAlternativeInput("proposed", baselineAttraction.NormalizedAttraction, 30, true)
            ]), parameters);
        var overridden = gravity.Allocate(new GravityOriginInput(
            "origin", 1_000, 0,
            [
                new GravityAlternativeInput("incumbent", 1, 30, true),
                new GravityAlternativeInput("proposed", overrideAttraction.NormalizedAttraction, 30, true)
            ]), parameters);

        Assert.Equal("user-override", overrideCoefficient.SourceLayer);
        Assert.Equal(0.5, baseline.FacilityAllocations.Single(row => row.FacilityKey == "proposed").Share, 12);
        Assert.True(
            overridden.FacilityAllocations.Single(row => row.FacilityKey == "proposed").Share >
            baseline.FacilityAllocations.Single(row => row.FacilityKey == "proposed").Share);
        Assert.Equal(1_000, overridden.AllocatedDemandSum, 8);
    }

    [Fact]
    public void Gravity_ReconcilesLargeCompetitiveSetWithoutOverflow()
    {
        var alternatives = Enumerable.Range(1, 5_000)
            .Select(index => new GravityAlternativeInput(
                $"facility-{index:D5}",
                Attraction: 0.5 + index / 5_000d,
                NetworkTravelTimeMinutes: 5 + index / 100d,
                RouteFound: true))
            .ToArray();

        var result = new GravityModelService().Allocate(
            new GravityOriginInput("large-field-origin", 25_000_000, 1, alternatives),
            new GravityParameters(1.2, TravelFrictionForm.InversePower, 1.7, 1));

        Assert.Equal(5_000, result.FacilityAllocations.Count);
        Assert.All(result.FacilityAllocations, allocation =>
        {
            Assert.True(double.IsFinite(allocation.Share));
            Assert.True(double.IsFinite(allocation.AllocatedDemand));
        });
        Assert.Equal(1, result.ShareSum, 12);
        Assert.Equal(25_000_000, result.AllocatedDemandSum, 6);
    }

    [Fact]
    public void Gravity_SparseRuralOriginKeepsUnreachableFacilitiesAuditableAndConservesDemand()
    {
        var result = new GravityModelService().Allocate(
            new GravityOriginInput(
                "sparse-rural-origin",
                25,
                1,
                [
                    new GravityAlternativeInput("distant-reachable", 0.4, 360, true),
                    new GravityAlternativeInput("unreachable-a", 1, null, false),
                    new GravityAlternativeInput("unreachable-b", 2, null, false)
                ]),
            new GravityParameters(
                1,
                TravelFrictionForm.InversePower,
                2,
                1,
                MissingRouteBehavior.ExcludeFacility));

        Assert.Equal(2, result.FacilityAllocations.Count(allocation => !allocation.RouteIncluded));
        Assert.True(result.OutsideOptionShare > 0.99);
        Assert.Equal(1, result.ShareSum, 12);
        Assert.Equal(25, result.AllocatedDemandSum, 12);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.000000000001d)]
    public void Gravity_ZeroAndNearZeroDemandRemainFiniteAndConserved(double demand)
    {
        var result = new GravityModelService().Allocate(
            new GravityOriginInput(
                "zero-safe-origin",
                demand,
                0.5,
                [new GravityAlternativeInput("facility", 1, 20, true)]),
            new GravityParameters(1, TravelFrictionForm.InversePower, 1.5, 1));

        Assert.All(result.FacilityAllocations, allocation => Assert.True(double.IsFinite(allocation.AllocatedDemand)));
        Assert.True(double.IsFinite(result.OutsideOptionAllocatedDemand));
        Assert.Equal(1, result.ShareSum, 12);
        Assert.InRange(Math.Abs(result.AllocatedDemandSum - demand), 0, 1e-24);
    }
}
