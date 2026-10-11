// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json.Serialization;

namespace SaveNEIN.Shared.Modeling;

public sealed record SocialCostDomainValue(
    [property: JsonPropertyName("domainKey")] string DomainKey,
    [property: JsonPropertyName("perCaseCost")] double PerCaseCost,
    [property: JsonPropertyName("scale")] double Scale = 1);

public sealed record SocialCostDomainAmount(
    [property: JsonPropertyName("domainKey")] string DomainKey,
    [property: JsonPropertyName("perCaseCost")] double PerCaseCost,
    [property: JsonPropertyName("annualCost")] double AnnualCost,
    [property: JsonPropertyName("lowAnnualCost")] double LowAnnualCost,
    [property: JsonPropertyName("highAnnualCost")] double HighAnnualCost);

public sealed record SocialCostTotals(
    [property: JsonPropertyName("annualCost")] double AnnualCost,
    [property: JsonPropertyName("lowAnnualCost")] double LowAnnualCost,
    [property: JsonPropertyName("highAnnualCost")] double HighAnnualCost,
    [property: JsonPropertyName("domains")] IReadOnlyList<SocialCostDomainAmount> Domains);

public sealed record SocialCostCalculationRequest(
    [property: JsonPropertyName("incrementalCases")] double IncrementalCases,
    [property: JsonPropertyName("domains")] IReadOnlyCollection<SocialCostDomainValue> Domains,
    [property: JsonPropertyName("lowCaseMultiplier")] double LowCaseMultiplier = 1,
    [property: JsonPropertyName("highCaseMultiplier")] double HighCaseMultiplier = 1);

/// <summary>Identical domain-cost arithmetic for server runs and WebAssembly visualization.</summary>
public static class SocialCostArithmetic
{
    public static SocialCostTotals Calculate(SocialCostCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Domains);
        RequireNonnegative(request.IncrementalCases, nameof(request.IncrementalCases));
        RequireNonnegative(request.LowCaseMultiplier, nameof(request.LowCaseMultiplier));
        RequireNonnegative(request.HighCaseMultiplier, nameof(request.HighCaseMultiplier));
        if (request.LowCaseMultiplier > request.HighCaseMultiplier)
            throw new InvalidOperationException("The low social-cost multiplier cannot exceed the high multiplier.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var domains = request.Domains.Select(domain =>
        {
            ArgumentNullException.ThrowIfNull(domain);
            if (string.IsNullOrWhiteSpace(domain.DomainKey) || !keys.Add(domain.DomainKey))
                throw new ArgumentException("Social-cost domain keys must be nonempty and unique.", nameof(request));
            RequireNonnegative(domain.PerCaseCost, nameof(domain.PerCaseCost));
            RequireNonnegative(domain.Scale, nameof(domain.Scale));
            var annualCost = request.IncrementalCases * domain.PerCaseCost * domain.Scale;
            var lowCost = annualCost * request.LowCaseMultiplier;
            var highCost = annualCost * request.HighCaseMultiplier;
            RequireNonnegative(annualCost, "annual domain cost");
            RequireNonnegative(lowCost, "low annual domain cost");
            RequireNonnegative(highCost, "high annual domain cost");
            return new SocialCostDomainAmount(domain.DomainKey, domain.PerCaseCost, annualCost, lowCost, highCost);
        }).ToArray();
        var total = domains.Sum(domain => domain.AnnualCost);
        var low = domains.Sum(domain => domain.LowAnnualCost);
        var high = domains.Sum(domain => domain.HighAnnualCost);
        RequireNonnegative(total, "annual social cost");
        RequireNonnegative(low, "low annual social cost");
        RequireNonnegative(high, "high annual social cost");
        return new SocialCostTotals(total, low, high, domains);
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, "A finite, nonnegative value is required.");
    }
}
