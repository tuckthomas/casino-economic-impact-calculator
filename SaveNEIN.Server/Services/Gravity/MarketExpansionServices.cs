// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record AccessibilityExpansionInput(
    string OriginKey,
    double BaselineResidentDemand,
    double BaselineLogAccessibility,
    double WithProjectLogAccessibility,
    double AccessibilityElasticity,
    double MaximumInducedDemandShare);

public sealed record AccessibilityExpansionResult(
    string OriginKey,
    double BaselineResidentDemand,
    double LogAccessibilityChange,
    double UnboundedInducedDemandShare,
    double AppliedInducedDemandShare,
    double InducedResidentDemand,
    IReadOnlyList<string> Warnings);

public interface IAccessibilityExpansionService
{
    AccessibilityExpansionResult Calculate(AccessibilityExpansionInput input);
}

public sealed class AccessibilityExpansionService : IAccessibilityExpansionService
{
    public AccessibilityExpansionResult Calculate(AccessibilityExpansionInput input)
    {
        RequireFiniteNonnegative(input.BaselineResidentDemand, nameof(input.BaselineResidentDemand));
        RequireFinite(input.BaselineLogAccessibility, nameof(input.BaselineLogAccessibility));
        RequireFinite(input.WithProjectLogAccessibility, nameof(input.WithProjectLogAccessibility));
        RequireFiniteNonnegative(input.AccessibilityElasticity, nameof(input.AccessibilityElasticity));
        RequireShare(input.MaximumInducedDemandShare, nameof(input.MaximumInducedDemandShare));
        if (string.IsNullOrWhiteSpace(input.OriginKey))
        {
            throw new ArgumentException("An origin key is required.", nameof(input));
        }

        var logChange = input.WithProjectLogAccessibility - input.BaselineLogAccessibility;
        var unboundedShare = input.AccessibilityElasticity * logChange;
        var warnings = new List<string>();
        if (unboundedShare < 0)
        {
            warnings.Add("With-project accessibility is below baseline; induced demand is floored at zero and the decline remains visible in the accessibility diagnostic.");
        }
        if (unboundedShare > input.MaximumInducedDemandShare)
        {
            warnings.Add("The unbounded accessibility response exceeded the configured induced-demand share cap.");
        }
        var appliedShare = Math.Clamp(unboundedShare, 0, input.MaximumInducedDemandShare);
        return new AccessibilityExpansionResult(
            input.OriginKey.Trim(),
            input.BaselineResidentDemand,
            logChange,
            unboundedShare,
            appliedShare,
            input.BaselineResidentDemand * appliedShare,
            warnings);
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "A finite value is required.");
        }
    }

    private static void RequireFiniteNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "A finite nonnegative value is required.");
        }
    }

    private static void RequireShare(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "A finite share between zero and one is required.");
        }
    }
}

public sealed record TourismDemandInput(
    string InputKey,
    double VisitorPersonTrips,
    double ResidentOriginOverlapShare,
    double CasinoEligibleVisitorShare,
    double GamingParticipationRate,
    double ProposedFacilityCaptureRate,
    double GgrPerCapturedParticipant);

public sealed record TourismDemandResult(
    string InputKey,
    double VisitorPersonTrips,
    double DeduplicatedVisitorPersonTrips,
    double EligibleVisitorTrips,
    double GamingParticipantTrips,
    double CapturedParticipantTrips,
    double TourismGgr);

public interface ITourismDemandService
{
    TourismDemandResult Calculate(TourismDemandInput input);
}

public sealed class TourismDemandService : ITourismDemandService
{
    public TourismDemandResult Calculate(TourismDemandInput input)
    {
        DemandLayerValidation.RequireKey(input.InputKey, nameof(input.InputKey));
        DemandLayerValidation.RequireNonnegative(input.VisitorPersonTrips, nameof(input.VisitorPersonTrips));
        DemandLayerValidation.RequireShare(input.ResidentOriginOverlapShare, nameof(input.ResidentOriginOverlapShare));
        DemandLayerValidation.RequireShare(input.CasinoEligibleVisitorShare, nameof(input.CasinoEligibleVisitorShare));
        DemandLayerValidation.RequireShare(input.GamingParticipationRate, nameof(input.GamingParticipationRate));
        DemandLayerValidation.RequireShare(input.ProposedFacilityCaptureRate, nameof(input.ProposedFacilityCaptureRate));
        DemandLayerValidation.RequireNonnegative(input.GgrPerCapturedParticipant, nameof(input.GgrPerCapturedParticipant));

        var deduplicated = input.VisitorPersonTrips * (1 - input.ResidentOriginOverlapShare);
        var eligible = deduplicated * input.CasinoEligibleVisitorShare;
        var participants = eligible * input.GamingParticipationRate;
        var captured = participants * input.ProposedFacilityCaptureRate;
        return new TourismDemandResult(
            input.InputKey.Trim(),
            input.VisitorPersonTrips,
            deduplicated,
            eligible,
            participants,
            captured,
            captured * input.GgrPerCapturedParticipant);
    }
}

public sealed record TrafficInterceptInput(
    string CorridorKey,
    double AnnualAverageDailyTraffic,
    int ObservationDays,
    double EligiblePassengersPerVehicle,
    double RelevantDirectionShare,
    double InterchangeAccessibilityModifier,
    double StopInterceptRate,
    double ResidentOriginOverlapShare,
    double TourismOverlapShare,
    double GgrPerInterceptedTraveler);

public sealed record TrafficInterceptResult(
    string CorridorKey,
    double AnnualVehicleTrips,
    double DirectionallyRelevantEligibleTravelerTrips,
    double AccessibleTravelerTrips,
    double InterceptedTravelerTripsBeforeDeduplication,
    double DeduplicatedInterceptedTravelerTrips,
    double TrafficGgr);

public interface ITrafficInterceptService
{
    TrafficInterceptResult Calculate(TrafficInterceptInput input);
}

public sealed class TrafficInterceptService : ITrafficInterceptService
{
    public TrafficInterceptResult Calculate(TrafficInterceptInput input)
    {
        DemandLayerValidation.RequireKey(input.CorridorKey, nameof(input.CorridorKey));
        DemandLayerValidation.RequireNonnegative(input.AnnualAverageDailyTraffic, nameof(input.AnnualAverageDailyTraffic));
        if (input.ObservationDays is < 1 or > 366)
        {
            throw new ArgumentOutOfRangeException(nameof(input.ObservationDays), "Observation days must be between 1 and 366.");
        }
        DemandLayerValidation.RequireNonnegative(input.EligiblePassengersPerVehicle, nameof(input.EligiblePassengersPerVehicle));
        DemandLayerValidation.RequireShare(input.RelevantDirectionShare, nameof(input.RelevantDirectionShare));
        DemandLayerValidation.RequireShare(input.InterchangeAccessibilityModifier, nameof(input.InterchangeAccessibilityModifier));
        DemandLayerValidation.RequireShare(input.StopInterceptRate, nameof(input.StopInterceptRate));
        DemandLayerValidation.RequireShare(input.ResidentOriginOverlapShare, nameof(input.ResidentOriginOverlapShare));
        DemandLayerValidation.RequireShare(input.TourismOverlapShare, nameof(input.TourismOverlapShare));
        DemandLayerValidation.RequireNonnegative(input.GgrPerInterceptedTraveler, nameof(input.GgrPerInterceptedTraveler));

        var vehicles = input.AnnualAverageDailyTraffic * input.ObservationDays;
        var directionallyRelevant = vehicles * input.EligiblePassengersPerVehicle * input.RelevantDirectionShare;
        var accessible = directionallyRelevant * input.InterchangeAccessibilityModifier;
        var intercepted = accessible * input.StopInterceptRate;
        var deduplicated = intercepted * (1 - input.ResidentOriginOverlapShare) * (1 - input.TourismOverlapShare);
        return new TrafficInterceptResult(
            input.CorridorKey.Trim(),
            vehicles,
            directionallyRelevant,
            accessible,
            intercepted,
            deduplicated,
            deduplicated * input.GgrPerInterceptedTraveler);
    }
}

public sealed record CapacityDiagnosticInput(
    double StabilizedGgr,
    int SlotOrVltPositions,
    int TableGameCount,
    int OperatingDaysPerYear,
    double ValidatedSlotWinPerUnitDayMinimum,
    double ValidatedSlotWinPerUnitDayMaximum,
    double ValidatedTableWinPerTableDayMinimum,
    double ValidatedTableWinPerTableDayMaximum,
    int HotelRoomCount,
    int EventCapacity);

public sealed record CapacityDiagnosticResult(
    double StabilizedGgr,
    double PlausibleCapacityMinimum,
    double PlausibleCapacityMaximum,
    double? ImpliedResidualSlotWinPerUnitDay,
    bool IsBelowValidatedRange,
    bool IsAboveValidatedRange,
    IReadOnlyList<string> Warnings);

public interface ICapacityDiagnosticService
{
    CapacityDiagnosticResult Evaluate(CapacityDiagnosticInput input);
}

public sealed class CapacityDiagnosticService : ICapacityDiagnosticService
{
    public CapacityDiagnosticResult Evaluate(CapacityDiagnosticInput input)
    {
        DemandLayerValidation.RequireNonnegative(input.StabilizedGgr, nameof(input.StabilizedGgr));
        if (input.SlotOrVltPositions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.SlotOrVltPositions), "Slot or VLT positions cannot be negative.");
        }
        if (input.TableGameCount < 0 || input.HotelRoomCount < 0 || input.EventCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Facility counts cannot be negative.");
        }
        if (input.SlotOrVltPositions == 0 && input.TableGameCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "At least one slot/VLT position or table game is required.");
        }
        if (input.OperatingDaysPerYear is < 1 or > 366)
        {
            throw new ArgumentOutOfRangeException(nameof(input.OperatingDaysPerYear));
        }
        DemandLayerValidation.RequireOrderedNonnegativeRange(
            input.ValidatedSlotWinPerUnitDayMinimum,
            input.ValidatedSlotWinPerUnitDayMaximum,
            "slot win per unit day");
        DemandLayerValidation.RequireOrderedNonnegativeRange(
            input.ValidatedTableWinPerTableDayMinimum,
            input.ValidatedTableWinPerTableDayMaximum,
            "table win per table day");

        var days = input.OperatingDaysPerYear;
        var capacityMinimum = days * (
            input.SlotOrVltPositions * input.ValidatedSlotWinPerUnitDayMinimum +
            input.TableGameCount * input.ValidatedTableWinPerTableDayMinimum);
        var capacityMaximum = days * (
            input.SlotOrVltPositions * input.ValidatedSlotWinPerUnitDayMaximum +
            input.TableGameCount * input.ValidatedTableWinPerTableDayMaximum);
        var midpointTableWin = (input.ValidatedTableWinPerTableDayMinimum + input.ValidatedTableWinPerTableDayMaximum) / 2;
        var residualSlotGgr = Math.Max(0, input.StabilizedGgr - input.TableGameCount * midpointTableWin * days);
        var impliedSlotWin = input.SlotOrVltPositions == 0
            ? (double?)null
            : residualSlotGgr / (input.SlotOrVltPositions * days);
        var below = input.StabilizedGgr < capacityMinimum;
        var above = input.StabilizedGgr > capacityMaximum;
        var warnings = new List<string>();
        if (below)
        {
            warnings.Add("Forecast productivity is below the configured validated facility-capacity range.");
        }
        if (above)
        {
            warnings.Add("Forecast productivity exceeds the configured validated facility-capacity range; the forecast was flagged but not silently capped.");
        }
        if (input.HotelRoomCount == 0 && input.EventCapacity == 0)
        {
            warnings.Add("No hotel or event capacity is configured; destination-demand assumptions require separate support.");
        }
        return new CapacityDiagnosticResult(
            input.StabilizedGgr,
            capacityMinimum,
            capacityMaximum,
            impliedSlotWin,
            below,
            above,
            warnings);
    }
}

public sealed record CapacityProductivityBenchmarkInput(
    Guid ObservedPerformanceSnapshotId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyCollection<CasinoCompetitor> Competitors,
    IReadOnlyCollection<CasinoGamingRevenuePeriod> PerformancePeriods);

public sealed record FacilityCapacityProductivity(
    string StableVenueId,
    double SlotWinPerUnitDay,
    double TableWinPerTableDay);

public sealed record CapacityProductivityBenchmark(
    Guid ObservedPerformanceSnapshotId,
    string Method,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    double SlotWinPerUnitDayMinimum,
    double SlotWinPerUnitDayMaximum,
    double TableWinPerTableDayMinimum,
    double TableWinPerTableDayMaximum,
    IReadOnlyList<FacilityCapacityProductivity> Facilities);

public sealed record CapacityProductivityBenchmarkResolution(
    CapacityProductivityBenchmark? Benchmark,
    IReadOnlyList<string> Warnings);

public interface ICapacityProductivityBenchmarkService
{
    CapacityProductivityBenchmarkResolution Resolve(CapacityProductivityBenchmarkInput input);
}

/// <summary>
/// Resolves a capacity range only from regulator-published slot/table revenue components
/// joined to the exact versioned competitor inventory used by the run. It does not split
/// total GGR using an assumed ratio or synthesize missing facility counts.
/// </summary>
public sealed class CapacityProductivityBenchmarkService : ICapacityProductivityBenchmarkService
{
    private const int MinimumFacilitySample = 3;
    private const string Method = "observed-facility-min-max-v1";

    public CapacityProductivityBenchmarkResolution Resolve(CapacityProductivityBenchmarkInput input)
    {
        if (input.PeriodEnd < input.PeriodStart)
        {
            throw new ArgumentException("Benchmark period end cannot precede its start.", nameof(input));
        }

        var periodsByFacilityAndMetric = input.PerformancePeriods
            .Where(period => period.DatasetSnapshotId == input.ObservedPerformanceSnapshotId &&
                             period.PeriodStart >= input.PeriodStart &&
                             period.PeriodEnd <= input.PeriodEnd &&
                             (period.ReportedMetricKey == GamingRevenueMetricKeys.SlotOrVltGamingRevenue ||
                              period.ReportedMetricKey == GamingRevenueMetricKeys.TableGameGamingRevenue))
            .GroupBy(period => (period.CasinoCompetitorId, period.ReportedMetricKey))
            .ToDictionary(group => group.Key, group => group.OrderBy(period => period.PeriodStart).ToArray());
        var facilities = new List<FacilityCapacityProductivity>();
        foreach (var competitor in input.Competitors.OrderBy(item => item.StableVenueId, StringComparer.Ordinal))
        {
            if (!periodsByFacilityAndMetric.TryGetValue(
                    (competitor.Id, GamingRevenueMetricKeys.SlotOrVltGamingRevenue),
                    out var slotPeriods) ||
                !periodsByFacilityAndMetric.TryGetValue(
                    (competitor.Id, GamingRevenueMetricKeys.TableGameGamingRevenue),
                    out var tablePeriods) ||
                !HasCompleteCleanCoverage(slotPeriods, input.PeriodStart, input.PeriodEnd) ||
                !HasCompleteCleanCoverage(tablePeriods, input.PeriodStart, input.PeriodEnd))
            {
                continue;
            }

            var slotRevenue = slotPeriods.Sum(period => Convert.ToDouble(
                period.InflationAdjustedAmount ?? period.ReportedAmount));
            var tableRevenue = tablePeriods.Sum(period => Convert.ToDouble(
                period.InflationAdjustedAmount ?? period.ReportedAmount));
            var slotUnitDays = slotPeriods.Sum(UnitDays);
            var tableUnitDays = tablePeriods.Sum(UnitDays);
            facilities.Add(new FacilityCapacityProductivity(
                competitor.StableVenueId,
                slotRevenue / slotUnitDays,
                tableRevenue / tableUnitDays));
        }

        if (facilities.Count < MinimumFacilitySample)
        {
            return new CapacityProductivityBenchmarkResolution(
                null,
                [
                    $"Observed-performance snapshot '{input.ObservedPerformanceSnapshotId}' supplied {facilities.Count} " +
                    $"complete facility-level slot/table productivity comparable(s); at least {MinimumFacilitySample} are required. " +
                    "No component split or facility inventory was synthesized."
                ]);
        }

        var slotProductivity = facilities.Select(item => item.SlotWinPerUnitDay).ToArray();
        var tableProductivity = facilities.Select(item => item.TableWinPerTableDay).ToArray();
        return new CapacityProductivityBenchmarkResolution(
            new CapacityProductivityBenchmark(
                input.ObservedPerformanceSnapshotId,
                Method,
                input.PeriodStart,
                input.PeriodEnd,
                slotProductivity.Min(),
                slotProductivity.Max(),
                tableProductivity.Min(),
                tableProductivity.Max(),
                facilities),
            [
                $"Capacity productivity uses the observed minimum-to-maximum range across {facilities.Count} complete regulator-published facilities. " +
                "Revenue and monthly unit counts come from the same versioned observed-performance snapshot and cover the requested period; explicit operating-hour normalization remains unavailable."
            ]);
    }

    private static bool HasCompleteCleanCoverage(
        IReadOnlyList<CasinoGamingRevenuePeriod> periods,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (periods.Count == 0 || periods[0].PeriodStart != periodStart || periods[^1].PeriodEnd != periodEnd ||
            periods.Any(period =>
                period.ReportedAmount < 0 ||
                period.ReportedUnitCount is not > 0 ||
                period.AnomalyFlagsJson != "[]"))
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

    private static double UnitDays(CasinoGamingRevenuePeriod period) =>
        period.ReportedUnitCount!.Value * (period.PeriodEnd.DayNumber - period.PeriodStart.DayNumber + 1);
}

public sealed record RampScheduleInput(
    double StabilizedGgr,
    int OpeningYear,
    int OpeningMonth,
    double FirstFullYearShare,
    double SecondFullYearShare,
    int StabilizedYearNumber,
    double StabilizedAnnualGrowthRate,
    int ProjectionYears);

public sealed record RampYearResult(
    int CalendarYear,
    int OperatingYearNumber,
    string PeriodKind,
    double OperatingYearFraction,
    double StabilizationShare,
    double ProjectedGgr);

public interface IRampScheduleService
{
    IReadOnlyList<RampYearResult> Build(RampScheduleInput input);
}

public sealed class RampScheduleService : IRampScheduleService
{
    public IReadOnlyList<RampYearResult> Build(RampScheduleInput input)
    {
        DemandLayerValidation.RequireNonnegative(input.StabilizedGgr, nameof(input.StabilizedGgr));
        if (input.OpeningYear is < 1900 or > 2200 || input.OpeningMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Opening year/month is outside the supported range.");
        }
        RequireRampShare(input.FirstFullYearShare, nameof(input.FirstFullYearShare));
        RequireRampShare(input.SecondFullYearShare, nameof(input.SecondFullYearShare));
        if (input.SecondFullYearShare < input.FirstFullYearShare)
        {
            throw new InvalidOperationException("Second-year stabilization share cannot be below first-year share.");
        }
        if (input.StabilizedYearNumber < 3 || input.ProjectionYears < input.StabilizedYearNumber || input.ProjectionYears > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Stabilized year must be at least three and within the projection horizon.");
        }
        if (!double.IsFinite(input.StabilizedAnnualGrowthRate) || input.StabilizedAnnualGrowthRate is <= -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input.StabilizedAnnualGrowthRate));
        }

        var results = new List<RampYearResult>(input.ProjectionYears + 1);
        var partialFraction = (13 - input.OpeningMonth) / 12d;
        results.Add(new RampYearResult(
            input.OpeningYear,
            0,
            "opening-partial-year",
            partialFraction,
            input.FirstFullYearShare,
            input.StabilizedGgr * input.FirstFullYearShare * partialFraction));
        for (var operatingYear = 1; operatingYear <= input.ProjectionYears; operatingYear++)
        {
            double share;
            string kind;
            if (operatingYear == 1)
            {
                share = input.FirstFullYearShare;
                kind = "first-full-year";
            }
            else if (operatingYear == 2)
            {
                share = input.SecondFullYearShare;
                kind = "second-full-year";
            }
            else if (operatingYear < input.StabilizedYearNumber)
            {
                var interpolationProgress = (operatingYear - 2d) / (input.StabilizedYearNumber - 2d);
                share = input.SecondFullYearShare + (1 - input.SecondFullYearShare) * interpolationProgress;
                kind = "ramp-year";
            }
            else
            {
                share = 1;
                kind = operatingYear == input.StabilizedYearNumber ? "stabilized-year" : "long-term";
            }
            var stabilizedYearsElapsed = Math.Max(0, operatingYear - input.StabilizedYearNumber);
            var growth = Math.Pow(1 + input.StabilizedAnnualGrowthRate, stabilizedYearsElapsed);
            results.Add(new RampYearResult(
                input.OpeningYear + operatingYear,
                operatingYear,
                kind,
                1,
                share,
                input.StabilizedGgr * share * growth));
        }
        return results;
    }

    private static void RequireRampShare(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1.5)
        {
            throw new ArgumentOutOfRangeException(name, "A ramp share must be between zero and 1.5.");
        }
    }
}

internal static class DemandLayerValidation
{
    public static void RequireKey(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty key is required.", name);
        }
    }

    public static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "A finite nonnegative value is required.");
        }
    }

    public static void RequireShare(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "A finite share between zero and one is required.");
        }
    }

    public static void RequireOrderedNonnegativeRange(double minimum, double maximum, string name)
    {
        RequireNonnegative(minimum, $"{name} minimum");
        RequireNonnegative(maximum, $"{name} maximum");
        if (maximum < minimum)
        {
            throw new InvalidOperationException($"The {name} maximum cannot be below its minimum.");
        }
    }
}
