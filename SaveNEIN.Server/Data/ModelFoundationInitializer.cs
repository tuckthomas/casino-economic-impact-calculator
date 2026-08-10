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
        "017_nullable_facility_evidence_flags.sql"
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
            new GamingAgeRulePayload("racino", 21),
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
                "commercial-casino-standard",
                "Indiana taxable AGR",
                [
                    new GamingTaxBracketPayload(25_000_000m, 0.10m),
                    new GamingTaxBracketPayload(50_000_000m, 0.20m),
                    new GamingTaxBracketPayload(75_000_000m, 0.25m),
                    new GamingTaxBracketPayload(150_000_000m, 0.30m),
                    new GamingTaxBracketPayload(600_000_000m, 0.35m),
                    new GamingTaxBracketPayload(null, 0.40m)
                ]),
            new DateOnly(2024, 7, 1),
            new DateOnly(2025, 6, 30),
            JurisdictionRuleValidationStates.Provisional,
            "https://www.in.gov/igc/files/FY2025-Annual.pdf",
            "FY2025 IGC schedule retained as a validation fixture only. It is intentionally not eligible for production fiscal calculation until independently validated and superseded by an effective current rule.",
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
        yield return Definition("facility.gaming_positions_coefficient", "facility-attraction", "Gaming positions coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.table_games_coefficient", "facility-attraction", "Table games coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.hotel_rooms_coefficient", "facility-attraction", "Hotel rooms coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.entertainment_capacity_coefficient", "facility-attraction", "Entertainment capacity coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.capital_scale_coefficient", "facility-attraction", "Development capital coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.highway_access_coefficient", "facility-attraction", "Direct highway access coefficient", "coefficient", 0, -10, 10, null, null, 0.01, "expert", false, "Inactive-neutral structural prior pending calibration.");
        yield return Definition("facility.reference_gaming_positions", "facility-attraction", "Reference gaming positions", "positions", 2000, 1, 100000, 500, 5000, 50, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_table_games", "facility-attraction", "Reference table games", "tables", 50, 0, 10000, 10, 200, 5, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_hotel_rooms", "facility-attraction", "Reference hotel rooms", "rooms", 250, 0, 100000, 0, 1000, 25, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_entertainment_capacity", "facility-attraction", "Reference entertainment capacity", "people", 1000, 0, 1000000, 0, 10000, 100, "expert", false, "Interpretability reference facility; calibrate with the selected property sample.");
        yield return Definition("facility.reference_capital_cost", "facility-attraction", "Reference development capital", "USD", 500000000, 1, 100000000000, 100000000, 2000000000, 10000000, "expert", false, "Interpretability reference in constant dollars; dollar-year normalization remains required.");
        yield return Definition("facility.reference_highway_access", "facility-attraction", "Reference direct highway access", "indicator", 1, 0, 1, 0, 1, 1, "expert", false, "Binary interpretability reference for direct limited-access interchange availability.");
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
        yield return Definition("capacity.diagnostic_enabled", "capacity", "Capacity diagnostic enabled", "indicator", 0, 0, 1, 0, 1, 1, "expert", false, "Disabled until validated productivity benchmarks are supplied.");
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
        yield return Definition("employment.direct_jobs_per_million_ggr", "employment", "Direct casino jobs per million GGR", "jobs/USD million", 0, 0, 1000, 0, null, 0.1, "advanced", false, "Zero-safe fallback pending comparable facility and wage data.");
        yield return Definition("employment.construction_job_years_per_million_capital_cost", "employment", "Construction job-years per million capital cost", "job-years/USD million", 0, 0, 1000, 0, null, 0.1, "advanced", false, "Zero-safe fallback pending a construction input-output assumption set.");
        yield return Definition("employment.indirect_induced_jobs_per_direct_job", "employment", "Indirect/induced jobs per direct job", "jobs/job", 0, 0, 100, 0, null, 0.01, "advanced", false, "Zero-safe fallback pending regional input-output multipliers.");
        yield return Definition("employment.incumbent_jobs_per_million_lost_ggr", "employment", "Incumbent jobs per million lost GGR", "jobs/USD million", 0, 0, 1000, 0, null, 0.1, "expert", false, "Zero-safe fallback pending comparable incumbent staffing evidence.");
        yield return Definition("employment.direct_average_annual_wage", "employment", "Direct casino average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "advanced", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("employment.indirect_average_annual_wage", "employment", "Indirect/induced average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "expert", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("employment.incumbent_average_annual_wage", "employment", "Incumbent casino average annual wage", "USD/job/year", 0, 0, 1_000_000, 0, null, 100, "expert", false, "Zero-safe fallback pending geography-specific wage evidence.");
        yield return Definition("fiscal.non_gaming_business_margin", "fiscal", "Non-gaming business income margin", "share", 0, 0, 1, 0, 1, 0.01, "expert", false, "Zero-safe fallback pending a jurisdiction/property operating assumption.");
        yield return Definition("social_cost.prevalence", "social-cost", "Problem-gambling prevalence", "share", 0, 0, 1, 0, 0.20, 0.001, "advanced", false, "Zero-safe fallback until a location-appropriate source is selected.");
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
            TechnicalDescription = $"Versioned model parameter '{key}'.",
            PlainLanguageDescription = displayName,
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
        var exists = await db.JurisdictionRules.AnyAsync(
            rule => rule.JurisdictionId == jurisdictionId &&
                    rule.RuleType == ruleType &&
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
            RuleValueJson = JsonSerializer.Serialize(payload, JsonOptions),
            ValidationState = validationState,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            SourceUrl = sourceUrl,
            ProvenanceNotes = provenanceNotes
        });
    }

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
