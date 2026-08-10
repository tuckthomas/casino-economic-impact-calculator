-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 007_model_data_foundation.sql
-- Immutable provenance, origin, income, competitor, and observed-performance foundation.

CREATE TABLE IF NOT EXISTS data_sources (
    id BIGSERIAL PRIMARY KEY,
    name VARCHAR(240) NOT NULL,
    publisher VARCHAR(240) NOT NULL,
    url VARCHAR(1000) NOT NULL,
    source_type VARCHAR(80) NOT NULL,
    geographic_coverage VARCHAR(240) NOT NULL,
    vintage_period VARCHAR(120) NOT NULL,
    retrieved_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    license_terms_notes TEXT NULL,
    content_hash VARCHAR(128) NOT NULL,
    is_authoritative BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NULL,
    UNIQUE (url, content_hash)
);

CREATE TABLE IF NOT EXISTS dataset_snapshots (
    id UUID PRIMARY KEY,
    data_source_id BIGINT NOT NULL REFERENCES data_sources(id),
    dataset_key VARCHAR(160) NOT NULL,
    period VARCHAR(120) NOT NULL,
    period_start DATE NULL,
    period_end DATE NULL,
    ingested_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    row_count BIGINT NOT NULL,
    checksum VARCHAR(128) NOT NULL,
    transform_version VARCHAR(120) NOT NULL,
    validation_state VARCHAR(30) NOT NULL,
    warnings_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    errors_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    UNIQUE (dataset_key, checksum),
    CHECK (row_count >= 0),
    CHECK (period_end IS NULL OR period_start IS NULL OR period_end >= period_start)
);

CREATE TABLE IF NOT EXISTS model_run_dataset_snapshot_references (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    role VARCHAR(80) NOT NULL,
    reference_key VARCHAR(160) NOT NULL DEFAULT 'default',
    UNIQUE (model_run_id, role, reference_key)
);

CREATE TABLE IF NOT EXISTS origin_zones (
    id BIGSERIAL PRIMARY KEY,
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    stable_origin_id VARCHAR(160) NOT NULL,
    origin_type VARCHAR(40) NOT NULL,
    geography_code VARCHAR(40) NOT NULL,
    country_code VARCHAR(3) NOT NULL,
    state_or_territory_code VARCHAR(10) NULL,
    county_equivalent_code VARCHAR(20) NULL,
    metropolitan_statistical_area_code VARCHAR(20) NULL,
    combined_statistical_area_code VARCHAR(20) NULL,
    representative_point geometry(Point, 4326) NOT NULL,
    area_geometry geometry(Geometry, 4326) NOT NULL,
    UNIQUE (dataset_snapshot_id, stable_origin_id)
);
CREATE INDEX IF NOT EXISTS ix_origin_zones_representative_point
    ON origin_zones USING gist (representative_point);
CREATE INDEX IF NOT EXISTS ix_origin_zones_area_geometry
    ON origin_zones USING gist (area_geometry);

CREATE TABLE IF NOT EXISTS origin_zone_age_bins (
    id BIGSERIAL PRIMARY KEY,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id),
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    observation_year INTEGER NOT NULL,
    minimum_age INTEGER NOT NULL,
    maximum_age INTEGER NULL,
    population BIGINT NOT NULL,
    interpolation_method VARCHAR(80) NOT NULL,
    control_validation_state VARCHAR(30) NOT NULL,
    UNIQUE (origin_zone_id, dataset_snapshot_id, observation_year, minimum_age),
    CHECK (minimum_age >= 0),
    CHECK (maximum_age IS NULL OR maximum_age >= minimum_age),
    CHECK (population >= 0)
);

CREATE TABLE IF NOT EXISTS origin_zone_income_periods (
    id BIGSERIAL PRIMARY KEY,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id),
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    tax_year INTEGER NOT NULL,
    return_count BIGINT NULL,
    adjusted_gross_income NUMERIC(20, 2) NULL,
    inflation_adjusted_adjusted_gross_income NUMERIC(20, 2) NULL,
    median_household_income NUMERIC(20, 2) NULL,
    dollar_year INTEGER NULL,
    notes TEXT NULL,
    UNIQUE (origin_zone_id, dataset_snapshot_id, tax_year),
    CHECK (return_count IS NULL OR return_count >= 0),
    CHECK (adjusted_gross_income IS NULL OR adjusted_gross_income >= 0),
    CHECK (inflation_adjusted_adjusted_gross_income IS NULL OR inflation_adjusted_adjusted_gross_income >= 0),
    CHECK (median_household_income IS NULL OR median_household_income >= 0)
);

ALTER TABLE casino_competitors
    ALTER COLUMN state TYPE VARCHAR(10),
    ADD COLUMN IF NOT EXISTS stable_venue_id VARCHAR(160) NULL,
    ADD COLUMN IF NOT EXISTS country_code VARCHAR(3) NOT NULL DEFAULT 'USA',
    ADD COLUMN IF NOT EXISTS facility_regime VARCHAR(60) NULL,
    ADD COLUMN IF NOT EXISTS regulatory_status VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS jurisdiction_id INTEGER NULL REFERENCES jurisdictions(id),
    ADD COLUMN IF NOT EXISTS regulator_name VARCHAR(240) NULL,
    ADD COLUMN IF NOT EXISTS regulator_license_id VARCHAR(160) NULL,
    ADD COLUMN IF NOT EXISTS tribal_nation_name VARCHAR(240) NULL,
    ADD COLUMN IF NOT EXISTS opened_on DATE NULL,
    ADD COLUMN IF NOT EXISTS closed_on DATE NULL,
    ADD COLUMN IF NOT EXISTS dataset_snapshot_id UUID NULL REFERENCES dataset_snapshots(id),
    ADD COLUMN IF NOT EXISTS gaming_positions INTEGER NULL,
    ADD COLUMN IF NOT EXISTS slot_vlt_positions INTEGER NULL,
    ADD COLUMN IF NOT EXISTS table_game_count INTEGER NULL,
    ADD COLUMN IF NOT EXISTS poker_table_count INTEGER NULL,
    ADD COLUMN IF NOT EXISTS gaming_floor_square_feet INTEGER NULL,
    ADD COLUMN IF NOT EXISTS hotel_room_count INTEGER NULL,
    ADD COLUMN IF NOT EXISTS event_capacity INTEGER NULL,
    ADD COLUMN IF NOT EXISTS food_beverage_venue_count INTEGER NULL,
    ADD COLUMN IF NOT EXISTS development_cost NUMERIC(20, 2) NULL,
    ADD COLUMN IF NOT EXISTS development_cost_dollar_year INTEGER NULL,
    ADD COLUMN IF NOT EXISTS access_context VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS limited_access_distance_miles DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS has_interchange_access BOOLEAN NULL,
    ADD COLUMN IF NOT EXISTS market_orientation VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS is_border_market BOOLEAN NOT NULL DEFAULT FALSE;

UPDATE casino_competitors
SET stable_venue_id = 'legacy-' || id::text
WHERE stable_venue_id IS NULL OR stable_venue_id = '';
ALTER TABLE casino_competitors
    ALTER COLUMN stable_venue_id SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_casino_competitors_stable_venue_id
    ON casino_competitors (stable_venue_id);

CREATE TABLE IF NOT EXISTS casino_competitor_history (
    id BIGSERIAL PRIMARY KEY,
    casino_competitor_id INTEGER NOT NULL REFERENCES casino_competitors(id),
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    event_type VARCHAR(60) NOT NULL,
    effective_from DATE NOT NULL,
    effective_to DATE NULL,
    operator_name VARCHAR(240) NULL,
    notes TEXT NULL,
    CHECK (effective_to IS NULL OR effective_to >= effective_from)
);
CREATE INDEX IF NOT EXISTS ix_casino_competitor_history_effective
    ON casino_competitor_history (casino_competitor_id, event_type, effective_from);

CREATE TABLE IF NOT EXISTS casino_gaming_revenue_periods (
    id BIGSERIAL PRIMARY KEY,
    casino_competitor_id INTEGER NOT NULL REFERENCES casino_competitors(id),
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    period_granularity VARCHAR(20) NOT NULL,
    reported_metric_key VARCHAR(80) NOT NULL,
    reported_metric_definition TEXT NOT NULL,
    reported_amount NUMERIC(20, 2) NOT NULL,
    inflation_adjusted_amount NUMERIC(20, 2) NULL,
    inflation_adjustment_dollar_year INTEGER NULL,
    anomaly_flags_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    notes TEXT NULL,
    UNIQUE (casino_competitor_id, period_start, period_end, reported_metric_key),
    CHECK (period_end >= period_start),
    CHECK (reported_amount >= 0),
    CHECK (inflation_adjusted_amount IS NULL OR inflation_adjusted_amount >= 0)
);

CREATE OR REPLACE FUNCTION prevent_dataset_snapshot_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Dataset snapshots are immutable; ingest a new snapshot instead.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_dataset_snapshot_mutation ON dataset_snapshots;
CREATE TRIGGER trg_prevent_dataset_snapshot_mutation
BEFORE UPDATE OR DELETE ON dataset_snapshots
FOR EACH ROW EXECUTE FUNCTION prevent_dataset_snapshot_mutation();

CREATE OR REPLACE FUNCTION prevent_referenced_data_source_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM dataset_snapshots snapshot
        WHERE snapshot.data_source_id = OLD.id
    ) THEN
        RAISE EXCEPTION 'A data source cited by a dataset snapshot is immutable; register a new source identity instead.';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_referenced_data_source_mutation ON data_sources;
CREATE TRIGGER trg_prevent_referenced_data_source_mutation
BEFORE UPDATE OR DELETE ON data_sources
FOR EACH ROW EXECUTE FUNCTION prevent_referenced_data_source_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_dataset_reference_mutation
    ON model_run_dataset_snapshot_references;
CREATE TRIGGER trg_prevent_finalized_dataset_reference_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_dataset_snapshot_references
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();
