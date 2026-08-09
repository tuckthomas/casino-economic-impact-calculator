using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("jurisdictions")]
public class Jurisdiction
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Kind { get; set; } = string.Empty;

    public int? ParentJurisdictionId { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("jurisdiction_rules")]
public class JurisdictionRule
{
    [Key]
    public long Id { get; set; }

    public int JurisdictionId { get; set; }

    [Required, MaxLength(100)]
    public string RuleType { get; set; } = string.Empty;

    [Required]
    public string RuleValueJson { get; set; } = "{}";

    [Required, MaxLength(30)]
    public string ValidationState { get; set; } = JurisdictionRuleValidationStates.Incomplete;

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? SourceUrl { get; set; }
    public string? ProvenanceNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("model_parameter_definitions")]
public class ModelParameterDefinition
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(160)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string TechnicalDescription { get; set; } = string.Empty;

    [Required]
    public string PlainLanguageDescription { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Units { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string DataType { get; set; } = "number";

    public double SystemDefaultValue { get; set; }
    public double? ComputationalMinimum { get; set; }
    public double? ComputationalMaximum { get; set; }
    public double? RecommendedMinimum { get; set; }
    public double? RecommendedMaximum { get; set; }
    public double? UiStep { get; set; }

    [Required, MaxLength(30)]
    public string UiExposureLevel { get; set; } = "advanced";

    public bool IsUserOverridable { get; set; }
    [Required, MaxLength(80)]
    public string ModelVersionApplicability { get; set; } = "gravity-v1";
    public string? ProvenanceNotes { get; set; }
    public bool IsCalibrated { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("model_parameter_sets")]
public class ModelParameterSet
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(160)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Scope { get; set; } = "national";

    public int? JurisdictionId { get; set; }
    [MaxLength(120)]
    public string? MarketCode { get; set; }
    [MaxLength(40)]
    public string? ScenarioKind { get; set; }
    [Required, MaxLength(60)]
    public string Version { get; set; } = "1";
    [Required, MaxLength(80)]
    public string ModelVersionApplicability { get; set; } = "gravity-v1";
    public bool IsImmutable { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CalibrationNotes { get; set; }
}

[Table("model_parameter_set_values")]
public class ModelParameterSetValue
{
    [Key]
    public long Id { get; set; }

    public long ParameterSetId { get; set; }
    public long ParameterDefinitionId { get; set; }
    public double Value { get; set; }
    public string? ProvenanceNotes { get; set; }
}

[Table("model_runs")]
public class ModelRun
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(80)]
    public string ModelVersion { get; set; } = "gravity-v1";

    [Required, MaxLength(30)]
    public string Status { get; set; } = ModelRunStatuses.Draft;

    public int? JurisdictionId { get; set; }
    public long? BaseParameterSetId { get; set; }
    public Guid? DevelopmentProgramId { get; set; }
    [Required]
    public string ResolvedInputJson { get; set; } = "{}";
    [Required]
    public string DataSnapshotReferencesJson { get; set; } = "{}";
    public double CandidateLatitude { get; set; }
    public double CandidateLongitude { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedAtUtc { get; set; }
    public TimeSpan? ExecutionDuration { get; set; }
    public string? WarningSummary { get; set; }
    public string? ErrorSummary { get; set; }
}

[Table("model_run_parameter_values")]
public class ModelRunParameterValue
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public long ParameterDefinitionId { get; set; }
    public double SystemFallbackValue { get; set; }
    public double DefaultValue { get; set; }
    public double? ScenarioValue { get; set; }
    public double? UserOverrideValue { get; set; }
    public double FinalValue { get; set; }
    [Required, MaxLength(40)]
    public string SourceLayer { get; set; } = string.Empty;
    public bool IsOutsideRecommendedRange { get; set; }
    public string? WarningText { get; set; }
}

[Table("model_run_parameter_set_references")]
public class ModelRunParameterSetReference
{
    [Key]
    public long Id { get; set; }

    public Guid ModelRunId { get; set; }
    public long ParameterSetId { get; set; }

    [Required, MaxLength(40)]
    public string SourceLayer { get; set; } = string.Empty;
}

public static class ModelRunStatuses
{
    public const string Draft = "draft";
    public const string Finalized = "finalized";
    public const string Failed = "failed";
}

public static class JurisdictionRuleValidationStates
{
    public const string Validated = "validated";
    public const string Provisional = "provisional";
    public const string Incomplete = "incomplete";
}
