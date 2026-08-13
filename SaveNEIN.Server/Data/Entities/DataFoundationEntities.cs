// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace SaveNEIN.Server.Data.Entities;

[Table("data_sources")]
public sealed class DataSource
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(240)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(240)]
    public string Publisher { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Url { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string SourceType { get; set; } = string.Empty;

    [Required, MaxLength(240)]
    public string GeographicCoverage { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string VintagePeriod { get; set; } = string.Empty;

    public DateTime RetrievedAtUtc { get; set; }

    public string? LicenseTermsNotes { get; set; }

    [Required, MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    public bool IsAuthoritative { get; set; }
    public string? Notes { get; set; }
}

[Table("dataset_snapshots")]
public sealed class DatasetSnapshot
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public long DataSourceId { get; set; }

    [Required, MaxLength(160)]
    public string DatasetKey { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Period { get; set; } = string.Empty;

    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
    public long RowCount { get; set; }

    [Required, MaxLength(128)]
    public string Checksum { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string TransformVersion { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string ValidationState { get; set; } = DatasetValidationStates.Pending;

    public bool IsSealed { get; set; }

    [Required]
    public string WarningsJson { get; set; } = "[]";

    [Required]
    public string ErrorsJson { get; set; } = "[]";
}

[Table("model_run_dataset_snapshot_references")]
public sealed class ModelRunDatasetSnapshotReference
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(80)]
    public string Role { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string ReferenceKey { get; set; } = "default";
}

[Table("origin_zones")]
public sealed class OriginZone
{
    [Key]
    public long Id { get; set; }

    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(160)]
    public string StableOriginId { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string OriginType { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string GeographyCode { get; set; } = string.Empty;

    [Required, MaxLength(3)]
    public string CountryCode { get; set; } = "USA";

    [MaxLength(10)]
    public string? StateOrTerritoryCode { get; set; }

    [MaxLength(20)]
    public string? CountyEquivalentCode { get; set; }

    [MaxLength(20)]
    public string? MetropolitanStatisticalAreaCode { get; set; }

    [MaxLength(20)]
    public string? CombinedStatisticalAreaCode { get; set; }

    [Column(TypeName = "geometry(Point, 4326)")]
    public Point RepresentativePoint { get; set; } = null!;

    [Column(TypeName = "geometry(Geometry, 4326)")]
    public Geometry AreaGeometry { get; set; } = null!;
}

[Table("origin_zone_age_bins")]
public sealed class OriginZoneAgeBin
{
    [Key]
    public long Id { get; set; }

    public long OriginZoneId { get; set; }
    public Guid DatasetSnapshotId { get; set; }
    public int ObservationYear { get; set; }
    public int MinimumAge { get; set; }
    public int? MaximumAge { get; set; }
    public long Population { get; set; }

    [Required, MaxLength(80)]
    public string InterpolationMethod { get; set; } = AgeBinInterpolationMethods.None;

    [Required, MaxLength(30)]
    public string ControlValidationState { get; set; } = DatasetValidationStates.Pending;
}

[Table("origin_zone_income_periods")]
public sealed class OriginZoneIncomePeriod
{
    [Key]
    public long Id { get; set; }

    public long OriginZoneId { get; set; }
    public Guid DatasetSnapshotId { get; set; }
    public int TaxYear { get; set; }
    public long? ReturnCount { get; set; }
    public decimal? AdjustedGrossIncome { get; set; }
    public decimal? InflationAdjustedAdjustedGrossIncome { get; set; }
    public decimal? MedianHouseholdIncome { get; set; }
    public int? DollarYear { get; set; }
    public string? Notes { get; set; }
}

[Table("casino_competitor_history")]
public sealed class CasinoCompetitorHistory
{
    [Key]
    public long Id { get; set; }

    public int CasinoCompetitorId { get; set; }
    public Guid DatasetSnapshotId { get; set; }

    [Required, MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    [MaxLength(240)]
    public string? OperatorName { get; set; }

    public string? Notes { get; set; }
}

[Table("casino_gaming_revenue_periods")]
public sealed class CasinoGamingRevenuePeriod
{
    [Key]
    public long Id { get; set; }

    public int CasinoCompetitorId { get; set; }
    public Guid DatasetSnapshotId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    [Required, MaxLength(20)]
    public string PeriodGranularity { get; set; } = "monthly";

    [Required, MaxLength(80)]
    public string ReportedMetricKey { get; set; } = string.Empty;

    [Required]
    public string ReportedMetricDefinition { get; set; } = string.Empty;

    public decimal ReportedAmount { get; set; }
    public decimal? InflationAdjustedAmount { get; set; }
    public int? InflationAdjustmentDollarYear { get; set; }
    public double? ReportedUnitCount { get; set; }

    [Required]
    public string AnomalyFlagsJson { get; set; } = "[]";

    public string? Notes { get; set; }
}

public static class DatasetValidationStates
{
    public const string Pending = "pending";
    public const string Validated = "validated";
    public const string Warning = "warning";
    public const string Rejected = "rejected";
}

public static class AgeBinInterpolationMethods
{
    public const string None = "none";
    public const string UniformWithinBin = "uniform-within-bin";
}
