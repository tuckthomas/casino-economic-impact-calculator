using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SaveFW.Server.Services;

namespace SaveFW.Server.Data
{
    public class TigerSeeder
    {
        private readonly TigerIngestionService _ingestionService;
        private readonly ILogger<TigerSeeder> _logger;
        private readonly IConfiguration _config;
        private static readonly string[] BlockGroupStateFips = new[]
        {
            "01", "02", "04", "05", "06", "08", "09", "10", "11", "12",
            "13", "15", "16", "17", "18", "19", "20", "21", "22", "23",
            "24", "25", "26", "27", "28", "29", "30", "31", "32", "33",
            "34", "35", "36", "37", "38", "39", "40", "41", "42", "44",
            "45", "46", "47", "48", "49", "50", "51", "53", "54", "55",
            "56", "60", "66", "69", "72", "78"
        };

        public TigerSeeder(TigerIngestionService ingestionService, ILogger<TigerSeeder> logger, IConfiguration config)
        {
            _ingestionService = ingestionService;
            _logger = logger;
            _config = config;
        }

        public async Task EnsureSeededAsync()
        {
            var connString = _config.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // 1. Check if States exist
            if (!await HasData(conn, "tiger_states"))
            {
                _logger.LogInformation("TigerSeeder: No states found. Seeding National States...");
                await _ingestionService.IngestNationalStates();
            }
            else
            {
                _logger.LogInformation("TigerSeeder: States already seeded.");
            }

            // 2. Check if Counties exist
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM tiger_counties";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                if (count < 3000) // US has ~3143 counties
                {
                    _logger.LogInformation($"TigerSeeder: Found {count} counties. Seeding/Updating National Counties...");
                    await _ingestionService.IngestNationalCounties();
                }
                else
                {
                    _logger.LogInformation("TigerSeeder: Counties already seeded (count > 3000).");
                }
            }

            // 3. Check if Block Groups exist for each state
            _logger.LogInformation("TigerSeeder: Checking block group data for all states/territories...");
            foreach (var fips in BlockGroupStateFips)
            {
                using var cmdState = conn.CreateCommand();
                cmdState.CommandText = "SELECT 1 FROM census_block_groups WHERE substring(geoid, 1, 2) = @fips LIMIT 1;";
                var p = cmdState.CreateParameter();
                p.ParameterName = "fips";
                p.Value = fips;
                cmdState.Parameters.Add(p);

                var stateHasData = await cmdState.ExecuteScalarAsync();
                if (stateHasData == null)
                {
                    _logger.LogInformation($"TigerSeeder: No block groups found for state {fips}. Ingesting...");
                    await _ingestionService.IngestState(fips);
                }

                // Check/Ingest Address Ranges
                // Since tiger_address_ranges doesn't have a state column, we use a simple "HasData" check 
                // to prevent re-ingesting 9GB of data on every startup.
                // To force a re-seed, the user must TRUNCATE/DROP the tiger_address_ranges table.
            }
            
            // Check if Address Ranges exist globally (once, outside the loop)
            if (!await HasData(conn, "tiger_address_ranges"))
            {
                _logger.LogInformation("TigerSeeder: No address ranges found. Ingesting for all defined states...");
                foreach (var fips in BlockGroupStateFips)
                {
                     _logger.LogInformation($"TigerSeeder: Ingesting address ranges for state {fips}...");
                     await _ingestionService.IngestAddressRanges(fips);
                }
            }
            else
            {
                _logger.LogInformation("TigerSeeder: Address ranges already seeded (table not empty). Skipping ingestion.");
            }
            _logger.LogInformation("TigerSeeder: Block Group & Address Range seeding check complete.");

            // 4. Ensure simplified geometry columns exist and are populated (visualization only)
            await EnsureSimplifiedGeometriesAsync(conn);
        }

        private async Task<bool> HasData(NpgsqlConnection conn, string tableName)
        {
            // First check if table exists to avoid exception
            using var cmdExists = conn.CreateCommand();
            cmdExists.CommandText = $"SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = '{tableName}');";
            var exists = (bool?)await cmdExists.ExecuteScalarAsync();
            if (exists != true) return false;

            // Check if rows exist
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {tableName} LIMIT 1;";
            var res = await cmd.ExecuteScalarAsync();
            return res != null;
        }

        private async Task EnsureSimplifiedGeometriesAsync(NpgsqlConnection conn)
        {
            await EnsureSimplifiedForTable(conn, "tiger_states", 100, 10000);
            await EnsureSimplifiedForTable(conn, "tiger_counties", 100, 10000);
            await EnsureSimplifiedForTable(conn, "census_block_groups", 10, 5000);
        }

        private async Task EnsureSimplifiedForTable(NpgsqlConnection conn, string tableName, int toleranceMeters, int batchSize)
        {
            // Skip if table doesn't exist
            using var cmdExists = conn.CreateCommand();
            cmdExists.CommandText = $"SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = '{tableName}');";
            var exists = (bool?)await cmdExists.ExecuteScalarAsync();
            if (exists != true) return;

            // Add simplified column and index if missing
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    ALTER TABLE {tableName}
                    ADD COLUMN IF NOT EXISTS geom_simplified geometry(MultiPolygon, 4326);
                    CREATE INDEX IF NOT EXISTS idx_{tableName}_geom_simplified
                    ON {tableName} USING GIST (geom_simplified);
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Populate once (only rows missing simplified geometry)
            // Skip if already fully simplified
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT EXISTS (SELECT 1 FROM {tableName} WHERE geom_simplified IS NULL LIMIT 1);";
                var needs = (bool?)await cmd.ExecuteScalarAsync();
                if (needs != true) return;
            }

            _logger.LogInformation($"TigerSeeder: Simplifying {tableName} (tolerance {toleranceMeters}m)...");
            var totalUpdated = 0;
            while (true)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 0;
                cmd.CommandText = $@"
                    WITH todo AS (
                        SELECT ctid
                        FROM {tableName}
                        WHERE geom_simplified IS NULL
                        LIMIT @batch
                    )
                    UPDATE {tableName} t
                    SET geom_simplified = ST_Transform(
                        ST_SimplifyPreserveTopology(ST_Transform(t.geom, 3857), @tol),
                        4326
                    )
                    FROM todo
                    WHERE t.ctid = todo.ctid;
                ";
                cmd.Parameters.AddWithValue("tol", toleranceMeters);
                cmd.Parameters.AddWithValue("batch", batchSize);
                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated <= 0) break;
                totalUpdated += updated;
                _logger.LogInformation($"TigerSeeder: {tableName} simplified {totalUpdated} rows...");
            }
        }

        // No meta table required; skip when geom_simplified is fully populated.
    }
}
