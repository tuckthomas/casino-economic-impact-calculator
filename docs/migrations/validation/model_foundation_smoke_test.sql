\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    required_table TEXT;
BEGIN
    FOREACH required_table IN ARRAY ARRAY[
        'jurisdictions',
        'jurisdiction_rules',
        'model_parameter_definitions',
        'model_parameter_sets',
        'model_parameter_set_values',
        'model_runs',
        'model_run_parameter_values',
        'model_run_parameter_set_references',
        'data_sources',
        'dataset_snapshots',
        'model_run_dataset_snapshot_references',
        'origin_zones',
        'origin_zone_age_bins',
        'origin_zone_income_periods',
        'casino_competitor_history',
        'casino_gaming_revenue_periods',
        'development_programs',
        'origin_facility_travel',
        'model_run_origin_results',
        'model_run_facility_results',
        'model_run_origin_facility_allocations',
        'tourism_market_observations',
        'traffic_corridor_observations',
        'local_economic_sector_observations',
        'model_run_demand_components',
        'model_run_capacity_diagnostics',
        'model_run_ramp_results',
        'model_run_geographic_accounting',
        'model_run_sector_displacement',
        'model_run_employment_impacts',
        'model_run_fiscal_impacts',
        'model_run_social_costs',
        'model_run_net_impacts'
    ]
    LOOP
        IF to_regclass(required_table) IS NULL THEN
            RAISE EXCEPTION 'Required table % is missing.', required_table;
        END IF;
    END LOOP;

    IF (
        SELECT data_type
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'dataset_snapshots'
          AND column_name = 'warnings_json'
    ) <> 'jsonb' THEN
        RAISE EXCEPTION 'dataset_snapshots.warnings_json is not jsonb.';
    END IF;

    IF (
        SELECT postgis_typmod_type(a.atttypmod)
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public'
          AND c.relname = 'origin_zones'
          AND a.attname = 'representative_point'
          AND NOT a.attisdropped
    ) <> 'Point' THEN
        RAISE EXCEPTION 'origin_zones.representative_point is not a PostGIS Point.';
    END IF;
END;
$$;

INSERT INTO data_sources (
    id, name, publisher, url, source_type, geographic_coverage,
    vintage_period, retrieved_at_utc, content_hash, is_authoritative
) VALUES (
    7000000001, 'Migration smoke source', 'Test publisher',
    'https://example.invalid/migration-smoke', 'test', 'test',
    '2026', CURRENT_TIMESTAMP, 'smoke-source-hash', FALSE
);

INSERT INTO dataset_snapshots (
    id, data_source_id, dataset_key, period, row_count, checksum,
    transform_version, validation_state, is_sealed
) VALUES (
    '00000000-0000-0000-0000-000000000701', 7000000001,
    'migration-smoke', '2026', 1, 'smoke-snapshot-hash',
    'smoke-v1', 'pending', FALSE
);

INSERT INTO jurisdictions (id, code, name, kind)
VALUES (700000001, 'TEST-SMOKE', 'Migration smoke jurisdiction', 'test');

INSERT INTO origin_zones (
    id, dataset_snapshot_id, stable_origin_id, origin_type, geography_code,
    country_code, representative_point, area_geometry
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000701',
    'TEST-ZCTA', 'zcta', '00000', 'USA',
    ST_SetSRID(ST_MakePoint(0, 0), 4326),
    ST_SetSRID(ST_MakeEnvelope(-0.1, -0.1, 0.1, 0.1), 4326)
);

UPDATE dataset_snapshots
SET validation_state = 'validated', is_sealed = TRUE
WHERE id = '00000000-0000-0000-0000-000000000701';

-- Prove that stable venue identity can repeat across immutable vintages and
-- that the same observed period can be versioned in separate snapshots.
INSERT INTO dataset_snapshots (
    id, data_source_id, dataset_key, period, row_count, checksum,
    transform_version, validation_state, is_sealed
) VALUES
    ('00000000-0000-0000-0000-000000000704', 7000000001, 'competitors', '2025', 1, 'smoke-competitors-2025', 'smoke-v1', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000705', 7000000001, 'competitors', '2026', 1, 'smoke-competitors-2026', 'smoke-v1', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000706', 7000000001, 'observed-performance', '2025', 1, 'smoke-ggr-v1', 'smoke-v1', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000707', 7000000001, 'observed-performance', '2025', 1, 'smoke-ggr-v2', 'smoke-v2', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000708', 7000000001, 'tourism', '2025', 1, 'smoke-tourism-v1', 'smoke-v1', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000709', 7000000001, 'traffic', '2025', 1, 'smoke-traffic-v1', 'smoke-v1', 'pending', FALSE),
    ('00000000-0000-0000-0000-000000000710', 7000000001, 'local-economic-inventory', '2025', 1, 'smoke-local-economic-v1', 'smoke-v1', 'pending', FALSE);

INSERT INTO casino_competitors (
    id, name, state, stable_venue_id, country_code, dataset_snapshot_id,
    latitude, longitude, venue_type, geom
) VALUES
    (700000002, 'Versioned smoke venue', 'IN', 'smoke-venue', 'USA',
     '00000000-0000-0000-0000-000000000704', 41, -85, 'commercial-casino',
     ST_SetSRID(ST_MakePoint(-85, 41), 4326)),
    (700000003, 'Versioned smoke venue', 'IN', 'smoke-venue', 'USA',
     '00000000-0000-0000-0000-000000000705', 41, -85, 'commercial-casino',
     ST_SetSRID(ST_MakePoint(-85, 41), 4326));

INSERT INTO casino_gaming_revenue_periods (
    id, casino_competitor_id, dataset_snapshot_id, period_start, period_end,
    period_granularity, reported_metric_key, reported_metric_definition,
    reported_amount
) VALUES
    (700000002, 700000002, '00000000-0000-0000-0000-000000000706',
     '2025-01-01', '2025-01-31', 'monthly', 'agr', 'Adjusted gross receipts', 100),
    (700000003, 700000002, '00000000-0000-0000-0000-000000000707',
     '2025-01-01', '2025-01-31', 'monthly', 'agr', 'Adjusted gross receipts', 101);

INSERT INTO tourism_market_observations (
    id, dataset_snapshot_id, stable_observation_id, market_key,
    geography_type, geography_code, period_start, period_end,
    source_metric_kind, source_quantity, normalized_visitor_person_trips,
    normalization_method
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000708',
    'smoke-tourism', 'smoke-market', 'county', '18003',
    '2025-01-01', '2025-12-31', 'visitor-person-trips', 1000, 1000,
    'provider-direct-person-trips'
);

INSERT INTO traffic_corridor_observations (
    id, dataset_snapshot_id, stable_observation_id, route_designation,
    jurisdiction_code, count_location, period_start, period_end,
    annual_average_daily_traffic, observation_days, count_method
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000709',
    'smoke-traffic', 'I-69', 'US-IN',
    ST_SetSRID(ST_MakePoint(-85.14, 41.08), 4326),
    '2025-01-01', '2025-12-31', 50000, 365, 'aadt'
);

INSERT INTO local_economic_sector_observations (
    id, dataset_snapshot_id, stable_observation_id, geography_type,
    geography_code, sector_key, naics_codes_json, period_start, period_end,
    establishments, employment, annual_payroll, annual_receipts_or_sales,
    source_metric_definition
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000710',
    'smoke-local-economic-restaurant', 'host-state', 'US-IN',
    'restaurant-hospitality', '["72"]'::jsonb,
    '2025-01-01', '2025-12-31', 1000, 20000, 500000000, 2000000000,
    'Migration smoke local-sector inventory'
);

UPDATE dataset_snapshots
SET validation_state = 'validated', is_sealed = TRUE
WHERE id IN (
    '00000000-0000-0000-0000-000000000704',
    '00000000-0000-0000-0000-000000000705',
    '00000000-0000-0000-0000-000000000706',
    '00000000-0000-0000-0000-000000000707',
    '00000000-0000-0000-0000-000000000708',
    '00000000-0000-0000-0000-000000000709',
    '00000000-0000-0000-0000-000000000710');

INSERT INTO development_programs (
    id, stable_program_id, version, name, slot_or_vlt_positions,
    table_game_count, stabilized_year_number
) VALUES (
    '00000000-0000-0000-0000-000000000703',
    'migration-smoke-program', '1', 'Migration smoke program', 1000, 25, 3
);

INSERT INTO model_parameter_definitions (
    id, key, category, display_name, technical_description,
    plain_language_description, units, data_type, system_default_value,
    ui_exposure_level, is_user_overridable, model_version_applicability,
    is_calibrated, is_active
) VALUES (
    7000000001, 'smoke.parameter', 'test', 'Smoke parameter',
    'Migration smoke parameter.', 'Migration smoke parameter.', 'unit',
    'number', 1, 'hidden', TRUE, 'gravity-v1', TRUE, TRUE
);

INSERT INTO model_parameter_sets (
    id, key, name, scope, jurisdiction_id, version,
    model_version_applicability, is_immutable
) VALUES (
    7000000001, 'smoke-set', 'Smoke set', 'jurisdiction', 700000001,
    '1', 'gravity-v1', FALSE
);

INSERT INTO model_parameter_set_values (
    id, parameter_set_id, parameter_definition_id, value
) VALUES (7000000001, 7000000001, 7000000001, 1.5);

INSERT INTO model_runs (
    id, model_version, status, jurisdiction_id, base_parameter_set_id,
    development_program_id, resolved_input_json, data_snapshot_references_json,
    candidate_latitude, candidate_longitude
) VALUES (
    '00000000-0000-0000-0000-000000000702', 'gravity-v1', 'draft',
    700000001, 7000000001, '00000000-0000-0000-0000-000000000703',
    '{}'::jsonb, '{}'::jsonb, 0, 0
);

INSERT INTO origin_facility_travel (
    id, origin_zone_id, model_run_id, facility_key, facility_kind,
    routing_graph_hash, costing_profile, travel_time_minutes,
    routed_distance_meters, route_found
) VALUES (
    7000000001, 7000000001, '00000000-0000-0000-0000-000000000702',
    'scenario:smoke', 'scenario', 'smoke-graph', 'auto', 10, 10000, TRUE
);

INSERT INTO model_run_origin_results (
    id, model_run_id, origin_zone_id, demand_specification, resident_demand,
    baseline_outside_share, with_project_outside_share, proposed_resident_ggr,
    host_jurisdiction_capture, external_jurisdiction_capture,
    tribal_or_other_jurisdiction_capture, outside_option_capture
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702', 7000000001,
    'agi-share', 1000, 1, 0.5, 500, 0, 0, 0, 500
);

INSERT INTO model_run_facility_results (
    id, model_run_id, facility_key, facility_kind, is_proposed_facility,
    normalized_attraction, baseline_resident_ggr, with_project_resident_ggr,
    change_in_resident_ggr
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'scenario:smoke', 'scenario', TRUE, 1, 0, 500, 500
);

INSERT INTO model_run_origin_facility_allocations (
    id, model_run_id, origin_zone_id, origin_facility_travel_id,
    facility_key, market_state, capture_source_category,
    is_proposed_facility, network_travel_time_minutes, routed_distance_meters,
    normalized_attraction, origin_facility_modifier, log_weight, share,
    allocated_resident_ggr
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702', 7000000001,
    7000000001, 'scenario:smoke', 'with-project',
    'external-commercial-incumbent', TRUE, 10, 10000, 1, 1, -1, 0.5, 500
);

INSERT INTO model_run_parameter_values (
    id, model_run_id, parameter_definition_id, system_fallback_value,
    default_value, final_value, source_layer
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    7000000001, 1, 1.5, 1.5, 'jurisdiction-market-set'
);

INSERT INTO model_run_parameter_set_references (
    id, model_run_id, parameter_set_id, source_layer
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    7000000001, 'jurisdiction-market-set'
);

INSERT INTO model_run_dataset_snapshot_references (
    id, model_run_id, dataset_snapshot_id, role, reference_key
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    '00000000-0000-0000-0000-000000000701', 'origin-demographics', 'default'
);

INSERT INTO model_run_dataset_snapshot_references (
    id, model_run_id, dataset_snapshot_id, role, reference_key
) VALUES
    (7000000002, '00000000-0000-0000-0000-000000000702',
     '00000000-0000-0000-0000-000000000708', 'tourism', 'visitor-person-trips'),
    (7000000003, '00000000-0000-0000-0000-000000000702',
     '00000000-0000-0000-0000-000000000709', 'traffic', 'corridor-counts');

INSERT INTO model_run_demand_components (
    id, model_run_id, dataset_snapshot_id, component_type, source_record_key,
    method_key, input_quantity, deduplicated_quantity, eligible_quantity,
    participating_quantity, captured_quantity, ggr, details_json
) VALUES
    (7000000001, '00000000-0000-0000-0000-000000000702',
     '00000000-0000-0000-0000-000000000708', 'tourism', 'smoke-tourism',
     'visitor-person-trips-v1', 1000, 900, 800, 80, 20, 2000, '{}'::jsonb),
    (7000000002, '00000000-0000-0000-0000-000000000702',
     '00000000-0000-0000-0000-000000000709', 'traffic', 'smoke-traffic',
     'aadt-intercept-v1', 18250000, 1000, 9000000, 10000, 1000, 50000, '{}'::jsonb);

INSERT INTO model_run_capacity_diagnostics (
    id, model_run_id, facility_key, status, stabilized_ggr,
    plausible_capacity_minimum, plausible_capacity_maximum,
    implied_residual_slot_win_per_unit_day
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'scenario:smoke', 'within-range', 552000, 100000, 1000000, 250
);

INSERT INTO model_run_ramp_results (
    id, model_run_id, facility_key, calendar_year, operating_year_number,
    period_kind, operating_year_fraction, stabilization_share, projected_ggr
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'scenario:smoke', 2028, 0, 'opening-partial-year', 0.5, 0.65, 179400
);

INSERT INTO model_run_geographic_accounting (
    id, model_run_id, scope_kind, scope_code, local_origin_count,
    host_jurisdiction_cannibalization, cross_jurisdiction_capture,
    outside_or_unmodeled_leakage_capture, induced_resident_ggr, tourism_ggr,
    traffic_ggr, transfer_effect_ggr, market_expansion_and_import_ggr,
    stabilized_ggr, local_resident_gaming_base,
    excluded_local_casino_cannibalization,
    excluded_repatriated_or_leaked_resident_ggr,
    remaining_local_resident_gaming_base, local_origin_ids_json
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 1, 100000, 200000, 50000, 100000,
    2000, 50000, 350000, 152000, 502000, 450000, 100000, 250000,
    100000, '["smoke-origin"]'::jsonb
);

INSERT INTO model_run_sector_displacement (
    id, model_run_id, scope_kind, scope_code, sector_key, normalized_weight,
    displacement_eligible_base, displacement_coefficient, displaced_sales,
    displaced_taxable_sales, displaced_business_income, sales_tax_loss,
    business_income_tax_loss, displaced_jobs
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 'restaurant-hospitality', 1, 75000, 0.5,
    37500, 30000, 7500, 2100, 375, 0.5
);

INSERT INTO model_run_employment_impacts (
    id, model_run_id, scope_kind, scope_code, direct_casino_jobs,
    construction_job_years, indirect_and_induced_jobs, displaced_sector_jobs,
    incumbent_casino_jobs_lost, net_permanent_jobs, direct_labor_income,
    indirect_labor_income, incumbent_labor_income_lost
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 100, 500, 50, 10, 20, 120,
    5000000, 2000000, 900000
);

INSERT INTO model_run_fiscal_impacts (
    id, model_run_id, scope_kind, scope_code, gross_gaming_tax,
    host_local_gross_public_revenue, host_state_gross_public_revenue,
    displaced_local_fiscal_loss, host_incumbent_gaming_tax_loss,
    other_jurisdiction_gaming_tax_loss, net_host_local_fiscal_impact,
    net_host_state_fiscal_impact, other_jurisdiction_fiscal_impact,
    rule_provenance_json
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 50000, 15000, 40000, 2475, 10000, 5000,
    12525, 30000, -5000, '{"fixture":true}'::jsonb
);

INSERT INTO model_run_social_costs (
    id, model_run_id, scope_kind, scope_code, domain_key,
    exposed_eligible_population, incremental_cases, per_case_cost,
    annual_cost, low_annual_cost, high_annual_cost, included
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 'treatment-health', 32000, 64, 1000,
    64000, 48000, 80000, TRUE
);

INSERT INTO model_run_net_impacts (
    id, model_run_id, scope_kind, scope_code, gross_property_ggr,
    transfer_effect_ggr, cross_jurisdiction_imported_ggr,
    outside_or_unmodeled_leakage_capture, induced_resident_ggr,
    tourism_and_traffic_import_ggr, local_discretionary_displacement,
    direct_and_indirect_labor_income, net_host_local_fiscal_impact,
    net_host_state_fiscal_impact, gross_social_cost,
    net_new_local_gaming_activity, net_host_local_impact,
    net_host_state_impact, accounting_method_key
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000702',
    'host-state', 'US-IN', 502000, 350000, 200000, 50000, 100000,
    52000, 37500, 7000000, 12525, 30000, 64000,
    314500, 263025, 280500, 'explicit-cash-flow-bridge-v1'
);

UPDATE model_parameter_sets
SET is_immutable = TRUE
WHERE id = 7000000001;

UPDATE model_runs
SET status = 'finalized', finalized_at_utc = CURRENT_TIMESTAMP
WHERE id = '00000000-0000-0000-0000-000000000702';

INSERT INTO validation_cases (
    id, case_key, name, market_code, jurisdiction_code, case_kind,
    dataset_partition, model_run_id, observed_revenue, observed_metric_key,
    observed_metric_definition, training_period_start, training_period_end,
    inclusion_rules_json, predictor_values_json, execution_request_json
) VALUES (
    '00000000-0000-0000-0000-000000000704', 'fixture-backtest', 'Fixture back-test',
    'US-IN-FIXTURE', 'US-IN', 'incumbent-backtest', 'training',
    '00000000-0000-0000-0000-000000000702', 500000, 'ggr',
    'Fixture stabilized gross gaming revenue', DATE '2025-01-01', DATE '2025-12-31',
    '{"fixture":true}'::jsonb, '{"accessible-population":32000}'::jsonb, '{}'::jsonb
);

INSERT INTO validation_evaluations (
    id, evaluation_key, version, model_version, objective_function, status,
    inclusion_rules_json, selected_parameters_json, training_metrics_json,
    holdout_metrics_json, benchmark_metrics_json, comparable_model_json,
    comparable_training_metrics_json, comparable_holdout_metrics_json,
    comparable_benchmark_metrics_json, training_case_count, holdout_case_count,
    benchmark_case_count
) VALUES (
    '00000000-0000-0000-0000-000000000705', 'fixture-evaluation', '1', 'gravity-v1',
    'smape', 'draft', '{"fixture":true}'::jsonb, '{"gravity.beta":1.5}'::jsonb,
    '{"observationCount":1}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
    '{}'::jsonb, '{}'::jsonb, 1, 0, 0
);

INSERT INTO validation_case_results (
    id, validation_evaluation_id, validation_case_id, model_run_id, prediction_kind,
    dataset_partition, observed_revenue, predicted_revenue, residual,
    absolute_percentage_error, symmetric_absolute_percentage_error, diagnostics_json
) VALUES (
    7000000001, '00000000-0000-0000-0000-000000000705',
    '00000000-0000-0000-0000-000000000704', '00000000-0000-0000-0000-000000000702',
    'gravity', 'training', 500000, 502000, 2000, 0.4, 0.3992015968,
    '{"fixture":true}'::jsonb
);

UPDATE validation_evaluations
SET status = 'finalized', is_immutable = TRUE, finalized_at_utc = CURRENT_TIMESTAMP
WHERE id = '00000000-0000-0000-0000-000000000705';

INSERT INTO model_run_report_artifacts (
    id, model_run_id, template_version, presentation_options_json,
    presentation_options_hash, report_model_json, report_model_hash,
    html_content, html_content_hash, pdf_content, pdf_content_hash,
    csv_content, csv_content_hash, generated_at_utc, is_immutable
) VALUES (
    '00000000-0000-0000-0000-000000000706',
    '00000000-0000-0000-0000-000000000702', 'professional-v1', '{}'::jsonb,
    repeat('a', 64), '{"runId":"00000000-0000-0000-0000-000000000702"}'::jsonb, repeat('b', 64),
    '<html>fixture</html>', repeat('c', 64), decode('25504446', 'hex'), repeat('d', 64),
    'table,key,value', repeat('e', 64), CURRENT_TIMESTAMP, TRUE
);

DO $$
BEGIN
    BEGIN
        UPDATE dataset_snapshots
        SET row_count = 2
        WHERE id = '00000000-0000-0000-0000-000000000701';
        RAISE EXCEPTION 'Snapshot immutability trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Snapshot immutability trigger did not reject an update.' OR
           position('Dataset snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE origin_zones
        SET geography_code = '99999'
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Sealed snapshot row trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Sealed snapshot row trigger did not reject an update.' OR
           position('Rows belonging to a sealed dataset snapshot are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE casino_competitors
        SET name = 'Changed venue'
        WHERE id = 700000002;
        RAISE EXCEPTION 'Sealed competitor snapshot trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Sealed competitor snapshot trigger did not reject an update.' OR
           position('Rows belonging to a sealed dataset snapshot are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE data_sources
        SET name = 'Changed source'
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Referenced-source immutability trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Referenced-source immutability trigger did not reject an update.' OR
           position('source cited by a dataset snapshot is immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_parameter_set_values
        SET value = 2
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Parameter-set immutability trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Parameter-set immutability trigger did not reject an update.' OR
           position('immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_parameter_sets
        SET name = 'Changed set'
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Parameter-set metadata trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Parameter-set metadata trigger did not reject an update.' OR
           position('immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_runs
        SET warning_summary = 'Changed run'
        WHERE id = '00000000-0000-0000-0000-000000000702';
        RAISE EXCEPTION 'Finalized-run trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized-run trigger did not reject an update.' OR
           position('Finalized model run' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_parameter_values
        SET final_value = 2
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized parameter-snapshot trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized parameter-snapshot trigger did not reject an update.' OR
           position('Finalized model-run snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_dataset_snapshot_references
        SET reference_key = 'changed'
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized dataset-reference trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized dataset-reference trigger did not reject an update.' OR
           position('Finalized model-run snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_origin_results
        SET proposed_resident_ggr = 600
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized origin-result trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized origin-result trigger did not reject an update.' OR
           position('Finalized model-run snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_demand_components
        SET ggr = 3000
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized demand-component trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized demand-component trigger did not reject an update.' OR
           position('Finalized model-run snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE local_economic_sector_observations
        SET employment = 1
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Sealed local-economic observation trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Sealed local-economic observation trigger did not reject an update.' OR
           position('Rows belonging to a sealed dataset snapshot are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_net_impacts
        SET net_host_local_impact = 0
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized net-impact trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized net-impact trigger did not reject an update.' OR
           position('Finalized model-run snapshots are immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE development_programs
        SET name = 'Changed program'
        WHERE id = '00000000-0000-0000-0000-000000000703';
        RAISE EXCEPTION 'Development-program immutability trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Development-program immutability trigger did not reject an update.' OR
           position('immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE validation_case_results
        SET predicted_revenue = 0
        WHERE id = 7000000001;
        RAISE EXCEPTION 'Finalized validation-result trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Finalized validation-result trigger did not reject an update.' OR
           position('immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;

    BEGIN
        UPDATE model_run_report_artifacts
        SET html_content = 'changed'
        WHERE id = '00000000-0000-0000-0000-000000000706';
        RAISE EXCEPTION 'Stored-report immutability trigger did not reject an update.';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM = 'Stored-report immutability trigger did not reject an update.' OR
           position('immutable' IN SQLERRM) = 0 THEN
            RAISE;
        END IF;
    END;
END;
$$;

ROLLBACK;

SELECT 'model foundation migration smoke test passed' AS result;
