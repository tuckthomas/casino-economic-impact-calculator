namespace SaveNEIN.Client.Models;

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

public sealed record FactCheckClaim(
    string Id,
    string Slug,
    string Claimant,
    string ClaimText,
    bool IsDirectQuote,
    string Category,
    FactCheckVerdict Verdict,
    IReadOnlyList<string> IssueTags,
    string ShortFinding,
    string DetailedExplanation,
    IReadOnlyList<string> KeyFacts,
    IReadOnlyList<FactCheckSource> Sources,
    IReadOnlyList<FactCheckEvidenceTable> EvidenceTables,
    string ClaimSourceUrl,
    string ClaimCapturedDate,
    string FirstPublished,
    string LastReviewed,
    IReadOnlyList<string> RevisionHistory);
