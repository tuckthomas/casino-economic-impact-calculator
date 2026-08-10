-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 014_indiana_benchmark_evidence.sql
-- Source-extracted Indiana public benchmark evidence. These values are validation anchors,
-- not coefficients or forced model targets. Source files are identified by SHA-256 checksum.

UPDATE benchmark_studies
SET
    development_program_json = '{
      "candidateLocationDescription":"Northeast proxy at I-69 and State Road 8 in DeKalb County",
      "candidateLocationPrecision":"published intersection proxy; not a proposed parcel",
      "developmentProgramPublished":false
    }'::jsonb,
    reported_outputs_json = '{
      "schemaVersion":"1.0",
      "currency":"USD",
      "stabilizedAnnual":{
        "estimatedGrossGamingRevenuePotential":219900000,
        "currentIndianaTheoreticalCapture":14670000,
        "retainedByMichiganCasinos":15600000,
        "proposedFacilityAdjustedGrossReceipts":204300000,
        "stateGamingTaxReceipts":61066000
      },
      "revenueShift":{
        "existingIndianaCommercialCasinoTheoreticalValueShift":14670000
      },
      "tribalCasinoSensitivity":{
        "proposedFacilityAdjustedGrossReceipts":43230000,
        "commercialGamingTaxReceipts":6146000,
        "revenueDivertedToMiamiNation":161070000
      },
      "tourismDefinitionSensitivity":{
        "outsideHostCountyRevenueShare":0.95,
        "coreMarketDriveTimeMinutes":90
      }
    }'::jsonb,
    reported_assumptions_json = '{
      "schemaVersion":"1.0",
      "demandMethod":"ZIP-level adjusted gross income market potential reconciled with aggregated rated-play data",
      "nationalCasinoRevenueToAgiPrior":0.0058,
      "indianaCasinoRevenueToAgiPrior":0.0066,
      "publishedDriveTimeBandsMinutes":[15,30,60],
      "trackedPlayDataAvailableToStudy":true,
      "trackedPlayDataAvailableToSaveNein":false,
      "methodologicalAnchors":["ZIP-level adjusted gross income","network drive-time capture","operator rated-play aggregation"]
    }'::jsonb,
    methodological_notes = 'Spectrum output is a market-potential and relocation benchmark. It benefits from confidential, aggregated operator rated-play data that SaveNEIN does not possess. Do not force calibration to equality.',
    source_file_checksum = '915F30300F5240252D020FF3F7E91A734982C5E18D4B7DCF25EDD4C2F05B27F6',
    provenance_notes = 'Primary PDF downloaded from the registered Indiana Gaming Commission URL. Values extracted from PDF pages iii-v and 16-25; the page iii tables and figures were visually verified on 2026-08-09. Monetary values are nominal published dollars.',
    validation_state = 'extracted'
WHERE benchmark_key = 'spectrum-in-relocation-2025';

UPDATE benchmark_studies
SET
    development_program_json = '{
      "candidateLocationDescription":"Greater Fort Wayne / Allen County interstate-access proxy",
      "candidateLocationPrecision":"published proxy; forecast described as broadly applicable within Allen County",
      "anticipatedInvestment":500000000,
      "hotelRooms":200,
      "eventCenterIncluded":true,
      "stabilizedYear":3
    }'::jsonb,
    reported_outputs_json = '{
      "schemaVersion":"1.0",
      "currency":"USD",
      "stabilizedAnnual":{
        "grossGamingRevenue":282300000,
        "nonGamingRevenue":48400000,
        "totalRevenue":330700000,
        "wageringTax":79400000,
        "supplementalWageringTax":8200000,
        "totalGamingTaxes":87600000,
        "nonGamingTaxes":19800000,
        "constructionJobs":5520,
        "operationsJobs":2001
      },
      "rampGrossGamingRevenue":{
        "year1":245600000,
        "year2":268200000,
        "year3":282300000,
        "year4":289500000,
        "year5":296900000
      },
      "competitorGrossGamingRevenueChanges":{
        "FireKeepersCasinoHotel":-35500000,
        "HollywoodCasinoToledo":-9100000,
        "HollywoodGamingAtDaytonRaceway":-3800000,
        "HollywoodColumbus":-1700000,
        "FourWindsCasinoSouthBend":-11600000,
        "HarrahsHoosierParkCasino":-8000000
      },
      "incrementalIndianaCommercialCasinoGrossGamingRevenue":274300000,
      "repatriatedFromMichigan":35500000,
      "repatriatedFromOhio":14700000,
      "distributionAtStabilization":{
        "Indiana":66300000,
        "FortWayne":25000000,
        "AllenCounty":1700000,
        "otherGoverningBodiesAndStakeholders":14500000,
        "total":107400000
      }
    }'::jsonb,
    reported_assumptions_json = '{
      "schemaVersion":"1.0",
      "demandMethod":"gravity model triangulated with an independent comparable-market regression",
      "localGravityGrossGamingRevenue":216700000,
      "localRegressionGrossGamingRevenue":215000000,
      "year1ShareOfStabilizedGrossGamingRevenue":0.87,
      "separateTrafficInterceptDemand":true,
      "methodologicalAnchors":["gravity model","traffic intercept","development program","ramp analysis","comparable-market regression"]
    }'::jsonb,
    methodological_notes = 'CBRE combines local gravity demand, out-of-market highway traffic, development-program uplift, ramp assumptions, and an independent regression sense-check. The published project program differs from other Indiana benchmarks.',
    source_file_checksum = '1A00F19766BA0361D4E8A6514D32701727BEDEFCADA73CCFA90729DB8107A510',
    provenance_notes = 'Primary PDF downloaded from the registered publication URL. Values extracted from PDF pages 2, 5-7, and 9-12; the executive-summary table on page 2 was visually verified on 2026-08-09. Monetary values are nominal published dollars.',
    validation_state = 'extracted'
WHERE benchmark_key = 'cbre-union-gaming-fort-wayne-2025';

UPDATE benchmark_studies
SET
    study_date = DATE '2026-03-05',
    development_program_json = '{
      "candidateLocationDescription":"Proposed destination-scale casino development in Steuben County, Indiana",
      "candidateLocationPrecision":"county-level public benchmark scenario",
      "minimumInvestment":500000000,
      "minimumPhaseOneShare":0.60,
      "stabilizedProjectionYear":2030
    }'::jsonb,
    reported_outputs_json = '{
      "schemaVersion":"1.0",
      "currency":"USD",
      "stabilized2030GrossGamingRevenue":{
        "low":188600000,
        "base":203100000,
        "high":214000000
      },
      "residentGrossGamingRevenue":{
        "low":181500000,
        "base":194500000,
        "high":202200000
      },
      "inducedLakeTourismGrossGamingRevenue":{
        "low":7100000,
        "base":8600000,
        "high":11800000
      },
      "stateOfResidenceShares":{"Michigan":0.48,"Indiana":0.38,"Ohio":0.14},
      "annualStateGamingTaxRevenue":{
        "low":56347601,
        "base":61939322,
        "high":66142515
      },
      "estimatedAnnualCountyRevenue":{
        "low":14124088,
        "base":15524883,
        "high":16577834
      },
      "directFullTimeEquivalentJobsRange":{"low":800,"high":1200},
      "annualPayrollRange":{"low":36000000,"high":72000000}
    }'::jsonb,
    reported_assumptions_json = '{
      "schemaVersion":"1.0",
      "demandMethod":"mass-weighted gravity model using ZIP-level eligible-adult population, income adjustment, travel friction, and incumbent 2024 GGR competitive mass",
      "eligibleAge":21,
      "perAdultExpenditureRange":{"low":350,"high":390},
      "distanceDecayBeta":1.5,
      "distanceDecaySensitivity":{"low":1.4,"high":1.6},
      "maximumResidentTradeAreaMinutes":120,
      "competitorMassBasis":"2024 observed GGR",
      "separateTourismDemand":true,
      "methodologicalAnchors":["eligible adult population","income-adjusted expenditure","network travel decay","observed GGR competitive mass","tourism"]
    }'::jsonb,
    methodological_notes = 'Steinberg publishes a low/base/high sensitivity suite with a fixed beta prior, broad competitive field, observed-GGR mass, and separately identified lake-tourism demand. Beta 1.5 and the revenue outputs are priors and benchmark targets, not universal constants.',
    source_file_checksum = '68D62E0EABA0619197DE14F3E24C484132CDD4CF73390F48CAA2C563C89A7E1E',
    provenance_notes = 'Primary PDF downloaded from the registered Steuben County EDC URL. Values extracted from PDF pages 3-6 and 46-60; the low/base/high table on page 4 was visually verified on 2026-08-09. Monetary values are nominal published dollars.',
    validation_state = 'extracted'
WHERE benchmark_key = 'steinberg-steuben-feasibility';

DO $$
BEGIN
    IF (SELECT COUNT(*) FROM benchmark_studies WHERE validation_state = 'extracted') < 3 THEN
        RAISE EXCEPTION 'Expected all three seeded Indiana benchmark studies to be source-extracted';
    END IF;
END;
$$;
