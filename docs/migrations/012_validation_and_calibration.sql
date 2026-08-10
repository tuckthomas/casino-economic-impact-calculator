-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 012_validation_and_calibration.sql
-- Immutable benchmark registry, validation cases, train/holdout evaluations, and comparable-model results.

CREATE TABLE IF NOT EXISTS benchmark_studies (
    id UUID PRIMARY KEY,
    benchmark_key VARCHAR(160) NOT NULL UNIQUE,
    title VARCHAR(300) NOT NULL,
    market_code VARCHAR(160) NOT NULL,
    geography_type VARCHAR(60) NOT NULL,
    geography_code VARCHAR(160) NOT NULL,
    study_date DATE NULL,
    consultant_or_source VARCHAR(300) NOT NULL,
    candidate_latitude DOUBLE PRECISION NULL,
    candidate_longitude DOUBLE PRECISION NULL,
    development_program_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    reported_outputs_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    reported_assumptions_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    methodological_notes TEXT NULL,
    source_url VARCHAR(1000) NOT NULL,
    source_file_checksum VARCHAR(128) NULL,
    provenance_notes TEXT NULL,
    validation_state VARCHAR(40) NOT NULL DEFAULT 'registered',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (candidate_latitude IS NULL OR candidate_latitude BETWEEN -90 AND 90),
    CHECK (candidate_longitude IS NULL OR candidate_longitude BETWEEN -180 AND 180),
    CHECK (validation_state IN ('registered', 'extracted', 'validated'))
);

CREATE TABLE IF NOT EXISTS validation_cases (
    id UUID PRIMARY KEY,
    benchmark_study_id UUID NULL REFERENCES benchmark_studies(id) ON DELETE RESTRICT,
    case_key VARCHAR(160) NOT NULL UNIQUE,
    name VARCHAR(300) NOT NULL,
    market_code VARCHAR(160) NOT NULL,
    jurisdiction_code VARCHAR(80) NOT NULL,
    case_kind VARCHAR(60) NOT NULL,
    dataset_partition VARCHAR(30) NOT NULL,
    holdout_group VARCHAR(160) NULL,
    target_casino_competitor_id INTEGER NULL REFERENCES casino_competitors(id) ON DELETE RESTRICT,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE RESTRICT,
    observed_revenue NUMERIC(20, 2) NOT NULL,
    observed_metric_key VARCHAR(80) NOT NULL,
    observed_metric_definition TEXT NOT NULL,
    training_period_start DATE NOT NULL,
    training_period_end DATE NOT NULL,
    validation_period_start DATE NULL,
    validation_period_end DATE NULL,
    inclusion_rules_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    predictor_values_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    execution_request_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    notes TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (case_kind IN ('incumbent-backtest', 'public-benchmark', 'synthetic-national')),
    CHECK (dataset_partition IN ('training', 'holdout', 'benchmark')),
    CHECK (observed_revenue >= 0),
    CHECK (training_period_end >= training_period_start),
    CHECK (validation_period_end IS NULL OR validation_period_start IS NOT NULL),
    CHECK (validation_period_end IS NULL OR validation_period_end >= validation_period_start)
);

CREATE TABLE IF NOT EXISTS validation_evaluations (
    id UUID PRIMARY KEY,
    evaluation_key VARCHAR(160) NOT NULL,
    version VARCHAR(60) NOT NULL,
    model_version VARCHAR(80) NOT NULL,
    objective_function VARCHAR(80) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'draft',
    published_parameter_set_id BIGINT NULL REFERENCES model_parameter_sets(id) ON DELETE RESTRICT,
    inclusion_rules_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    selected_parameters_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    training_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    holdout_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    benchmark_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    comparable_model_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    comparable_training_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    comparable_holdout_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    comparable_benchmark_metrics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    training_case_count INTEGER NOT NULL DEFAULT 0,
    holdout_case_count INTEGER NOT NULL DEFAULT 0,
    benchmark_case_count INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    finalized_at_utc TIMESTAMPTZ NULL,
    is_immutable BOOLEAN NOT NULL DEFAULT FALSE,
    UNIQUE (evaluation_key, version),
    CHECK (objective_function IN ('mae', 'mape', 'smape', 'rmse')),
    CHECK (status IN ('draft', 'finalized')),
    CHECK (training_case_count >= 0),
    CHECK (holdout_case_count >= 0),
    CHECK (benchmark_case_count >= 0),
    CHECK ((status = 'finalized') = is_immutable),
    CHECK ((status = 'finalized') = (finalized_at_utc IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS validation_case_results (
    id BIGSERIAL PRIMARY KEY,
    validation_evaluation_id UUID NOT NULL REFERENCES validation_evaluations(id) ON DELETE RESTRICT,
    validation_case_id UUID NOT NULL REFERENCES validation_cases(id) ON DELETE RESTRICT,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE RESTRICT,
    prediction_kind VARCHAR(40) NOT NULL,
    dataset_partition VARCHAR(30) NOT NULL,
    observed_revenue NUMERIC(20, 2) NOT NULL,
    predicted_revenue NUMERIC(20, 2) NOT NULL,
    residual NUMERIC(20, 2) NOT NULL,
    absolute_percentage_error DOUBLE PRECISION NULL,
    symmetric_absolute_percentage_error DOUBLE PRECISION NOT NULL,
    diagnostics_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (validation_evaluation_id, validation_case_id, prediction_kind),
    CHECK (prediction_kind IN ('gravity', 'comparable-log-linear')),
    CHECK (dataset_partition IN ('training', 'holdout', 'benchmark')),
    CHECK (observed_revenue >= 0),
    CHECK (predicted_revenue >= 0),
    CHECK (absolute_percentage_error IS NULL OR absolute_percentage_error >= 0),
    CHECK (symmetric_absolute_percentage_error BETWEEN 0 AND 200)
);

CREATE OR REPLACE FUNCTION prevent_immutable_validation_evaluation_mutation()
RETURNS TRIGGER AS $$
DECLARE evaluation_id UUID;
DECLARE immutable BOOLEAN;
BEGIN
    evaluation_id := COALESCE(NEW.validation_evaluation_id, OLD.validation_evaluation_id);
    SELECT is_immutable INTO immutable FROM validation_evaluations WHERE id = evaluation_id;
    IF immutable THEN
        RAISE EXCEPTION 'Finalized validation evaluation % is immutable', evaluation_id;
    END IF;
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_immutable_validation_case_result_mutation ON validation_case_results;
CREATE TRIGGER trg_prevent_immutable_validation_case_result_mutation
BEFORE INSERT OR UPDATE OR DELETE ON validation_case_results
FOR EACH ROW EXECUTE FUNCTION prevent_immutable_validation_evaluation_mutation();

CREATE OR REPLACE FUNCTION prevent_finalized_validation_evaluation_change()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.is_immutable THEN
        RAISE EXCEPTION 'Finalized validation evaluation % is immutable', OLD.id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_finalized_validation_evaluation_change ON validation_evaluations;
CREATE TRIGGER trg_prevent_finalized_validation_evaluation_change
BEFORE UPDATE OR DELETE ON validation_evaluations
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_validation_evaluation_change();

INSERT INTO benchmark_studies (
    id, benchmark_key, title, market_code, geography_type, geography_code,
    study_date, consultant_or_source, reported_assumptions_json, methodological_notes,
    source_url, provenance_notes, validation_state)
VALUES
    ('45c835d7-63af-41b8-bba3-118b44c8eca0', 'spectrum-in-relocation-2025',
     'Indiana Gaming Market and Casino Relocation Analysis', 'US-IN-NORTHEAST', 'market', 'US-IN-NORTHEAST',
     DATE '2025-09-30', 'Spectrum Gaming Group',
     '{"methodologicalAnchors":["ZIP-level adjusted gross income","drive-time capture","market potential"]}'::jsonb,
     'Registered as a validation anchor; reported outputs must be source-extracted and reviewed before the state advances.',
     'https://www.in.gov/igc/files/publications/Spectrum-Relocation-Report-to-Indiana-Gaming-Commission-9-30-2025-Final.pdf',
     'Canonical-plan benchmark registry seed.', 'registered'),
    ('f3fae147-af74-410a-8cc4-e0a72d059558', 'cbre-union-gaming-fort-wayne-2025',
     'Greater Fort Wayne Area Casino Analysis', 'US-IN-FORT-WAYNE', 'market', 'US-IN-FORT-WAYNE',
     DATE '2025-12-03', 'CBRE / Union Gaming Analytics',
     '{"methodologicalAnchors":["gravity model","development program","traffic demand","ramp analysis","comparable-market regression"]}'::jsonb,
     'Registered as a validation anchor; reported outputs must be source-extracted and reviewed before the state advances.',
     'https://cdn.insideindianabusiness.com/wp-content/uploads/2026/01/GFWI-Casino-Analysis-Presentation-Final-2025-12-03.pdf',
     'Canonical-plan benchmark registry seed.', 'registered'),
    ('61146275-1a75-412a-98d3-f88848b6e5d4', 'steinberg-steuben-feasibility',
     'Steuben County Gaming Market Feasibility Study', 'US-IN-STEUBEN', 'county', 'US-IN-18151',
     NULL, 'A.M. Steinberg Advisors',
     '{"methodologicalAnchors":["eligible adult population","income-adjusted expenditure","travel decay","beta prior 1.5","observed GGR mass","tourism"]}'::jsonb,
     'Registered as a validation anchor; reported outputs must be source-extracted and reviewed before the state advances.',
     'https://www.steubenedc.com/media/userfiles/subsite_259/files/SCEDC_Feasibility_Study_FINAL.pdf',
     'Canonical-plan benchmark registry seed.', 'registered')
ON CONFLICT (benchmark_key) DO NOTHING;
