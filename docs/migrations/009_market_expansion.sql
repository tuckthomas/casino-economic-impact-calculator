-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 009_market_expansion.sql
-- Explicit accessibility-induced resident demand and per-facility attribution.

ALTER TABLE model_run_origin_results
    ADD COLUMN IF NOT EXISTS baseline_log_accessibility DOUBLE PRECISION NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS with_project_log_accessibility DOUBLE PRECISION NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS induced_resident_demand NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS induced_outside_option_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS proposed_induced_resident_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_proposed_resident_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0;

ALTER TABLE model_run_facility_results
    ADD COLUMN IF NOT EXISTS induced_resident_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_with_project_resident_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0;

ALTER TABLE model_run_origin_facility_allocations
    ADD COLUMN IF NOT EXISTS allocated_induced_resident_ggr NUMERIC(20, 2) NOT NULL DEFAULT 0;

ALTER TABLE model_run_origin_results
    DROP CONSTRAINT IF EXISTS ck_model_run_origin_results_induced_nonnegative;
ALTER TABLE model_run_origin_results
    ADD CONSTRAINT ck_model_run_origin_results_induced_nonnegative
    CHECK (
        induced_resident_demand >= 0 AND
        induced_outside_option_ggr >= 0 AND
        proposed_induced_resident_ggr >= 0 AND
        total_proposed_resident_ggr >= 0);

ALTER TABLE model_run_facility_results
    DROP CONSTRAINT IF EXISTS ck_model_run_facility_results_induced_nonnegative;
ALTER TABLE model_run_facility_results
    ADD CONSTRAINT ck_model_run_facility_results_induced_nonnegative
    CHECK (induced_resident_ggr >= 0 AND total_with_project_resident_ggr >= 0);

ALTER TABLE model_run_origin_facility_allocations
    DROP CONSTRAINT IF EXISTS ck_model_run_allocations_induced_nonnegative;
ALTER TABLE model_run_origin_facility_allocations
    ADD CONSTRAINT ck_model_run_allocations_induced_nonnegative
    CHECK (allocated_induced_resident_ggr >= 0);
