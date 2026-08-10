-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 006_gravity_model_foundation.sql
-- Additive foundation for the nationally reusable gravity-model architecture.

CREATE TABLE IF NOT EXISTS jurisdictions (
    id SERIAL PRIMARY KEY,
    code VARCHAR(80) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    kind VARCHAR(40) NOT NULL,
    parent_jurisdiction_id INTEGER NULL REFERENCES jurisdictions(id),
    external_code TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS jurisdiction_rules (
    id BIGSERIAL PRIMARY KEY,
    jurisdiction_id INTEGER NOT NULL REFERENCES jurisdictions(id),
    rule_type VARCHAR(100) NOT NULL,
    rule_value_json JSONB NOT NULL,
    validation_state VARCHAR(30) NOT NULL DEFAULT 'incomplete',
    effective_from DATE NOT NULL,
    effective_to DATE NULL,
    source_url TEXT NULL,
    provenance_notes TEXT NULL,
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (effective_to IS NULL OR effective_to >= effective_from)
);
CREATE INDEX IF NOT EXISTS ix_jurisdiction_rules_effective
    ON jurisdiction_rules (jurisdiction_id, rule_type, effective_from);

CREATE TABLE IF NOT EXISTS model_parameter_definitions (
    id BIGSERIAL PRIMARY KEY,
    key VARCHAR(160) NOT NULL UNIQUE,
    category VARCHAR(80) NOT NULL,
    display_name VARCHAR(200) NOT NULL,
    technical_description TEXT NOT NULL,
    plain_language_description TEXT NOT NULL,
    units VARCHAR(60) NOT NULL,
    data_type VARCHAR(40) NOT NULL,
    system_default_value DOUBLE PRECISION NOT NULL,
    computational_minimum DOUBLE PRECISION NULL,
    computational_maximum DOUBLE PRECISION NULL,
    recommended_minimum DOUBLE PRECISION NULL,
    recommended_maximum DOUBLE PRECISION NULL,
    ui_step DOUBLE PRECISION NULL,
    ui_exposure_level VARCHAR(30) NOT NULL,
    is_user_overridable BOOLEAN NOT NULL DEFAULT FALSE,
    model_version_applicability VARCHAR(80) NOT NULL,
    provenance_notes TEXT NULL,
    is_calibrated BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    CHECK (computational_minimum IS NULL OR computational_maximum IS NULL OR computational_minimum <= computational_maximum),
    CHECK (recommended_minimum IS NULL OR recommended_maximum IS NULL OR recommended_minimum <= recommended_maximum)
);

CREATE TABLE IF NOT EXISTS model_parameter_sets (
    id BIGSERIAL PRIMARY KEY,
    key VARCHAR(160) NOT NULL,
    name VARCHAR(200) NOT NULL,
    scope VARCHAR(40) NOT NULL,
    jurisdiction_id INTEGER NULL REFERENCES jurisdictions(id),
    market_code VARCHAR(120) NULL,
    scenario_kind VARCHAR(40) NULL,
    version VARCHAR(60) NOT NULL,
    model_version_applicability VARCHAR(80) NOT NULL DEFAULT 'gravity-v1',
    is_immutable BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    calibration_notes TEXT NULL,
    UNIQUE (key, version)
);

CREATE TABLE IF NOT EXISTS model_parameter_set_values (
    id BIGSERIAL PRIMARY KEY,
    parameter_set_id BIGINT NOT NULL REFERENCES model_parameter_sets(id),
    parameter_definition_id BIGINT NOT NULL REFERENCES model_parameter_definitions(id),
    value DOUBLE PRECISION NOT NULL,
    provenance_notes TEXT NULL,
    UNIQUE (parameter_set_id, parameter_definition_id)
);

CREATE TABLE IF NOT EXISTS model_runs (
    id UUID PRIMARY KEY,
    model_version VARCHAR(80) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'draft',
    jurisdiction_id INTEGER NULL REFERENCES jurisdictions(id),
    base_parameter_set_id BIGINT NULL REFERENCES model_parameter_sets(id),
    resolved_input_json JSONB NOT NULL,
    data_snapshot_references_json JSONB NOT NULL,
    candidate_latitude DOUBLE PRECISION NOT NULL,
    candidate_longitude DOUBLE PRECISION NOT NULL,
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    finalized_at_utc TIMESTAMP WITH TIME ZONE NULL,
    execution_duration INTERVAL NULL,
    warning_summary TEXT NULL,
    error_summary TEXT NULL
);

CREATE TABLE IF NOT EXISTS model_run_parameter_values (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    parameter_definition_id BIGINT NOT NULL REFERENCES model_parameter_definitions(id),
    system_fallback_value DOUBLE PRECISION NOT NULL,
    default_value DOUBLE PRECISION NOT NULL,
    scenario_value DOUBLE PRECISION NULL,
    user_override_value DOUBLE PRECISION NULL,
    final_value DOUBLE PRECISION NOT NULL,
    source_layer VARCHAR(40) NOT NULL,
    is_outside_recommended_range BOOLEAN NOT NULL DEFAULT FALSE,
    warning_text TEXT NULL,
    UNIQUE (model_run_id, parameter_definition_id)
);

CREATE TABLE IF NOT EXISTS model_run_parameter_set_references (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    parameter_set_id BIGINT NOT NULL REFERENCES model_parameter_sets(id),
    source_layer VARCHAR(40) NOT NULL,
    UNIQUE (model_run_id, parameter_set_id, source_layer)
);

-- Enforce calibration immutability below the service layer. Direct SQL and
-- future code paths cannot change a parameter set once a finalized run cites it.
CREATE OR REPLACE FUNCTION prevent_referenced_parameter_set_mutation()
RETURNS TRIGGER AS $$
DECLARE
    target_parameter_set_ids BIGINT[];
BEGIN
    target_parameter_set_ids := CASE TG_OP
        WHEN 'INSERT' THEN ARRAY[NEW.parameter_set_id]
        WHEN 'DELETE' THEN ARRAY[OLD.parameter_set_id]
        ELSE ARRAY[OLD.parameter_set_id, NEW.parameter_set_id]
    END;
    IF EXISTS (
        SELECT 1
        FROM model_parameter_sets parameter_set
        WHERE parameter_set.id = ANY(target_parameter_set_ids)
          AND parameter_set.is_immutable
    ) OR EXISTS (
        SELECT 1
        FROM model_run_parameter_set_references reference
        JOIN model_runs run ON run.id = reference.model_run_id
        WHERE reference.parameter_set_id = ANY(target_parameter_set_ids)
          AND run.status = 'finalized'
    ) THEN
        RAISE EXCEPTION 'A referenced parameter set is immutable; create a new version instead.';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_referenced_parameter_set_mutation
    ON model_parameter_set_values;
CREATE TRIGGER trg_prevent_referenced_parameter_set_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_parameter_set_values
FOR EACH ROW EXECUTE FUNCTION prevent_referenced_parameter_set_mutation();

CREATE OR REPLACE FUNCTION prevent_immutable_parameter_set_metadata_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.is_immutable OR EXISTS (
        SELECT 1
        FROM model_run_parameter_set_references reference
        JOIN model_runs run ON run.id = reference.model_run_id
        WHERE reference.parameter_set_id = OLD.id
          AND run.status = 'finalized'
    ) THEN
        RAISE EXCEPTION 'Parameter set % is immutable; create a new version instead.', OLD.id;
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_immutable_parameter_set_metadata_mutation
    ON model_parameter_sets;
CREATE TRIGGER trg_prevent_immutable_parameter_set_metadata_mutation
BEFORE UPDATE OR DELETE ON model_parameter_sets
FOR EACH ROW EXECUTE FUNCTION prevent_immutable_parameter_set_metadata_mutation();

CREATE OR REPLACE FUNCTION prevent_finalized_model_run_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.status = 'finalized' THEN
        RAISE EXCEPTION 'Finalized model run % is immutable.', OLD.id;
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_finalized_model_run_mutation ON model_runs;
CREATE TRIGGER trg_prevent_finalized_model_run_mutation
BEFORE UPDATE OR DELETE ON model_runs
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_mutation();

CREATE OR REPLACE FUNCTION prevent_finalized_model_run_child_mutation()
RETURNS TRIGGER AS $$
DECLARE
    target_model_run_ids UUID[];
BEGIN
    target_model_run_ids := CASE TG_OP
        WHEN 'INSERT' THEN ARRAY[NEW.model_run_id]
        WHEN 'DELETE' THEN ARRAY[OLD.model_run_id]
        ELSE ARRAY[OLD.model_run_id, NEW.model_run_id]
    END;
    IF EXISTS (
        SELECT 1
        FROM model_runs run
        WHERE run.id = ANY(target_model_run_ids)
          AND run.status = 'finalized'
    ) THEN
        RAISE EXCEPTION 'Finalized model-run snapshots are immutable.';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_finalized_parameter_snapshot_mutation
    ON model_run_parameter_values;
CREATE TRIGGER trg_prevent_finalized_parameter_snapshot_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_parameter_values
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_parameter_reference_mutation
    ON model_run_parameter_set_references;
CREATE TRIGGER trg_prevent_finalized_parameter_reference_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_parameter_set_references
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

-- Make the script safely forward-compatible with databases that received an
-- earlier development version of this additive migration.
ALTER TABLE model_parameter_definitions
    ADD COLUMN IF NOT EXISTS is_calibrated BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE model_runs
    ADD COLUMN IF NOT EXISTS status VARCHAR(30) NOT NULL DEFAULT 'draft',
    ADD COLUMN IF NOT EXISTS finalized_at_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE model_run_parameter_values
    ADD COLUMN IF NOT EXISTS system_fallback_value DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE jurisdiction_rules
    ADD COLUMN IF NOT EXISTS validation_state VARCHAR(30) NOT NULL DEFAULT 'incomplete';
ALTER TABLE model_parameter_sets
    ADD COLUMN IF NOT EXISTS model_version_applicability VARCHAR(80) NOT NULL DEFAULT 'gravity-v1';

ALTER TABLE model_run_parameter_values
    DROP CONSTRAINT IF EXISTS model_run_parameter_values_model_run_id_fkey,
    ADD CONSTRAINT model_run_parameter_values_model_run_id_fkey
        FOREIGN KEY (model_run_id) REFERENCES model_runs(id) ON DELETE CASCADE;

ALTER TABLE jurisdiction_rules
    ALTER COLUMN rule_value_json TYPE JSONB USING rule_value_json::jsonb;
ALTER TABLE model_runs
    ALTER COLUMN resolved_input_json TYPE JSONB USING resolved_input_json::jsonb,
    ALTER COLUMN data_snapshot_references_json TYPE JSONB USING data_snapshot_references_json::jsonb;
