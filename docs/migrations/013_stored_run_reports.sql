-- 013_stored_run_reports.sql
-- Deterministic, immutable HTML/PDF/JSON/CSV report bundles derived from finalized model runs.

CREATE TABLE IF NOT EXISTS model_run_report_artifacts (
    id UUID PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE RESTRICT,
    template_version VARCHAR(80) NOT NULL,
    presentation_options_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    presentation_options_hash VARCHAR(64) NOT NULL,
    report_model_json JSONB NOT NULL,
    report_model_hash VARCHAR(64) NOT NULL,
    html_content TEXT NOT NULL,
    html_content_hash VARCHAR(64) NOT NULL,
    pdf_content BYTEA NOT NULL,
    pdf_content_hash VARCHAR(64) NOT NULL,
    csv_content TEXT NOT NULL,
    csv_content_hash VARCHAR(64) NOT NULL,
    generated_at_utc TIMESTAMPTZ NOT NULL,
    is_immutable BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (model_run_id, template_version, presentation_options_hash),
    CHECK (length(presentation_options_hash) = 64),
    CHECK (length(report_model_hash) = 64),
    CHECK (length(html_content_hash) = 64),
    CHECK (length(pdf_content_hash) = 64),
    CHECK (length(csv_content_hash) = 64),
    CHECK (octet_length(pdf_content) > 0),
    CHECK (is_immutable)
);

CREATE OR REPLACE FUNCTION prevent_model_run_report_artifact_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Stored model-run report artifacts are immutable';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_model_run_report_artifact_mutation ON model_run_report_artifacts;
CREATE TRIGGER trg_prevent_model_run_report_artifact_mutation
BEFORE UPDATE OR DELETE ON model_run_report_artifacts
FOR EACH ROW EXECUTE FUNCTION prevent_model_run_report_artifact_mutation();
