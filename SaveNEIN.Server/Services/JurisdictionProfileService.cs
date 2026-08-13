// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public static class JurisdictionRuleTypes
{
    public const string LegalGamingAge = "legal-gaming-age";
    public const string PermittedGamingProducts = "permitted-gaming-products";
    public const string GamingRevenueDefinition = "gaming-revenue-definition";
    public const string GamingTaxSchedule = "gaming-tax-schedule";
    public const string SupplementalGamingTaxSchedule = "supplemental-gaming-tax-schedule";
    public const string GamingTaxDistribution = "gaming-tax-distribution";
    public const string PromotionalCreditTreatment = "promotional-credit-treatment";
    public const string GeneralFiscalRates = "general-fiscal-rates";
    public const string ProblemGamblingPrevalence = "problem-gambling-prevalence";
}

public sealed class UnsupportedJurisdictionException(string message) : InvalidOperationException(message);

public interface IJurisdictionProfileService
{
    Task<Jurisdiction?> GetJurisdictionAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JurisdictionRule>> GetEffectiveRulesAsync(
        int jurisdictionId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JurisdictionRule>> GetEffectiveProfileRulesAsync(
        string jurisdictionCode,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public sealed class JurisdictionProfileService(AppDbContext db) : IJurisdictionProfileService
{
    public Task<Jurisdiction?> GetJurisdictionAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        return db.Jurisdictions.SingleOrDefaultAsync(
            jurisdiction => jurisdiction.Code == normalizedCode && jurisdiction.IsActive,
            cancellationToken);
    }

    public async Task<IReadOnlyList<JurisdictionRule>> GetEffectiveRulesAsync(
        int jurisdictionId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default) =>
        await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == jurisdictionId &&
                           rule.EffectiveFrom <= effectiveOn &&
                           (rule.EffectiveTo == null || rule.EffectiveTo >= effectiveOn))
            .OrderBy(rule => rule.RuleType)
            .ThenByDescending(rule => rule.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JurisdictionRule>> GetEffectiveProfileRulesAsync(
        string jurisdictionCode,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var jurisdiction = await GetJurisdictionAsync(jurisdictionCode, cancellationToken)
            ?? throw new UnsupportedJurisdictionException($"Jurisdiction '{jurisdictionCode}' is not configured.");
        var rules = new List<JurisdictionRule>();
        var visited = new HashSet<int>();

        while (visited.Add(jurisdiction.Id))
        {
            rules.AddRange(await GetEffectiveRulesAsync(jurisdiction.Id, effectiveOn, cancellationToken));
            if (jurisdiction.ParentJurisdictionId is not { } parentId)
            {
                break;
            }

            jurisdiction = await db.Jurisdictions.SingleOrDefaultAsync(
                item => item.Id == parentId && item.IsActive,
                cancellationToken)
                ?? throw new InvalidOperationException($"Jurisdiction hierarchy is broken at parent '{parentId}'.");
        }

        return rules;
    }
}

public interface IGamingAgeResolver
{
    Task<int> ResolveMinimumAgeAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public sealed class GamingAgeResolver(IJurisdictionProfileService profiles) : IGamingAgeResolver
{
    public async Task<int> ResolveMinimumAgeAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var rules = await profiles.GetEffectiveProfileRulesAsync(jurisdictionCode, effectiveOn, cancellationToken);
        var matchingRules = rules.Where(item =>
                item.RuleType == JurisdictionRuleTypes.LegalGamingAge &&
                item.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<GamingAgeRulePayload>(rule)))
            .Where(item => item.Payload.FacilityRegime.Equals(facilityRegime, StringComparison.OrdinalIgnoreCase) ||
                           item.Payload.FacilityRegime == "*")
            .ToArray();
        if (matchingRules.Length == 0)
        {
            throw new UnsupportedJurisdictionException(
                $"No validated effective legal gaming age is configured for jurisdiction '{jurisdictionCode}', regime '{facilityRegime}', on {effectiveOn:yyyy-MM-dd}.");
        }

        var highestPriorityJurisdictionId = matchingRules[0].Rule.JurisdictionId;
        var jurisdictionMatches = matchingRules
            .Where(item => item.Rule.JurisdictionId == highestPriorityJurisdictionId)
            .ToArray();
        var exactMatches = jurisdictionMatches
            .Where(item => item.Payload.FacilityRegime.Equals(facilityRegime, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedRules = exactMatches.Length > 0
            ? exactMatches
            : jurisdictionMatches.Where(item => item.Payload.FacilityRegime == "*").ToArray();
        if (selectedRules.Length != 1)
        {
            throw new InvalidOperationException(
                $"Multiple effective legal-gaming-age rules exist for jurisdiction '{jurisdictionCode}' and regime '{facilityRegime}'.");
        }
        var payload = selectedRules[0].Payload;
        if (payload.MinimumAge is < 0 or > 120)
        {
            throw new InvalidOperationException("Legal gaming age must be between zero and 120.");
        }
        return payload.MinimumAge;
    }

    private static T Deserialize<T>(JurisdictionRule rule) where T : class =>
        JsonSerializer.Deserialize<T>(rule.RuleValueJson, JurisdictionJson.Options)
        ?? throw new InvalidOperationException($"Jurisdiction rule '{rule.Id}' contains invalid JSON.");
}

public sealed record GamingTaxRequest(
    string JurisdictionCode,
    string FacilityRegime,
    DateOnly EffectiveOn,
    decimal PriorPeriodTaxableGamingRevenue,
    decimal CurrentTaxableGamingRevenue,
    decimal? PriorFiscalYearTaxableGamingRevenue = null);

public sealed record GamingTaxResult(
    decimal TaxableGamingRevenue,
    decimal GamingTax,
    string RevenueDefinition,
    string SourceUrl);

public interface IGamingTaxCalculator
{
    Task<GamingTaxResult> CalculateAsync(
        GamingTaxRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GamingTaxCalculator(IJurisdictionProfileService profiles) : IGamingTaxCalculator
{
    public async Task<GamingTaxResult> CalculateAsync(
        GamingTaxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PriorPeriodTaxableGamingRevenue < 0 ||
            request.CurrentTaxableGamingRevenue < 0 ||
            request.PriorFiscalYearTaxableGamingRevenue is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Taxable gaming revenue cannot be negative.");
        }

        var rules = await profiles.GetEffectiveProfileRulesAsync(
            request.JurisdictionCode,
            request.EffectiveOn,
            cancellationToken);
        var matchingRules = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.GamingTaxSchedule &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<GamingTaxSchedulePayload>(rule)))
            .Where(item => item.Payload.FacilityRegime.Equals(request.FacilityRegime, StringComparison.OrdinalIgnoreCase) ||
                           item.Payload.FacilityRegime == "*")
            .ToArray();

        if (matchingRules.Length == 0)
        {
            throw new UnsupportedJurisdictionException(
                $"No validated effective gaming-tax schedule is configured for jurisdiction '{request.JurisdictionCode}', regime '{request.FacilityRegime}', on {request.EffectiveOn:yyyy-MM-dd}.");
        }
        var highestPriorityJurisdictionId = matchingRules[0].Rule.JurisdictionId;
        var jurisdictionMatches = matchingRules
            .Where(item => item.Rule.JurisdictionId == highestPriorityJurisdictionId)
            .ToArray();
        var exactMatches = jurisdictionMatches
            .Where(item => item.Payload.FacilityRegime.Equals(request.FacilityRegime, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedRules = exactMatches.Length > 0
            ? exactMatches
            : jurisdictionMatches.Where(item => item.Payload.FacilityRegime == "*").ToArray();
        if (selectedRules.Length != 1)
        {
            throw new InvalidOperationException(
                $"Multiple effective gaming-tax schedules exist for jurisdiction '{request.JurisdictionCode}' and regime '{request.FacilityRegime}'.");
        }
        var matchingRule = selectedRules[0];

        var brackets = matchingRule.Payload.Brackets;
        decimal additionalTax = 0;
        if (matchingRule.Payload.PriorFiscalYearSchedules is { Count: > 0 } conditionalSchedules)
        {
            if (request.PriorFiscalYearTaxableGamingRevenue is not { } priorFiscalYearRevenue)
            {
                throw new ArgumentException(
                    "Prior fiscal-year taxable gaming revenue is required by the effective conditional tax schedule.",
                    nameof(request));
            }
            var schedules = conditionalSchedules.OrderBy(schedule => schedule.PriorFiscalYearUpperBoundExclusive).ToArray();
            if (schedules.Any(schedule =>
                    schedule.PriorFiscalYearUpperBoundExclusive <= 0 ||
                    string.IsNullOrWhiteSpace(schedule.Key) ||
                    schedule.AdditionalTaxAmount < 0 ||
                    schedule.CurrentFiscalYearAdditionalTaxThreshold < 0) ||
                schedules.Select(schedule => schedule.PriorFiscalYearUpperBoundExclusive).Distinct().Count() != schedules.Length)
            {
                throw new InvalidOperationException("Conditional gaming-tax schedules require unique positive prior-year bounds and nonnegative surcharge values.");
            }
            var conditional = schedules.FirstOrDefault(schedule =>
                priorFiscalYearRevenue < schedule.PriorFiscalYearUpperBoundExclusive);
            if (conditional is not null)
            {
                brackets = conditional.Brackets;
                if (conditional.CurrentFiscalYearAdditionalTaxThreshold is { } threshold &&
                    request.PriorPeriodTaxableGamingRevenue <= threshold &&
                    request.PriorPeriodTaxableGamingRevenue + request.CurrentTaxableGamingRevenue > threshold)
                {
                    additionalTax = conditional.AdditionalTaxAmount;
                }
            }
        }
        var tax = CalculateIncrementalBracketTax(
            request.PriorPeriodTaxableGamingRevenue,
            request.CurrentTaxableGamingRevenue,
            brackets) + additionalTax;
        return new GamingTaxResult(
            request.CurrentTaxableGamingRevenue,
            tax,
            matchingRule.Payload.RevenueDefinition,
            matchingRule.Rule.SourceUrl ?? string.Empty);
    }

    internal static decimal CalculateIncrementalBracketTax(
        decimal priorRevenue,
        decimal currentRevenue,
        IReadOnlyCollection<GamingTaxBracketPayload> brackets)
    {
        var ordered = brackets.OrderBy(bracket => bracket.UpperBound ?? decimal.MaxValue).ToArray();
        if (ordered.Length == 0 || ordered[^1].UpperBound is not null)
        {
            throw new InvalidOperationException("Gaming-tax schedule must contain a final open-ended bracket.");
        }

        decimal tax = 0;
        var periodEnd = priorRevenue + currentRevenue;
        decimal lowerBound = 0;
        foreach (var bracket in ordered)
        {
            if (bracket.Rate < 0 || bracket.Rate > 1)
            {
                throw new InvalidOperationException("Gaming-tax bracket rates must be between zero and one.");
            }

            var upperBound = bracket.UpperBound ?? decimal.MaxValue;
            if (upperBound <= lowerBound)
            {
                throw new InvalidOperationException("Gaming-tax bracket upper bounds must be strictly increasing.");
            }

            var taxableStart = Math.Max(priorRevenue, lowerBound);
            var taxableEnd = Math.Min(periodEnd, upperBound);
            if (taxableEnd > taxableStart)
            {
                tax += (taxableEnd - taxableStart) * bracket.Rate;
            }
            if (periodEnd <= upperBound)
            {
                break;
            }

            lowerBound = upperBound;
        }

        return decimal.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    private static T Deserialize<T>(JurisdictionRule rule) where T : class =>
        JsonSerializer.Deserialize<T>(rule.RuleValueJson, JurisdictionJson.Options)
        ?? throw new InvalidOperationException($"Jurisdiction rule '{rule.Id}' contains invalid JSON.");
}

public sealed record GamingFiscalAllocationRequest(
    string JurisdictionCode,
    string FacilityRegime,
    DateOnly EffectiveOn,
    decimal CurrentTaxableGamingRevenue,
    decimal BaseGamingTax,
    double CandidateLatitude,
    double CandidateLongitude,
    string? StableVenueId = null);

public sealed record SupplementalGamingTaxRequest(
    string JurisdictionCode,
    string FacilityRegime,
    DateOnly EffectiveOn,
    decimal CurrentTaxableGamingRevenue,
    double FacilityLatitude,
    double FacilityLongitude,
    string? StableVenueId = null);

public sealed record SupplementalGamingTaxResult(
    decimal Rate,
    decimal SupplementalGamingTax,
    CandidateFiscalLocation Location,
    string SourceUrl);

public sealed record GamingFiscalAllocationResult(
    decimal BaseGamingTax,
    decimal SupplementalGamingTax,
    decimal GrossGamingTax,
    decimal HostMunicipalityShare,
    decimal HostCountyShare,
    decimal HostRegionalShare,
    decimal HostStateShare,
    CandidateFiscalLocation Location,
    IReadOnlyList<string> SourceUrls);

public interface IGamingFiscalAllocationCalculator
{
    Task<GamingFiscalAllocationResult> CalculateAsync(
        GamingFiscalAllocationRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplementalGamingTaxResult> CalculateSupplementalTaxAsync(
        SupplementalGamingTaxRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GamingFiscalAllocationCalculator(
    IJurisdictionProfileService profiles,
    ICandidateFiscalLocationResolver locations) : IGamingFiscalAllocationCalculator
{
    public async Task<GamingFiscalAllocationResult> CalculateAsync(
        GamingFiscalAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CurrentTaxableGamingRevenue < 0 || request.BaseGamingTax < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Gaming revenue and tax cannot be negative.");
        }

        var location = await locations.ResolveAsync(
            request.CandidateLatitude,
            request.CandidateLongitude,
            cancellationToken);
        var rules = await profiles.GetEffectiveProfileRulesAsync(
            request.JurisdictionCode,
            request.EffectiveOn,
            cancellationToken);

        var supplemental = CalculateSupplementalTax(
            request.JurisdictionCode,
            request.FacilityRegime,
            request.CurrentTaxableGamingRevenue,
            request.StableVenueId,
            location,
            rules);
        var distributionRules = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.GamingTaxDistribution &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<GamingTaxDistributionPayload>(rule)))
            .Where(item => RegimeMatches(item.Payload.FacilityRegime, request.FacilityRegime) &&
                           CountyMatches(item.Payload.EligibleCountyFips, location.CountyFips))
            .ToArray();
        var baseDistribution = SelectDistribution(
            distributionRules,
            GamingTaxComponents.Base,
            request,
            location);
        var supplementalDistribution = SelectDistribution(
            distributionRules,
            GamingTaxComponents.Supplemental,
            request,
            location);

        var baseShares = Allocate(request.BaseGamingTax, baseDistribution.Payload);
        var supplementalShares = Allocate(supplemental.SupplementalGamingTax, supplementalDistribution.Payload);
        return new GamingFiscalAllocationResult(
            request.BaseGamingTax,
            supplemental.SupplementalGamingTax,
            request.BaseGamingTax + supplemental.SupplementalGamingTax,
            baseShares.Municipality + supplementalShares.Municipality,
            baseShares.County + supplementalShares.County,
            baseShares.Regional + supplementalShares.Regional,
            baseShares.State + supplementalShares.State,
            location,
            new[]
            {
                supplemental.SourceUrl,
                baseDistribution.Rule.SourceUrl,
                supplementalDistribution.Rule.SourceUrl
            }.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url!).Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<SupplementalGamingTaxResult> CalculateSupplementalTaxAsync(
        SupplementalGamingTaxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CurrentTaxableGamingRevenue < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Current taxable gaming revenue cannot be negative.");
        }

        var location = await locations.ResolveAsync(
            request.FacilityLatitude,
            request.FacilityLongitude,
            cancellationToken);
        var rules = await profiles.GetEffectiveProfileRulesAsync(
            request.JurisdictionCode,
            request.EffectiveOn,
            cancellationToken);
        return CalculateSupplementalTax(
            request.JurisdictionCode,
            request.FacilityRegime,
            request.CurrentTaxableGamingRevenue,
            request.StableVenueId,
            location,
            rules);
    }

    private static SupplementalGamingTaxResult CalculateSupplementalTax(
        string jurisdictionCode,
        string facilityRegime,
        decimal currentTaxableGamingRevenue,
        string? stableVenueId,
        CandidateFiscalLocation location,
        IReadOnlyCollection<JurisdictionRule> rules)
    {
        var supplementalRules = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.SupplementalGamingTaxSchedule &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<SupplementalGamingTaxPayload>(rule)))
            .Where(item => RegimeMatches(item.Payload.FacilityRegime, facilityRegime) &&
                           CountyMatches(item.Payload.EligibleCountyFips, location.CountyFips) &&
                           VenueMatches(item.Payload.EligibleStableVenueIds, stableVenueId))
            .ToArray();
        var supplemental = SelectSingleRule(
            supplementalRules,
            jurisdictionCode,
            facilityRegime,
            "supplemental gaming-tax schedule");
        ValidateSupplementalRateBasis(supplemental.Payload);
        var supplementalTax = decimal.Round(
            currentTaxableGamingRevenue * supplemental.Payload.Rate,
            2,
            MidpointRounding.AwayFromZero);
        return new SupplementalGamingTaxResult(
            supplemental.Payload.Rate,
            supplementalTax,
            location,
            supplemental.Rule.SourceUrl ?? string.Empty);
    }

    private static void ValidateSupplementalRateBasis(SupplementalGamingTaxPayload payload)
    {
        if (payload.Rate is < 0 or > 1)
        {
            throw new InvalidOperationException("Supplemental gaming-tax rate must be between zero and one.");
        }
        if (payload.RateSourceKind == SupplementalGamingTaxRateSourceKinds.StatutoryQuotient)
        {
            if (payload.ReferenceAdmissionsTax is not > 0 || payload.ReferenceAdjustedGrossReceipts is not > 0)
            {
                throw new InvalidOperationException(
                    "A statutory-quotient supplemental rate requires positive reference admissions tax and adjusted gross receipts.");
            }
            if (payload.MaximumRate is { } configuredMaximum &&
                (configuredMaximum <= 0 || configuredMaximum > 1))
            {
                throw new InvalidOperationException("A supplemental gaming-tax maximum rate must be between zero and one.");
            }
            var quotient = decimal.Round(
                payload.ReferenceAdmissionsTax.Value / payload.ReferenceAdjustedGrossReceipts.Value,
                4,
                MidpointRounding.AwayFromZero);
            var expectedRate = payload.MaximumRate is { } maximumRate
                ? decimal.Min(quotient, maximumRate)
                : quotient;
            if (payload.Rate != expectedRate)
            {
                throw new InvalidOperationException(
                    $"Supplemental gaming-tax rate {payload.Rate} does not equal its sourced statutory quotient {expectedRate}.");
            }
            return;
        }
        if (payload.RateSourceKind is not SupplementalGamingTaxRateSourceKinds.FixedStatute and
            not SupplementalGamingTaxRateSourceKinds.RegulatorConfirmed)
        {
            throw new InvalidOperationException(
                $"Unsupported supplemental gaming-tax rate source kind '{payload.RateSourceKind}'.");
        }
    }

    private static (JurisdictionRule Rule, GamingTaxDistributionPayload Payload) SelectDistribution(
        IReadOnlyCollection<(JurisdictionRule Rule, GamingTaxDistributionPayload Payload)> rules,
        string component,
        GamingFiscalAllocationRequest request,
        CandidateFiscalLocation location)
    {
        var matching = rules
            .Where(item => item.Payload.Component.Equals(component, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selected = SelectSingleRule(
            matching,
            request.JurisdictionCode,
            request.FacilityRegime,
            $"{component} gaming-tax distribution");
        if (selected.Payload.MunicipalityRequired && location.MunicipalityGeoid is null)
        {
            throw new UnsupportedJurisdictionException(
                $"The effective {component} gaming-tax distribution requires an incorporated host municipality, but candidate " +
                $"{request.CandidateLatitude:R},{request.CandidateLongitude:R} in county {location.CountyFips} is not contained by an active TIGER place. No county fallback is authorized.");
        }
        return selected;
    }

    private static GamingTaxShares Allocate(decimal tax, GamingTaxDistributionPayload payload)
    {
        var shares = new[] { payload.StateShare, payload.CountyShare, payload.MunicipalityShare, payload.RegionalShare };
        if (shares.Any(share => share is < 0 or > 1) || decimal.Abs(shares.Sum() - 1m) > 0.0000001m)
        {
            throw new InvalidOperationException($"Gaming-tax distribution '{payload.Component}' must contain nonnegative shares summing to one.");
        }
        return new GamingTaxShares(
            Round(tax * payload.StateShare),
            Round(tax * payload.CountyShare),
            Round(tax * payload.MunicipalityShare),
            Round(tax * payload.RegionalShare));
    }

    private static (JurisdictionRule Rule, T Payload) SelectSingleRule<T>(
        IReadOnlyCollection<(JurisdictionRule Rule, T Payload)> matchingRules,
        string jurisdictionCode,
        string facilityRegime,
        string ruleLabel) where T : class
    {
        if (matchingRules.Count == 0)
        {
            throw new UnsupportedJurisdictionException(
                $"No validated effective {ruleLabel} is configured for jurisdiction '{jurisdictionCode}', regime '{facilityRegime}', and the candidate county.");
        }
        var highestPriorityJurisdictionId = matchingRules.First().Rule.JurisdictionId;
        var jurisdictionMatches = matchingRules.Where(item => item.Rule.JurisdictionId == highestPriorityJurisdictionId).ToArray();
        var exactMatches = jurisdictionMatches.Where(item => GetFacilityRegime(item.Payload).Equals(facilityRegime, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = exactMatches.Length > 0
            ? exactMatches
            : jurisdictionMatches.Where(item => GetFacilityRegime(item.Payload) == "*").ToArray();
        if (selected.Length != 1)
        {
            throw new InvalidOperationException(
                $"Multiple effective {ruleLabel} rules exist for jurisdiction '{jurisdictionCode}' and regime '{facilityRegime}'.");
        }
        return selected[0];
    }

    private static string GetFacilityRegime<T>(T payload) => payload switch
    {
        SupplementalGamingTaxPayload supplemental => supplemental.FacilityRegime,
        GamingTaxDistributionPayload distribution => distribution.FacilityRegime,
        _ => throw new InvalidOperationException($"Unsupported fiscal rule payload '{typeof(T).Name}'.")
    };

    private static bool RegimeMatches(string configured, string requested) =>
        configured == "*" || configured.Equals(requested, StringComparison.OrdinalIgnoreCase);

    private static bool CountyMatches(IReadOnlyCollection<string> configured, string countyFips) =>
        configured.Count == 0 || configured.Contains(countyFips, StringComparer.Ordinal);

    private static bool VenueMatches(IReadOnlyCollection<string>? configured, string? stableVenueId) =>
        configured is null || configured.Count == 0 ||
        (!string.IsNullOrWhiteSpace(stableVenueId) && configured.Contains(stableVenueId, StringComparer.Ordinal));

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record GamingTaxShares(decimal State, decimal County, decimal Municipality, decimal Regional);

    private static T Deserialize<T>(JurisdictionRule rule) where T : class =>
        JsonSerializer.Deserialize<T>(rule.RuleValueJson, JurisdictionJson.Options)
        ?? throw new InvalidOperationException($"Jurisdiction rule '{rule.Id}' contains invalid JSON.");
}

public sealed record GeneralFiscalRuleResult(
    decimal SalesTaxRate,
    decimal BusinessIncomeTaxRate,
    decimal PayrollIncomeTaxRate,
    decimal AnnualPropertyTax,
    decimal NonGamingTaxableRevenueShareOfGgr,
    string SourceUrl);

public interface IGeneralFiscalRuleResolver
{
    Task<GeneralFiscalRuleResult> ResolveAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public sealed class GeneralFiscalRuleResolver(IJurisdictionProfileService profiles) : IGeneralFiscalRuleResolver
{
    public async Task<GeneralFiscalRuleResult> ResolveAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var rules = await profiles.GetEffectiveProfileRulesAsync(jurisdictionCode, effectiveOn, cancellationToken);
        var matchingRules = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.GeneralFiscalRates &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<GeneralFiscalRulePayload>(rule)))
            .Where(item => item.Payload.FacilityRegime.Equals(facilityRegime, StringComparison.OrdinalIgnoreCase) ||
                           item.Payload.FacilityRegime == "*")
            .ToArray();
        if (matchingRules.Length == 0)
        {
            throw new UnsupportedJurisdictionException(
                $"No validated effective general fiscal-rate rule is configured for jurisdiction '{jurisdictionCode}', regime '{facilityRegime}', on {effectiveOn:yyyy-MM-dd}.");
        }

        var highestPriorityJurisdictionId = matchingRules[0].Rule.JurisdictionId;
        var jurisdictionMatches = matchingRules
            .Where(item => item.Rule.JurisdictionId == highestPriorityJurisdictionId)
            .ToArray();
        var exactMatches = jurisdictionMatches
            .Where(item => item.Payload.FacilityRegime.Equals(facilityRegime, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedRules = exactMatches.Length > 0
            ? exactMatches
            : jurisdictionMatches.Where(item => item.Payload.FacilityRegime == "*").ToArray();
        if (selectedRules.Length != 1)
        {
            throw new InvalidOperationException(
                $"Multiple effective general fiscal-rate rules exist for jurisdiction '{jurisdictionCode}' and regime '{facilityRegime}'.");
        }

        var selected = selectedRules[0];
        var rates = new[]
        {
            selected.Payload.SalesTaxRate,
            selected.Payload.BusinessIncomeTaxRate,
            selected.Payload.PayrollIncomeTaxRate,
            selected.Payload.NonGamingTaxableRevenueShareOfGgr
        };
        if (rates.Any(rate => rate is < 0 or > 1) || selected.Payload.AnnualPropertyTax < 0)
        {
            throw new InvalidOperationException("General fiscal rates must be between zero and one and annual property tax cannot be negative.");
        }
        return new GeneralFiscalRuleResult(
            selected.Payload.SalesTaxRate,
            selected.Payload.BusinessIncomeTaxRate,
            selected.Payload.PayrollIncomeTaxRate,
            selected.Payload.AnnualPropertyTax,
            selected.Payload.NonGamingTaxableRevenueShareOfGgr,
            selected.Rule.SourceUrl ?? string.Empty);
    }

    private static T Deserialize<T>(JurisdictionRule rule) where T : class =>
        JsonSerializer.Deserialize<T>(rule.RuleValueJson, JurisdictionJson.Options)
        ?? throw new InvalidOperationException($"Jurisdiction rule '{rule.Id}' contains invalid JSON.");
}

public sealed record GamingAgeRulePayload(string FacilityRegime, int MinimumAge);
public sealed record GamingTaxSchedulePayload(
    string FacilityRegime,
    string RevenueDefinition,
    IReadOnlyCollection<GamingTaxBracketPayload> Brackets,
    IReadOnlyCollection<PriorFiscalYearGamingTaxSchedulePayload>? PriorFiscalYearSchedules = null);
public sealed record GamingTaxBracketPayload(decimal? UpperBound, decimal Rate);
public sealed record PriorFiscalYearGamingTaxSchedulePayload(
    string Key,
    decimal PriorFiscalYearUpperBoundExclusive,
    IReadOnlyCollection<GamingTaxBracketPayload> Brackets,
    decimal? CurrentFiscalYearAdditionalTaxThreshold = null,
    decimal AdditionalTaxAmount = 0);
public sealed record SupplementalGamingTaxPayload(
    string FacilityRegime,
    decimal Rate,
    IReadOnlyCollection<string> EligibleCountyFips,
    IReadOnlyCollection<string>? EligibleStableVenueIds = null,
    string RateSourceKind = SupplementalGamingTaxRateSourceKinds.FixedStatute,
    decimal? ReferenceAdmissionsTax = null,
    decimal? ReferenceAdjustedGrossReceipts = null,
    decimal? MaximumRate = null);
public static class SupplementalGamingTaxRateSourceKinds
{
    public const string FixedStatute = "fixed-statute";
    public const string StatutoryQuotient = "statutory-quotient";
    public const string RegulatorConfirmed = "regulator-confirmed";
}
public sealed record GamingTaxDistributionPayload(
    string FacilityRegime,
    string Component,
    IReadOnlyCollection<string> EligibleCountyFips,
    bool MunicipalityRequired,
    decimal StateShare,
    decimal CountyShare,
    decimal MunicipalityShare,
    decimal RegionalShare);
public static class GamingTaxComponents
{
    public const string Base = "base";
    public const string Supplemental = "supplemental";
}
public sealed record GeneralFiscalRulePayload(
    string FacilityRegime,
    decimal SalesTaxRate,
    decimal BusinessIncomeTaxRate,
    decimal PayrollIncomeTaxRate,
    decimal AnnualPropertyTax,
    decimal NonGamingTaxableRevenueShareOfGgr);
public sealed record ProblemGamblingPrevalenceRulePayload(
    double Prevalence,
    double LowerConfidenceBound,
    double UpperConfidenceBound,
    int ObservationYear,
    string Instrument,
    string Population,
    string Citation,
    string SourceSha256);

internal static class JurisdictionJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
