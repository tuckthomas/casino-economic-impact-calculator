namespace SaveNEIN.Server.Data.Entities;

public sealed class ArchivedWebSource
{
    public Guid Id { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public DateTime? ObservedAtUtc { get; set; }
    public string? ObservationType { get; set; }
    public string ArchiveBoxSnapshotId { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }
    public string PublicArchivedUrl { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string CaptureStatus { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string NormalizedTextSha256 { get; set; } = string.Empty;
    public string ArtifactManifestJson { get; set; } = "[]";
    public string ArchiveRelativePath { get; set; } = string.Empty;
    public DateTime? VerifiedAtUtc { get; set; }
    public string VerificationStatus { get; set; } = string.Empty;
    public string? VerificationNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
