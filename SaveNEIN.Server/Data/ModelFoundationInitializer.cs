// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Data;

public static class ModelFoundationInitializer
{
    private static readonly string[] MigrationResourceSuffixes =
    [
        "005_casino_competitors.sql",
        "006_gravity_model_foundation.sql",
        "007_model_data_foundation.sql",
        "008_gravity_engine.sql",
        "009_market_expansion.sql",
        "010_tourism_traffic_capacity_ramp.sql",
        "011_comprehensive_impact_accounting.sql",
        "012_validation_and_calibration.sql",
        "013_stored_run_reports.sql",
        "014_indiana_benchmark_evidence.sql",
        "015_sensitivity_analyses.sql",
        "016_local_economic_inventory.sql",
        "017_nullable_facility_evidence_flags.sql",
        "018_candidate_location_travel_cache.sql",
        "019_indiana_benchmark_reconciliation_outputs.sql",
        "020_coordinate_versioned_incumbent_travel_cache.sql",
        "021_component_gaming_fiscal_allocation.sql",
        "022_employment_assumption_provenance.sql",
        "023_capacity_productivity_benchmark_provenance.sql",
        "024_reported_casino_employment.sql",
        "025_validation_geographic_residual_patterns.sql",
        "026_other_gaming_revenue_charges.sql"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task ApplySchemaAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resources = assembly.GetManifestResourceNames();
            foreach (var suffix in MigrationResourceSuffixes)
            {
                var resourceName = resources.SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Embedded migration '{suffix}' was not found.");
                await using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' could not be opened.");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (closeConnection)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var national = await GetOrCreateJurisdictionAsync(
            db,
            code: "US",
            name: "United States",
            kind: "federal",
            parentJurisdictionId: null,
            cancellationToken);
        var indiana = await GetOrCreateJurisdictionAsync(
            db,
            code: "US-IN",
            name: "Indiana",
            kind: "state",
            parentJurisdictionId: national.Id,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Remove superseded development-only fiscal fixtures. The application is still in
        // development, so unsupported rules must not remain available to effective-rule selection.
        var obsoleteGamingTaxRules = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == indiana.Id &&
                           rule.RuleType == JurisdictionRuleTypes.GamingTaxSchedule &&
                           rule.ValidationState == JurisdictionRuleValidationStates.Provisional &&
                           rule.SourceUrl == "https://www.in.gov/igc/files/FY2025-Annual.pdf")
            .ToArrayAsync(cancellationToken);
        if (obsoleteGamingTaxRules.Length > 0)
        {
            db.JurisdictionRules.RemoveRange(obsoleteGamingTaxRules);
            await db.SaveChangesAsync(cancellationToken);
        }
        var obsoleteFlatRevenueShareRules = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == indiana.Id && rule.RuleType == "local-revenue-share")
            .ToArrayAsync(cancellationToken);
        if (obsoleteFlatRevenueShareRules.Length > 0)
        {
            db.JurisdictionRules.RemoveRange(obsoleteFlatRevenueShareRules);
            await db.SaveChangesAsync(cancellationToken);
        }
        var supplementalRulesForUpgrade = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == indiana.Id &&
                           rule.RuleType == JurisdictionRuleTypes.SupplementalGamingTaxSchedule)
            .ToArrayAsync(cancellationToken);
        var obsoleteUnversionedSupplementalRules = supplementalRulesForUpgrade
            .Where(rule => !rule.RuleValueJson.Contains("\"rateSourceKind\"", StringComparison.Ordinal))
            .ToArray();
        if (obsoleteUnversionedSupplementalRules.Length > 0)
        {
            db.JurisdictionRules.RemoveRange(obsoleteUnversionedSupplementalRules);
            await db.SaveChangesAsync(cancellationToken);
        }
        var distributionRulesForUpgrade = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == indiana.Id &&
                           rule.RuleType == JurisdictionRuleTypes.GamingTaxDistribution)
            .ToArrayAsync(cancellationToken);
        var obsoleteAggregateOnlyDistributionRules = distributionRulesForUpgrade
            .Where(rule => !rule.RuleValueJson.Contains("\"recipients\"", StringComparison.Ordinal))
            .ToArray();
        if (obsoleteAggregateOnlyDistributionRules.Length > 0)
        {
            db.JurisdictionRules.RemoveRange(obsoleteAggregateOnlyDistributionRules);
            await db.SaveChangesAsync(cancellationToken);
        }

        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("commercial-casino", 21),
            new DateOnly(2013, 1, 2),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://www.in.gov/legislative/iac/T00680/A00010.PDF",
            "68 IAC 1-11-1 states that a person under 21 may not be present in a casino.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.LegalGamingAge,
            new GamingAgeRulePayload("commercial-racino", 21),
            new DateOnly(2013, 1, 2),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://www.in.gov/legislative/iac/T00680/A00010.PDF",
            "68 IAC 1-11-1 applies to casino licensees, including casino gaming at racetracks.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.PermittedGamingProducts,
            new
            {
                facilityRegime = "commercial-casino",
                products = new[] { "electronic-gaming-devices", "table-games", "poker", "sports-wagering" }
            },
            new DateOnly(2025, 7, 1),
            null,
            JurisdictionRuleValidationStates.Provisional,
            "https://www.in.gov/igc/statutes-and-rules/",
            "Initial Indiana adapter inventory. Product-level applicability requires validation against the effective statute and facility license.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingRevenueDefinition,
            new
            {
                facilityRegime = "commercial-casino",
                metric = "taxable-agr",
                expression = "win - free_play + other_adjustments"
            },
            new DateOnly(2025, 7, 1),
            null,
            JurisdictionRuleValidationStates.Provisional,
            "https://www.in.gov/igc/files/2025-08-Revenue.pdf",
            "The IGC monthly report separately identifies win, free play, other adjustments, and taxable AGR. Final fiscal use requires effective-law reconciliation.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxSchedule,
            new GamingTaxSchedulePayload(
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
                        CurrentFiscalYearAdditionalTaxThreshold: 75_000_000m,
                        AdditionalTaxAmount: 2_500_000m)
                ]),
            new DateOnly(2021, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-13-1.5",
            "IC 4-33-13-1.5(a)-(c), verified in the official 2026 Indiana Code: the ordinary and prior-year-under-$75M graduated schedules apply after June 30, 2021; subsection (c) adds $2.5M when a low-prior-year property exceeds $75M in the current fiscal year.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxSchedule,
            new GamingTaxSchedulePayload(
                "commercial-racino",
                "Indiana adjusted gross receipts from gambling games at racetracks",
                [
                    new GamingTaxBracketPayload(100_000_000m, 0.25m),
                    new GamingTaxBracketPayload(null, 0.30m)
                ]),
            new DateOnly(2021, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8-1",
            "IC 4-35-8-1, verified in the official 2026 Indiana Code: 25% of the first $100M and 30% above $100M for fiscal years beginning after June 30, 2021.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingRevenueChargeSchedule,
            new GamingRevenueChargePayload(
                GamingTaxComponents.CountyWageringFee,
                "County gambling game wagering fee",
                "commercial-racino",
                0.03m,
                ["18095", "18145"],
                AnnualMaximum: 8_000_000m),
            new DateOnly(2015, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8.5-1",
            "IC 4-35-8.5-1 imposes a monthly county gambling game wagering fee equal to 3% of racino adjusted gross receipts, capped at $8M per licensee/racetrack per state fiscal year.",
            cancellationToken);
        const string indianaSupplementalCodeUrl = "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-1.5";
        const string fy2017MonthlyArchiveUrl = "https://www.in.gov/igc/publications/archived-monthly-revenue-reports/";
        var incumbentSupplementalRates = new[]
        {
            new { Name = "Ameristar Casino", StableVenueId = "USA-IN-IGC-ameristar-casino", CountyFips = "18089", Rate = 0.0316m, AdmissionsTax = 6_451_533m, TaxableAgr = 204_146_106m },
            new { Name = "Bally's Evansville", StableVenueId = "USA-IN-IGC-ballys-evansville", CountyFips = "18163", Rate = 0.0287m, AdmissionsTax = 3_465_765m, TaxableAgr = 120_714_269m },
            new { Name = "Belterra Casino", StableVenueId = "USA-IN-IGC-belterra-casino", CountyFips = "18155", Rate = 0.0299m, AdmissionsTax = 3_324_621m, TaxableAgr = 111_209_971m },
            new { Name = "Blue Chip Casino", StableVenueId = "USA-IN-IGC-blue-chip-casino", CountyFips = "18091", Rate = 0.0350m, AdmissionsTax = 6_759_780m, TaxableAgr = 152_560_969m },
            new { Name = "Caesars Southern Indiana", StableVenueId = "USA-IN-IGC-caesars-southern-indiana", CountyFips = "18061", Rate = 0.0228m, AdmissionsTax = 5_525_910m, TaxableAgr = 242_123_393m },
            new { Name = "Hollywood Lawrenceburg", StableVenueId = "USA-IN-IGC-hollywood-lawrenceburg", CountyFips = "18029", Rate = 0.0263m, AdmissionsTax = 4_289_004m, TaxableAgr = 162_872_617m },
            new { Name = "Horseshoe Hammond", StableVenueId = "USA-IN-IGC-horseshoe-hammond", CountyFips = "18089", Rate = 0.0259m, AdmissionsTax = 10_333_035m, TaxableAgr = 399_253_291m },
            new { Name = "Rising Star Casino", StableVenueId = "USA-IN-IGC-rising-star-casino", CountyFips = "18115", Rate = 0.0350m, AdmissionsTax = 2_263_842m, TaxableAgr = 45_825_400m }
        };
        foreach (var seed in incumbentSupplementalRates)
        {
            await AddRuleIfMissingAsync(
                db,
                indiana.Id,
                JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                new SupplementalGamingTaxPayload(
                    "commercial-casino",
                    seed.Rate,
                    [seed.CountyFips],
                    [seed.StableVenueId],
                    SupplementalGamingTaxRateSourceKinds.StatutoryQuotient,
                    seed.AdmissionsTax,
                    seed.TaxableAgr,
                    0.035m),
                new DateOnly(2019, 7, 1),
                null,
                JurisdictionRuleValidationStates.Validated,
                indianaSupplementalCodeUrl,
                $"IC 4-33-12-1.5(b)-(c) uses FY2017 admissions tax divided by FY2017 adjusted gross receipts and caps the post-June-2019 rate at 3.5%. " +
                $"For {seed.Name}, the official July 2016-June 2017 monthly reports at {fy2017MonthlyArchiveUrl} reconcile ${seed.AdmissionsTax:N0} admissions tax and ${seed.TaxableAgr:N0} taxable AGR; " +
                $"the four-decimal effective rate is {seed.Rate:P2}.",
                cancellationToken);
        }
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload(
                "commercial-casino",
                0.0298m,
                ["18089"],
                ["USA-IN-IGC-hard-rock-casino-northern-indiana"],
                SupplementalGamingTaxRateSourceKinds.RegulatorConfirmed),
            new DateOnly(2025, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://www.in.gov/igc/files/reports/2026/2026-04-Revenue.pdf",
            "IC 4-33-12-0.7(d) treats the post-June-2025 Gary operation as one riverboat. The IGC April 2026 report confirms the current 2.98% effective rate: $1,064,157 supplemental tax on $35,709,955 taxable AGR, rounded to reported whole dollars.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload(
                "commercial-casino",
                0.029m,
                ["18167"],
                ["USA-IN-IGC-terre-haute-casino"],
                SupplementalGamingTaxRateSourceKinds.FixedStatute),
            new DateOnly(2024, 4, 5),
            null,
            JurisdictionRuleValidationStates.Validated,
            indianaSupplementalCodeUrl,
            "IC 4-33-12-1.5(d) fixes the Vigo County inland casino supplemental wagering-tax rate at 2.9% of adjusted gross receipts.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload(
                "commercial-casino",
                0m,
                ["18117"],
                ["USA-IN-IGC-french-lick-resort"],
                SupplementalGamingTaxRateSourceKinds.FixedStatute),
            new DateOnly(2015, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-0.5",
            "IC 4-33-12-0.5 excludes a riverboat in a historic hotel district from the supplemental wagering-tax chapter; the effective modeled rate is explicitly zero.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload(
                "commercial-racino",
                0m,
                ["18095", "18145"],
                ["USA-IN-IGC-harrahs-hoosier-park", "USA-IN-IGC-horseshoe-indianapolis"],
                SupplementalGamingTaxRateSourceKinds.FixedStatute),
            new DateOnly(2012, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8.9-1",
            "IC 4-35-8.9-1 limits the former 1% slot-machine supplemental fee to fiscal years ending before July 1, 2012; current racino supplemental liability is explicitly zero.",
            cancellationToken);
        var northeastIndianaCountyFips = new[] { "18003", "18033", "18151" };
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
            new SupplementalGamingTaxPayload(
                "commercial-casino",
                0.035m,
                northeastIndianaCountyFips,
                RateSourceKind: SupplementalGamingTaxRateSourceKinds.FixedStatute),
            new DateOnly(2026, 3, 4),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-1.5",
            "IC 4-33-12-1.5(e), added by P.L.77-2026 effective March 4, 2026, imposes a 3.5% supplemental wagering tax on the new IC 4-33-6.8 casino in Allen, DeKalb, or Steuben County.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-casino",
                GamingTaxComponents.Base,
                northeastIndianaCountyFips,
                MunicipalityRequired: false,
                StateShare: 1m,
                CountyShare: 0m,
                MunicipalityShare: 0m,
                RegionalShare: 0m,
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "indiana-public-revenue-after-fixed-set-asides",
                        "Indiana public revenue after fixed statewide distributions",
                        GamingTaxRecipientScopeKinds.HostState,
                        0m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2026, 3, 12),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-13-5",
            "Current IC 4-33-13-5 as amended by P.L.157-2026 effective March 12, 2026 does not include Allen, DeKalb, or Steuben County in the 25% host-local distribution. For host-state accounting, the base tax therefore remains Indiana public revenue rather than host city/county revenue; this classification is not a claim that every dollar enters the state general fund.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-casino",
                GamingTaxComponents.Supplemental,
                northeastIndianaCountyFips,
                MunicipalityRequired: true,
                StateShare: 0m,
                CountyShare: 0.45m,
                MunicipalityShare: 0.45m,
                RegionalShare: 0.10m,
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "host-city",
                        "Host city",
                        GamingTaxRecipientScopeKinds.HostMunicipality,
                        0.45m),
                    new GamingTaxRecipientPayload(
                        "host-county",
                        "Host county",
                        GamingTaxRecipientScopeKinds.HostCounty,
                        0.45m),
                    new GamingTaxRecipientPayload(
                        "northeast-indiana-rda",
                        "Northeast Indiana Regional Development Authority",
                        GamingTaxRecipientScopeKinds.HostRegion,
                        0.10m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2026, 3, 4),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-8.7",
            "IC 4-33-12-8.7, added by P.L.77-2026 effective March 4, 2026, distributes the new northeast casino's supplemental tax 45% to the city, 45% to the county, and 10% to the Northeast Indiana Regional Development Authority. The statute supplies no unincorporated-site county fallback.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-casino",
                GamingTaxComponents.Supplemental,
                ["18167"],
                MunicipalityRequired: true,
                StateShare: 0m,
                CountyShare: 0.45m,
                MunicipalityShare: 0.40m,
                RegionalShare: 0.15m,
                EligibleStableVenueIds: ["USA-IN-IGC-terre-haute-casino"],
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "terre-haute",
                        "City of Terre Haute",
                        GamingTaxRecipientScopeKinds.HostMunicipality,
                        0.40m),
                    new GamingTaxRecipientPayload(
                        "vigo-county",
                        "Vigo County",
                        GamingTaxRecipientScopeKinds.HostCounty,
                        0.30m),
                    new GamingTaxRecipientPayload(
                        "vigo-county-school-corporation",
                        "Vigo County school corporation",
                        GamingTaxRecipientScopeKinds.HostCounty,
                        0.15m),
                    new GamingTaxRecipientPayload(
                        "west-central-2025",
                        "West Central 2025",
                        GamingTaxRecipientScopeKinds.HostRegion,
                        0.15m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2024, 4, 5),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-8.5",
            "IC 4-33-12-8.5 distributes Vigo County inland-casino supplemental tax to four named recipients: 40% Terre Haute, 30% Vigo County, 15% the Vigo County school corporation, and 15% West Central 2025.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-casino",
                GamingTaxComponents.Supplemental,
                ["18117"],
                MunicipalityRequired: false,
                StateShare: 1m,
                CountyShare: 0m,
                MunicipalityShare: 0m,
                RegionalShare: 0m,
                EligibleStableVenueIds: ["USA-IN-IGC-french-lick-resort"],
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "not-applicable-historic-hotel-district",
                        "No supplemental tax: historic hotel district exclusion",
                        GamingTaxRecipientScopeKinds.HostState,
                        0m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2015, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-33-12-0.5",
            "The supplemental-tax chapter does not apply to the historic-hotel-district riverboat; the named distribution therefore reconciles an explicit zero liability.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-racino",
                GamingTaxComponents.Supplemental,
                ["18095", "18145"],
                MunicipalityRequired: false,
                StateShare: 1m,
                CountyShare: 0m,
                MunicipalityShare: 0m,
                RegionalShare: 0m,
                EligibleStableVenueIds: ["USA-IN-IGC-harrahs-hoosier-park", "USA-IN-IGC-horseshoe-indianapolis"],
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "not-applicable-expired-racino-supplemental-fee",
                        "No supplemental tax: former racino fee expired",
                        GamingTaxRecipientScopeKinds.HostState,
                        0m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2012, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8.9-1",
            "The former racino supplemental-fee chapter expired before July 1, 2012; the named distribution therefore reconciles an explicit zero liability.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-racino",
                GamingTaxComponents.Base,
                ["18095", "18145"],
                MunicipalityRequired: false,
                StateShare: 1m,
                CountyShare: 0m,
                MunicipalityShare: 0m,
                RegionalShare: 0m,
                Recipients:
                [
                    new GamingTaxRecipientPayload(
                        "indiana-state-general-fund",
                        "Indiana state general fund",
                        GamingTaxRecipientScopeKinds.HostState,
                        0m,
                        ReceivesResidual: true)
                ]),
            new DateOnly(2008, 7, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8-3",
            "IC 4-35-8-3 requires racino wagering taxes to be deposited in the state general fund.",
            cancellationToken);
        const string indiana2020PopulationUrl = "https://www.in.gov/indot/doing-business-with-indot/files/2020-City_Town_-County-Populations.pdf";
        const string indiana2020PopulationSha256 = "dea5792efc347572bfbb2742e8cf88aa121831a70ae7db9086704e3485396b90";
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-racino",
                GamingTaxComponents.CountyWageringFee,
                ["18095"],
                MunicipalityRequired: false,
                StateShare: 0m,
                CountyShare: 44_182m / 130_129m,
                MunicipalityShare: 85_947m / 130_129m,
                RegionalShare: 0m,
                Recipients:
                [
                    PopulationRecipient("alexandria", "City of Alexandria", 5_149, 130_129),
                    PopulationRecipient("anderson", "City of Anderson", 54_788, 130_129),
                    PopulationRecipient("chesterfield", "Town of Chesterfield", 2_490, 130_129),
                    PopulationRecipient("country-club-heights", "Town of Country Club Heights", 98, 130_129),
                    PopulationRecipient("edgewood", "Town of Edgewood", 2_053, 130_129),
                    PopulationRecipient("elwood", "City of Elwood", 8_410, 130_129),
                    PopulationRecipient("frankton", "Town of Frankton", 1_775, 130_129),
                    PopulationRecipient("ingalls", "Town of Ingalls", 2_223, 130_129),
                    PopulationRecipient("lapel", "Town of Lapel", 2_325, 130_129),
                    PopulationRecipient("markleville", "Town of Markleville", 484, 130_129),
                    PopulationRecipient("orestes", "Town of Orestes", 329, 130_129),
                    PopulationRecipient("pendleton", "Town of Pendleton", 4_717, 130_129),
                    PopulationRecipient("river-forest", "Town of River Forest", 26, 130_129),
                    PopulationRecipient("summitville", "Town of Summitville", 989, 130_129),
                    PopulationRecipient("woodlawn-heights", "Town of Woodlawn Heights", 91, 130_129),
                    new GamingTaxRecipientPayload(
                        "madison-county",
                        "Madison County",
                        GamingTaxRecipientScopeKinds.HostCounty,
                        44_182m / 130_129m,
                        ReceivesResidual: true)
                ],
                PopulationSourceUrl: indiana2020PopulationUrl,
                PopulationSourceSha256: indiana2020PopulationSha256,
                PopulationYear: 2020,
                PopulationBasis: "Most recent effective federal decennial census under IC 1-1-3.5-3; each city/town receives its population divided by Madison County population, with the county retaining the remainder under IC 4-35-8.5-3."),
            new DateOnly(2022, 4, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8.5-3",
            "IC 4-35-8.5-2 and -3 distribute each racino fee to its county, then to every city/town by population ratio, with the county retaining the remainder. Counts are the official 2020 decennial values effective April 1, 2022 under IC 1-1-3.5-3.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.GamingTaxDistribution,
            new GamingTaxDistributionPayload(
                "commercial-racino",
                GamingTaxComponents.CountyWageringFee,
                ["18145"],
                MunicipalityRequired: false,
                StateShare: 0m,
                CountyShare: 23_241m / 45_055m,
                MunicipalityShare: 21_814m / 45_055m,
                RegionalShare: 0m,
                Recipients:
                [
                    PopulationRecipient("fairland", "Town of Fairland", 542, 45_055),
                    PopulationRecipient("morristown", "Town of Morristown", 1_205, 45_055),
                    PopulationRecipient("shelbyville", "City of Shelbyville", 20_067, 45_055),
                    new GamingTaxRecipientPayload(
                        "shelby-county",
                        "Shelby County",
                        GamingTaxRecipientScopeKinds.HostCounty,
                        23_241m / 45_055m,
                        ReceivesResidual: true)
                ],
                PopulationSourceUrl: indiana2020PopulationUrl,
                PopulationSourceSha256: indiana2020PopulationSha256,
                PopulationYear: 2020,
                PopulationBasis: "Most recent effective federal decennial census under IC 1-1-3.5-3; each city/town receives its population divided by Shelby County population, with the county retaining the remainder under IC 4-35-8.5-3."),
            new DateOnly(2022, 4, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://iga.in.gov/laws/2026/ic/titles/4#4-35-8.5-3",
            "IC 4-35-8.5-2 and -3 distribute each racino fee to its county, then to every city/town by population ratio, with the county retaining the remainder. Counts are the official 2020 decennial values effective April 1, 2022 under IC 1-1-3.5-3.",
            cancellationToken);
        await AddRuleIfMissingAsync(
            db,
            indiana.Id,
            JurisdictionRuleTypes.ProblemGamblingPrevalence,
            new ProblemGamblingPrevalenceRulePayload(
                Prevalence: 0.041,
                LowerConfidenceBound: 0.018,
                UpperConfidenceBound: 0.090,
                ObservationYear: 2021,
                Instrument: "DSM-V",
                Population: "Indiana adults",
                Citation: "Indiana State Epidemiological Outcomes Workgroup, 2025 annual report, Table 8.2 (Jun et al., 2021)",
                SourceSha256: "9414096e164ce4a68ba700a46e659e662328403aaa82ec0209c0d03a25a47ee3"),
            new DateOnly(2021, 1, 1),
            null,
            JurisdictionRuleValidationStates.Validated,
            "https://secure.in.gov/fssa/dmha/files/2025SEOWAnnualReport.pdf",
            "Official Indiana FSSA/DMHA report page 117: DSM-V gambling-disorder prevalence 4.1%, 95% CI 1.8%-9.0%. This is observed existing prevalence, not a causal estimate of incremental cases from a new casino; exposure response and nonoverlapping cost domains require separate evidence.",
            cancellationToken);

        var definitions = ParameterDefinitionSeeds().ToArray();
        var existingDefinitionKeys = await db.ModelParameterDefinitions
            .Select(definition => definition.Key)
            .ToHashSetAsync(cancellationToken);
        foreach (var definition in definitions.Where(definition => !existingDefinitionKeys.Contains(definition.Key)))
        {
            db.ModelParameterDefinitions.Add(definition);
        }
        await db.SaveChangesAsync(cancellationToken);

        var definitionsByKey = await db.ModelParameterDefinitions
            .ToDictionaryAsync(definition => definition.Key, StringComparer.Ordinal, cancellationToken);
        foreach (var seededDefinition in definitions)
        {
            var storedDefinition = definitionsByKey[seededDefinition.Key];
            if (storedDefinition.TechnicalDescription == $"Versioned model parameter '{storedDefinition.Key}'.")
            {
                storedDefinition.TechnicalDescription = seededDefinition.TechnicalDescription;
            }
            if (storedDefinition.PlainLanguageDescription == storedDefinition.DisplayName)
            {
                storedDefinition.PlainLanguageDescription = seededDefinition.PlainLanguageDescription;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var nationalBase = await GetOrCreateParameterSetAsync(
            db,
            "national-base",
            "National Base Prior",
            "national",
            null,
            null,
            null,
            "0.1.0-provisional",
            "Initial transparent priors. Values remain uncalibrated until multi-market validation is complete.",
            cancellationToken);
        var indianaBase = await GetOrCreateParameterSetAsync(
            db,
            "indiana-base",
            "Indiana Base Prior",
            "jurisdiction",
            indiana.Id,
            "US-IN",
            null,
            "0.1.0-provisional",
            "Initial Indiana benchmark priors from the canonical plan; not a finalized calibration.",
            cancellationToken);
        var conservative = await GetOrCreateParameterSetAsync(
            db, "scenario-conservative", "Conservative Scenario", "scenario", null, null, "conservative",
            "0.1.0-provisional", "Versioned conservative scenario preset.", cancellationToken);
        var baseScenario = await GetOrCreateParameterSetAsync(
            db, "scenario-base", "Base Scenario", "scenario", null, null, "base",
            "0.1.0-provisional", "Versioned base scenario preset.", cancellationToken);
        var high = await GetOrCreateParameterSetAsync(
            db, "scenario-high", "High Scenario", "scenario", null, null, "high",
            "0.1.0-provisional", "Versioned high scenario preset.", cancellationToken);
        var spectrumBenchmark = await GetOrCreateParameterSetAsync(
            db, "benchmark-spectrum-indiana", "Spectrum Indiana Benchmark", "benchmark", indiana.Id, "US-IN", null,
            "2025.1", "Public-study validation prior; never a universal production constant.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await AddParameterValuesIfMissingAsync(db, nationalBase.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["gravity.beta"] = 1.5,
            ["gravity.alpha"] = 1.0,
            ["gravity.regularization_minutes"] = 1.0,
            ["demand.income_elasticity"] = 1.0,
            ["demand.regional_intensity_multiplier"] = 1.0
        }, cancellationToken);
        await AddParameterValuesIfMissingAsync(db, indianaBase.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["gravity.beta"] = 1.5,
            ["demand.gaming_income_share"] = 0.0066
        }, cancellationToken);
        await AddParameterValuesIfMissingAsync(db, conservative.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["demand.regional_intensity_multiplier"] = 0.85,
            ["market_expansion.maximum_induced_demand_share"] = 0.10,
            ["tourism.capture_rate"] = 0.0,
            ["traffic.intercept_rate"] = 0.0
        }, cancellationToken);
        await AddParameterValuesIfMissingAsync(db, baseScenario.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["demand.regional_intensity_multiplier"] = 1.0
        }, cancellationToken);
        await AddParameterValuesIfMissingAsync(db, high.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["demand.regional_intensity_multiplier"] = 1.15,
            ["market_expansion.maximum_induced_demand_share"] = 0.25
        }, cancellationToken);
        await AddParameterValuesIfMissingAsync(db, spectrumBenchmark.Id, definitionsByKey, new Dictionary<string, double>
        {
            ["demand.gaming_income_share"] = 0.0058
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<ModelParameterDefinition> ParameterDefinitionSeeds()
    {
        yield return Definition("gravity.beta", "gravity", "Travel-time decay beta", "power", 1.5, 0.1, 10, 1.4, 1.6, 0.05, "advanced", false,
            "Steinberg Indiana study prior described in the canonical plan; calibration is still required.");
        yield return Definition("gravity.alpha", "gravity", "Facility attraction elasticity", "elasticity", 1.0, 0, 5, 0.5, 1.5, 0.05, "expert", false,
            "Neutral elasticity prior; requires empirical calibration.");
        yield return Definition("gravity.exponential_lambda", "gravity", "Exponential travel decay", "per minute", 0.03, 0, 1, 0.005, 0.2, 0.005, "expert", false,
            "Validation-only exponential-friction prior.");
        yield return Definition("gravity.outside_option_weight", "gravity", "Outside-option weight", "attraction weight", 1.0, 0, 1_000_000, 0, null, 0.1, "expert", false,
            "Neutral starting value; must be calibrated against incomplete market capture.");
        yield return Definition("gravity.regularization_minutes", "gravity", "Travel-time regularization", "minutes", 1.0, 0.01, 60, 0.1, 5, 0.1, "expert", false,
            "Numerical regularization prior to prevent singular weights.");
        yield return Definition("demand.gaming_income_share", "demand", "Gaming expenditure share of income", "share", 0.0058, 0, 0.10, 0.004, 0.008, 0.0001, "advanced", false,
            "Spectrum national benchmark prior (approximately 0.58%) from the canonical plan.");
        yield return Definition("demand.base_ggr_per_eligible_adult", "demand", "Base GGR per eligible adult", "USD/person/year", 0, 0, 10_000, 100, 2_000, 10, "advanced", false,
            "Zero-safe fallback prevents unsupported production forecasts until calibrated.");
        yield return Definition("demand.income_elasticity", "demand", "Income elasticity", "elasticity", 1.0, -5, 5, 0, 2, 0.05, "advanced", false,
            "Unit-elastic prior; requires validation.");
        yield return Definition("demand.income_adjustment_minimum", "demand", "Minimum income adjustment", "multiplier", 0.5, 0, 10, 0.25, 1, 0.05, "expert", false,
            "Numerical and model-risk bound pending market calibration.");
        yield return Definition("demand.income_adjustment_maximum", "demand", "Maximum income adjustment", "multiplier", 1.5, 0, 10, 1, 3, 0.05, "expert", false,
            "Numerical and model-risk bound pending market calibration.");
        yield return Definition("demand.regional_intensity_multiplier", "demand", "Regional gaming intensity", "multiplier", 1.0, 0, 10, 0.5, 1.5, 0.05, "advanced", false,
            "Neutral regional multiplier.");
        yield return Definition("demographics.population_annual_growth_rate", "demographics", "Annual population growth", "rate/year", 0, -0.10, 0.10, -0.03, 0.03, 0.001, "advanced", false,
            "Explicit constant-population default. A nonzero versioned value compounds the selected Census/ACS age-population observation year to the scenario effective year; use a jurisdiction-appropriate official projection source and retain it in parameter-set provenance.");
        yield return Definition("facility.gaming_positions_coefficient", "facility-attraction", "Gaming positions coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.table_games_coefficient", "facility-attraction", "Table games coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.hotel_rooms_coefficient", "facility-attraction", "Hotel rooms coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.gaming_floor_coefficient", "facility-attraction", "Gaming-floor coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration against regulator-published gaming-floor area.");
        yield return Definition("facility.food_beverage_coefficient", "facility-attraction", "Food and beverage coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration against published venue counts.");
        yield return Definition("facility.entertainment_capacity_coefficient", "facility-attraction", "Entertainment capacity coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.capital_scale_coefficient", "facility-attraction", "Development capital coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.highway_access_coefficient", "facility-attraction", "Direct highway access coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.reference_gaming_positions", "facility-attraction", "Reference gaming positions", "positions", 2000, 1, 100000, 500, 5000, 50, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_table_games", "facility-attraction", "Reference table games", "tables", 50, 0, 10000, 10, 200, 5, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_hotel_rooms", "facility-attraction", "Reference hotel rooms", "rooms", 250, 0, 100000, 0, 1000, 25, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_gaming_floor_square_feet", "facility-attraction", "Reference gaming-floor area", "square feet", 75_000, 0, 10_000_000, 25_000, 250_000, 5_000, "expert", false, "Interpretability reference facility; source area must remain attributable to a versioned facility snapshot.");
        yield return Definition("facility.reference_food_beverage_venues", "facility-attraction", "Reference food and beverage venues", "venues", 4, 0, 1_000, 0, 25, 1, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_entertainment_capacity", "facility-attraction", "Reference entertainment capacity", "people", 1000, 0, 1000000, 0, 10000, 100, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_capital_cost", "facility-attraction", "Reference development capital", "USD", 500000000, 1, 100000000000, 100000000, 2000000000, 10000000, "expert", false, "Interpretability reference in constant dollars; dollar-year normalization remains required.");
        yield return Definition("facility.reference_highway_access", "facility-attraction", "Reference direct highway access", "indicator", 1, 0, 1, 0, 1, 1, "expert", false, "Binary interpretability reference for direct limited-access interchange availability.");
        yield return Definition("facility.gaming_positions_offset", "facility-attraction", "Gaming positions log offset", "positions", 2000, 1, 100000, 100, 10000, 50, "expert", false, "Versioned zero-safe log transform offset; the initial reference-sized offset avoids treating a zero count as near-zero facility mass.");
        yield return Definition("facility.table_games_offset", "facility-attraction", "Table games log offset", "tables", 50, 1, 10000, 5, 500, 5, "expert", false, "Versioned zero-safe log transform offset for table-game breadth.");
        yield return Definition("facility.hotel_rooms_offset", "facility-attraction", "Hotel rooms log offset", "rooms", 250, 1, 100000, 25, 2500, 25, "expert", false, "Versioned zero-safe log transform offset so a legitimate no-hotel property is not collapsed toward zero competitive mass.");
        yield return Definition("facility.gaming_floor_square_feet_offset", "facility-attraction", "Gaming-floor log offset", "square feet", 75_000, 1, 10_000_000, 5_000, 500_000, 5_000, "expert", false, "Versioned zero-safe log transform offset for gaming-floor area.");
        yield return Definition("facility.food_beverage_venues_offset", "facility-attraction", "Food and beverage log offset", "venues", 4, 1, 1_000, 1, 50, 1, "expert", false, "Versioned zero-safe log transform offset for venue count.");
        yield return Definition("facility.entertainment_capacity_offset", "facility-attraction", "Entertainment capacity log offset", "people", 1000, 1, 1_000_000, 100, 25_000, 100, "expert", false, "Versioned zero-safe log transform offset for entertainment capacity.");
        yield return Definition("facility.capital_cost_offset", "facility-attraction", "Development capital log offset", "USD", 500_000_000, 1, 100_000_000_000, 10_000_000, 5_000_000_000, 10_000_000, "expert", false, "Versioned zero-safe log transform offset for constant-dollar development capital.");
        yield return Definition("facility.highway_access_offset", "facility-attraction", "Highway access log offset", "indicator", 1, 0.01, 10, 0.1, 2, 0.1, "expert", false, "Versioned zero-safe log transform offset for the binary direct-access indicator.");
        yield return Definition("facility.proposed_scale_multiplier", "facility-attraction", "Proposed-property scale", "multiplier", 1.0, 0, 10, 0.25, 4, 0.05, "advanced", false, "Neutral proposed-property scale prior.");
        yield return Definition("facility.comparable_scale_multiplier", "facility-attraction", "Comparable-property scale", "multiplier", 1.0, 0, 10, 0.25, 4, 0.05, "expert", false, "Neutral comparable-property scaling prior.");
        yield return Definition("market_expansion.accessibility_elasticity", "market-expansion", "Accessibility-induced demand elasticity", "elasticity", 0, 0, 10, 0, 1, 0.01, "expert", false, "Zero-safe fallback pending causal calibration.");
        yield return Definition("market_expansion.maximum_induced_demand_share", "market-expansion", "Maximum induced-demand share", "share", 0.25, 0, 1, 0, 0.5, 0.01, "advanced", false, "Sensitivity cap prior, not a calibrated estimate.");
        yield return Definition("tourism.participation_rate", "tourism", "Tourist gaming participation", "share", 0, 0, 1, 0, 0.5, 0.01, "advanced", false, "Zero-safe fallback pending tourism data.");
        yield return Definition("tourism.capture_rate", "tourism", "Tourism capture rate", "share", 0, 0, 1, 0, 0.5, 0.01, "advanced", false, "Zero-safe fallback pending tourism data.");
        yield return Definition("tourism.eligible_visitor_share", "tourism", "Casino-eligible visitor share", "share", 0, 0, 1, 0, 1, 0.01, "advanced", false, "Zero-safe fallback pending visitor demographic evidence.");
        yield return Definition("tourism.resident_origin_overlap_share", "tourism", "Tourism/resident overlap", "share", 0, 0, 1, 0, 1, 0.01, "expert", false, "Explicit deduplication parameter; zero until derived from provider coverage and resident origins.");
        yield return Definition("tourism.ggr_per_captured_participant", "tourism", "GGR per captured visitor participant", "USD/trip", 0, 0, 100000, 0, null, 1, "advanced", false, "Zero-safe fallback pending observed visitor gaming spend evidence.");
        yield return Definition("traffic.intercept_rate", "traffic", "Through-traffic intercept rate", "share", 0, 0, 1, 0, 0.25, 0.001, "advanced", false, "Zero-safe fallback pending DOT/intercept evidence.");
        yield return Definition("traffic.eligible_passengers_per_vehicle", "traffic", "Eligible passengers per vehicle", "people/vehicle", 1.0, 0, 20, 1, 3, 0.1, "expert", false, "Conservative occupancy prior pending corridor evidence.");
        yield return Definition("traffic.resident_origin_overlap_share", "traffic", "Traffic/resident overlap deduction", "share", 0, 0, 1, 0, 1, 0.01, "expert", false, "Zero-safe deduplication fallback pending corridor-to-origin matching.");
        yield return Definition("traffic.overlap_deduplication_share", "traffic", "Traffic/tourism overlap deduction", "share", 0, 0, 1, 0, 1, 0.01, "expert", false, "Zero-safe overlap prior pending deduplication evidence.");
        yield return Definition("traffic.ggr_per_intercepted_traveler", "traffic", "GGR per intercepted traveler", "USD/trip", 0, 0, 100000, 0, null, 1, "advanced", false, "Zero-safe fallback pending corridor intercept and spend evidence.");
        yield return Definition("ramp.first_year_share", "ramp", "First-year stabilization share", "share", 0.65, 0, 1.5, 0.4, 1.0, 0.05, "standard", false, "Scenario prior requiring comparable-opening validation.");
        yield return Definition("ramp.second_year_share", "ramp", "Second-year stabilization share", "share", 0.85, 0, 1.5, 0.6, 1.1, 0.05, "standard", false, "Scenario prior requiring comparable-opening validation.");
        yield return Definition("ramp.stabilized_year", "ramp", "Stabilized year", "year number", 3, 1, 20, 2, 5, 1, "standard", false, "Scenario prior requiring comparable-opening validation.");
        yield return Definition("ramp.stabilized_annual_growth_rate", "ramp", "Post-stabilization annual growth", "rate", 0, -0.99, 1, -0.1, 0.1, 0.005, "advanced", false, "Zero real-growth fallback pending a versioned growth scenario.");
        yield return Definition("ramp.projection_years", "ramp", "Ramp projection horizon", "years", 5, 3, 50, 3, 10, 1, "advanced", false, "Five-year reporting horizon prior.");
        yield return Definition("capacity.diagnostic_enabled", "capacity", "Parameter-range capacity diagnostic enabled", "indicator", 0, 0, 1, 0, 1, 1, "expert", false, "Enables an explicit versioned parameter range when no complete regulator-observed benchmark is available; a complete observed benchmark activates automatically.");
        yield return Definition("capacity.operating_days_per_year", "capacity", "Operating days per year", "days", 365, 1, 366, 300, 366, 1, "expert", false, "Full-year operation prior.");
        yield return Definition("capacity.slot_win_per_unit_day_minimum", "capacity", "Minimum slot win per unit day", "USD/unit/day", 0, 0, 100000, 0, null, 1, "expert", false, "Zero-safe placeholder pending comparable-facility calibration.");
        yield return Definition("capacity.slot_win_per_unit_day_maximum", "capacity", "Maximum slot win per unit day", "USD/unit/day", 0, 0, 100000, 0, null, 1, "expert", false, "Zero-safe placeholder pending comparable-facility calibration.");
        yield return Definition("capacity.table_win_per_table_day_minimum", "capacity", "Minimum table win per table day", "USD/table/day", 0, 0, 1000000, 0, null, 10, "expert", false, "Zero-safe placeholder pending comparable-facility calibration.");
        yield return Definition("capacity.table_win_per_table_day_maximum", "capacity", "Maximum table win per table day", "USD/table/day", 0, 0, 1000000, 0, null, 10, "expert", false, "Zero-safe placeholder pending comparable-facility calibration.");
        yield return Definition("displacement.local_patron_share", "displacement", "Local patron share", "share", 0.5, 0, 1, 0, 1, 0.01, "standard", false, "Neutral sensitivity prior; must be derived from modeled origins.");
        yield return Definition("displacement.coefficient", "displacement", "Eligible spending displacement", "share", 0.5, 0, 1, 0, 1, 0.01, "standard", false, "Sensitivity prior pending sector calibration.");
        yield return Definition("displacement.sector_prior_scale", "displacement", "Sector-prior scale", "multiplier", 1, 0, 10, 0.25, 4, 0.05, "expert", false, "Neutral scaling prior for sector-specific shares.");
        yield return Definition("displacement.taxable_share", "displacement", "Displaced taxable share", "share", 1, 0, 1, 0, 1, 0.01, "expert", false, "Upper-bound fallback pending jurisdiction/sector evidence.");
        yield return Definition("displacement.business_margin", "displacement", "Displaced business margin", "share", 0.20, 0, 1, 0, 0.5, 0.01, "expert", false, "Sensitivity prior pending sector evidence.");
        yield return Definition("displacement.eligible_base_share", "displacement", "Economically eligible local resident share", "share", 0, 0, 1, 0, 1, 0.01, "advanced", false, "Zero-safe fallback; excludes casino transfers, repatriation/leakage, tourism, and traffic before application.");
        yield return Definition("displacement.restaurant_hospitality_weight", "displacement", "Restaurant/hospitality sector prior", "weight", 0.40, 0, 1, 0, 1, 0.01, "advanced", false, "Transparent national prior requiring local inventory modulation.");
        yield return Definition("displacement.retail_weight", "displacement", "Retail sector prior", "weight", 0.35, 0, 1, 0, 1, 0.01, "advanced", false, "Transparent national prior requiring local inventory modulation.");
        yield return Definition("displacement.arts_entertainment_recreation_weight", "displacement", "Arts/entertainment/recreation sector prior", "weight", 0.25, 0, 1, 0, 1, 0.01, "advanced", false, "Transparent national prior requiring local inventory modulation.");
        yield return Definition("displacement.restaurant_hospitality_inventory_modifier", "displacement", "Restaurant/hospitality local inventory modifier", "multiplier", 1, 0, 10, 0.25, 4, 0.05, "expert", false, "Neutral fallback pending versioned local inventory data.");
        yield return Definition("displacement.retail_inventory_modifier", "displacement", "Retail local inventory modifier", "multiplier", 1, 0, 10, 0.25, 4, 0.05, "expert", false, "Neutral fallback pending versioned local inventory data.");
        yield return Definition("displacement.arts_entertainment_recreation_inventory_modifier", "displacement", "Arts/entertainment/recreation local inventory modifier", "multiplier", 1, 0, 10, 0.25, 4, 0.05, "expert", false, "Neutral fallback pending versioned local inventory data.");
        yield return Definition("displacement.restaurant_hospitality_annual_sales_per_job", "displacement", "Restaurant/hospitality annual sales per job", "USD/job/year", 1_000_000, 1, 100_000_000, 1, null, 1_000, "expert", false, "Conservative placeholder requiring local economic-data calibration.");
        yield return Definition("displacement.retail_annual_sales_per_job", "displacement", "Retail annual sales per job", "USD/job/year", 1_000_000, 1, 100_000_000, 1, null, 1_000, "expert", false, "Conservative placeholder requiring local economic-data calibration.");
        yield return Definition("displacement.arts_entertainment_recreation_annual_sales_per_job", "displacement", "Arts/entertainment/recreation annual sales per job", "USD/job/year", 1_000_000, 1, 100_000_000, 1, null, 1_000, "expert", false, "Conservative placeholder requiring local economic-data calibration.");
        yield return Definition("employment.direct_jobs_per_million_ggr", "employment", "Direct casino jobs per million GGR", "jobs/USD million", 0, 0, 1000, 0, null, 0.1, "advanced", false, "Zero selects an automatic regulator-observed employment/GGR benchmark when a complete linked sample exists; otherwise it remains a zero-safe fallback. A positive versioned value explicitly supersedes the observed benchmark.");
        yield return Definition("employment.construction_job_years_per_million_capital_cost", "employment", "Construction job-years per million capital cost", "job-years/USD million", 0, 0, 1000, 0, null, 0.1, "advanced", false, "Zero-safe fallback pending a construction input-output assumption set.");
        yield return Definition("employment.indirect_induced_jobs_per_direct_job", "employment", "Indirect/induced jobs per direct job", "jobs/job", 0, 0, 100, 0, null, 0.01, "advanced", false, "Zero-safe fallback pending regional input-output multipliers.");
        yield return Definition("employment.incumbent_jobs_per_million_lost_ggr", "employment", "Incumbent jobs per million lost GGR", "jobs/USD million", 0, 0, 1000, 0, null, 0.1, "expert", false, "Zero selects the same complete regulator-observed employment/GGR benchmark used for direct jobs; otherwise it remains zero-safe. A positive versioned value explicitly supersedes the observed benchmark.");
        yield return Definition("employment.direct_average_annual_wage", "employment", "Direct casino average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("employment.indirect_average_annual_wage", "employment", "Indirect/induced average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "expert", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("employment.incumbent_average_annual_wage", "employment", "Incumbent casino average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "expert", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("fiscal.non_gaming_business_margin", "fiscal", "Non-gaming business income margin", "share", 0, 0, 1, 0, 1, 0.01, "expert", false, "Zero-safe fallback pending a jurisdiction/property operating assumption.");
        yield return Definition("social_cost.prevalence", "social-cost", "Problem-gambling prevalence", "share", 0, 0, 1, 0, 0.20, 0.001, "advanced", false, "Zero-safe system fallback. When no parameter set or user override supplies this key, runtime may select one validated effective jurisdiction prevalence rule; any explicit resolved value, including zero, supersedes that evidence rule.");
        yield return Definition("social_cost.exposure_response", "social-cost", "Exposure risk-response", "coefficient", 0, 0, 100, 0, null, 0.01, "expert", false, "Zero-safe fallback pending calibration.");
        yield return Definition("social_cost.per_case_cost", "social-cost", "Social cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback until included domains and source year are selected.");
        yield return Definition("social_cost.crime_public_safety_productivity_scale", "social-cost", "Crime/public-safety/productivity scale", "multiplier", 1, 0, 10, 0, 4, 0.05, "expert", false, "Neutral scaling prior; component overlap must be resolved before activation.");
        yield return Definition("social_cost.low_case_multiplier", "social-cost", "Low social-cost case multiplier", "multiplier", 0.75, 0, 10, 0, 2, 0.05, "advanced", false, "Transparent uncertainty prior requiring study-specific bounds.");
        yield return Definition("social_cost.high_case_multiplier", "social-cost", "High social-cost case multiplier", "multiplier", 1.25, 0, 10, 0, 4, 0.05, "advanced", false, "Transparent uncertainty prior requiring study-specific bounds.");
        yield return Definition("social_cost.treatment_health_per_case", "social-cost", "Treatment and health cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
        yield return Definition("social_cost.bankruptcy_debt_per_case", "social-cost", "Bankruptcy and debt-stress cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
        yield return Definition("social_cost.crime_public_safety_per_case", "social-cost", "Crime and public-safety cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
        yield return Definition("social_cost.productivity_employment_per_case", "social-cost", "Productivity and employment loss per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
        yield return Definition("social_cost.family_household_per_case", "social-cost", "Family and household cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
        yield return Definition("social_cost.public_assistance_administration_per_case", "social-cost", "Public-assistance and administrative cost per case", "USD/case/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending a nonoverlapping source estimate.");
    }

    private static ModelParameterDefinition Definition(
        string key,
        string category,
        string displayName,
        string units,
        double fallback,
        double? hardMinimum,
        double? hardMaximum,
        double? recommendedMinimum,
        double? recommendedMaximum,
        double step,
        string exposure,
        bool calibrated,
        string provenance) => new()
        {
            Key = key,
            Category = category,
            DisplayName = displayName,
            TechnicalDescription = $"'{key}' is the {units} input consumed by the {category} module for {displayName.ToLowerInvariant()}.",
            PlainLanguageDescription = PlainLanguageInterpretation(key, displayName, units),
            Units = units,
            DataType = "number",
            SystemDefaultValue = fallback,
            ComputationalMinimum = hardMinimum,
            ComputationalMaximum = hardMaximum,
            RecommendedMinimum = recommendedMinimum,
            RecommendedMaximum = recommendedMaximum,
            UiStep = step,
            UiExposureLevel = exposure,
            IsUserOverridable = true,
            ModelVersionApplicability = "gravity-v1",
            ProvenanceNotes = provenance,
            IsCalibrated = calibrated,
            IsActive = true
        };

    private static string PlainLanguageInterpretation(string key, string displayName, string units) => key switch
    {
        "gravity.beta" => "Controls how sharply casino attraction falls as routed drive time increases. Higher values make the modeled market more local.",
        "gravity.alpha" => "Controls how strongly differences in facility attraction affect patron choice. Higher values favor larger or more attractive facilities more strongly.",
        "gravity.outside_option_weight" => "Represents relevant gaming supply or leakage that is not explicitly modeled as a named facility.",
        "demand.gaming_income_share" => "Sets the share of adjusted gross income available to the resident gaming-demand pool before facility allocation.",
        "demand.income_elasticity" => "Controls how gaming demand changes with origin income relative to the model reference level.",
        "demand.regional_intensity_multiplier" => "Scales resident gaming demand for the selected market while preserving the same origin-level allocation equations.",
        "market_expansion.accessibility_elasticity" => "Controls how much resident gaming demand may expand when the project improves routed access to gaming.",
        "tourism.capture_rate" => "Sets the share of eligible visiting participants captured by the proposed facility after resident overlap is removed.",
        "traffic.intercept_rate" => "Sets the share of eligible through-travelers intercepted by the proposed facility after resident and tourism overlap is removed.",
        "displacement.local_patron_share" => "Sets the modeled share of project patrons treated as local for displacement accounting.",
        "displacement.eligible_base_share" => "Limits displacement to the economically eligible local resident revenue base after transfers, tourism, and traffic are excluded.",
        "displacement.coefficient" => "Sets the share of the eligible local resident base expected to displace spending in other local sectors.",
        "social_cost.prevalence" => "Sets the modeled share of the exposed population experiencing the defined problem-gambling condition.",
        "ramp.first_year_share" => "Sets first-year project revenue as a share of stabilized annual revenue.",
        "ramp.second_year_share" => "Sets second-year project revenue as a share of stabilized annual revenue.",
        _ when key.EndsWith("_coefficient", StringComparison.Ordinal) =>
            $"Controls the marginal effect of {displayName.ToLowerInvariant()}. Positive values increase the associated modeled result; negative values reduce it.",
        _ when key.Contains("reference_", StringComparison.Ordinal) =>
            $"Defines the normalization reference for {displayName.ToLowerInvariant()} so structural attraction remains interpretable.",
        _ when units is "share" or "rate" =>
            $"Sets {displayName.ToLowerInvariant()} as a proportion from zero to one unless the hard bounds state otherwise.",
        _ when units is "multiplier" or "scale" =>
            $"Scales {displayName.ToLowerInvariant()}; a value of one is neutral unless the provenance note states otherwise.",
        _ when units is "indicator" =>
            $"Turns {displayName.ToLowerInvariant()} off at zero and on at one.",
        _ => $"Sets {displayName.ToLowerInvariant()} in {units} for the authoritative backend model."
    };

    private static async Task<Jurisdiction> GetOrCreateJurisdictionAsync(
        AppDbContext db,
        string code,
        string name,
        string kind,
        int? parentJurisdictionId,
        CancellationToken cancellationToken)
    {
        var existing = await db.Jurisdictions.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var jurisdiction = new Jurisdiction
        {
            Code = code,
            Name = name,
            Kind = kind,
            ParentJurisdictionId = parentJurisdictionId
        };
        db.Jurisdictions.Add(jurisdiction);
        await db.SaveChangesAsync(cancellationToken);
        return jurisdiction;
    }

    private static async Task AddRuleIfMissingAsync<T>(
        AppDbContext db,
        int jurisdictionId,
        string ruleType,
        T payload,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string validationState,
        string sourceUrl,
        string provenanceNotes,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var exists = await db.JurisdictionRules.AnyAsync(
            rule => rule.JurisdictionId == jurisdictionId &&
                    rule.RuleType == ruleType &&
                    rule.RuleValueJson == payloadJson &&
                    rule.EffectiveFrom == effectiveFrom &&
                    rule.EffectiveTo == effectiveTo &&
                    rule.SourceUrl == sourceUrl,
            cancellationToken);
        if (exists)
        {
            return;
        }

        db.JurisdictionRules.Add(new JurisdictionRule
        {
            JurisdictionId = jurisdictionId,
            RuleType = ruleType,
            RuleValueJson = payloadJson,
            ValidationState = validationState,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            SourceUrl = sourceUrl,
            ProvenanceNotes = provenanceNotes
        });
    }

    private static GamingTaxRecipientPayload PopulationRecipient(
        string recipientKey,
        string recipientLabel,
        int population,
        int countyPopulation) =>
        new(
            recipientKey,
            recipientLabel,
            GamingTaxRecipientScopeKinds.Municipality,
            (decimal)population / countyPopulation);

    private static async Task<ModelParameterSet> GetOrCreateParameterSetAsync(
        AppDbContext db,
        string key,
        string name,
        string scope,
        int? jurisdictionId,
        string? marketCode,
        string? scenarioKind,
        string version,
        string calibrationNotes,
        CancellationToken cancellationToken)
    {
        var existing = await db.ModelParameterSets.SingleOrDefaultAsync(
            set => set.Key == key && set.Version == version,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var parameterSet = new ModelParameterSet
        {
            Key = key,
            Name = name,
            Scope = scope,
            JurisdictionId = jurisdictionId,
            MarketCode = marketCode,
            ScenarioKind = scenarioKind,
            Version = version,
            ModelVersionApplicability = "gravity-v1",
            CalibrationNotes = calibrationNotes
        };
        db.ModelParameterSets.Add(parameterSet);
        await db.SaveChangesAsync(cancellationToken);
        return parameterSet;
    }

    private static async Task AddParameterValuesIfMissingAsync(
        AppDbContext db,
        long parameterSetId,
        IReadOnlyDictionary<string, ModelParameterDefinition> definitions,
        IReadOnlyDictionary<string, double> values,
        CancellationToken cancellationToken)
    {
        var existingIds = await db.ModelParameterSetValues
            .Where(value => value.ParameterSetId == parameterSetId)
            .Select(value => value.ParameterDefinitionId)
            .ToHashSetAsync(cancellationToken);
        foreach (var (key, value) in values)
        {
            var definition = definitions[key];
            if (existingIds.Contains(definition.Id))
            {
                continue;
            }

            db.ModelParameterSetValues.Add(new ModelParameterSetValue
            {
                ParameterSetId = parameterSetId,
                ParameterDefinitionId = definition.Id,
                Value = value,
                ProvenanceNotes = "Seeded with the immutable parameter-set version."
            });
        }
    }
}
