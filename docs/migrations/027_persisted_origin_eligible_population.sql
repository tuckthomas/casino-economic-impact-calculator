-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

BEGIN;

-- Historical runs retain NULL because their computed population was not persisted.
-- Do not reconstruct it from sources or jurisdiction rules that may have changed.
ALTER TABLE model_run_origin_results
    ADD COLUMN IF NOT EXISTS eligible_population DOUBLE PRECISION;
ALTER TABLE model_run_origin_results
    DROP CONSTRAINT IF EXISTS ck_model_run_origin_eligible_population;
ALTER TABLE model_run_origin_results
    ADD CONSTRAINT ck_model_run_origin_eligible_population CHECK (
        eligible_population IS NULL OR
        (eligible_population >= 0 AND eligible_population < 'Infinity'::DOUBLE PRECISION)
    );

COMMIT;
