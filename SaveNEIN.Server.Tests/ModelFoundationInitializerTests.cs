// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class ModelFoundationInitializerTests
{
    [Fact]
    public async Task SeedAsync_ProvidesScenarioPresetsAndInterpretableParameterMetadata()
    {
        await using var db = CreateDb();

        await ModelFoundationInitializer.SeedAsync(db);

        var beta = await db.ModelParameterDefinitions.SingleAsync(definition => definition.Key == "gravity.beta");
        Assert.Contains("routed drive time", beta.PlainLanguageDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gravity module", beta.TechnicalDescription, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(beta.ProvenanceNotes));
        Assert.Equal("advanced", beta.UiExposureLevel);
        Assert.True(beta.IsUserOverridable);

        var populationGrowth = await db.ModelParameterDefinitions.SingleAsync(
            definition => definition.Key == "demographics.population_annual_growth_rate");
        Assert.Equal("rate/year", populationGrowth.Units);
        Assert.Equal(0, populationGrowth.SystemDefaultValue);
        Assert.True(populationGrowth.IsUserOverridable);
        Assert.Contains("Census/ACS", populationGrowth.ProvenanceNotes, StringComparison.OrdinalIgnoreCase);

        var scenarioKinds = await db.ModelParameterSets
            .Where(set => set.Scope == "scenario")
            .Select(set => set.ScenarioKind!)
            .OrderBy(kind => kind)
            .ToArrayAsync();
        Assert.Equal(["base", "conservative", "high"], scenarioKinds);
    }

    [Fact]
    public async Task SeedAsync_UpgradesOnlyPlaceholderDescriptionsOnExistingDefinition()
    {
        await using var db = CreateDb();
        db.ModelParameterDefinitions.Add(new ModelParameterDefinition
        {
            Key = "gravity.beta",
            Category = "gravity",
            DisplayName = "Travel-time decay beta",
            TechnicalDescription = "Versioned model parameter 'gravity.beta'.",
            PlainLanguageDescription = "Travel-time decay beta",
            Units = "power",
            SystemDefaultValue = 1.5,
            UiExposureLevel = "advanced",
            IsUserOverridable = true,
            ModelVersionApplicability = "gravity-v1",
            ProvenanceNotes = "Existing provenance must survive metadata refresh."
        });
        await db.SaveChangesAsync();

        await ModelFoundationInitializer.SeedAsync(db);

        var beta = await db.ModelParameterDefinitions.SingleAsync(definition => definition.Key == "gravity.beta");
        Assert.Contains("routed drive time", beta.PlainLanguageDescription, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Versioned model parameter 'gravity.beta'.", beta.TechnicalDescription);
        Assert.Equal("Existing provenance must survive metadata refresh.", beta.ProvenanceNotes);
    }

    [Fact]
    public async Task SeedAsync_ReplacesSupersededFiscalFixturesAndSeedsValidatedIndianaComponents()
    {
        await using var db = CreateDb();
        var indiana = new Jurisdiction { Code = "US-IN", Name = "Indiana", Kind = "state" };
        db.Jurisdictions.Add(indiana);
        await db.SaveChangesAsync();
        db.JurisdictionRules.Add(new JurisdictionRule
        {
            JurisdictionId = indiana.Id,
            RuleType = JurisdictionRuleTypes.GamingTaxSchedule,
            RuleValueJson = JsonSerializer.Serialize(new GamingTaxSchedulePayload(
                "commercial-casino-standard",
                "Indiana taxable AGR",
                [new GamingTaxBracketPayload(null, 0.10m)]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ValidationState = JurisdictionRuleValidationStates.Provisional,
            EffectiveFrom = new DateOnly(2024, 7, 1),
            EffectiveTo = new DateOnly(2025, 6, 30),
            SourceUrl = "https://www.in.gov/igc/files/FY2025-Annual.pdf"
        });
        db.JurisdictionRules.Add(new JurisdictionRule
        {
            JurisdictionId = indiana.Id,
            RuleType = "local-revenue-share",
            RuleValueJson = "{\"facilityRegime\":\"commercial-casino\",\"shareOfGamingTax\":0.25}",
            ValidationState = JurisdictionRuleValidationStates.Validated,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            SourceUrl = "https://example.invalid/stale-flat-share"
        });
        db.JurisdictionRules.Add(new JurisdictionRule
        {
            JurisdictionId = indiana.Id,
            RuleType = JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            RuleValueJson = "{\"facilityRegime\":\"commercial-casino\",\"rate\":0.035,\"eligibleCountyFips\":[\"18003\"]}",
            ValidationState = JurisdictionRuleValidationStates.Validated,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            SourceUrl = "https://example.invalid/unversioned-supplemental-rate"
        });
        await db.SaveChangesAsync();

        await ModelFoundationInitializer.SeedAsync(db);
        await ModelFoundationInitializer.SeedAsync(db);

        var rules = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == indiana.Id)
            .ToArrayAsync();
        var ageRegimes = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.LegalGamingAge)
            .Select(rule => JsonSerializer.Deserialize<GamingAgeRulePayload>(
                rule.RuleValueJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.FacilityRegime)
            .OrderBy(regime => regime)
            .ToArray();
        var taxRegimes = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.GamingTaxSchedule)
            .Select(rule => new
            {
                rule.ValidationState,
                Payload = JsonSerializer.Deserialize<GamingTaxSchedulePayload>(
                    rule.RuleValueJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            })
            .OrderBy(item => item.Payload.FacilityRegime)
            .ToArray();

        Assert.Equal(["commercial-casino", "commercial-racino"], ageRegimes);
        Assert.Equal(["commercial-casino", "commercial-racino"], taxRegimes.Select(item => item.Payload.FacilityRegime));
        Assert.All(taxRegimes, item => Assert.Equal(JurisdictionRuleValidationStates.Validated, item.ValidationState));
        Assert.DoesNotContain(rules, rule => rule.SourceUrl == "https://www.in.gov/igc/files/FY2025-Annual.pdf");
        Assert.DoesNotContain(rules, rule => rule.SourceUrl == "https://example.invalid/unversioned-supplemental-rate");
        Assert.DoesNotContain(rules, rule => rule.RuleType == "local-revenue-share");

        var prevalenceRule = Assert.Single(rules, rule =>
            rule.RuleType == JurisdictionRuleTypes.ProblemGamblingPrevalence);
        var prevalence = JsonSerializer.Deserialize<ProblemGamblingPrevalenceRulePayload>(
            prevalenceRule.RuleValueJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(JurisdictionRuleValidationStates.Validated, prevalenceRule.ValidationState);
        Assert.Equal(0.041, prevalence.Prevalence);
        Assert.Equal(0.018, prevalence.LowerConfidenceBound);
        Assert.Equal(0.090, prevalence.UpperConfidenceBound);
        Assert.Equal("9414096e164ce4a68ba700a46e659e662328403aaa82ec0209c0d03a25a47ee3", prevalence.SourceSha256);

        var supplemental = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.SupplementalGamingTaxSchedule)
            .Select(rule => new
            {
                Rule = rule,
                Payload = JsonSerializer.Deserialize<SupplementalGamingTaxPayload>(
                    rule.RuleValueJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            })
            .ToArray();
        Assert.Equal(13, supplemental.Length);
        Assert.All(supplemental, item =>
            Assert.Equal(JurisdictionRuleValidationStates.Validated, item.Rule.ValidationState));
        Assert.Equal(8, supplemental.Count(item =>
            item.Payload.RateSourceKind == SupplementalGamingTaxRateSourceKinds.StatutoryQuotient));
        Assert.Equal(2, supplemental.Count(item => item.Payload.Rate == 0m));

        var northeastSupplemental = Assert.Single(supplemental, item =>
            item.Payload.EligibleCountyFips.SequenceEqual(["18003", "18033", "18151"]));
        Assert.Equal(0.035m, northeastSupplemental.Payload.Rate);
        Assert.Equal(SupplementalGamingTaxRateSourceKinds.FixedStatute, northeastSupplemental.Payload.RateSourceKind);

        var ameristarSupplemental = Assert.Single(supplemental, item =>
            item.Payload.EligibleStableVenueIds?.Contains("USA-IN-IGC-ameristar-casino") == true);
        Assert.Equal(0.0316m, ameristarSupplemental.Payload.Rate);
        Assert.Equal(6_451_533m, ameristarSupplemental.Payload.ReferenceAdmissionsTax);
        Assert.Equal(204_146_106m, ameristarSupplemental.Payload.ReferenceAdjustedGrossReceipts);
        Assert.Equal(0.035m, ameristarSupplemental.Payload.MaximumRate);

        var hardRockSupplemental = Assert.Single(supplemental, item =>
            item.Payload.EligibleStableVenueIds?.Contains("USA-IN-IGC-hard-rock-casino-northern-indiana") == true);
        Assert.Equal(0.0298m, hardRockSupplemental.Payload.Rate);
        Assert.Equal(SupplementalGamingTaxRateSourceKinds.RegulatorConfirmed, hardRockSupplemental.Payload.RateSourceKind);

        var countyWageringFee = Assert.Single(rules, rule =>
            rule.RuleType == JurisdictionRuleTypes.GamingRevenueChargeSchedule);
        var countyWageringFeePayload = JsonSerializer.Deserialize<GamingRevenueChargePayload>(
            countyWageringFee.RuleValueJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(GamingTaxComponents.CountyWageringFee, countyWageringFeePayload.Component);
        Assert.Equal(0.03m, countyWageringFeePayload.Rate);
        Assert.Equal(8_000_000m, countyWageringFeePayload.AnnualMaximum);

        var distributions = rules
            .Where(rule => rule.RuleType == JurisdictionRuleTypes.GamingTaxDistribution)
            .Select(rule => JsonSerializer.Deserialize<GamingTaxDistributionPayload>(
                rule.RuleValueJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .ToArray();
        Assert.Equal(8, distributions.Length);
        Assert.All(distributions, payload =>
        {
            Assert.NotEmpty(payload.Recipients!);
            Assert.Single(payload.Recipients!, recipient => recipient.ReceivesResidual);
        });
        var northeastBaseDistribution = Assert.Single(distributions, payload =>
            payload.Component == GamingTaxComponents.Base &&
            payload.FacilityRegime == "commercial-casino");
        Assert.Equal(1m, northeastBaseDistribution.StateShare);
        var racinoBaseDistribution = Assert.Single(distributions, payload =>
            payload.Component == GamingTaxComponents.Base &&
            payload.FacilityRegime == "commercial-racino");
        Assert.Equal("indiana-state-general-fund", Assert.Single(racinoBaseDistribution.Recipients!).RecipientKey);

        var northeastSupplementalDistribution = Assert.Single(distributions, payload =>
            payload.Component == GamingTaxComponents.Supplemental &&
            payload.EligibleStableVenueIds is null);
        Assert.True(northeastSupplementalDistribution.MunicipalityRequired);
        Assert.Equal(0.45m, northeastSupplementalDistribution.CountyShare);
        Assert.Equal(0.45m, northeastSupplementalDistribution.MunicipalityShare);
        Assert.Equal(0.10m, northeastSupplementalDistribution.RegionalShare);
        Assert.Contains(northeastSupplementalDistribution.Recipients!, recipient =>
            recipient.RecipientKey == "northeast-indiana-rda" && recipient.Share == 0.10m);

        var vigoDistribution = Assert.Single(distributions, payload =>
            payload.EligibleStableVenueIds?.Contains("USA-IN-IGC-terre-haute-casino") == true);
        Assert.Equal(0.40m, vigoDistribution.MunicipalityShare);
        Assert.Contains(vigoDistribution.Recipients!, recipient =>
            recipient.RecipientKey == "vigo-county-school-corporation" && recipient.Share == 0.15m);
        var countyFeeDistributions = distributions
            .Where(payload => payload.Component == GamingTaxComponents.CountyWageringFee)
            .ToArray();
        Assert.Equal(2, countyFeeDistributions.Length);
        Assert.All(countyFeeDistributions, payload =>
        {
            Assert.Equal(2020, payload.PopulationYear);
            Assert.Equal("dea5792efc347572bfbb2742e8cf88aa121831a70ae7db9086704e3485396b90", payload.PopulationSourceSha256);
        });
        var madisonCountyFee = Assert.Single(countyFeeDistributions, payload =>
            payload.EligibleCountyFips.SequenceEqual(["18095"]));
        Assert.Equal(16, madisonCountyFee.Recipients!.Count);
        Assert.Contains(madisonCountyFee.Recipients!, recipient =>
            recipient.RecipientKey == "anderson" && recipient.Share == 54_788m / 130_129m);
        var shelbyCountyFee = Assert.Single(countyFeeDistributions, payload =>
            payload.EligibleCountyFips.SequenceEqual(["18145"]));
        Assert.Equal(4, shelbyCountyFee.Recipients!.Count);
        Assert.Contains(shelbyCountyFee.Recipients!, recipient =>
            recipient.RecipientKey == "shelbyville" && recipient.Share == 20_067m / 45_055m);
        Assert.Equal(2, distributions.Count(payload =>
            payload.Recipients!.Any(recipient => recipient.RecipientKey.StartsWith("not-applicable-", StringComparison.Ordinal))));
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"model-foundation-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}
