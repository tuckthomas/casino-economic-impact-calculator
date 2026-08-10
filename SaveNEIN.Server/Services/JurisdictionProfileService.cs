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
    public const string PromotionalCreditTreatment = "promotional-credit-treatment";
    public const string LocalRevenueShare = "local-revenue-share";
    public const string GeneralFiscalRates = "general-fiscal-rates";
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
    decimal CurrentTaxableGamingRevenue);

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
        if (request.PriorPeriodTaxableGamingRevenue < 0 || request.CurrentTaxableGamingRevenue < 0)
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

        var tax = CalculateIncrementalBracketTax(
            request.PriorPeriodTaxableGamingRevenue,
            request.CurrentTaxableGamingRevenue,
            matchingRule.Payload.Brackets);
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

public interface ILocalRevenueShareCalculator
{
    Task<decimal> CalculateAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        decimal gamingTax,
        CancellationToken cancellationToken = default);
}

public sealed class LocalRevenueShareCalculator(IJurisdictionProfileService profiles) : ILocalRevenueShareCalculator
{
    public async Task<decimal> CalculateAsync(
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        decimal gamingTax,
        CancellationToken cancellationToken = default)
    {
        if (gamingTax < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gamingTax));
        }

        var rules = await profiles.GetEffectiveProfileRulesAsync(jurisdictionCode, effectiveOn, cancellationToken);
        var matchingRules = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.LocalRevenueShare &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize<LocalRevenueSharePayload>(rule)))
            .Where(item => item.Payload.FacilityRegime.Equals(facilityRegime, StringComparison.OrdinalIgnoreCase) ||
                           item.Payload.FacilityRegime == "*")
            .ToArray();
        if (matchingRules.Length == 0)
        {
            throw new UnsupportedJurisdictionException(
                $"No validated effective local revenue-sharing rule is configured for jurisdiction '{jurisdictionCode}', regime '{facilityRegime}', on {effectiveOn:yyyy-MM-dd}.");
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
                $"Multiple effective local-revenue-share rules exist for jurisdiction '{jurisdictionCode}' and regime '{facilityRegime}'.");
        }
        var matchingRule = selectedRules[0].Payload;

        if (matchingRule.ShareOfGamingTax < 0 || matchingRule.ShareOfGamingTax > 1)
        {
            throw new InvalidOperationException("Local revenue-sharing percentage must be between zero and one.");
        }

        return decimal.Round(gamingTax * matchingRule.ShareOfGamingTax, 2, MidpointRounding.AwayFromZero);
    }

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
    IReadOnlyCollection<GamingTaxBracketPayload> Brackets);
public sealed record GamingTaxBracketPayload(decimal? UpperBound, decimal Rate);
public sealed record LocalRevenueSharePayload(string FacilityRegime, decimal ShareOfGamingTax);
public sealed record GeneralFiscalRulePayload(
    string FacilityRegime,
    decimal SalesTaxRate,
    decimal BusinessIncomeTaxRate,
    decimal PayrollIncomeTaxRate,
    decimal AnnualPropertyTax,
    decimal NonGamingTaxableRevenueShareOfGgr);

internal static class JurisdictionJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
