using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Data
{
    public class TigerSeeder
    {
        private readonly TigerIngestionService _ingestionService;
        private readonly ILogger<TigerSeeder> _logger;
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;
        private static readonly string[] BlockGroupStateFips = new[]
        {
            "18", "17", "21", "26", "39", "55", // Launch and bordering casino catchment region first.
            "01", "02", "04", "05", "06", "08", "09", "10", "11", "12",
            "13", "15", "16", "19", "20", "22", "23",
            "24", "25", "27", "28", "29", "30", "31", "32", "33",
            "34", "35", "36", "37", "38", "40", "41", "42", "44",
            "45", "46", "47", "48", "49", "50", "51", "53", "54",
            "56", "60", "66", "69", "72", "78"
        };
        private static readonly string[] PopulationStateFips = { "18", "17", "21", "26", "39", "55" };

        public TigerSeeder(
            TigerIngestionService ingestionService,
            ILogger<TigerSeeder> logger,
            IConfiguration config,
            AppDbContext db)
        {
            _ingestionService = ingestionService;
            _logger = logger;
            _config = config;
            _db = db;
        }

        /// <summary>
        /// Ensures PLACE geometry required by validated, municipality-dependent fiscal rules is
        /// available before requests are served. Required states are derived from jurisdiction
        /// data rather than a parallel tax-allocation configuration.
        /// </summary>
        public async Task EnsureRequiredFiscalPlaceDataAsync()
        {
            var ruleJson = await _db.JurisdictionRules.AsNoTracking()
                .Where(rule => rule.RuleType == JurisdictionRuleTypes.GamingTaxDistribution &&
                               rule.ValidationState == JurisdictionRuleValidationStates.Validated)
                .Select(rule => rule.RuleValueJson)
                .ToArrayAsync();
            var requiredStateFips = ruleJson
                .Select(json => JsonSerializer.Deserialize<GamingTaxDistributionPayload>(json, JurisdictionJson.Options))
                .Where(payload => payload?.MunicipalityRequired == true)
                .SelectMany(payload => payload!.EligibleCountyFips)
                .Where(countyFips => countyFips.Length == 5 && countyFips.All(char.IsAsciiDigit))
                .Select(countyFips => countyFips[..2])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (requiredStateFips.Length == 0)
            {
                return;
            }

            var connectionString = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is required for fiscal PLACE readiness.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var placeSchemaChanged = await TigerPlaceSchema.EnsureMunicipalityLookupContractAsync(connection);
            if (placeSchemaChanged)
            {
                _logger.LogWarning(
                    "TigerSeeder: Existing TIGER PLACE schema was missing municipality lookup fields. Required states will be reingested before startup.");
            }

            foreach (var stateFips in requiredStateFips)
            {
                if (placeSchemaChanged || !await HasUsablePlaceDataForStateAsync(connection, stateFips))
                {
                    await _ingestionService.IngestPlacesForState(stateFips);
                }
                if (!await HasUsablePlaceDataForStateAsync(connection, stateFips))
                {
                    throw new InvalidOperationException(
                        $"TIGER PLACE data required by validated fiscal rules for state {stateFips} is unavailable.");
                }

                await VerifyMunicipalityLookupContractAsync(connection, stateFips);

                _logger.LogInformation(
                    "TigerSeeder: Fiscal PLACE readiness verified for state {StateFips}.",
                    stateFips);
            }
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
            var countyCount = await GetRowCountAsync(conn, "tiger_counties");
            if (countyCount < 3000) // US has ~3143 counties
            {
                _logger.LogInformation($"TigerSeeder: Found {countyCount} counties. Seeding/Updating National Counties...");
                await _ingestionService.IngestNationalCounties();
            }
            else
            {
                _logger.LogInformation("TigerSeeder: Counties already seeded (count >= 3000).");
            }

            // 2b. Check if Places exist for each state/territory
            _logger.LogInformation("TigerSeeder: Checking place data for all states/territories...");
            var hasPlacesTable = await TableExists(conn, "tiger_places");
            foreach (var fips in BlockGroupStateFips)
            {
                object? stateHasPlaces = null;
                if (hasPlacesTable)
                {
                    using var cmdState = conn.CreateCommand();
                    cmdState.CommandText = "SELECT 1 FROM tiger_places WHERE state_fp = @fips LIMIT 1;";
                    var p = cmdState.CreateParameter();
                    p.ParameterName = "fips";
                    p.Value = fips;
                    cmdState.Parameters.Add(p);
                    stateHasPlaces = await cmdState.ExecuteScalarAsync();
                }

                if (stateHasPlaces == null)
                {
                    _logger.LogInformation($"TigerSeeder: No places found for state {fips}. Ingesting...");
                    await _ingestionService.IngestPlacesForState(fips);
                    hasPlacesTable = true;
                }
            }

            // 3. Check if Block Groups exist for each state
            _logger.LogInformation("TigerSeeder: Checking block group data for all states/territories...");
            var hasBlockGroupTable = await TableExists(conn, "census_block_groups");
            foreach (var fips in BlockGroupStateFips)
            {
                object? stateHasData = null;
                if (hasBlockGroupTable)
                {
                    using var cmdState = conn.CreateCommand();
                    cmdState.CommandText = "SELECT 1 FROM census_block_groups WHERE substring(geoid, 1, 2) = @fips LIMIT 1;";
                    var p = cmdState.CreateParameter();
                    p.ParameterName = "fips";
                    p.Value = fips;
                    cmdState.Parameters.Add(p);
                    stateHasData = await cmdState.ExecuteScalarAsync();
                }

                if (stateHasData == null)
                {
                    _logger.LogInformation($"TigerSeeder: No block groups found for state {fips}. Ingesting...");
                    await _ingestionService.IngestState(fips);
                    hasBlockGroupTable = true;
                }

                if (PopulationStateFips.Contains(fips))
                {
                    using var populationCheck = conn.CreateCommand();
                    populationCheck.CommandText = @"
                            SELECT COALESCE(SUM(pop_total), 0), COALESCE(SUM(pop_18_plus), 0)
                            FROM census_block_groups
                            WHERE substring(geoid, 1, 2) = @fips;
                        ";
                    populationCheck.Parameters.AddWithValue("fips", fips);
                    await using var populationReader = await populationCheck.ExecuteReaderAsync();
                    await populationReader.ReadAsync();
                    var statePopulation = populationReader.GetInt64(0);
                    var stateAdultPopulation = populationReader.GetInt64(1);
                    await populationReader.CloseAsync();

                    if (statePopulation == 0 || stateAdultPopulation < statePopulation / 2)
                    {
                        try
                        {
                            await _ingestionService.BackfillPopulationForStateAsync(fips);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "TigerSeeder: Failed to backfill Census population for state {StateFips}.", fips);
                        }
                    }
                }

                // Check/Ingest Address Ranges
                // Since tiger_address_ranges doesn't have a state column, we use a simple "HasData" check 
                // to prevent re-ingesting 9GB of data on every startup.
                // To force a re-seed, the user must TRUNCATE/DROP the tiger_address_ranges table.
            }
            
            // Address-range ingestion currently targets the legacy lowercase-column schema,
            // while EF creates the newer TigerAddressRange entity schema. Keep the importer
            // opt-in until those two representations are reconciled.
            var ingestAddressRanges = _config.GetValue<bool>("TigerSeeding:IngestAddressRanges");

            // Check if Address Ranges exist globally (once, outside the loop)
            if (ingestAddressRanges && !await HasData(conn, "tiger_address_ranges"))
            {
                _logger.LogInformation("TigerSeeder: No address ranges found. Ingesting for all defined states...");
                foreach (var fips in BlockGroupStateFips)
                {
                     _logger.LogInformation($"TigerSeeder: Ingesting address ranges for state {fips}...");
                     await _ingestionService.IngestAddressRanges(fips);
                }
            }
            else if (!ingestAddressRanges)
            {
                _logger.LogInformation("TigerSeeder: Address range ingestion disabled by configuration.");
            }
            else
            {
                _logger.LogInformation("TigerSeeder: Address ranges already seeded (table not empty). Skipping ingestion.");
            }
            _logger.LogInformation("TigerSeeder: Block Group & Address Range seeding check complete.");

            // 4. Ensure simplified geometry columns exist and are populated (visualization only)
            await EnsureSimplifiedGeometriesAsync(conn);
        }

        private async Task<bool> HasUsablePlaceDataForStateAsync(NpgsqlConnection conn, string stateFips)
        {
            if (!await TableExists(conn, "tiger_places"))
            {
                return false;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                    FROM tiger_places
                    WHERE state_fp = @fips
                      AND funcstat = 'A'
                      AND geom IS NOT NULL
                      AND NOT ST_IsEmpty(geom)
                );
            ";
            cmd.Parameters.AddWithValue("fips", stateFips);
            return (bool?)await cmd.ExecuteScalarAsync() == true;
        }

        private async Task VerifyMunicipalityLookupContractAsync(NpgsqlConnection conn, string stateFips)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                WITH sample AS (
                    SELECT ST_PointOnSurface(geom) AS pt
                    FROM tiger_places
                    WHERE state_fp = @fips
                      AND funcstat = 'A'
                    ORDER BY geoid
                    LIMIT 1
                )
                SELECT place.geoid
                FROM sample
                JOIN tiger_places AS place
                  ON place.state_fp = @fips
                 AND place.funcstat = 'A'
                 AND ST_Covers(place.geom, sample.pt)
                ORDER BY COALESCE(place.aland, 0) ASC, place.geoid
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("fips", stateFips);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value)
            {
                throw new InvalidOperationException(
                    $"TIGER PLACE data for required state {stateFips} cannot execute the municipality containment lookup contract.");
            }
        }

        private async Task<bool> HasData(NpgsqlConnection conn, string tableName)
        {
            // First check if table exists to avoid exception
            var exists = await TableExists(conn, tableName);
            if (exists != true) return false;

            // Check if rows exist
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {tableName} LIMIT 1;";
            var res = await cmd.ExecuteScalarAsync();
            return res != null;
        }

        private async Task<bool> TableExists(NpgsqlConnection conn, string tableName)
        {
            using var cmdExists = conn.CreateCommand();
            cmdExists.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = @tableName
                );
            ";
            cmdExists.Parameters.AddWithValue("tableName", tableName);
            return (bool?)await cmdExists.ExecuteScalarAsync() == true;
        }

        private async Task<int> GetRowCountAsync(NpgsqlConnection conn, string tableName)
        {
            if (!await TableExists(conn, tableName))
            {
                return 0;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        private async Task EnsureSimplifiedGeometriesAsync(NpgsqlConnection conn)
        {
            await EnsureSimplifiedForTable(conn, "tiger_states", 100, 10000);
            await EnsureSimplifiedForTable(conn, "tiger_counties", 100, 10000);
            await EnsureSimplifiedForTable(conn, "tiger_places", 25, 5000);
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
                
                if (tableName == "census_block_groups") 
                {
                    cmd.CommandText += $@"
                        CREATE INDEX IF NOT EXISTS idx_{tableName}_geoid 
                        ON {tableName} (geoid text_pattern_ops);
                    ";
                }
                
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
