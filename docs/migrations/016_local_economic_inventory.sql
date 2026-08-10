-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

-- 016_local_economic_inventory.sql
-- Versioned local business/economic inventory used to modulate displacement-sector priors.

CREATE TABLE IF NOT EXISTS local_economic_sector_observations (
    id BIGSERIAL PRIMARY KEY,
    dataset_snapshot_id UUID NOT NULL REFERENCES dataset_snapshots(id),
    stable_observation_id VARCHAR(160) NOT NULL,
    geography_type VARCHAR(40) NOT NULL,
    geography_code VARCHAR(80) NOT NULL,
    sector_key VARCHAR(80) NOT NULL,
    naics_codes_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    establishments BIGINT NULL,
    employment BIGINT NULL,
    annual_payroll NUMERIC(20, 2) NULL,
    annual_receipts_or_sales NUMERIC(20, 2) NULL,
    source_metric_definition VARCHAR(200) NOT NULL,
    notes TEXT NULL,
    UNIQUE (dataset_snapshot_id, stable_observation_id),
    CHECK (period_end >= period_start),
    CHECK (jsonb_typeof(naics_codes_json) = 'array'),
    CHECK (jsonb_array_length(naics_codes_json) > 0),
    CHECK (establishments IS NULL OR establishments >= 0),
    CHECK (employment IS NULL OR employment >= 0),
    CHECK (annual_payroll IS NULL OR annual_payroll >= 0),
    CHECK (annual_receipts_or_sales IS NULL OR annual_receipts_or_sales >= 0),
    CHECK (
        establishments IS NOT NULL OR
        employment IS NOT NULL OR
        annual_payroll IS NOT NULL OR
        annual_receipts_or_sales IS NOT NULL
    )
);

CREATE INDEX IF NOT EXISTS ix_local_economic_sector_observations_geography
    ON local_economic_sector_observations (
        dataset_snapshot_id,
        geography_type,
        geography_code,
        sector_key
    );

DROP TRIGGER IF EXISTS trg_prevent_sealed_local_economic_observation_mutation
    ON local_economic_sector_observations;
CREATE TRIGGER trg_prevent_sealed_local_economic_observation_mutation
BEFORE INSERT OR UPDATE OR DELETE ON local_economic_sector_observations
FOR EACH ROW EXECUTE FUNCTION prevent_sealed_snapshot_data_mutation();
