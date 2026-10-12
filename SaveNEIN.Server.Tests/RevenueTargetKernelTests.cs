using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Tests;

public sealed class RevenueTargetKernelTests
{
    [Fact]
    public void ObservedGamingPositionsRemainAuthoritativeAndComponentFallbackMatchesCandidate()
    {
        Assert.Equal(1050, GravityModelExecutionService.KnownGamingPositionCount(new CasinoCompetitor
            { SlotOrVltPositions = 1000, TableGameCount = 50 }));
        Assert.Equal(1100, GravityModelExecutionService.KnownGamingPositionCount(new CasinoCompetitor
            { GamingPositions = 1100, SlotOrVltPositions = 1000, TableGameCount = 50 }));
        Assert.Null(GravityModelExecutionService.KnownGamingPositionCount(new CasinoCompetitor { TableGameCount = 50 }));
        Assert.Throws<OverflowException>(() => GravityModelExecutionService.KnownGamingPositionCount(new CasinoCompetitor
            { SlotOrVltPositions = int.MaxValue, TableGameCount = 1 }));
    }

    [Fact]
    public async Task ComponentInspectionPreventsReuseAcrossSnapshotPropertyIdentities()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CasinoCompetitors.AddRange(new CasinoCompetitor { Id = 1, StableVenueId = "inspected", Geom = new NetTopologySuite.Geometries.Point(-85, 41) },
            new CasinoCompetitor { Id = 2, StableVenueId = "inspected", Geom = new NetTopologySuite.Geometries.Point(-85, 41) },
            new CasinoCompetitor { Id = 3, StableVenueId = "untouched", Geom = new NetTopologySuite.Geometries.Point(-84, 41) });
        db.ValidationEvaluations.Add(new ValidationEvaluation
        {
            EvaluationKey = "component", Status = ValidationEvaluationStatuses.Finalized, IsImmutable = true,
            InclusionRulesJson = """{"evaluationKind":"resident-component-inspection","inspectedPropertyKeys":["inspected"],"releaseAccepted":false}"""
        });
        await db.SaveChangesAsync();
        var previous = await ModelReleaseAcceptance.PreviouslyEvaluatedPropertyIdsAsync(db, default);
        Assert.Equal(new[] { 1, 2 }, previous.Order().ToArray());
        Assert.False(ModelReleaseAcceptance.HasAcceptedReport(db.ValidationEvaluations.Single().InclusionRulesJson));
    }

    [Fact]
    public void StructuralNormalizationPreservesRelativePhysicalMassAndRejectsInvalidInputs()
    {
        var incumbent = FacilityAttractivenessService.NormalizeStructuralMass(2, 4);
        var proposal = FacilityAttractivenessService.NormalizeStructuralMass(3, 4);
        Assert.Equal(1.5, proposal / incumbent);
        Assert.Equal(0, FacilityAttractivenessService.NormalizeStructuralMass(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FacilityAttractivenessService.NormalizeStructuralMass(-1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => FacilityAttractivenessService.NormalizeStructuralMass(2, double.NaN));
        Assert.Throws<InvalidOperationException>(() => FacilityAttractivenessService.NormalizeStructuralMass(double.MaxValue, 2));
    }

    [Fact]
    public void PairedRunFingerprintDistinguishesLegacyAndCorrectedStructuralScaling()
    {
        using var legacy = JsonDocument.Parse("{}");
        using var corrected = JsonDocument.Parse("""{"hybridStructuralScalingVersion":"consistent-physical-v2"}""");
        Assert.Equal("legacy-proposed-only-v1", DemandSpecificationValidationEvaluationService.ReadHybridScalingVersion(legacy.RootElement));
        Assert.NotEqual(DemandSpecificationValidationEvaluationService.ReadHybridScalingVersion(legacy.RootElement),
            DemandSpecificationValidationEvaluationService.ReadHybridScalingVersion(corrected.RootElement));
    }

    [Theory]
    [InlineData(0.00000001, 0.8, 0.75)]
    [InlineData(0.0001, 1.2, 1.0)]
    [InlineData(0.01, 2.0, 1.25)]
    public void TargetDemandMatchesFullAuthoritativeAllocation(double outside, double beta, double alpha)
    {
        var alternatives = Enumerable.Range(1, 70).Select(index => new GravityAlternativeInput(
            $"facility-{index}", index / 10d, index * 5d, index % 11 != 0, IsProposedFacility: index == 40)).ToArray();
        var input = new GravityOriginInput("origin", 50_000_000, outside, alternatives);
        var parameters = new GravityParameters(alpha, TravelFrictionForm.InversePower, beta, 1, MissingRouteBehavior.ExcludeFacility);
        var expected = new GravityModelService().Allocate(input, parameters).FacilityAllocations.Single(row => row.IsProposedFacility).AllocatedDemand;
        var actual = GravityModelService.AllocateTargetDemand(input, parameters, "facility-40");
        Assert.InRange(Math.Abs(actual - expected), 0, 0.000001);
    }

    [Fact]
    public void UnreachableTargetHasZeroDemandAndUnknownTargetFails()
    {
        var input = new GravityOriginInput("origin", 100, 1,
            [new("target", 1, null, false, IsProposedFacility: true)]);
        var parameters = new GravityParameters(1, TravelFrictionForm.InversePower, 1.5, 1, MissingRouteBehavior.ExcludeFacility);
        Assert.Equal(0, GravityModelService.AllocateTargetDemand(input, parameters, "target"));
        Assert.Throws<ArgumentException>(() => GravityModelService.AllocateTargetDemand(input, parameters, "missing"));
    }
}
