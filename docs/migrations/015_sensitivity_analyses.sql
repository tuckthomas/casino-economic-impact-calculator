-- 015_sensitivity_analyses.sql
-- Immutable one-at-a-time sensitivity studies backed by complete stored model runs.

CREATE TABLE IF NOT EXISTS sensitivity_analyses (
    id UUID PRIMARY KEY,
    analysis_key VARCHAR(160) NOT NULL,
    version VARCHAR(60) NOT NULL,
    name VARCHAR(300) NOT NULL,
    baseline_model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE RESTRICT,
    output_metric VARCHAR(80) NOT NULL,
    baseline_metric_value NUMERIC(20, 4) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'draft',
    input_json JSONB NOT NULL,
    error_summary TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    finalized_at_utc TIMESTAMPTZ NULL,
    is_immutable BOOLEAN NOT NULL DEFAULT FALSE,
    UNIQUE (analysis_key, version),
    CHECK (output_metric IN (
        'stabilized-total-ggr', 'local-discretionary-displacement', 'gross-gaming-tax',
        'gross-social-cost', 'net-permanent-jobs', 'net-host-local-impact', 'net-host-state-impact')),
    CHECK (status IN ('draft', 'finalized', 'failed')),
    CHECK ((status = 'finalized') = is_immutable),
    CHECK ((status = 'finalized') = (finalized_at_utc IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS sensitivity_analysis_points (
    id BIGSERIAL PRIMARY KEY,
    sensitivity_analysis_id UUID NOT NULL REFERENCES sensitivity_analyses(id) ON DELETE RESTRICT,
    parameter_key VARCHAR(160) NOT NULL,
    direction VARCHAR(10) NOT NULL,
    parameter_value DOUBLE PRECISION NOT NULL,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE RESTRICT,
    output_metric_value NUMERIC(20, 4) NOT NULL,
    delta_from_baseline NUMERIC(20, 4) NOT NULL,
    stabilized_total_ggr NUMERIC(20, 2) NOT NULL,
    local_discretionary_displacement NUMERIC(20, 2) NOT NULL,
    gross_gaming_tax NUMERIC(20, 2) NOT NULL,
    gross_social_cost NUMERIC(20, 2) NOT NULL,
    net_permanent_jobs DOUBLE PRECISION NOT NULL,
    net_host_local_impact NUMERIC(20, 2) NOT NULL,
    net_host_state_impact NUMERIC(20, 2) NOT NULL,
    UNIQUE (sensitivity_analysis_id, parameter_key, direction),
    UNIQUE (sensitivity_analysis_id, model_run_id),
    CHECK (direction IN ('low', 'high'))
);

CREATE OR REPLACE FUNCTION prevent_immutable_sensitivity_point_mutation()
RETURNS TRIGGER AS $$
DECLARE analysis_id UUID;
DECLARE immutable BOOLEAN;
BEGIN
    analysis_id := COALESCE(NEW.sensitivity_analysis_id, OLD.sensitivity_analysis_id);
    SELECT is_immutable INTO immutable FROM sensitivity_analyses WHERE id = analysis_id;
    IF immutable THEN
        RAISE EXCEPTION 'Finalized sensitivity analysis % is immutable', analysis_id;
    END IF;
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_immutable_sensitivity_point_mutation ON sensitivity_analysis_points;
CREATE TRIGGER trg_prevent_immutable_sensitivity_point_mutation
BEFORE INSERT OR UPDATE OR DELETE ON sensitivity_analysis_points
FOR EACH ROW EXECUTE FUNCTION prevent_immutable_sensitivity_point_mutation();

CREATE OR REPLACE FUNCTION prevent_finalized_sensitivity_analysis_change()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.is_immutable THEN
        RAISE EXCEPTION 'Finalized sensitivity analysis % is immutable', OLD.id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_finalized_sensitivity_analysis_change ON sensitivity_analyses;
CREATE TRIGGER trg_prevent_finalized_sensitivity_analysis_change
BEFORE UPDATE OR DELETE ON sensitivity_analyses
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_sensitivity_analysis_change();
