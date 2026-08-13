-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

ALTER TABLE casino_competitors
    ADD COLUMN IF NOT EXISTS reported_employment integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_casino_competitors_reported_employment_positive'
    ) THEN
        ALTER TABLE casino_competitors
            ADD CONSTRAINT ck_casino_competitors_reported_employment_positive
            CHECK (reported_employment IS NULL OR reported_employment > 0);
    END IF;
END $$;
