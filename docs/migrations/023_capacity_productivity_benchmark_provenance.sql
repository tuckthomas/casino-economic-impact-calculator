-- Persist the exact regulator-derived productivity interval used by each capacity diagnostic.

ALTER TABLE casino_gaming_revenue_periods
    ADD COLUMN IF NOT EXISTS reported_unit_count DOUBLE PRECISION NULL;

ALTER TABLE model_run_capacity_diagnostics
    ADD COLUMN IF NOT EXISTS benchmark_dataset_snapshot_id UUID NULL,
    ADD COLUMN IF NOT EXISTS benchmark_method VARCHAR(80) NULL,
    ADD COLUMN IF NOT EXISTS benchmark_sample_size INTEGER NULL,
    ADD COLUMN IF NOT EXISTS slot_win_per_unit_day_minimum DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS slot_win_per_unit_day_maximum DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS table_win_per_table_day_minimum DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS table_win_per_table_day_maximum DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS benchmark_provenance_json JSONB NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_casino_gaming_revenue_reported_unit_count'
    ) THEN
        ALTER TABLE casino_gaming_revenue_periods
            ADD CONSTRAINT ck_casino_gaming_revenue_reported_unit_count CHECK (
                reported_unit_count IS NULL OR reported_unit_count > 0
            );
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_model_run_capacity_benchmark_snapshot'
    ) THEN
        ALTER TABLE model_run_capacity_diagnostics
            ADD CONSTRAINT fk_model_run_capacity_benchmark_snapshot
            FOREIGN KEY (benchmark_dataset_snapshot_id) REFERENCES dataset_snapshots(id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_model_run_capacity_benchmark_values'
    ) THEN
        ALTER TABLE model_run_capacity_diagnostics
            ADD CONSTRAINT ck_model_run_capacity_benchmark_values CHECK (
                (benchmark_sample_size IS NULL OR benchmark_sample_size >= 0) AND
                (slot_win_per_unit_day_minimum IS NULL OR slot_win_per_unit_day_minimum >= 0) AND
                (slot_win_per_unit_day_maximum IS NULL OR slot_win_per_unit_day_maximum >= slot_win_per_unit_day_minimum) AND
                (table_win_per_table_day_minimum IS NULL OR table_win_per_table_day_minimum >= 0) AND
                (table_win_per_table_day_maximum IS NULL OR table_win_per_table_day_maximum >= table_win_per_table_day_minimum)
            );
    END IF;
END $$;
