using Npgsql;

namespace SaveNEIN.Server.Data;

internal static class TigerPlaceSchema
{
    private static readonly string[] MunicipalityLookupColumns =
    {
        "geoid",
        "name",
        "state_fp",
        "place_fp",
        "funcstat",
        "lsad",
        "aland",
        "awater",
        "geom"
    };

    /// <summary>
    /// Upgrades an existing TIGER PLACE table to the schema required by municipality
    /// containment. CREATE TABLE IF NOT EXISTS alone is insufficient for long-lived
    /// production databases because it does not add columns introduced after the table
    /// was first created.
    /// </summary>
    public static async Task<bool> EnsureMunicipalityLookupContractAsync(NpgsqlConnection conn)
    {
        var existingColumns = await GetColumnsAsync(conn);
        var schemaChanged = MunicipalityLookupColumns.Any(column => !existingColumns.Contains(column));

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS tiger_places (
                geoid text PRIMARY KEY,
                name text NOT NULL,
                state_fp text NOT NULL,
                place_fp text NOT NULL,
                funcstat text,
                lsad text,
                aland bigint,
                awater bigint,
                geom geometry(MultiPolygon, 4326)
            );

            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS name text;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS state_fp text;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS place_fp text;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS funcstat text;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS lsad text;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS aland bigint;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS awater bigint;
            ALTER TABLE tiger_places ADD COLUMN IF NOT EXISTS geom geometry(MultiPolygon, 4326);

            CREATE INDEX IF NOT EXISTS idx_tiger_places_geom ON tiger_places USING GIST (geom);
            CREATE INDEX IF NOT EXISTS idx_tiger_places_state_fp ON tiger_places (state_fp);
            CREATE INDEX IF NOT EXISTS idx_tiger_places_funcstat ON tiger_places (funcstat);
            CREATE INDEX IF NOT EXISTS idx_tiger_places_name ON tiger_places (name);
        ";
        await cmd.ExecuteNonQueryAsync();

        return schemaChanged;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(NpgsqlConnection conn)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'tiger_places';
        ";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
