// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Data;

public static class MichiganCommercialGamingRulesSeeder
{
    public const string TaxSourceUrl = "https://www.michigan.gov/mgcb/Detroit-Casinos/resources/revenues-and-wagering-tax-information";
    public const string AgeSourceUrl = "https://www.michigan.gov/mgcb/news/2024/10/02/reminder-to-wager-responsibly";
    private static readonly DateOnly EffectiveFrom = new(2025, 1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var national = await db.Jurisdictions.SingleAsync(row => row.Code == "US", cancellationToken);
        var michigan = await db.Jurisdictions.SingleOrDefaultAsync(row => row.Code == "US-MI", cancellationToken);
        if (michigan is null)
        {
            michigan = new Jurisdiction { Code = "US-MI", Name = "Michigan", Kind = "state", ParentJurisdictionId = national.Id };
            db.Jurisdictions.Add(michigan);
            await db.SaveChangesAsync(cancellationToken);
        }
        await AddAsync(JurisdictionRuleTypes.LegalGamingAge, new GamingAgeRulePayload("commercial-casino", 21), AgeSourceUrl,
            "MGCB confirms age 21 for Detroit commercial casino patrons. The modeling support window begins in 2025; this is not a tribal gaming-age rule.");
        await AddAsync(JurisdictionRuleTypes.GamingTaxSchedule,
            new GamingTaxSchedulePayload("commercial-casino", "MGCB-reported combined state/city land-based casino AGR levy; excludes retail sports and online gaming", [new(null, 0.19m)]),
            TaxSourceUrl, "MGCB reports 8.1% of land-based AGR to Michigan school aid and 10.9% to Detroit. Model the combined reported basis; additional license/development-agreement payments are outside this schedule.");
        await AddAsync(JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload("commercial-casino", 0m, ["26163"]), TaxSourceUrl,
            "The combined 19% reporting basis already includes the reported city levy; do not add it again as supplemental tax. Other fees are not inferred.");
        await AddAsync(JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload("commercial-casino", GamingTaxComponents.Base, ["26163"], true,
                0.081m / 0.19m, 0m, 0.109m / 0.19m, 0m,
                Recipients: [new("detroit-city", "City of Detroit", GamingTaxRecipientScopeKinds.HostMunicipality, 0.109m / 0.19m),
                             new("michigan-school-aid", "Michigan school aid", GamingTaxRecipientScopeKinds.HostState, 0.081m / 0.19m, ReceivesResidual: true)],
                EligibleMunicipalityGeoids: ["2622000"]), TaxSourceUrl,
            "MGCB land-based reporting split, restricted to Detroit in Wayne County. This does not establish licensing eligibility, statewide commercial expansion, tribal distributions or additional fees.");
        await AddAsync(JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload("commercial-casino", GamingTaxComponents.Supplemental, ["26163"], true,
                1m, 0m, 0m, 0m, EligibleMunicipalityGeoids: ["2622000"]), TaxSourceUrl,
            "Allocation of the explicitly zero supplemental component; reported city payments are already allocated from the combined base.");
        await db.SaveChangesAsync(cancellationToken);

        async Task AddAsync(string type, object payload, string source, string notes)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            if (await db.JurisdictionRules.AnyAsync(row => row.JurisdictionId == michigan.Id && row.RuleType == type &&
                row.EffectiveFrom == EffectiveFrom && row.RuleValueJson == json && row.SourceUrl == source, cancellationToken)) return;
            db.JurisdictionRules.Add(new JurisdictionRule
            {
                JurisdictionId = michigan.Id, RuleType = type, RuleValueJson = json,
                EffectiveFrom = EffectiveFrom, ValidationState = JurisdictionRuleValidationStates.Validated,
                SourceUrl = source, ProvenanceNotes = notes
            });
        }
    }
}
