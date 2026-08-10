// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("model_run_report_artifacts")]
public sealed class ModelRunReportArtifact
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ModelRunId { get; set; }

    [Required, MaxLength(80)]
    public string TemplateVersion { get; set; } = string.Empty;

    [Required]
    public string PresentationOptionsJson { get; set; } = "{}";

    [Required, MaxLength(64)]
    public string PresentationOptionsHash { get; set; } = string.Empty;

    [Required]
    public string ReportModelJson { get; set; } = "{}";

    [Required, MaxLength(64)]
    public string ReportModelHash { get; set; } = string.Empty;

    [Required]
    public string HtmlContent { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string HtmlContentHash { get; set; } = string.Empty;

    [Required]
    public byte[] PdfContent { get; set; } = [];

    [Required, MaxLength(64)]
    public string PdfContentHash { get; set; } = string.Empty;

    [Required]
    public string CsvContent { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string CsvContentHash { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsImmutable { get; set; } = true;
}
