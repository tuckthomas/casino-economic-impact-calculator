-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

CREATE TABLE IF NOT EXISTS validation_geographic_residual_patterns (
    id BIGSERIAL PRIMARY KEY,
    validation_evaluation_id UUID NOT NULL REFERENCES validation_evaluations(id) ON DELETE RESTRICT,
    prediction_kind VARCHAR(40) NOT NULL,
    dataset_partition VARCHAR(30) NOT NULL,
    geography_kind VARCHAR(40) NOT NULL,
    geography_code VARCHAR(160) NOT NULL,
    observation_count INTEGER NOT NULL,
    observed_revenue NUMERIC(20, 2) NOT NULL,
    predicted_revenue NUMERIC(20, 2) NOT NULL,
    residual NUMERIC(20, 2) NOT NULL,
    mean_residual NUMERIC(20, 2) NOT NULL,
    mean_absolute_error NUMERIC(20, 2) NOT NULL,
    mean_absolute_percentage_error DOUBLE PRECISION NULL,
    symmetric_mean_absolute_percentage_error DOUBLE PRECISION NOT NULL,
    overprediction_count INTEGER NOT NULL,
    underprediction_count INTEGER NOT NULL,
    exact_prediction_count INTEGER NOT NULL,
    UNIQUE (
        validation_evaluation_id,
        prediction_kind,
        dataset_partition,
        geography_kind,
        geography_code),
    CHECK (prediction_kind IN ('gravity', 'comparable-log-linear')),
    CHECK (dataset_partition IN ('training', 'holdout', 'benchmark')),
    CHECK (geography_kind IN ('market', 'jurisdiction', 'holdout-group')),
    CHECK (observation_count > 0),
    CHECK (observed_revenue >= 0),
    CHECK (predicted_revenue >= 0),
    CHECK (mean_absolute_error >= 0),
    CHECK (mean_absolute_percentage_error IS NULL OR mean_absolute_percentage_error >= 0),
    CHECK (symmetric_mean_absolute_percentage_error BETWEEN 0 AND 200),
    CHECK (overprediction_count >= 0),
    CHECK (underprediction_count >= 0),
    CHECK (exact_prediction_count >= 0),
    CHECK (overprediction_count + underprediction_count + exact_prediction_count = observation_count)
);

DROP TRIGGER IF EXISTS trg_prevent_immutable_validation_geo_residual_mutation
    ON validation_geographic_residual_patterns;
CREATE TRIGGER trg_prevent_immutable_validation_geo_residual_mutation
BEFORE INSERT OR UPDATE OR DELETE ON validation_geographic_residual_patterns
FOR EACH ROW EXECUTE FUNCTION prevent_immutable_validation_evaluation_mutation();
