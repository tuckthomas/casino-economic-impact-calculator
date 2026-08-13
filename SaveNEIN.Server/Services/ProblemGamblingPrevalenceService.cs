// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public sealed record ProblemGamblingPrevalenceAssumption(
    double Prevalence,
    double LowerConfidenceBound,
    double UpperConfidenceBound,
    int ObservationYear,
    string Instrument,
    string Population,
    string Citation,
    string SourceUrl,
    string SourceSha256,
    int JurisdictionId,
    long JurisdictionRuleId);

public interface IProblemGamblingPrevalenceResolver
{
    Task<ProblemGamblingPrevalenceAssumption?> ResolveAsync(
        string jurisdictionCode,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public sealed class ProblemGamblingPrevalenceResolver(IJurisdictionProfileService profiles)
    : IProblemGamblingPrevalenceResolver
{
    public async Task<ProblemGamblingPrevalenceAssumption?> ResolveAsync(
        string jurisdictionCode,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var rules = await profiles.GetEffectiveProfileRulesAsync(jurisdictionCode, effectiveOn, cancellationToken);
        var candidates = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.ProblemGamblingPrevalence &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Validated)
            .Select(rule => (Rule: rule, Payload: Deserialize(rule)))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var highestPriorityJurisdictionId = candidates[0].Rule.JurisdictionId;
        var selected = candidates.Where(candidate => candidate.Rule.JurisdictionId == highestPriorityJurisdictionId).ToArray();
        if (selected.Length != 1)
        {
            throw new InvalidOperationException(
                $"Multiple validated effective problem-gambling prevalence rules exist for jurisdiction '{jurisdictionCode}'.");
        }

        var (rule, payload) = selected[0];
        Validate(payload);
        return new ProblemGamblingPrevalenceAssumption(
            payload.Prevalence,
            payload.LowerConfidenceBound,
            payload.UpperConfidenceBound,
            payload.ObservationYear,
            payload.Instrument,
            payload.Population,
            payload.Citation,
            rule.SourceUrl ?? string.Empty,
            payload.SourceSha256,
            rule.JurisdictionId,
            rule.Id);
    }

    private static ProblemGamblingPrevalenceRulePayload Deserialize(JurisdictionRule rule) =>
        JsonSerializer.Deserialize<ProblemGamblingPrevalenceRulePayload>(rule.RuleValueJson, JurisdictionJson.Options)
        ?? throw new InvalidOperationException($"Jurisdiction rule '{rule.Id}' contains invalid problem-gambling prevalence JSON.");

    private static void Validate(ProblemGamblingPrevalenceRulePayload payload)
    {
        var values = new[] { payload.Prevalence, payload.LowerConfidenceBound, payload.UpperConfidenceBound };
        if (values.Any(value => !double.IsFinite(value) || value is < 0 or > 1) ||
            payload.LowerConfidenceBound > payload.Prevalence ||
            payload.Prevalence > payload.UpperConfidenceBound)
        {
            throw new InvalidOperationException("Problem-gambling prevalence and confidence bounds must be ordered shares between zero and one.");
        }
        if (payload.ObservationYear is < 1900 or > 2200 ||
            string.IsNullOrWhiteSpace(payload.Instrument) ||
            string.IsNullOrWhiteSpace(payload.Population) ||
            string.IsNullOrWhiteSpace(payload.Citation) ||
            payload.SourceSha256.Length != 64 ||
            !payload.SourceSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Problem-gambling prevalence provenance is incomplete or invalid.");
        }
    }
}

public sealed record SocialCostPrevalenceSelection(
    double AppliedPrevalence,
    string SourceKey,
    ProblemGamblingPrevalenceAssumption? JurisdictionAssumption);

public static class SocialCostPrevalenceSelector
{
    public static SocialCostPrevalenceSelection Select(
        ResolvedModelParameter prevalenceParameter,
        ProblemGamblingPrevalenceAssumption? jurisdictionAssumption)
    {
        ArgumentNullException.ThrowIfNull(prevalenceParameter);
        if (!string.Equals(prevalenceParameter.Definition.Key, "social_cost.prevalence", StringComparison.Ordinal))
        {
            throw new ArgumentException("The resolved parameter must be social_cost.prevalence.", nameof(prevalenceParameter));
        }

        if (jurisdictionAssumption is { } selectedAssumption &&
            prevalenceParameter.SourceLayer is "system-fallback" or "national-calibrated-set")
        {
            return new SocialCostPrevalenceSelection(
                selectedAssumption.Prevalence,
                "validated-jurisdiction-rule",
                selectedAssumption);
        }

        return new SocialCostPrevalenceSelection(
            prevalenceParameter.FinalValue,
            prevalenceParameter.SourceLayer,
            null);
    }
}
