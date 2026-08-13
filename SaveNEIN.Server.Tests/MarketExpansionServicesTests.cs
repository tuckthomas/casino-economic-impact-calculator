using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class MarketExpansionServicesTests
{
    [Fact]
    public void InclusiveAccessibility_UsesOutsideOptionWhenNoFacilityRouteExists()
    {
        var result = new GravityOriginResult(
            "USA-ZCTA-42413",
            100,
            [new GravityFacilityAllocation(
                "facility", null, false, null, 0, 0,
                CaptureSourceCategories.ExternalCommercialIncumbent, false)],
            Math.Log(2),
            1,
            100,
            1,
            100);

        var logAccessibility = GravityModelExecutionService.LogInclusiveAccessibility(result);

        Assert.Equal(Math.Log(2), logAccessibility, 10);
    }

    [Fact]
    public void InclusiveAccessibility_UsesFullChoiceSetLogsum()
    {
        var result = new GravityOriginResult(
            "USA-ZCTA-46802",
            100,
            [new GravityFacilityAllocation(
                "facility", 15, true, Math.Log(3), 0.6, 60,
                CaptureSourceCategories.ExternalCommercialIncumbent, false)],
            Math.Log(2),
            0.4,
            40,
            1,
            100);

        var logAccessibility = GravityModelExecutionService.LogInclusiveAccessibility(result);

        Assert.Equal(Math.Log(5), logAccessibility, 10);
    }

    [Fact]
    public void AccessibilityExpansion_UsesLogChangeAndVisibleCap()
    {
        var result = new AccessibilityExpansionService().Calculate(new AccessibilityExpansionInput(
            "USA-ZCTA-46802", 1_000_000, Math.Log(10), Math.Log(20), 0.5, 0.25));

        Assert.Equal(Math.Log(2), result.LogAccessibilityChange, 10);
        Assert.True(result.UnboundedInducedDemandShare > 0.25);
        Assert.Equal(0.25, result.AppliedInducedDemandShare, 10);
        Assert.Equal(250_000, result.InducedResidentDemand, 6);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void AccessibilityExpansion_DoesNotCreateNegativeInducedDemand()
    {
        var result = new AccessibilityExpansionService().Calculate(new AccessibilityExpansionInput(
            "origin", 100, Math.Log(20), Math.Log(10), 0.2, 0.5));

        Assert.Equal(0, result.InducedResidentDemand);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void TourismDemand_DeduplicatesBeforeParticipationAndCapture()
    {
        var result = new TourismDemandService().Calculate(new TourismDemandInput(
            "visitor-days", 1_000, 0.2, 0.75, 0.1, 0.25, 100));

        Assert.Equal(800, result.DeduplicatedVisitorPersonTrips, 8);
        Assert.Equal(600, result.EligibleVisitorTrips, 8);
        Assert.Equal(15, result.CapturedParticipantTrips, 8);
        Assert.Equal(1_500, result.TourismGgr, 8);
    }

    [Fact]
    public void TrafficDemand_KeepsDirectionalAccessAndBothOverlapDeductionsExplicit()
    {
        var result = new TrafficInterceptService().Calculate(new TrafficInterceptInput(
            "I-69", 10_000, 365, 1.2, 0.5, 0.8, 0.01, 0.25, 0.2, 80));

        Assert.Equal(3_650_000, result.AnnualVehicleTrips, 6);
        Assert.Equal(1_752_000, result.AccessibleTravelerTrips, 6);
        Assert.Equal(10_512, result.DeduplicatedInterceptedTravelerTrips, 6);
        Assert.Equal(840_960, result.TrafficGgr, 6);
    }

    [Fact]
    public void CapacityDiagnostic_FlagsButDoesNotCapHighForecast()
    {
        var input = new CapacityDiagnosticInput(
            500_000_000, 1_000, 25, 365, 100, 400, 1_000, 4_000, 0, 0);
        var result = new CapacityDiagnosticService().Evaluate(input);

        Assert.True(result.IsAboveValidatedRange);
        Assert.Equal(input.StabilizedGgr, result.StabilizedGgr);
        Assert.Contains(result.Warnings, warning => warning.Contains("not silently capped", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("hotel or event", StringComparison.Ordinal));
    }

    [Fact]
    public void CapacityDiagnostic_SupportsTableOnlyFacilityWithoutInventingSlotProductivity()
    {
        var result = new CapacityDiagnosticService().Evaluate(new CapacityDiagnosticInput(
            10_000_000, 0, 25, 365, 100, 400, 1_000, 4_000, 0, 0));

        Assert.Null(result.ImpliedResidualSlotWinPerUnitDay);
        Assert.Equal(9_125_000, result.PlausibleCapacityMinimum, 6);
    }

    [Fact]
    public void CapacityBenchmark_UsesPublishedMonthlyUnitDaysAndComponentRevenue()
    {
        var snapshotId = Guid.NewGuid();
        var competitors = Enumerable.Range(1, 3).Select(index => new CasinoCompetitor
        {
            Id = index,
            StableVenueId = $"facility-{index}"
        }).ToArray();
        var periods = competitors.SelectMany((competitor, index) => new[]
        {
            Performance(competitor.Id, snapshotId, GamingRevenueMetricKeys.SlotOrVltGamingRevenue,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), 31 * (100 + index * 10) * (200 + index * 20), 100 + index * 10),
            Performance(competitor.Id, snapshotId, GamingRevenueMetricKeys.SlotOrVltGamingRevenue,
                new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), 28 * (120 + index * 10) * (200 + index * 20), 120 + index * 10),
            Performance(competitor.Id, snapshotId, GamingRevenueMetricKeys.TableGameGamingRevenue,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), 31 * (10 + index) * (1_000 + index * 100), 10 + index),
            Performance(competitor.Id, snapshotId, GamingRevenueMetricKeys.TableGameGamingRevenue,
                new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), 28 * (12 + index) * (1_000 + index * 100), 12 + index)
        }).ToArray();

        var resolution = new CapacityProductivityBenchmarkService().Resolve(
            new CapacityProductivityBenchmarkInput(
                snapshotId,
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 2, 28),
                competitors,
                periods));

        var benchmark = Assert.IsType<CapacityProductivityBenchmark>(resolution.Benchmark);
        Assert.Equal(3, benchmark.Facilities.Count);
        Assert.Equal(200, benchmark.SlotWinPerUnitDayMinimum, 9);
        Assert.Equal(240, benchmark.SlotWinPerUnitDayMaximum, 9);
        Assert.Equal(1_000, benchmark.TableWinPerTableDayMinimum, 9);
        Assert.Equal(1_200, benchmark.TableWinPerTableDayMaximum, 9);
        Assert.Contains(resolution.Warnings, warning => warning.Contains("operating-hour", StringComparison.Ordinal));
    }

    [Fact]
    public void CapacityBenchmark_RefusesToSynthesizeMissingComponentOrUnitEvidence()
    {
        var snapshotId = Guid.NewGuid();
        var competitor = new CasinoCompetitor { Id = 1, StableVenueId = "incomplete" };
        var periods = new[]
        {
            Performance(1, snapshotId, GamingRevenueMetricKeys.SlotOrVltGamingRevenue,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), 1_000, null)
        };

        var resolution = new CapacityProductivityBenchmarkService().Resolve(
            new CapacityProductivityBenchmarkInput(
                snapshotId,
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 1, 31),
                [competitor],
                periods));

        Assert.Null(resolution.Benchmark);
        Assert.Contains(resolution.Warnings, warning => warning.Contains("No component split or facility inventory was synthesized", StringComparison.Ordinal));
    }

    [Fact]
    public void RampSchedule_SeparatesPartialFirstSecondAndStabilizedYears()
    {
        var results = new RampScheduleService().Build(new RampScheduleInput(
            100_000_000, 2028, 7, 0.65, 0.85, 3, 0.02, 5));

        Assert.Equal(6, results.Count);
        Assert.Equal("opening-partial-year", results[0].PeriodKind);
        Assert.Equal(32_500_000, results[0].ProjectedGgr, 6);
        Assert.Equal("first-full-year", results[1].PeriodKind);
        Assert.Equal(65_000_000, results[1].ProjectedGgr, 6);
        Assert.Equal("second-full-year", results[2].PeriodKind);
        Assert.Equal(85_000_000, results[2].ProjectedGgr, 6);
        Assert.Equal("stabilized-year", results[3].PeriodKind);
        Assert.Equal(100_000_000, results[3].ProjectedGgr, 6);
        Assert.Equal(104_040_000, results[5].ProjectedGgr, 4);
    }

    [Fact]
    public void DemandLayers_RejectUnsafeSharesAndRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TourismDemandService().Calculate(
            new TourismDemandInput("tourism", 1, 1.1, 1, 1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficInterceptService().Calculate(
            new TrafficInterceptInput("road", 1, 365, 1, 1, 1.1, 0, 0, 0, 1)));
        Assert.Throws<InvalidOperationException>(() => new CapacityDiagnosticService().Evaluate(
            new CapacityDiagnosticInput(1, 1, 0, 365, 2, 1, 0, 0, 0, 0)));
    }

    private static CasinoGamingRevenuePeriod Performance(
        int competitorId,
        Guid snapshotId,
        string metricKey,
        DateOnly start,
        DateOnly end,
        double amount,
        double? unitCount) => new()
    {
        CasinoCompetitorId = competitorId,
        DatasetSnapshotId = snapshotId,
        PeriodStart = start,
        PeriodEnd = end,
        ReportedMetricKey = metricKey,
        ReportedMetricDefinition = metricKey,
        ReportedAmount = Convert.ToDecimal(amount),
        ReportedUnitCount = unitCount,
        AnomalyFlagsJson = "[]"
    };
}
