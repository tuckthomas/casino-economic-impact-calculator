using System.Text.Json;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class JurisdictionServicesTests
{
    [Fact]
    public async Task GamingAgeResolver_ReturnsValidatedEffectiveRule()
    {
        var profiles = new FakeProfiles(Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("commercial-casino", 21),
            JurisdictionRuleValidationStates.Validated));
        var resolver = new GamingAgeResolver(profiles);

        var age = await resolver.ResolveMinimumAgeAsync(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1));

        Assert.Equal(21, age);
    }

    [Fact]
    public async Task GamingAgeResolver_RejectsProvisionalRule()
    {
        var profiles = new FakeProfiles(Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("commercial-casino", 21),
            JurisdictionRuleValidationStates.Provisional));
        var resolver = new GamingAgeResolver(profiles);

        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() => resolver.ResolveMinimumAgeAsync(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task GamingAgeResolver_PrefersExactRegimeOverWildcardAtSameJurisdiction()
    {
        var exact = Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("tribal-casino", 18),
            JurisdictionRuleValidationStates.Validated);
        var wildcard = Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("*", 21),
            JurisdictionRuleValidationStates.Validated);
        var resolver = new GamingAgeResolver(new FakeProfiles(wildcard, exact));

        var age = await resolver.ResolveMinimumAgeAsync(
            "TEST",
            "tribal-casino",
            new DateOnly(2026, 1, 1));

        Assert.Equal(18, age);
    }

    [Fact]
    public async Task GamingAgeResolver_PrefersChildJurisdictionRuleOverParent()
    {
        var child = Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("*", 18),
            JurisdictionRuleValidationStates.Validated);
        child.JurisdictionId = 2;
        var parent = Rule(
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("commercial-casino", 21),
            JurisdictionRuleValidationStates.Validated);
        parent.JurisdictionId = 1;
        var resolver = new GamingAgeResolver(new FakeProfiles(child, parent));

        var age = await resolver.ResolveMinimumAgeAsync(
            "TEST-CHILD",
            "commercial-casino",
            new DateOnly(2026, 1, 1));

        Assert.Equal(18, age);
    }

    [Fact]
    public async Task GamingTaxCalculator_AppliesIncrementalBracketsAcrossThreshold()
    {
        var schedule = new GamingTaxSchedulePayload(
            "commercial-casino-standard",
            "taxable AGR",
            [
                new GamingTaxBracketPayload(25_000_000m, 0.10m),
                new GamingTaxBracketPayload(null, 0.20m)
            ]);
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            schedule,
            JurisdictionRuleValidationStates.Validated)));

        var result = await calculator.CalculateAsync(new GamingTaxRequest(
            "TEST",
            "commercial-casino-standard",
            new DateOnly(2026, 1, 1),
            PriorPeriodTaxableGamingRevenue: 20_000_000m,
            CurrentTaxableGamingRevenue: 10_000_000m));

        Assert.Equal(1_500_000m, result.GamingTax);
    }

    [Fact]
    public async Task GamingTaxCalculator_DoesNotApplyProvisionalFiscalRule()
    {
        var schedule = new GamingTaxSchedulePayload(
            "commercial-casino-standard",
            "taxable AGR",
            [new GamingTaxBracketPayload(null, 0.10m)]);
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            schedule,
            JurisdictionRuleValidationStates.Provisional)));

        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() => calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-casino-standard",
            new DateOnly(2026, 1, 1),
            0,
            1_000_000m)));
    }

    private static JurisdictionRule Rule<T>(string type, T payload, string validationState) => new()
    {
        Id = 1,
        JurisdictionId = 1,
        RuleType = type,
        RuleValueJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        ValidationState = validationState,
        EffectiveFrom = new DateOnly(2020, 1, 1),
        SourceUrl = "https://example.test/source"
    };

    private sealed class FakeProfiles(params JurisdictionRule[] rules) : IJurisdictionProfileService
    {
        public Task<Jurisdiction?> GetJurisdictionAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<Jurisdiction?>(new Jurisdiction { Id = 1, Code = code, Name = code, Kind = "test" });

        public Task<IReadOnlyList<JurisdictionRule>> GetEffectiveRulesAsync(
            int jurisdictionId,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JurisdictionRule>>(rules);

        public Task<IReadOnlyList<JurisdictionRule>> GetEffectiveProfileRulesAsync(
            string jurisdictionCode,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JurisdictionRule>>(rules);
    }
}
