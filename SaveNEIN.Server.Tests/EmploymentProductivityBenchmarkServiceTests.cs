// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class EmploymentProductivityBenchmarkServiceTests
{
    [Fact]
    public void Resolve_DerivesRevenueWeightedJobsPerMillionFromCompleteObservedFacilities()
    {
        var snapshotId = Guid.NewGuid();
        var competitors = Enumerable.Range(1, 5)
            .Select(index => new CasinoCompetitor
            {
                Id = index,
                StableVenueId = $"facility-{index}",
                ReportedEmployment = index * 100
            })
            .ToArray();
        var periods = competitors.Select(competitor => Period(
            competitor.Id,
            snapshotId,
            100_000_000m)).ToArray();

        var result = new EmploymentProductivityBenchmarkService().Resolve(new EmploymentProductivityBenchmarkInput(
            snapshotId,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            competitors,
            periods));

        var benchmark = Assert.IsType<EmploymentProductivityBenchmark>(result.Benchmark);
        Assert.Equal("reported-employment-per-observed-ggr-v1", benchmark.Method);
        Assert.Equal(3d, benchmark.WeightedJobsPerMillionGgr, 10);
        Assert.Equal(1d, benchmark.MinimumJobsPerMillionGgr, 10);
        Assert.Equal(5d, benchmark.MaximumJobsPerMillionGgr, 10);
        Assert.Equal(5, benchmark.Facilities.Count);
        Assert.Contains("occupation mix", Assert.Single(result.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FailsClosedWhenEmploymentOrObservedCoverageIsIncomplete()
    {
        var snapshotId = Guid.NewGuid();
        var competitors = Enumerable.Range(1, 5)
            .Select(index => new CasinoCompetitor
            {
                Id = index,
                StableVenueId = $"facility-{index}",
                ReportedEmployment = index == 5 ? null : index * 100
            })
            .ToArray();
        var periods = competitors.Select(competitor => Period(
            competitor.Id,
            snapshotId,
            100_000_000m)).ToArray();

        var result = new EmploymentProductivityBenchmarkService().Resolve(new EmploymentProductivityBenchmarkInput(
            snapshotId,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            competitors,
            periods));

        Assert.Null(result.Benchmark);
        Assert.Contains("supplied 4", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Contains("No employee count", result.Warnings[0], StringComparison.Ordinal);
    }

    private static CasinoGamingRevenuePeriod Period(int competitorId, Guid snapshotId, decimal amount) => new()
    {
        CasinoCompetitorId = competitorId,
        DatasetSnapshotId = snapshotId,
        PeriodStart = new DateOnly(2025, 1, 1),
        PeriodEnd = new DateOnly(2025, 12, 31),
        PeriodGranularity = "annual",
        ReportedMetricKey = GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
        ReportedMetricDefinition = "Test comparable",
        ReportedAmount = amount,
        AnomalyFlagsJson = "[]"
    };
}
