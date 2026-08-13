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
    public async Task ProblemGamblingPrevalenceResolver_ReturnsValidatedJurisdictionEvidence()
    {
        var rule = Rule(
            JurisdictionRuleTypes.ProblemGamblingPrevalence,
            IndianaProblemGamblingPrevalence(),
            JurisdictionRuleValidationStates.Validated);
        rule.Id = 117;
        rule.SourceUrl = "https://secure.in.gov/fssa/dmha/files/2025SEOWAnnualReport.pdf";
        var resolver = new ProblemGamblingPrevalenceResolver(new FakeProfiles(rule));

        var result = await resolver.ResolveAsync("US-IN", new DateOnly(2026, 8, 13));

        Assert.NotNull(result);
        Assert.Equal(0.041, result.Prevalence);
        Assert.Equal(0.018, result.LowerConfidenceBound);
        Assert.Equal(0.090, result.UpperConfidenceBound);
        Assert.Equal(117, result.JurisdictionRuleId);
    }

    [Fact]
    public async Task ProblemGamblingPrevalenceResolver_RejectsInvalidConfidenceInterval()
    {
        var invalid = IndianaProblemGamblingPrevalence() with
        {
            LowerConfidenceBound = 0.05,
            UpperConfidenceBound = 0.03
        };
        var resolver = new ProblemGamblingPrevalenceResolver(new FakeProfiles(Rule(
            JurisdictionRuleTypes.ProblemGamblingPrevalence,
            invalid,
            JurisdictionRuleValidationStates.Validated)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("US-IN", new DateOnly(2026, 8, 13)));
    }

    [Fact]
    public void SocialCostPrevalenceSelector_PreservesExplicitZeroOverride()
    {
        var definition = new ModelParameterDefinition
        {
            Key = "social_cost.prevalence",
            DisplayName = "Problem-gambling prevalence",
            Category = "social-cost",
            Units = "share",
            TechnicalDescription = "test",
            PlainLanguageDescription = "test",
            ProvenanceNotes = "test",
            ModelVersionApplicability = "gravity-v1"
        };
        var resolved = new ResolvedModelParameter(
            definition,
            0,
            0,
            null,
            0,
            0,
            "user-override",
            false,
            null);
        var evidence = new ProblemGamblingPrevalenceAssumption(
            0.041, 0.018, 0.090, 2021, "DSM-V", "Indiana adults", "Jun et al. 2021",
            "https://example.test/source", new string('a', 64), 1, 117);

        var selection = SocialCostPrevalenceSelector.Select(resolved, evidence);

        Assert.Equal(0, selection.AppliedPrevalence);
        Assert.Equal("user-override", selection.SourceKey);
        Assert.Null(selection.JurisdictionAssumption);
    }

    [Fact]
    public void SocialCostPrevalenceSelector_PrefersValidatedLocalEvidenceOverNationalPrior()
    {
        var definition = new ModelParameterDefinition
        {
            Key = "social_cost.prevalence",
            DisplayName = "Problem-gambling prevalence",
            Category = "social-cost",
            Units = "share",
            TechnicalDescription = "test",
            PlainLanguageDescription = "test",
            ProvenanceNotes = "test",
            ModelVersionApplicability = "gravity-v1"
        };
        var resolved = new ResolvedModelParameter(
            definition,
            0,
            0.01,
            null,
            null,
            0.01,
            "national-calibrated-set",
            false,
            null);
        var evidence = new ProblemGamblingPrevalenceAssumption(
            0.041, 0.018, 0.090, 2021, "DSM-V", "Indiana adults", "Jun et al. 2021",
            "https://example.test/source", new string('a', 64), 1, 117);

        var selection = SocialCostPrevalenceSelector.Select(resolved, evidence);

        Assert.Equal(0.041, selection.AppliedPrevalence);
        Assert.Equal("validated-jurisdiction-rule", selection.SourceKey);
        Assert.Same(evidence, selection.JurisdictionAssumption);
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

    [Fact]
    public async Task GamingTaxCalculator_AppliesIndianaLowPriorYearScheduleAndCrossingSurchargeOnce()
    {
        var schedule = IndianaRiverboatSchedule();
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            schedule,
            JurisdictionRuleValidationStates.Validated)));

        var crossing = await calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1),
            PriorPeriodTaxableGamingRevenue: 74_000_000m,
            CurrentTaxableGamingRevenue: 2_000_000m,
            PriorFiscalYearTaxableGamingRevenue: 50_000_000m));
        var afterCrossing = await calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1),
            PriorPeriodTaxableGamingRevenue: 76_000_000m,
            CurrentTaxableGamingRevenue: 1_000_000m,
            PriorFiscalYearTaxableGamingRevenue: 50_000_000m));

        Assert.Equal(3_000_000m, crossing.GamingTax);
        Assert.Equal(300_000m, afterCrossing.GamingTax);
    }

    [Fact]
    public async Task GamingTaxCalculator_UsesOrdinaryIndianaScheduleWhenPriorYearIsNotLow()
    {
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            IndianaRiverboatSchedule(),
            JurisdictionRuleValidationStates.Validated)));

        var result = await calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1),
            0,
            80_000_000m,
            PriorFiscalYearTaxableGamingRevenue: 75_000_000m));

        Assert.Equal(15_250_000m, result.GamingTax);
    }

    [Fact]
    public async Task GamingTaxCalculator_RequiresPriorYearRevenueForConditionalSchedule()
    {
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            IndianaRiverboatSchedule(),
            JurisdictionRuleValidationStates.Validated)));

        await Assert.ThrowsAsync<ArgumentException>(() => calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 1, 1),
            0,
            80_000_000m)));
    }

    [Fact]
    public async Task GamingTaxCalculator_AppliesIndianaRacinoSchedule()
    {
        var calculator = new GamingTaxCalculator(new FakeProfiles(Rule(
            JurisdictionRuleTypes.GamingTaxSchedule,
            new GamingTaxSchedulePayload(
                "commercial-racino",
                "Indiana racino AGR",
                [
                    new GamingTaxBracketPayload(100_000_000m, 0.25m),
                    new GamingTaxBracketPayload(null, 0.30m)
                ]),
            JurisdictionRuleValidationStates.Validated)));

        var result = await calculator.CalculateAsync(new GamingTaxRequest(
            "US-IN",
            "commercial-racino",
            new DateOnly(2026, 1, 1),
            0,
            150_000_000m));

        Assert.Equal(40_000_000m, result.GamingTax);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_AppliesNortheastIndianaComponentsWithoutFlatShare()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(
                Rule(
                    JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                    new SupplementalGamingTaxPayload("commercial-casino", 0.035m, ["18003"]),
                    JurisdictionRuleValidationStates.Validated),
                Rule(
                    JurisdictionRuleTypes.GamingTaxDistribution,
                    new GamingTaxDistributionPayload(
                        "commercial-casino", GamingTaxComponents.Base, ["18003"], false, 1m, 0m, 0m, 0m),
                    JurisdictionRuleValidationStates.Validated),
                Rule(
                    JurisdictionRuleTypes.GamingTaxDistribution,
                    new GamingTaxDistributionPayload(
                        "commercial-casino", GamingTaxComponents.Supplemental, ["18003"], true, 0m, 0.45m, 0.45m, 0.10m),
                    JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18003", "Fort Wayne", "1825000"));

        var result = await calculator.CalculateAsync(new GamingFiscalAllocationRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 8, 13),
            80_000_000m,
            15_250_000m,
            41.0793,
            -85.1394));

        Assert.Equal(2_800_000m, result.SupplementalGamingTax);
        Assert.Equal(18_050_000m, result.GrossGamingTax);
        Assert.Equal(1_260_000m, result.HostMunicipalityShare);
        Assert.Equal(1_260_000m, result.HostCountyShare);
        Assert.Equal(280_000m, result.HostRegionalShare);
        Assert.Equal(15_250_000m, result.HostStateShare);
        Assert.Equal("1825000", result.Location.MunicipalityGeoid);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_SelectsVenueSpecificStatutoryQuotient()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(
                Rule(
                    JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                    new SupplementalGamingTaxPayload(
                        "commercial-casino",
                        0.0316m,
                        ["18089"],
                        ["USA-IN-IGC-ameristar-casino"],
                        SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                        6_451_533m,
                        204_146_106m,
                        0.035m),
                    JurisdictionRuleValidationStates.Validated),
                Rule(
                    JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                    new SupplementalGamingTaxPayload(
                        "commercial-casino",
                        0.0259m,
                        ["18089"],
                        ["USA-IN-IGC-horseshoe-hammond"],
                        SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                        10_333_035m,
                        399_253_291m,
                        0.035m),
                    JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18089", "East Chicago", "1821290"));

        var result = await calculator.CalculateSupplementalTaxAsync(new SupplementalGamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 8, 13),
            100_000_000m,
            41.6521503,
            -87.4356793,
            "USA-IN-IGC-ameristar-casino"));

        Assert.Equal(0.0316m, result.Rate);
        Assert.Equal(3_160_000m, result.SupplementalGamingTax);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_AppliesStatutoryQuotientCap()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(Rule(
                JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                new SupplementalGamingTaxPayload(
                    "commercial-casino",
                    0.035m,
                    ["18091"],
                    ["USA-IN-IGC-blue-chip-casino"],
                    SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                    6_759_780m,
                    152_560_969m,
                    0.035m),
                JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18091", "Michigan City", "1848798"));

        var result = await calculator.CalculateSupplementalTaxAsync(new SupplementalGamingTaxRequest(
            "US-IN",
            "commercial-casino",
            new DateOnly(2026, 8, 13),
            9_747_252m,
            41.718776974043,
            -86.891430253456,
            "USA-IN-IGC-blue-chip-casino"));

        Assert.Equal(0.035m, result.Rate);
        Assert.Equal(341_153.82m, result.SupplementalGamingTax);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_RequiresVenueIdentityForVenueSpecificRate()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(Rule(
                JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                new SupplementalGamingTaxPayload(
                    "commercial-casino",
                    0.0316m,
                    ["18089"],
                    ["USA-IN-IGC-ameristar-casino"],
                    SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                    6_451_533m,
                    204_146_106m,
                    0.035m),
                JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18089", "East Chicago", "1821290"));

        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() =>
            calculator.CalculateSupplementalTaxAsync(new SupplementalGamingTaxRequest(
                "US-IN", "commercial-casino", new DateOnly(2026, 8, 13), 1_000_000m, 41.65, -87.44)));
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_RejectsRateThatDoesNotMatchStatutoryQuotient()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(Rule(
                JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                new SupplementalGamingTaxPayload(
                    "commercial-casino",
                    0.03m,
                    ["18089"],
                    ["USA-IN-IGC-ameristar-casino"],
                    SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                    6_451_533m,
                    204_146_106m,
                    0.035m),
                JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18089", "East Chicago", "1821290"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            calculator.CalculateSupplementalTaxAsync(new SupplementalGamingTaxRequest(
                "US-IN",
                "commercial-casino",
                new DateOnly(2026, 8, 13),
                1_000_000m,
                41.65,
                -87.44,
                "USA-IN-IGC-ameristar-casino")));

        Assert.Contains("does not equal its sourced statutory quotient", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_ReturnsExplicitZeroForExpiredRacinoFee()
    {
        var calculator = new GamingFiscalAllocationCalculator(
            new FakeProfiles(Rule(
                JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                new SupplementalGamingTaxPayload(
                    "commercial-racino",
                    0m,
                    ["18095", "18145"],
                    ["USA-IN-IGC-harrahs-hoosier-park", "USA-IN-IGC-horseshoe-indianapolis"],
                    SupplementalGamingTaxRateSourceKinds.FixedStatute),
                JurisdictionRuleValidationStates.Validated)),
            new FakeFiscalLocation("18095", "Anderson", "1801468"));

        var result = await calculator.CalculateSupplementalTaxAsync(new SupplementalGamingTaxRequest(
            "US-IN",
            "commercial-racino",
            new DateOnly(2026, 8, 13),
            20_000_000m,
            40.0679,
            -85.641,
            "USA-IN-IGC-harrahs-hoosier-park"));

        Assert.Equal(0m, result.Rate);
        Assert.Equal(0m, result.SupplementalGamingTax);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_RejectsUnincorporatedSiteWhenStatuteRequiresCity()
    {
        var calculator = NortheastIndianaFiscalCalculator(new FakeFiscalLocation("18033", null, null));

        var error = await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() =>
            calculator.CalculateAsync(new GamingFiscalAllocationRequest(
                "US-IN", "commercial-casino", new DateOnly(2026, 8, 13), 10_000_000m, 1_000_000m, 41.4, -85.0)));

        Assert.Contains("No county fallback is authorized", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GamingFiscalAllocationCalculator_RejectsCountyWithoutValidatedRule()
    {
        var calculator = NortheastIndianaFiscalCalculator(new FakeFiscalLocation("18089", "Hammond", "1831000"));

        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() =>
            calculator.CalculateAsync(new GamingFiscalAllocationRequest(
                "US-IN", "commercial-casino", new DateOnly(2026, 8, 13), 10_000_000m, 1_000_000m, 41.6, -87.5)));
    }

    private static GamingFiscalAllocationCalculator NortheastIndianaFiscalCalculator(ICandidateFiscalLocationResolver location) =>
        new(
            new FakeProfiles(
                Rule(
                    JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                    new SupplementalGamingTaxPayload("commercial-casino", 0.035m, ["18003", "18033", "18151"]),
                    JurisdictionRuleValidationStates.Validated),
                Rule(
                    JurisdictionRuleTypes.GamingTaxDistribution,
                    new GamingTaxDistributionPayload(
                        "commercial-casino", GamingTaxComponents.Base, ["18003", "18033", "18151"], false, 1m, 0m, 0m, 0m),
                    JurisdictionRuleValidationStates.Validated),
                Rule(
                    JurisdictionRuleTypes.GamingTaxDistribution,
                    new GamingTaxDistributionPayload(
                        "commercial-casino", GamingTaxComponents.Supplemental, ["18003", "18033", "18151"], true, 0m, 0.45m, 0.45m, 0.10m),
                    JurisdictionRuleValidationStates.Validated)),
            location);

    private static GamingTaxSchedulePayload IndianaRiverboatSchedule() => new(
        "commercial-casino",
        "Indiana taxable AGR",
        [
            new GamingTaxBracketPayload(25_000_000m, 0.10m),
            new GamingTaxBracketPayload(50_000_000m, 0.20m),
            new GamingTaxBracketPayload(75_000_000m, 0.25m),
            new GamingTaxBracketPayload(150_000_000m, 0.30m),
            new GamingTaxBracketPayload(600_000_000m, 0.35m),
            new GamingTaxBracketPayload(null, 0.40m)
        ],
        [
            new PriorFiscalYearGamingTaxSchedulePayload(
                "prior-year-agr-below-75m",
                75_000_000m,
                [
                    new GamingTaxBracketPayload(25_000_000m, 0.025m),
                    new GamingTaxBracketPayload(50_000_000m, 0.10m),
                    new GamingTaxBracketPayload(75_000_000m, 0.20m),
                    new GamingTaxBracketPayload(150_000_000m, 0.30m),
                    new GamingTaxBracketPayload(600_000_000m, 0.35m),
                    new GamingTaxBracketPayload(null, 0.40m)
                ],
                75_000_000m,
                2_500_000m)
        ]);

    private static ProblemGamblingPrevalenceRulePayload IndianaProblemGamblingPrevalence() => new(
        0.041,
        0.018,
        0.090,
        2021,
        "DSM-V",
        "Indiana adults",
        "Indiana SEOW Table 8.2 (Jun et al., 2021)",
        "9414096e164ce4a68ba700a46e659e662328403aaa82ec0209c0d03a25a47ee3");

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

    private sealed class FakeFiscalLocation(
        string countyFips,
        string? municipalityName,
        string? municipalityGeoid) : ICandidateFiscalLocationResolver
    {
        public Task<CandidateFiscalLocation> ResolveAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CandidateFiscalLocation(
                countyFips[..2],
                countyFips,
                "Test County",
                municipalityGeoid,
                municipalityName));
    }
}
