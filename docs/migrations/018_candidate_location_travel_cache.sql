-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-- SaveNEIN Advanced Economic Modeling Subsystem
-- Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
-- Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

BEGIN;

CREATE TABLE IF NOT EXISTS candidate_location_travel_cache (
    id BIGSERIAL PRIMARY KEY,
    origin_zone_id BIGINT NOT NULL REFERENCES origin_zones(id) ON DELETE CASCADE,
    candidate_coordinate_hash VARCHAR(64) NOT NULL,
    candidate_latitude DOUBLE PRECISION NOT NULL,
    candidate_longitude DOUBLE PRECISION NOT NULL,
    routing_graph_hash VARCHAR(128) NOT NULL,
    valhalla_version VARCHAR(80) NOT NULL,
    tileset_last_modified BIGINT NULL,
    costing_profile VARCHAR(40) NOT NULL,
    travel_time_minutes DOUBLE PRECISION NULL,
    routed_distance_meters DOUBLE PRECISION NULL,
    route_found BOOLEAN NOT NULL,
    route_failure_reason VARCHAR(500) NULL,
    calculated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_candidate_location_travel_cache_latitude
        CHECK (candidate_latitude BETWEEN -90 AND 90),
    CONSTRAINT ck_candidate_location_travel_cache_longitude
        CHECK (candidate_longitude BETWEEN -180 AND 180),
    CONSTRAINT ck_candidate_location_travel_cache_route_values
        CHECK ((route_found AND travel_time_minutes IS NOT NULL AND routed_distance_meters IS NOT NULL)
            OR (NOT route_found AND travel_time_minutes IS NULL AND routed_distance_meters IS NULL))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_candidate_location_travel_cache_identity
    ON candidate_location_travel_cache (
        origin_zone_id,
        candidate_coordinate_hash,
        routing_graph_hash,
        costing_profile);

COMMIT;
