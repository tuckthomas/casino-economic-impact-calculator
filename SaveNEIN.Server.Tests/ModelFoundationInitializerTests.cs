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
    public async Task SeedAsync_ReplacesLegacyFiscalFixtureAndSeedsDistinctValidatedIndianaRegimes()
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
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"model-foundation-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}
