// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("development_programs")]
public sealed class DevelopmentProgram
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(160)]
    public string StableProgramId { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Version { get; set; } = string.Empty;

    [Required, MaxLength(240)]
    public string Name { get; set; } = string.Empty;

    public int SlotOrVltPositions { get; set; }
    public int TableGameCount { get; set; }
    public int PokerTableCount { get; set; }
    public bool HasSportsbook { get; set; }
    public int HotelRoomCount { get; set; }
    public int GamingFloorSquareFeet { get; set; }
    public int FoodBeverageVenueCount { get; set; }
    public int EventCapacity { get; set; }
    public int ResortAmenityCount { get; set; }
    public decimal? CapitalCost { get; set; }
    public int? CapitalCostDollarYear { get; set; }
    public DateOnly? PlannedOpeningDate { get; set; }
    public int StabilizedYearNumber { get; set; } = 3;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsImmutable { get; set; }
    public string? Notes { get; set; }
}

[Table("origin_facility_travel")]
public sealed class OriginFacilityTravel
{
    [Key]
    public long Id { get; set; }

    public long OriginZoneId { get; set; }
    public int? CasinoCompetitorId { get; set; }
    public Guid? ModelRunId { get; set; }

    [Required, MaxLength(160)]
    public string FacilityKey { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string FacilityKind { get; set; } = FacilityKinds.Incumbent;

    [Required, MaxLength(128)]
    public string RoutingGraphHash { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string CostingProfile { get; set; } = "auto";

    public double? TravelTimeMinutes { get; set; }
    public double? RoutedDistanceMeters { get; set; }
    public bool RouteFound { get; set; }

    [MaxLength(500)]
    public string? RouteFailureReason { get; set; }

    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("model_run_origin_results")]
public sealed class ModelRunOriginResult
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public long OriginZoneId { get; set; }

    [Required, MaxLength(40)]
    public string DemandSpecification { get; set; } = string.Empty;

    public decimal ResidentDemand { get; set; }
    public double BaselineLogAccessibility { get; set; }
    public double WithProjectLogAccessibility { get; set; }
    public decimal InducedResidentDemand { get; set; }
    public decimal InducedOutsideOptionGgr { get; set; }
    public double BaselineOutsideShare { get; set; }
    public double WithProjectOutsideShare { get; set; }
    public decimal ProposedResidentGgr { get; set; }
    public decimal ProposedInducedResidentGgr { get; set; }
    public decimal TotalProposedResidentGgr { get; set; }
    public decimal HostJurisdictionCapture { get; set; }
    public decimal ExternalJurisdictionCapture { get; set; }
    public decimal TribalOrOtherJurisdictionCapture { get; set; }
    public decimal OutsideOptionCapture { get; set; }
}

[Table("model_run_facility_results")]
public sealed class ModelRunFacilityResult
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public int? CasinoCompetitorId { get; set; }

    [Required, MaxLength(160)]
    public string FacilityKey { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string FacilityKind { get; set; } = FacilityKinds.Incumbent;

    public bool IsProposedFacility { get; set; }
    public double NormalizedAttraction { get; set; }
    public decimal BaselineResidentGgr { get; set; }
    public decimal WithProjectResidentGgr { get; set; }
    public decimal ChangeInResidentGgr { get; set; }
    public decimal InducedResidentGgr { get; set; }
    public decimal TotalWithProjectResidentGgr { get; set; }
    public decimal TourismGgr { get; set; }
    public decimal TrafficGgr { get; set; }
    public decimal StabilizedTotalGgr { get; set; }
}

[Table("model_run_origin_facility_allocations")]
public sealed class ModelRunOriginFacilityAllocation
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public long OriginZoneId { get; set; }
    public long? OriginFacilityTravelId { get; set; }
    public int? CasinoCompetitorId { get; set; }

    [Required, MaxLength(160)]
    public string FacilityKey { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string MarketState { get; set; } = MarketStates.Baseline;

    [Required, MaxLength(80)]
    public string CaptureSourceCategory { get; set; } = string.Empty;

    public bool IsProposedFacility { get; set; }
    public double? NetworkTravelTimeMinutes { get; set; }
    public double? RoutedDistanceMeters { get; set; }
    public double NormalizedAttraction { get; set; }
    public double OriginFacilityModifier { get; set; } = 1d;
    public double? LogWeight { get; set; }
    public double Share { get; set; }
    public decimal AllocatedResidentGgr { get; set; }
    public decimal AllocatedInducedResidentGgr { get; set; }
}

public static class FacilityKinds
{
    public const string Incumbent = "incumbent";
    public const string Scenario = "scenario";
}

public static class MarketStates
{
    public const string Baseline = "baseline";
    public const string WithProject = "with-project";
}
