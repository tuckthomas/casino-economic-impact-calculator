// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("benchmark_studies")]
public sealed class BenchmarkStudy
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(160)]
    public string BenchmarkKey { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string MarketCode { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string GeographyType { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string GeographyCode { get; set; } = string.Empty;

    public DateOnly? StudyDate { get; set; }

    [Required, MaxLength(300)]
    public string ConsultantOrSource { get; set; } = string.Empty;

    public double? CandidateLatitude { get; set; }
    public double? CandidateLongitude { get; set; }

    [Required]
    public string DevelopmentProgramJson { get; set; } = "{}";

    [Required]
    public string ReportedOutputsJson { get; set; } = "{}";

    [Required]
    public string ReportedAssumptionsJson { get; set; } = "{}";

    public string? MethodologicalNotes { get; set; }

    [Required, MaxLength(1_000)]
    public string SourceUrl { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? SourceFileChecksum { get; set; }

    public string? ProvenanceNotes { get; set; }

    [Required, MaxLength(40)]
    public string ValidationState { get; set; } = BenchmarkValidationStates.Registered;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("validation_cases")]
public sealed class ValidationCase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? BenchmarkStudyId { get; set; }

    [Required, MaxLength(160)]
    public string CaseKey { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string MarketCode { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string JurisdictionCode { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string CaseKind { get; set; } = ValidationCaseKinds.IncumbentBacktest;

    [Required, MaxLength(30)]
    public string DatasetPartition { get; set; } = ValidationPartitions.Training;

    [MaxLength(160)]
    public string? HoldoutGroup { get; set; }

    public int? TargetCasinoCompetitorId { get; set; }
    public Guid ModelRunId { get; set; }

    public decimal ObservedRevenue { get; set; }

    [Required, MaxLength(80)]
    public string ObservedMetricKey { get; set; } = string.Empty;

    [Required]
    public string ObservedMetricDefinition { get; set; } = string.Empty;

    public DateOnly TrainingPeriodStart { get; set; }
    public DateOnly TrainingPeriodEnd { get; set; }
    public DateOnly? ValidationPeriodStart { get; set; }
    public DateOnly? ValidationPeriodEnd { get; set; }

    [Required]
    public string InclusionRulesJson { get; set; } = "{}";

    [Required]
    public string PredictorValuesJson { get; set; } = "{}";

    [Required]
    public string ExecutionRequestJson { get; set; } = "{}";

    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("validation_evaluations")]
public sealed class ValidationEvaluation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(160)]
    public string EvaluationKey { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Version { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string ModelVersion { get; set; } = "gravity-v1";

    [Required, MaxLength(80)]
    public string ObjectiveFunction { get; set; } = ValidationObjectiveFunctions.Smape;

    [Required, MaxLength(30)]
    public string Status { get; set; } = ValidationEvaluationStatuses.Draft;

    public long? PublishedParameterSetId { get; set; }

    [Required]
    public string InclusionRulesJson { get; set; } = "{}";

    [Required]
    public string SelectedParametersJson { get; set; } = "{}";

    [Required]
    public string TrainingMetricsJson { get; set; } = "{}";

    [Required]
    public string HoldoutMetricsJson { get; set; } = "{}";

    [Required]
    public string BenchmarkMetricsJson { get; set; } = "{}";

    [Required]
    public string ComparableModelJson { get; set; } = "{}";

    [Required]
    public string ComparableTrainingMetricsJson { get; set; } = "{}";

    [Required]
    public string ComparableHoldoutMetricsJson { get; set; } = "{}";

    [Required]
    public string ComparableBenchmarkMetricsJson { get; set; } = "{}";

    public int TrainingCaseCount { get; set; }
    public int HoldoutCaseCount { get; set; }
    public int BenchmarkCaseCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedAtUtc { get; set; }
    public bool IsImmutable { get; set; }
}

[Table("validation_case_results")]
public sealed class ValidationCaseResult
{
    [Key]
    public long Id { get; set; }

    public Guid ValidationEvaluationId { get; set; }
    public Guid ValidationCaseId { get; set; }
    public Guid ModelRunId { get; set; }

    [Required, MaxLength(40)]
    public string PredictionKind { get; set; } = ValidationPredictionKinds.Gravity;

    [Required, MaxLength(30)]
    public string DatasetPartition { get; set; } = ValidationPartitions.Training;

    public decimal ObservedRevenue { get; set; }
    public decimal PredictedRevenue { get; set; }
    public decimal Residual { get; set; }
    public double? AbsolutePercentageError { get; set; }
    public double SymmetricAbsolutePercentageError { get; set; }

    [Required]
    public string DiagnosticsJson { get; set; } = "{}";
}

public static class BenchmarkValidationStates
{
    public const string Registered = "registered";
    public const string Extracted = "extracted";
    public const string Validated = "validated";
}

public static class ValidationCaseKinds
{
    public const string IncumbentBacktest = "incumbent-backtest";
    public const string PublicBenchmark = "public-benchmark";
    public const string SyntheticNational = "synthetic-national";
}

public static class ValidationPartitions
{
    public const string Training = "training";
    public const string Holdout = "holdout";
    public const string Benchmark = "benchmark";
}

public static class ValidationObjectiveFunctions
{
    public const string Mae = "mae";
    public const string Mape = "mape";
    public const string Smape = "smape";
    public const string Rmse = "rmse";
}

public static class ValidationEvaluationStatuses
{
    public const string Draft = "draft";
    public const string Finalized = "finalized";
}

public static class ValidationPredictionKinds
{
    public const string Gravity = "gravity";
    public const string Comparable = "comparable-log-linear";
}
