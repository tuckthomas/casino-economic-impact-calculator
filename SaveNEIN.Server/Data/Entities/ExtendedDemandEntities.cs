// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace SaveNEIN.Server.Data.Entities;

[Table("tourism_market_observations")]
public sealed class TourismMarketObservation
{
    [Key]
    public long Id { get; set; }
    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(160)]
    public string StableObservationId { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string MarketKey { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string GeographyType { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string GeographyCode { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    [Required, MaxLength(60)]
    public string SourceMetricKind { get; set; } = string.Empty;

    public decimal SourceQuantity { get; set; }
    public decimal NormalizedVisitorPersonTrips { get; set; }

    [Required, MaxLength(160)]
    public string NormalizationMethod { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

[Table("traffic_corridor_observations")]
public sealed class TrafficCorridorObservation
{
    [Key]
    public long Id { get; set; }
    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(160)]
    public string StableObservationId { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string RouteDesignation { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string JurisdictionCode { get; set; } = string.Empty;

    [Column(TypeName = "geometry(Point, 4326)")]
    public Point CountLocation { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public double AnnualAverageDailyTraffic { get; set; }
    public int ObservationDays { get; set; } = 365;

    [Required, MaxLength(120)]
    public string CountMethod { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? DirectionDefinition { get; set; }

    public string? Notes { get; set; }
}

[Table("local_economic_sector_observations")]
public sealed class LocalEconomicSectorObservation
{
    [Key]
    public long Id { get; set; }
    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(160)]
    public string StableObservationId { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string GeographyType { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string GeographyCode { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string SectorKey { get; set; } = string.Empty;

    [Required]
    public string NaicsCodesJson { get; set; } = "[]";

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public long? Establishments { get; set; }
    public long? Employment { get; set; }
    public decimal? AnnualPayroll { get; set; }
    public decimal? AnnualReceiptsOrSales { get; set; }

    [Required, MaxLength(200)]
    public string SourceMetricDefinition { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

[Table("model_run_demand_components")]
public sealed class ModelRunDemandComponent
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }
    public Guid? DatasetSnapshotId { get; set; }

    [Required, MaxLength(40)]
    public string ComponentType { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string SourceRecordKey { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string MethodKey { get; set; } = string.Empty;

    public decimal InputQuantity { get; set; }
    public decimal DeduplicatedQuantity { get; set; }
    public decimal EligibleQuantity { get; set; }
    public decimal ParticipatingQuantity { get; set; }
    public decimal CapturedQuantity { get; set; }
    public decimal Ggr { get; set; }

    [Required]
    public string DetailsJson { get; set; } = "{}";
}

[Table("model_run_capacity_diagnostics")]
public sealed class ModelRunCapacityDiagnostic
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }

    [Required, MaxLength(160)]
    public string FacilityKey { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Status { get; set; } = CapacityDiagnosticStatuses.NotEvaluated;

    public decimal StabilizedGgr { get; set; }
    public decimal? PlausibleCapacityMinimum { get; set; }
    public decimal? PlausibleCapacityMaximum { get; set; }
    public double? ImpliedResidualSlotWinPerUnitDay { get; set; }
    public bool IsBelowValidatedRange { get; set; }
    public bool IsAboveValidatedRange { get; set; }
    public string? WarningText { get; set; }
}

[Table("model_run_ramp_results")]
public sealed class ModelRunRampResult
{
    [Key]
    public long Id { get; set; }
    public Guid ModelRunId { get; set; }

    [Required, MaxLength(160)]
    public string FacilityKey { get; set; } = string.Empty;

    public int CalendarYear { get; set; }
    public int OperatingYearNumber { get; set; }

    [Required, MaxLength(40)]
    public string PeriodKind { get; set; } = string.Empty;

    public double OperatingYearFraction { get; set; }
    public double StabilizationShare { get; set; }
    public decimal ProjectedGgr { get; set; }
}

public static class ModelDemandComponentTypes
{
    public const string Tourism = "tourism";
    public const string Traffic = "traffic";
}

public static class CapacityDiagnosticStatuses
{
    public const string WithinRange = "within-range";
    public const string BelowRange = "below-range";
    public const string AboveRange = "above-range";
    public const string NotEvaluated = "not-evaluated";
}
