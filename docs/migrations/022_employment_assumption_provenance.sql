-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem

ALTER TABLE model_run_employment_impacts
    ADD COLUMN IF NOT EXISTS direct_average_annual_wage numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS indirect_average_annual_wage numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS incumbent_average_annual_wage numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS assumption_provenance_json text NOT NULL DEFAULT '{}';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_model_run_employment_wages_nonnegative'
    ) THEN
        ALTER TABLE model_run_employment_impacts
            ADD CONSTRAINT ck_model_run_employment_wages_nonnegative CHECK (
                direct_average_annual_wage >= 0 AND
                indirect_average_annual_wage >= 0 AND
                incumbent_average_annual_wage >= 0
            );
    END IF;
END $$;
