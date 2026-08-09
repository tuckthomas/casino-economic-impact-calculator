-- Preserve the difference between an observed false facility attribute and an
-- attribute that the provider did not report. Attraction inputs must not turn
-- missing evidence into a negative feature.
ALTER TABLE casino_competitors
    ALTER COLUMN has_slots DROP DEFAULT,
    ALTER COLUMN has_slots DROP NOT NULL,
    ALTER COLUMN has_table_games DROP DEFAULT,
    ALTER COLUMN has_table_games DROP NOT NULL,
    ALTER COLUMN has_poker DROP DEFAULT,
    ALTER COLUMN has_poker DROP NOT NULL,
    ALTER COLUMN has_sportsbook DROP DEFAULT,
    ALTER COLUMN has_sportsbook DROP NOT NULL,
    ALTER COLUMN has_racetrack DROP DEFAULT,
    ALTER COLUMN has_racetrack DROP NOT NULL,
    ALTER COLUMN has_hotel DROP DEFAULT,
    ALTER COLUMN has_hotel DROP NOT NULL,
    ALTER COLUMN has_restaurants DROP DEFAULT,
    ALTER COLUMN has_restaurants DROP NOT NULL,
    ALTER COLUMN has_entertainment DROP DEFAULT,
    ALTER COLUMN has_entertainment DROP NOT NULL,
    ALTER COLUMN has_loyalty_program DROP DEFAULT,
    ALTER COLUMN has_loyalty_program DROP NOT NULL,
    ALTER COLUMN has_resort_amenities DROP DEFAULT,
    ALTER COLUMN has_resort_amenities DROP NOT NULL,
    ALTER COLUMN is_border_market DROP DEFAULT,
    ALTER COLUMN is_border_market DROP NOT NULL;
