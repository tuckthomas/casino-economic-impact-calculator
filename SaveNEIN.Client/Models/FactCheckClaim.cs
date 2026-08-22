using System.Text.Json.Serialization;

namespace SaveNEIN.Client.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FactCheckVerdict
{
    True,
    MostlyTrue,
    MostlyFalse,
    False
}

public sealed record FactCheckSource(
    string Label,
    string Citation,
    string? Url = null);

public sealed record FactCheckEvidenceTable(
    string Caption,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record FactCheckDocument(
    int SchemaVersion,
    IReadOnlyList<FactCheckClaim> FactChecks);

public sealed record FactCheckClaim(
    string Id,
    string Slug,
    string Claimant,
    string ClaimText,
    bool IsDirectQuote,
    string Category,
    FactCheckVerdict Verdict,
    IReadOnlyList<string> IssueTags,
    string FindingHeadline,
    string ShortFinding,
    string DetailedExplanation,
    IReadOnlyList<string> KeyFacts,
    IReadOnlyList<FactCheckSource> Sources,
    IReadOnlyList<FactCheckEvidenceTable> EvidenceTables,
    string ClaimSourceUrl,
    DateOnly? ClaimSourceObservedOn,
    string? ClaimSourceObservationType,
    string? ClaimSourceArchivedUrl,
    string FirstPublished,
    string LastReviewed,
    IReadOnlyList<string> RevisionHistory);
