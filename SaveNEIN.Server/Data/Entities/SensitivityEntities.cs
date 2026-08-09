using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("sensitivity_analyses")]
public sealed class SensitivityAnalysis
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(160)]
    public string AnalysisKey { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Version { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    public Guid BaselineModelRunId { get; set; }

    [Required, MaxLength(80)]
    public string OutputMetric { get; set; } = SensitivityOutputMetrics.StabilizedTotalGgr;

    public decimal BaselineMetricValue { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = SensitivityAnalysisStatuses.Draft;

    [Required]
    public string InputJson { get; set; } = "{}";

    public string? ErrorSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedAtUtc { get; set; }
    public bool IsImmutable { get; set; }
}

[Table("sensitivity_analysis_points")]
public sealed class SensitivityAnalysisPoint
{
    [Key]
    public long Id { get; set; }

    public Guid SensitivityAnalysisId { get; set; }

    [Required, MaxLength(160)]
    public string ParameterKey { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string Direction { get; set; } = string.Empty;

    public double ParameterValue { get; set; }
    public Guid ModelRunId { get; set; }
    public decimal OutputMetricValue { get; set; }
    public decimal DeltaFromBaseline { get; set; }
    public decimal StabilizedTotalGgr { get; set; }
    public decimal LocalDiscretionaryDisplacement { get; set; }
    public decimal GrossGamingTax { get; set; }
    public decimal GrossSocialCost { get; set; }
    public double NetPermanentJobs { get; set; }
    public decimal NetHostLocalImpact { get; set; }
    public decimal NetHostStateImpact { get; set; }
}

public static class SensitivityAnalysisStatuses
{
    public const string Draft = "draft";
    public const string Finalized = "finalized";
    public const string Failed = "failed";
}

public static class SensitivityOutputMetrics
{
    public const string StabilizedTotalGgr = "stabilized-total-ggr";
    public const string LocalDiscretionaryDisplacement = "local-discretionary-displacement";
    public const string GrossGamingTax = "gross-gaming-tax";
    public const string GrossSocialCost = "gross-social-cost";
    public const string NetPermanentJobs = "net-permanent-jobs";
    public const string NetHostLocalImpact = "net-host-local-impact";
    public const string NetHostStateImpact = "net-host-state-impact";

    public static bool IsSupported(string metric) => metric is
        StabilizedTotalGgr or LocalDiscretionaryDisplacement or GrossGamingTax or GrossSocialCost or
        NetPermanentJobs or NetHostLocalImpact or NetHostStateImpact;
}
