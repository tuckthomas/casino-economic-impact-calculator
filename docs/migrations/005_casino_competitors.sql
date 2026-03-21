-- 005_casino_competitors.sql
-- Creates the casino_competitors table and indexes

CREATE TABLE IF NOT EXISTS casino_competitors (
    id SERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    state VARCHAR(2) NOT NULL,
    county VARCHAR(100),
    city VARCHAR(100),
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    notes TEXT,
    
    venue_type VARCHAR(50) NOT NULL,
    operator_name VARCHAR(200),
    market_notes TEXT,
    source_url VARCHAR(500),
    last_verified_at TIMESTAMP WITH TIME ZONE,
    
    has_slots BOOLEAN NOT NULL DEFAULT FALSE,
    has_table_games BOOLEAN NOT NULL DEFAULT FALSE,
    has_poker BOOLEAN NOT NULL DEFAULT FALSE,
    has_sportsbook BOOLEAN NOT NULL DEFAULT FALSE,
    has_racetrack BOOLEAN NOT NULL DEFAULT FALSE,
    has_hotel BOOLEAN NOT NULL DEFAULT FALSE,
    has_restaurants BOOLEAN NOT NULL DEFAULT FALSE,
    has_entertainment BOOLEAN NOT NULL DEFAULT FALSE,
    has_loyalty_program BOOLEAN NOT NULL DEFAULT FALSE,
    has_resort_amenities BOOLEAN NOT NULL DEFAULT FALSE,
    
    estimated_competition_weight DOUBLE PRECISION,
    geom geometry(Point, 4326) NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_casino_competitors_geom ON casino_competitors USING gist (geom);
