// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

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

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"model-foundation-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}
