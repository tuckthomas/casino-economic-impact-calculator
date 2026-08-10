-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 010_tourism_traffic_capacity_ramp.sql
-- Versioned tourism/traffic observations and immutable nonresident/capacity/ramp outputs.

CREATE TABLE IF NOT EXISTS tourism_market_observations (
    id BIGSERIAL PRIMARY KEY,
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    stable_observation_id VARCHAR(160) NOT NULL,
    market_key VARCHAR(160) NOT NULL,
    geography_type VARCHAR(40) NOT NULL,
    geography_code VARCHAR(80) NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    source_metric_kind VARCHAR(60) NOT NULL,
    source_quantity NUMERIC(20, 4) NOT NULL,
    normalized_visitor_person_trips NUMERIC(20, 4) NOT NULL,
    normalization_method VARCHAR(160) NOT NULL,
    notes TEXT NULL,
    UNIQUE (dataset_snapshot_id, stable_observation_id),
    CHECK (period_end >= period_start),
    CHECK (source_quantity >= 0),
    CHECK (normalized_visitor_person_trips >= 0)
);

CREATE TABLE IF NOT EXISTS traffic_corridor_observations (
    id BIGSERIAL PRIMARY KEY,
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    stable_observation_id VARCHAR(160) NOT NULL,
    route_designation VARCHAR(80) NOT NULL,
    jurisdiction_code VARCHAR(80) NOT NULL,
    count_location geometry(Point, 4326) NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    annual_average_daily_traffic DOUBLE PRECISION NOT NULL,
    observation_days INTEGER NOT NULL DEFAULT 365,
    count_method VARCHAR(120) NOT NULL,
    direction_definition VARCHAR(120) NULL,
    notes TEXT NULL,
    UNIQUE (dataset_snapshot_id, stable_observation_id),
    CHECK (period_end >= period_start),
    CHECK (annual_average_daily_traffic >= 0),
    CHECK (observation_days BETWEEN 1 AND 366)
);
CREATE INDEX IF NOT EXISTS ix_traffic_corridor_observations_location
    ON traffic_corridor_observations USING gist (count_location);

CREATE TABLE IF NOT EXISTS model_run_demand_components (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    dataset_snapshot_id UUID NULL REFERENCES dataset_snapshots(id),
    component_type VARCHAR(40) NOT NULL,
    source_record_key VARCHAR(160) NOT NULL,
    method_key VARCHAR(80) NOT NULL,
    input_quantity NUMERIC(20, 4) NOT NULL,
    deduplicated_quantity NUMERIC(20, 4) NOT NULL,
    eligible_quantity NUMERIC(20, 4) NOT NULL,
    participating_quantity NUMERIC(20, 4) NOT NULL,
    captured_quantity NUMERIC(20, 4) NOT NULL,
    ggr NUMERIC(20, 2) NOT NULL,
    details_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (model_run_id, component_type, source_record_key),
    CHECK (input_quantity >= 0),
    CHECK (deduplicated_quantity >= 0),
    CHECK (eligible_quantity >= 0),
    CHECK (participating_quantity >= 0),
    CHECK (captured_quantity >= 0),
    CHECK (ggr >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_capacity_diagnostics (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    facility_key VARCHAR(160) NOT NULL,
    status VARCHAR(30) NOT NULL,
    stabilized_ggr NUMERIC(20, 2) NOT NULL,
    plausible_capacity_minimum NUMERIC(20, 2) NULL,
    plausible_capacity_maximum NUMERIC(20, 2) NULL,
    implied_residual_slot_win_per_unit_day DOUBLE PRECISION NULL,
    is_below_validated_range BOOLEAN NOT NULL DEFAULT FALSE,
    is_above_validated_range BOOLEAN NOT NULL DEFAULT FALSE,
    warning_text TEXT NULL,
    UNIQUE (model_run_id, facility_key),
    CHECK (stabilized_ggr >= 0),
    CHECK (plausible_capacity_minimum IS NULL OR plausible_capacity_minimum >= 0),
    CHECK (plausible_capacity_maximum IS NULL OR plausible_capacity_maximum >= plausible_capacity_minimum)
);

CREATE TABLE IF NOT EXISTS model_run_ramp_results (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    facility_key VARCHAR(160) NOT NULL,
    calendar_year INTEGER NOT NULL,
    operating_year_number INTEGER NOT NULL,
    period_kind VARCHAR(40) NOT NULL,
    operating_year_fraction DOUBLE PRECISION NOT NULL,
    stabilization_share DOUBLE PRECISION NOT NULL,
    projected_ggr NUMERIC(20, 2) NOT NULL,
    UNIQUE (model_run_id, facility_key, calendar_year),
    CHECK (calendar_year BETWEEN 1900 AND 2300),
    CHECK (operating_year_number >= 0),
    CHECK (operating_year_fraction > 0 AND operating_year_fraction <= 1),
    CHECK (stabilization_share >= 0),
    CHECK (projected_ggr >= 0)
);

ALTER TABLE model_run_facility_results
    ADD COLUMN IF NOT EXISTS tourism_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS traffic_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS stabilized_total_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0;

DROP TRIGGER IF EXISTS trg_prevent_sealed_tourism_observation_mutation
    ON tourism_market_observations;
CREATE TRIGGER trg_prevent_sealed_tourism_observation_mutation
BEFORE INSERT OR UPDATE OR DELETE ON tourism_market_observations
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_traffic_observation_mutation
    ON traffic_corridor_observations;
CREATE TRIGGER trg_prevent_sealed_traffic_observation_mutation
BEFORE INSERT OR UPDATE OR DELETE ON traffic_corridor_observations
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_demand_component_mutation
    ON model_run_demand_components;
CREATE TRIGGER trg_prevent_finalized_demand_component_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_demand_components
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_capacity_diagnostic_mutation
    ON model_run_capacity_diagnostics;
CREATE TRIGGER trg_prevent_finalized_capacity_diagnostic_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_capacity_diagnostics
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_ramp_result_mutation
    ON model_run_ramp_results;
CREATE TRIGGER trg_prevent_finalized_ramp_result_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_ramp_results
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();
