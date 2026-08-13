// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record EmploymentProductivityBenchmarkInput(
    Guid ObservedPerformanceSnapshotId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyCollection<CasinoCompetitor> Competitors,
    IReadOnlyCollection<CasinoGamingRevenuePeriod> PerformancePeriods);

public sealed record FacilityEmploymentProductivity(
    string StableVenueId,
    int ReportedEmployment,
    decimal ObservedGgr,
    double JobsPerMillionGgr);

public sealed record EmploymentProductivityBenchmark(
    Guid ObservedPerformanceSnapshotId,
    string Method,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    double WeightedJobsPerMillionGgr,
    double MinimumJobsPerMillionGgr,
    double MaximumJobsPerMillionGgr,
    IReadOnlyList<FacilityEmploymentProductivity> Facilities);

public sealed record EmploymentProductivityBenchmarkResolution(
    EmploymentProductivityBenchmark? Benchmark,
    IReadOnlyList<string> Warnings);

public interface IEmploymentProductivityBenchmarkService
{
    EmploymentProductivityBenchmarkResolution Resolve(EmploymentProductivityBenchmarkInput input);
}

/// <summary>
/// Derives a direct casino job-density benchmark only when regulator-reported property employment
/// can be joined to complete, clean observed GGR for the same immutable facility snapshot.
/// </summary>
public sealed class EmploymentProductivityBenchmarkService : IEmploymentProductivityBenchmarkService
{
    private const int MinimumFacilitySample = 5;
    private const string Method = "reported-employment-per-observed-ggr-v1";

    public EmploymentProductivityBenchmarkResolution Resolve(EmploymentProductivityBenchmarkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PeriodEnd < input.PeriodStart)
        {
            throw new ArgumentException("Employment benchmark period end cannot precede its start.", nameof(input));
        }

        var periodsByFacility = input.PerformancePeriods
            .Where(period => period.DatasetSnapshotId == input.ObservedPerformanceSnapshotId &&
                             period.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue &&
                             period.PeriodStart >= input.PeriodStart &&
                             period.PeriodEnd <= input.PeriodEnd)
            .GroupBy(period => period.CasinoCompetitorId)
            .ToDictionary(group => group.Key, group => group.OrderBy(period => period.PeriodStart).ToArray());
        var facilities = new List<FacilityEmploymentProductivity>();
        foreach (var competitor in input.Competitors.OrderBy(item => item.StableVenueId, StringComparer.Ordinal))
        {
            if (competitor.ReportedEmployment is not > 0 ||
                !periodsByFacility.TryGetValue(competitor.Id, out var periods) ||
                !HasCompleteCleanCoverage(periods, input.PeriodStart, input.PeriodEnd))
            {
                continue;
            }
            var observedGgr = periods.Sum(period => period.ReportedAmount);
            if (observedGgr <= 0)
            {
                continue;
            }
            facilities.Add(new FacilityEmploymentProductivity(
                competitor.StableVenueId,
                competitor.ReportedEmployment.Value,
                observedGgr,
                competitor.ReportedEmployment.Value / Convert.ToDouble(observedGgr / 1_000_000m)));
        }

        if (facilities.Count < MinimumFacilitySample)
        {
            return new EmploymentProductivityBenchmarkResolution(
                null,
                [
                    $"Observed-performance snapshot '{input.ObservedPerformanceSnapshotId}' supplied {facilities.Count} " +
                    $"complete facility-level employment/GGR comparable(s); at least {MinimumFacilitySample} are required. " +
                    "No employee count, GGR, or jobs-per-million ratio was imputed."
                ]);
        }

        var totalEmployment = facilities.Sum(facility => facility.ReportedEmployment);
        var totalGgr = facilities.Sum(facility => facility.ObservedGgr);
        var ratios = facilities.Select(facility => facility.JobsPerMillionGgr).ToArray();
        return new EmploymentProductivityBenchmarkResolution(
            new EmploymentProductivityBenchmark(
                input.ObservedPerformanceSnapshotId,
                Method,
                input.PeriodStart,
                input.PeriodEnd,
                totalEmployment / Convert.ToDouble(totalGgr / 1_000_000m),
                ratios.Min(),
                ratios.Max(),
                facilities),
            [
                $"Direct and incumbent casino job density uses the revenue-weighted ratio across {facilities.Count} " +
                "facilities with regulator-reported total employment and complete observed comparable GGR. " +
                "The regulator source does not identify occupation mix, full-time-equivalent status, or indirect/induced employment."
            ]);
    }

    private static bool HasCompleteCleanCoverage(
        IReadOnlyList<CasinoGamingRevenuePeriod> periods,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (periods.Count == 0 || periods[0].PeriodStart != periodStart || periods[^1].PeriodEnd != periodEnd ||
            periods.Any(period => period.ReportedAmount < 0 || period.AnomalyFlagsJson != "[]"))
        {
            return false;
        }
        for (var index = 1; index < periods.Count; index++)
        {
            if (periods[index].PeriodStart != periods[index - 1].PeriodEnd.AddDays(1))
            {
                return false;
            }
        }
        return true;
    }
}
