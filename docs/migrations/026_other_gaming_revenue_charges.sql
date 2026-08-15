-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

BEGIN;

ALTER TABLE model_run_fiscal_impacts
    ADD COLUMN IF NOT EXISTS other_gaming_revenue_charges NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS municipal_other_gaming_revenue_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS county_other_gaming_revenue_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS regional_other_gaming_revenue_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS state_other_gaming_revenue_share NUMERIC(20, 2) NOT NULL DEFAULT 0;

ALTER TABLE model_run_fiscal_impacts
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_other_gaming_revenue_charges,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_municipal_other_gaming_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_county_other_gaming_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_regional_other_gaming_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_state_other_gaming_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_other_gaming_reconciliation;

ALTER TABLE model_run_fiscal_impacts
    ADD CONSTRAINT ck_model_run_fiscal_other_gaming_revenue_charges CHECK (other_gaming_revenue_charges >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_municipal_other_gaming_share CHECK (municipal_other_gaming_revenue_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_county_other_gaming_share CHECK (county_other_gaming_revenue_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_regional_other_gaming_share CHECK (regional_other_gaming_revenue_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_state_other_gaming_share CHECK (state_other_gaming_revenue_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_other_gaming_reconciliation CHECK (
        ABS(other_gaming_revenue_charges - (
            municipal_other_gaming_revenue_share +
            county_other_gaming_revenue_share +
            regional_other_gaming_revenue_share +
            state_other_gaming_revenue_share)) <= 0.01
    ) NOT VALID;

COMMIT;
