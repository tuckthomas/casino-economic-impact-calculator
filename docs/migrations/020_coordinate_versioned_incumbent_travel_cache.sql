-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

BEGIN;

ALTER TABLE origin_facility_travel
    ADD COLUMN IF NOT EXISTS facility_coordinate_hash VARCHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS facility_latitude DOUBLE PRECISION NULL,
    ADD COLUMN IF NOT EXISTS facility_longitude DOUBLE PRECISION NULL;

UPDATE origin_facility_travel travel
SET facility_latitude = competitor.latitude,
    facility_longitude = competitor.longitude,
    facility_coordinate_hash = 'legacy-' || travel.id::text
FROM casino_competitors competitor
WHERE travel.casino_competitor_id = competitor.id
  AND travel.facility_coordinate_hash IS NULL;

UPDATE origin_facility_travel travel
SET facility_latitude = run.candidate_latitude,
    facility_longitude = run.candidate_longitude,
    facility_coordinate_hash = 'legacy-' || travel.id::text
FROM model_runs run
WHERE travel.model_run_id = run.id
  AND travel.facility_coordinate_hash IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM origin_facility_travel
        WHERE facility_coordinate_hash IS NULL
           OR facility_latitude IS NULL
           OR facility_longitude IS NULL) THEN
        RAISE EXCEPTION 'Every persisted route must resolve an exact facility coordinate before coordinate-versioned caching is enabled';
    END IF;
END;
$$;

ALTER TABLE origin_facility_travel
    ALTER COLUMN facility_coordinate_hash SET NOT NULL,
    ALTER COLUMN facility_latitude SET NOT NULL,
    ALTER COLUMN facility_longitude SET NOT NULL;

DO $$
DECLARE
    constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = 'origin_facility_travel'::regclass
          AND contype = 'u'
          AND pg_get_constraintdef(oid) =
              'UNIQUE (origin_zone_id, facility_key, routing_graph_hash, costing_profile)'
    LOOP
        EXECUTE format('ALTER TABLE origin_facility_travel DROP CONSTRAINT %I', constraint_name);
    END LOOP;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_origin_facility_travel_location_identity
    ON origin_facility_travel (
        origin_zone_id,
        facility_key,
        facility_coordinate_hash,
        routing_graph_hash,
        costing_profile);

ALTER TABLE origin_facility_travel
    DROP CONSTRAINT IF EXISTS ck_origin_facility_travel_latitude,
    DROP CONSTRAINT IF EXISTS ck_origin_facility_travel_longitude;
ALTER TABLE origin_facility_travel
    ADD CONSTRAINT ck_origin_facility_travel_latitude
        CHECK (facility_latitude BETWEEN -90 AND 90),
    ADD CONSTRAINT ck_origin_facility_travel_longitude
        CHECK (facility_longitude BETWEEN -180 AND 180);

COMMIT;
