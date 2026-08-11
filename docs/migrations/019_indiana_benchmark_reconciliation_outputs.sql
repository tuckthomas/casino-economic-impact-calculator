-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 019_indiana_benchmark_reconciliation_outputs.sql
-- Expose the two published CBRE local-model outputs as reported output metrics so
-- the generic public-benchmark case API can read them without accepting target
-- values from a caller. They remain duplicated in reported assumptions because
-- the original source presents them as component-model anchors for the total GGR.

UPDATE benchmark_studies
SET reported_outputs_json = jsonb_set(
        jsonb_set(
            reported_outputs_json,
            '{stabilizedAnnual,localGravityGrossGamingRevenue}',
            '216700000'::jsonb,
            true),
        '{stabilizedAnnual,localRegressionGrossGamingRevenue}',
        '215000000'::jsonb,
        true)
WHERE benchmark_key = 'cbre-union-gaming-fort-wayne-2025'
  AND source_file_checksum = '1A00F19766BA0361D4E8A6514D32701727BEDEFCADA73CCFA90729DB8107A510';

DO $$
DECLARE
    outputs jsonb;
BEGIN
    SELECT reported_outputs_json
    INTO outputs
    FROM benchmark_studies
    WHERE benchmark_key = 'cbre-union-gaming-fort-wayne-2025';

    IF outputs #>> '{stabilizedAnnual,localGravityGrossGamingRevenue}' <> '216700000' OR
       outputs #>> '{stabilizedAnnual,localRegressionGrossGamingRevenue}' <> '215000000' THEN
        RAISE EXCEPTION 'CBRE local benchmark reconciliation outputs were not persisted';
    END IF;
END;
$$;
