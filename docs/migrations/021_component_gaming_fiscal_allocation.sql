-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

BEGIN;

ALTER TABLE model_run_fiscal_impacts
    ADD COLUMN IF NOT EXISTS base_gaming_tax NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS supplemental_gaming_tax NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS host_municipality_gaming_tax_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS host_county_gaming_tax_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS host_regional_gaming_tax_share NUMERIC(20, 2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS host_state_gaming_tax_share NUMERIC(20, 2) NOT NULL DEFAULT 0;

ALTER TABLE model_run_fiscal_impacts
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_base_gaming_tax,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_supplemental_gaming_tax,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_municipality_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_county_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_regional_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_state_share,
    DROP CONSTRAINT IF EXISTS ck_model_run_fiscal_component_reconciliation;

ALTER TABLE model_run_fiscal_impacts
    ADD CONSTRAINT ck_model_run_fiscal_base_gaming_tax CHECK (base_gaming_tax >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_supplemental_gaming_tax CHECK (supplemental_gaming_tax >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_municipality_share CHECK (host_municipality_gaming_tax_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_county_share CHECK (host_county_gaming_tax_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_regional_share CHECK (host_regional_gaming_tax_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_state_share CHECK (host_state_gaming_tax_share >= 0),
    ADD CONSTRAINT ck_model_run_fiscal_component_reconciliation CHECK (
        ABS(gross_gaming_tax - (base_gaming_tax + supplemental_gaming_tax)) <= 0.01
        AND ABS(gross_gaming_tax - (
            host_municipality_gaming_tax_share +
            host_county_gaming_tax_share +
            host_regional_gaming_tax_share +
            host_state_gaming_tax_share)) <= 0.01
    ) NOT VALID;

COMMIT;
