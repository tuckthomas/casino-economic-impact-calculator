-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 008_gravity_engine.sql
-- Versioned development program, Valhalla route cache, and immutable gravity-run results.

-- These prototype artifacts are intentionally removed. Their hand-scored
-- output has been replaced by the persisted gravity execution path below.
DROP TABLE IF EXISTS site_scores;
ALTER TABLE casino_competitors
    DROP COLUMN IF EXISTS estimated_competition_weight;

-- Prototype rows were never provenance-backed and cannot participate in a
-- reproducible run. The application is pre-production, so remove them rather
-- than carrying an unversioned compatibility path into the canonical model.
DELETE FROM casino_gaming_revenue_periods revenue
USING casino_competitors competitor
WHERE revenue.casino_competitor_id = competitor.id
  AND competitor.dataset_snapshot_id IS NULL;
DELETE FROM casino_competitor_history history
USING casino_competitors competitor
WHERE history.casino_competitor_id = competitor.id
  AND competitor.dataset_snapshot_id IS NULL;
DELETE FROM casino_competitors
WHERE dataset_snapshot_id IS NULL;

DROP INDEX IF EXISTS ix_casino_competitors_stable_venue_id;
ALTER TABLE casino_competitors
    ALTER COLUMN dataset_snapshot_id SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_casino_competitors_snapshot_stable_venue_id
    ON casino_competitors (dataset_snapshot_id, stable_venue_id);

ALTER TABLE casino_gaming_revenue_periods
    DROP CONSTRAINT IF EXISTS casino_gaming_revenue_periods_casino_competitor_id_period_s_key;
CREATE UNIQUE INDEX IF NOT EXISTS ix_casino_gaming_revenue_snapshot_period
    ON casino_gaming_revenue_periods (
        dataset_snapshot_id,
        casino_competitor_id,
        period_start,
        period_end,
        reported_metric_key);

ALTER TABLE dataset_snapshots
    ADD COLUMN IF NOT EXISTS is_sealed BOOLEAN NOT NULL DEFAULT TRUE;

CREATE OR REPLACE FUNCTION prevent_dataset_snapshot_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.is_sealed THEN
        RAISE EXCEPTION 'Dataset snapshots are immutable after they are sealed.';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    IF NEW.id IS DISTINCT FROM OLD.id
       OR NEW.data_source_id IS DISTINCT FROM OLD.data_source_id
       OR NEW.dataset_key IS DISTINCT FROM OLD.dataset_key
       OR NEW.period IS DISTINCT FROM OLD.period
       OR NEW.period_start IS DISTINCT FROM OLD.period_start
       OR NEW.period_end IS DISTINCT FROM OLD.period_end
       OR NEW.ingested_at_utc IS DISTINCT FROM OLD.ingested_at_utc
       OR NEW.checksum IS DISTINCT FROM OLD.checksum
       OR NEW.transform_version IS DISTINCT FROM OLD.transform_version THEN
        RAISE EXCEPTION 'Dataset snapshot identity and provenance cannot change during ingestion.';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION prevent_sealed_snapshot_data_mutation()
RETURNS TRIGGER AS $$
DECLARE
    target_snapshot_ids UUID[];
BEGIN
    target_snapshot_ids := CASE TG_OP
        WHEN 'INSERT' THEN ARRAY[NEW.dataset_snapshot_id]
        WHEN 'DELETE' THEN ARRAY[OLD.dataset_snapshot_id]
        ELSE ARRAY[OLD.dataset_snapshot_id, NEW.dataset_snapshot_id]
    END;
    IF EXISTS (
        SELECT 1
        FROM dataset_snapshots snapshot
        WHERE snapshot.id = ANY(target_snapshot_ids)
          AND snapshot.is_sealed
    ) THEN
        RAISE EXCEPTION 'Rows belonging to a sealed dataset snapshot are immutable.';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_sealed_origin_zone_mutation ON origin_zones;
CREATE TRIGGER trg_prevent_sealed_origin_zone_mutation
BEFORE INSERT OR UPDATE OR DELETE ON origin_zones
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_age_bin_mutation ON origin_zone_age_bins;
CREATE TRIGGER trg_prevent_sealed_age_bin_mutation
BEFORE INSERT OR UPDATE OR DELETE ON origin_zone_age_bins
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_income_period_mutation ON origin_zone_income_periods;
CREATE TRIGGER trg_prevent_sealed_income_period_mutation
BEFORE INSERT OR UPDATE OR DELETE ON origin_zone_income_periods
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_competitor_mutation ON casino_competitors;
CREATE TRIGGER trg_prevent_sealed_competitor_mutation
BEFORE INSERT OR UPDATE OR DELETE ON casino_competitors
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_competitor_history_mutation ON casino_competitor_history;
CREATE TRIGGER trg_prevent_sealed_competitor_history_mutation
BEFORE INSERT OR UPDATE OR DELETE ON casino_competitor_history
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

DROP TRIGGER IF EXISTS trg_prevent_sealed_gaming_revenue_mutation ON casino_gaming_revenue_periods;
CREATE TRIGGER trg_prevent_sealed_gaming_revenue_mutation
BEFORE INSERT OR UPDATE OR DELETE ON casino_gaming_revenue_periods
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();

CREATE TABLE IF NOT EXISTS development_programs (
    id UUID PRIMARY KEY,
    stable_program_id VARCHAR(160) NOT NULL,
    version VARCHAR(40) NOT NULL,
    name VARCHAR(240) NOT NULL,
    slot_or_vlt_positions INTEGER NOT NULL DEFAULT 0,
    table_game_count INTEGER NOT NULL DEFAULT 0,
    poker_table_count INTEGER NOT NULL DEFAULT 0,
    has_sportsbook BOOLEAN NOT NULL DEFAULT FALSE,
    hotel_room_count INTEGER NOT NULL DEFAULT 0,
    gaming_floor_square_feet INTEGER NOT NULL DEFAULT 0,
    food_beverage_venue_count INTEGER NOT NULL DEFAULT 0,
    event_capacity INTEGER NOT NULL DEFAULT 0,
    resort_amenity_count INTEGER NOT NULL DEFAULT 0,
    capital_cost NUMERIC(20, 2) NULL,
    capital_cost_dollar_year INTEGER NULL,
    planned_opening_date DATE NULL,
    stabilized_year_number INTEGER NOT NULL DEFAULT 3,
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_immutable BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NULL,
    UNIQUE (stable_program_id, version),
    CHECK (slot_or_vlt_positions >= 0),
    CHECK (table_game_count >= 0),
    CHECK (poker_table_count >= 0),
    CHECK (hotel_room_count >= 0),
    CHECK (gaming_floor_square_feet >= 0),
    CHECK (food_beverage_venue_count >= 0),
    CHECK (event_capacity >= 0),
    CHECK (resort_amenity_count >= 0),
    CHECK (capital_cost IS NULL OR capital_cost >= 0),
    CHECK (stabilized_year_number >= 1)
);

ALTER TABLE model_runs
    ADD COLUMN IF NOT EXISTS development_program_id UUID NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'model_runs_development_program_id_fkey'
    ) THEN
        ALTER TABLE model_runs
            ADD CONSTRAINT model_runs_development_program_id_fkey
            FOREIGN KEY (development_program_id) REFERENCES development_programs(id);
    END IF;
END;
$$;

CREATE TABLE IF NOT EXISTS origin_facility_travel (
    id BIGSERIAL PRIMARY KEY,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id),
    casino_competitor_id INTEGER NULL REFERENCES casino_competitors(id),
    model_run_id UUID NULL REFERENCES model_runs(id),
    facility_key VARCHAR(160) NOT NULL,
    facility_kind VARCHAR(30) NOT NULL,
    routing_graph_hash VARCHAR(128) NOT NULL,
    costing_profile VARCHAR(40) NOT NULL,
    travel_time_minutes DOUBLE PRECISION NULL,
    routed_distance_meters DOUBLE PRECISION NULL,
    route_found BOOLEAN NOT NULL,
    route_failure_reason VARCHAR(500) NULL,
    calculated_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (origin_zone_id, facility_key, routing_graph_hash, costing_profile),
    CHECK (facility_kind IN ('incumbent', 'scenario')),
    CHECK (travel_time_minutes IS NULL OR travel_time_minutes >= 0),
    CHECK (routed_distance_meters IS NULL OR routed_distance_meters >= 0),
    CHECK (
        (route_found AND travel_time_minutes IS NOT NULL AND routed_distance_meters IS NOT NULL)
        OR
        (NOT route_found AND travel_time_minutes IS NULL AND routed_distance_meters IS NULL)
    )
);
CREATE INDEX IF NOT EXISTS ix_origin_facility_travel_facility
    ON origin_facility_travel (facility_key, routing_graph_hash, costing_profile);

CREATE TABLE IF NOT EXISTS model_run_origin_results (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id),
    demand_specification VARCHAR(40) NOT NULL,
    resident_demand NUMERIC(20, 2) NOT NULL,
    baseline_outside_share DOUBLE PRECISION NOT NULL,
    with_project_outside_share DOUBLE PRECISION NOT NULL,
    proposed_resident_ggr NUMERIC(20, 2) NOT NULL,
    host_jurisdiction_capture NUMERIC(20, 2) NOT NULL,
    external_jurisdiction_capture NUMERIC(20, 2) NOT NULL,
    tribal_or_other_jurisdiction_capture NUMERIC(20, 2) NOT NULL,
    outside_option_capture NUMERIC(20, 2) NOT NULL,
    UNIQUE (model_run_id, origin_zone_id, demand_specification),
    CHECK (resident_demand >= 0),
    CHECK (baseline_outside_share BETWEEN 0 AND 1),
    CHECK (with_project_outside_share BETWEEN 0 AND 1),
    CHECK (proposed_resident_ggr >= 0),
    CHECK (host_jurisdiction_capture >= 0),
    CHECK (external_jurisdiction_capture >= 0),
    CHECK (tribal_or_other_jurisdiction_capture >= 0),
    CHECK (outside_option_capture >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_facility_results (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    casino_competitor_id INTEGER NULL REFERENCES casino_competitors(id),
    facility_key VARCHAR(160) NOT NULL,
    facility_kind VARCHAR(30) NOT NULL,
    is_proposed_facility BOOLEAN NOT NULL,
    normalized_attraction DOUBLE PRECISION NOT NULL,
    baseline_resident_ggr NUMERIC(20, 2) NOT NULL,
    with_project_resident_ggr NUMERIC(20, 2) NOT NULL,
    change_in_resident_ggr NUMERIC(20, 2) NOT NULL,
    UNIQUE (model_run_id, facility_key),
    CHECK (facility_kind IN ('incumbent', 'scenario')),
    CHECK (normalized_attraction >= 0),
    CHECK (baseline_resident_ggr >= 0),
    CHECK (with_project_resident_ggr >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_origin_facility_allocations (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id),
    origin_facility_travel_id BIGINT NULL REFERENCES origin_facility_travel(id),
    casino_competitor_id INTEGER NULL REFERENCES casino_competitors(id),
    facility_key VARCHAR(160) NOT NULL,
    market_state VARCHAR(30) NOT NULL,
    capture_source_category VARCHAR(80) NOT NULL,
    is_proposed_facility BOOLEAN NOT NULL,
    network_travel_time_minutes DOUBLE PRECISION NULL,
    routed_distance_meters DOUBLE PRECISION NULL,
    normalized_attraction DOUBLE PRECISION NOT NULL,
    origin_facility_modifier DOUBLE PRECISION NOT NULL,
    log_weight DOUBLE PRECISION NULL,
    share DOUBLE PRECISION NOT NULL,
    allocated_resident_ggr NUMERIC(20, 2) NOT NULL,
    UNIQUE (model_run_id, origin_zone_id, facility_key, market_state),
    CHECK (market_state IN ('baseline', 'with-project')),
    CHECK (network_travel_time_minutes IS NULL OR network_travel_time_minutes >= 0),
    CHECK (routed_distance_meters IS NULL OR routed_distance_meters >= 0),
    CHECK (normalized_attraction >= 0),
    CHECK (origin_facility_modifier >= 0),
    CHECK (share BETWEEN 0 AND 1),
    CHECK (allocated_resident_ggr >= 0)
);
CREATE INDEX IF NOT EXISTS ix_model_run_allocations_origin
    ON model_run_origin_facility_allocations (model_run_id, origin_zone_id, market_state);

CREATE OR REPLACE FUNCTION prevent_immutable_development_program_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.is_immutable OR EXISTS (
        SELECT 1
        FROM model_runs run
        WHERE run.development_program_id = OLD.id
          AND run.status = 'finalized'
    ) THEN
        RAISE EXCEPTION 'Development program % is immutable; create a new version instead.', OLD.id;
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_immutable_development_program_mutation
    ON development_programs;
CREATE TRIGGER trg_prevent_immutable_development_program_mutation
BEFORE UPDATE OR DELETE ON development_programs
FOR EACH ROW EXECUTE FUNCTION prevent_immutable_development_program_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_origin_result_mutation
    ON model_run_origin_results;
CREATE TRIGGER trg_prevent_finalized_origin_result_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_origin_results
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_facility_result_mutation
    ON model_run_facility_results;
CREATE TRIGGER trg_prevent_finalized_facility_result_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_facility_results
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_allocation_mutation
    ON model_run_origin_facility_allocations;
CREATE TRIGGER trg_prevent_finalized_allocation_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_origin_facility_allocations
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();
