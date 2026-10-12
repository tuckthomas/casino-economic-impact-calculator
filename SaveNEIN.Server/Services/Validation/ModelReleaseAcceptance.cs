// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Validation;

public sealed record ReleaseAcceptanceGate(string Key, bool Passed, string Reason);

public sealed record ReleaseAcceptanceReport(
    string PolicyVersion,
    bool Accepted,
    double? Mape,
    double? Wape,
    double? NormalizedSignedBias,
    int DistinctHoldoutProperties,
    IReadOnlyList<ReleaseAcceptanceGate> Gates);

/// <summary>
/// Evidence supplied by the predeclared validation protocol. Missing evidence
/// fails closed. Audit checksums identify the independently reviewed inputs and
/// reproducibility evidence; they are not substitutes for performing that audit.
/// </summary>
public sealed record ReleaseAcceptanceEvidence(
    string PolicyVersion,
    DateTime FrozenAtUtc,
    DateTime SearchStartedAtUtc,
    IReadOnlyCollection<int> EligiblePropertyIds,
    IReadOnlyDictionary<string, double> BaselinePredictions,
    double ExpectedEligiblePopulation,
    double CoveredEligiblePopulation,
    int KnownCompetitorCount,
    int IncludedCompetitorCount,
    int FailedRouteOrCaseCount,
    bool RequiredInputsVerified,
    bool TargetRevenueLeakageExcluded,
    bool ReproducibilityVerified,
    string InputAuditSha256,
    string ReproducibilityAuditSha256);

public static class ModelReleaseAcceptance
{
    public const string PolicyVersion = "gravity-economic-acceptance-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredGateKeys =
    [
        "observation-integrity", "holdout-mape", "holdout-wape", "holdout-bias", "property-maximum", "property-tail",
        "holdout-properties", "holdout-identities", "holdout-market-coverage", "holdout-jurisdiction-coverage",
        "property-partition-isolation", "reserved-market", "fresh-holdout", "frozen-evidence", "eligible-property-pool",
        "baseline-improvement", "population-coverage", "competitor-coverage", "failed-routes-or-cases", "required-inputs",
        "target-revenue-leakage", "reproducibility", "specification-selection", "property-year-uniqueness"
    ];

    public static async Task<IReadOnlyCollection<int>> PreviouslyEvaluatedPropertyIdsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var finalized = await db.ValidationEvaluations.AsNoTracking()
            .Where(item => item.IsImmutable && item.Status == ValidationEvaluationStatuses.Finalized)
            .ToListAsync(cancellationToken);
        var ids = finalized.Select(item => item.Id).ToArray();
        var caseIds = (await db.ValidationCaseResults.AsNoTracking()
            .Where(item => ids.Contains(item.ValidationEvaluationId))
            .Select(item => item.ValidationCaseId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        foreach (var evaluation in finalized)
        {
            using var document = JsonDocument.Parse(evaluation.InclusionRulesJson);
            if (document.RootElement.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in pairs.EnumerateArray())
                {
                    if (pair.TryGetProperty("validationCaseId", out var id) && id.TryGetGuid(out var value)) caseIds.Add(value);
                }
            }
        }
        var previousIds = await db.ValidationCases.AsNoTracking()
            .Where(item => caseIds.Contains(item.Id) && item.TargetCasinoCompetitorId.HasValue)
            .Select(item => item.TargetCasinoCompetitorId!.Value).Distinct().ToArrayAsync(cancellationToken);
        var venueKeys = await db.CasinoCompetitors.AsNoTracking().Where(item => previousIds.Contains(item.Id))
            .Select(item => item.StableVenueId).Distinct().ToArrayAsync(cancellationToken);
        return await db.CasinoCompetitors.AsNoTracking().Where(item => venueKeys.Contains(item.StableVenueId))
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<int, int>> PropertyIdentityMapAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var properties = await db.CasinoCompetitors.AsNoTracking().Select(item => new { item.Id, item.StableVenueId }).ToListAsync(cancellationToken);
        return properties.GroupBy(item => item.StableVenueId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Select(item => new KeyValuePair<int, int>(item.Id, group.Min(row => row.Id))))
            .ToDictionary(item => item.Key, item => item.Value);
    }

    public static ReleaseAcceptanceReport Evaluate(
        IReadOnlyCollection<ValidationCase> cases,
        IReadOnlyCollection<ValidationObservation> observations,
        ReleaseAcceptanceEvidence? evidence,
        DateTime earliestRunCreatedAtUtc,
        IReadOnlyCollection<int>? previouslyEvaluatedPropertyIds = null,
        IReadOnlyDictionary<int, int>? propertyIdentityMap = null)
    {
        var gates = new List<ReleaseAcceptanceGate>();
        if (evidence is not null && (evidence.EligiblePropertyIds is null || evidence.BaselinePredictions is null)) evidence = null;
        void Check(string key, bool passed, string reason) => gates.Add(new(key, passed, reason));
        int Identity(int id) => propertyIdentityMap is not null && propertyIdentityMap.TryGetValue(id, out var canonical) ? canonical : id;
        var byKey = observations.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        var holdout = cases.Where(item => item.DatasetPartition == ValidationPartitions.Holdout).ToArray();
        var matched = cases.All(item => byKey.ContainsKey(item.CaseKey)) &&
                      byKey.Count == cases.Count &&
                      cases.Select(item => item.CaseKey).Distinct(StringComparer.Ordinal).Count() == cases.Count &&
                      observations.All(item => double.IsFinite(item.Observed) && item.Observed >= 0 &&
                                               double.IsFinite(item.Predicted) && item.Predicted >= 0) &&
                      cases.All(item => byKey[item.CaseKey].Observed == Convert.ToDouble(item.ObservedRevenue));
        Check("observation-integrity", matched, "Every declared case must have exactly one finite nonnegative observation and prediction.");
        if (!matched)
        {
            return new(PolicyVersion, false, null, null, null, 0, gates);
        }
        var scored = holdout.Select(item => byKey[item.CaseKey]).ToArray();
        var positive = scored.Where(item => item.Observed > 1e-12).ToArray();
        var total = scored.Sum(item => item.Observed);
        var mape = positive.Length == 0 ? (double?)null : positive.Average(Ape);
        var wape = total > 0 ? 100 * scored.Sum(item => Math.Abs(item.Predicted - item.Observed)) / total : (double?)null;
        var bias = total > 0 ? 100 * scored.Sum(item => item.Predicted - item.Observed) / total : (double?)null;
        Check("holdout-mape", mape is <= 20, "Positive-revenue holdout MAPE must be <= 20%.");
        Check("holdout-wape", wape is <= 15, "Holdout WAPE, including zero-observed cases, must be <= 15%.");
        Check("holdout-bias", bias.HasValue && Math.Abs(bias.Value) <= 5, "Absolute normalized signed holdout bias must be <= 5%.");
        Check("property-maximum", positive.Length > 0 && positive.All(item => Ape(item) <= 40), "Every positive-revenue holdout property APE must be <= 40%.");
        var propertyErrors = holdout.Where(item => item.ObservedRevenue > 0 && item.TargetCasinoCompetitorId.HasValue)
            .GroupBy(item => Identity(item.TargetCasinoCompetitorId!.Value))
            .Select(group => group.Max(item => Ape(byKey[item.CaseKey]))).ToArray();
        Check("property-tail", propertyErrors.Length > 0 && propertyErrors.Count(error => error <= 30) >= Math.Ceiling(propertyErrors.Length * 0.9), "At least 90% of distinct positive-revenue holdout properties must have APE <= 30% in every tested year.");
        Check("property-year-uniqueness", holdout.Where(item => item.TargetCasinoCompetitorId.HasValue)
            .GroupBy(item => (Property: Identity(item.TargetCasinoCompetitorId!.Value), Year: item.ValidationPeriodEnd?.Year ?? item.TrainingPeriodEnd.Year))
            .All(group => group.Count() == 1), "A property can contribute only one annual observation per year, regardless of snapshot-specific IDs.");

        var properties = holdout.Where(item => item.ObservedRevenue > 0 && item.TargetCasinoCompetitorId.HasValue)
            .Select(item => Identity(item.TargetCasinoCompetitorId!.Value)).Distinct().ToArray();
        var requiredCount = Math.Max(12, (int)Math.Ceiling((evidence?.EligiblePropertyIds.Select(Identity).Distinct().Count() ?? cases.Count) * 0.2));
        Check("holdout-properties", properties.Length >= requiredCount, $"Require at least {requiredCount} distinct positive-revenue holdout properties; found {properties.Length}.");
        Check("holdout-identities", holdout.All(item => item.TargetCasinoCompetitorId.HasValue &&
                                                      (propertyIdentityMap is null || propertyIdentityMap.ContainsKey(item.TargetCasinoCompetitorId.Value)) &&
                                                      !string.IsNullOrWhiteSpace(item.MarketCode) &&
                                                      !string.IsNullOrWhiteSpace(item.JurisdictionCode)), "Holdout cases need stable property, market and jurisdiction identities.");
        Check("holdout-market-coverage", holdout.Select(item => item.MarketCode).Distinct(StringComparer.Ordinal).Count() >= 3, "Require at least three holdout markets.");
        Check("holdout-jurisdiction-coverage", holdout.Select(item => item.JurisdictionCode).Distinct(StringComparer.Ordinal).Count() >= 2, "Require at least two holdout jurisdictions.");
        foreach (var dimension in new[] { "market", "jurisdiction" })
        {
            foreach (var group in holdout.GroupBy(item => dimension == "market" ? item.MarketCode : item.JurisdictionCode, StringComparer.Ordinal))
            {
                var values = group.Select(item => byKey[item.CaseKey]).ToArray();
                var observed = values.Sum(item => item.Observed);
                var groupWape = observed > 0 ? 100 * values.Sum(item => Math.Abs(item.Predicted - item.Observed)) / observed : double.PositiveInfinity;
                var groupBias = observed > 0 ? 100 * Math.Abs(values.Sum(item => item.Predicted - item.Observed)) / observed : double.PositiveInfinity;
                Check($"{dimension}:{group.Key}:coverage", group.Where(item => item.ObservedRevenue > 0)
                    .Where(item => item.TargetCasinoCompetitorId.HasValue).Select(item => Identity(item.TargetCasinoCompetitorId!.Value)).Distinct().Count() >= 3, "Each claimed group needs at least three distinct positive-revenue holdout properties.");
                Check($"{dimension}:{group.Key}:error", groupWape <= 20 && groupBias <= 10, $"Group WAPE {groupWape:G6}% must be <= 20% and absolute bias {groupBias:G6}% <= 10%.");
            }
        }
        // Check every observation year separately; pooled years cannot hide drift.
        foreach (var group in holdout.GroupBy(item => item.ValidationPeriodEnd?.Year ?? item.TrainingPeriodEnd.Year))
        {
            var values = group.Select(item => byKey[item.CaseKey]).ToArray();
            var observed = values.Sum(item => item.Observed);
            var eligible = values.Where(item => item.Observed > 1e-12).ToArray();
            Check($"year:{group.Key}:error", observed > 0 && eligible.Length > 0 &&
                eligible.Average(Ape) <= 20 &&
                100 * values.Sum(item => Math.Abs(item.Predicted - item.Observed)) / observed <= 15 &&
                100 * Math.Abs(values.Sum(item => item.Predicted - item.Observed)) / observed <= 5,
                "Each observation year must meet the same MAPE, WAPE and bias limits.");
        }
        var holdoutIds = properties.ToHashSet();
        Check("property-partition-isolation", !cases.Any(item => item.DatasetPartition != ValidationPartitions.Holdout &&
            item.TargetCasinoCompetitorId.HasValue && holdoutIds.Contains(Identity(item.TargetCasinoCompetitorId.Value))), "All records for held-out properties must remain outside training and selection.");
        var developmentMarkets = cases.Where(item => item.DatasetPartition is ValidationPartitions.Training or DemandValidationPartitions.Selection)
            .Select(item => item.MarketCode).ToHashSet(StringComparer.Ordinal);
        Check("reserved-market", holdout.Any(item => !developmentMarkets.Contains(item.MarketCode)), "At least one entire holdout market must be absent from training and selection.");
        Check("specification-selection", cases.Count(item => item.DatasetPartition == ValidationPartitions.Training) >= 2 &&
            cases.Any(item => item.DatasetPartition == DemandValidationPartitions.Selection), "Release requires distinct training, specification-selection and final-holdout partitions.");
        Check("fresh-holdout", previouslyEvaluatedPropertyIds is not null && !properties.Intersect(previouslyEvaluatedPropertyIds.Select(Identity)).Any(), "Holdout properties must not appear in prior finalized evaluations; unknown history cannot pass.");
        Check("frozen-evidence", evidence is not null && evidence.PolicyVersion == PolicyVersion &&
            evidence.FrozenAtUtc.Kind == DateTimeKind.Utc && evidence.SearchStartedAtUtc.Kind == DateTimeKind.Utc &&
            evidence.FrozenAtUtc <= evidence.SearchStartedAtUtc && evidence.SearchStartedAtUtc <= earliestRunCreatedAtUtc &&
            IsSha256(evidence.InputAuditSha256) && IsSha256(evidence.ReproducibilityAuditSha256), "Require versioned pre-search evidence with UTC timestamps and input/reproducibility audit checksums.");
        Check("eligible-property-pool", evidence is not null && evidence.EligiblePropertyIds.Count > 0 &&
            evidence.EligiblePropertyIds.All(id => id > 0) && evidence.EligiblePropertyIds.Distinct().Count() == evidence.EligiblePropertyIds.Count &&
            cases.All(item => item.TargetCasinoCompetitorId.HasValue && evidence.EligiblePropertyIds.Contains(item.TargetCasinoCompetitorId.Value)), "Every evaluated property must belong to the frozen eligible pool.");
        var baselineComplete = evidence is not null && scored.Length > 0 && scored.All(item =>
            evidence.BaselinePredictions.TryGetValue(item.CaseKey, out var value) && double.IsFinite(value) && value >= 0);
        var baselineWape = baselineComplete && total > 0 ? 100 * scored.Sum(item => Math.Abs(evidence!.BaselinePredictions[item.CaseKey] - item.Observed)) / total : (double?)null;
        Check("baseline-improvement", wape.HasValue && baselineWape.HasValue &&
            (baselineWape.Value == 0 ? wape.Value == 0 : wape.Value <= baselineWape.Value * 0.9), "Require frozen baseline predictions for every holdout case and at least 10% relative WAPE improvement.");
        Check("population-coverage", evidence is not null && double.IsFinite(evidence.ExpectedEligiblePopulation) && evidence.ExpectedEligiblePopulation > 0 &&
            double.IsFinite(evidence.CoveredEligiblePopulation) && evidence.CoveredEligiblePopulation >= evidence.ExpectedEligiblePopulation * 0.99 &&
            evidence.CoveredEligiblePopulation <= evidence.ExpectedEligiblePopulation, "Require >= 99% eligible-population coverage against independent controls, without exceeding the denominator.");
        Check("competitor-coverage", evidence is not null && evidence.KnownCompetitorCount > 0 && evidence.KnownCompetitorCount == evidence.IncludedCompetitorCount, "All known operating competitors must be represented.");
        Check("failed-routes-or-cases", evidence?.FailedRouteOrCaseCount == 0, "No failed route or case may be omitted.");
        Check("required-inputs", evidence?.RequiredInputsVerified == true, "Independent audit must verify complete required observations, attributes and effective fiscal inputs.");
        Check("target-revenue-leakage", evidence?.TargetRevenueLeakageExcluded == true, "Independent audit must exclude held-out GGR from attraction inputs, predictors, fitting and selection.");
        Check("reproducibility", evidence?.ReproducibilityVerified == true, "Frozen-input reruns and UI/API/report reconciliation must meet declared tolerances.");
        return new(PolicyVersion, gates.All(item => item.Passed), mape, wape, bias, properties.Length, gates);
    }

    public static string Persist(string inclusionRulesJson, ReleaseAcceptanceReport report, ReleaseAcceptanceEvidence? evidence)
    {
        var root = JsonNode.Parse(inclusionRulesJson) as JsonObject ?? throw new ArgumentException("Inclusion rules must be a JSON object.", nameof(inclusionRulesJson));
        root["releaseAcceptance"] = JsonSerializer.SerializeToNode(report, JsonOptions);
        root["releaseAcceptanceEvidence"] = JsonSerializer.SerializeToNode(evidence, JsonOptions);
        return root.ToJsonString(JsonOptions);
    }

    public static bool HasAcceptedReport(string inclusionRulesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inclusionRulesJson);
            if (!document.RootElement.TryGetProperty("releaseAcceptance", out var element)) return false;
            var report = element.Deserialize<ReleaseAcceptanceReport>(JsonOptions);
            return report is { Accepted: true, PolicyVersion: PolicyVersion, Mape: <= 20, Wape: <= 15, DistinctHoldoutProperties: >= 12 } &&
                report.NormalizedSignedBias.HasValue && Math.Abs(report.NormalizedSignedBias.Value) <= 5 &&
                report.Gates is { Count: >= 24 } && report.Gates.All(item => item is { Passed: true }) &&
                RequiredGateKeys.All(key => report.Gates.Any(item => item.Key == key));
        }
        catch (JsonException) { return false; }
    }

    private static double Ape(ValidationObservation item) => 100 * Math.Abs(item.Predicted - item.Observed) / item.Observed;
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
