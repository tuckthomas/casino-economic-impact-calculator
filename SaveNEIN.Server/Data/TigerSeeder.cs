using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Data
{
    public class TigerSeeder
    {
        private readonly TigerIngestionService _ingestionService;
        private readonly ILogger<TigerSeeder> _logger;
        private readonly IConfiguration _config;
        private readonly TaxAllocationOptions _taxAllocationOptions;
        private static readonly string[] BlockGroupStateFips = new[]
        {
            "18", "39", "26", // Launch region first: Indiana, Ohio, Michigan.
            "01", "02", "04", "05", "06", "08", "09", "10", "11", "12",
            "13", "15", "16", "17", "19", "20", "21", "22", "23",
            "24", "25", "27", "28", "29", "30", "31", "32", "33",
            "34", "35", "36", "37", "38", "40", "41", "42", "44",
            "45", "46", "47", "48", "49", "50", "51", "53", "54", "55",
            "56", "60", "66", "69", "72", "78"
        };
        private static readonly string[] PopulationStateFips = { "18", "39", "26" };

        public TigerSeeder(
            TigerIngestionService ingestionService,
            ILogger<TigerSeeder> logger,
            IConfiguration config,
            IOptions<TaxAllocationOptions> taxAllocationOptions)
        {
            _ingestionService = ingestionService;
            _logger = logger;
            _config = config;
            _taxAllocationOptions = taxAllocationOptions.Value;
        }

        /// <summary>
        /// Ensures the TIGER PLACE data required by active municipal tax-allocation scenarios
        /// exists before the application begins serving requests. Municipality containment is
        /// part of the fiscal model, so this dataset is a correctness dependency rather than
        /// optional background map data.
        /// </summary>
        public async Task EnsureRequiredMunicipalityPlaceDataAsync()
        {
            var requiredStateFips = _taxAllocationOptions
                .GetMunicipalEligibleCountyFips()
                .Select(countyFips => countyFips[..2])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(stateFips => stateFips, StringComparer.Ordinal)
                .ToArray();

            if (requiredStateFips.Length == 0)
            {
                _logger.LogInformation("TigerSeeder: No municipality PLACE readiness states are configured.");
                return;
            }

            var connString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connString))
            {
                throw new InvalidOperationException("DefaultConnection is required to verify municipality PLACE data readiness.");
            }

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var placeSchemaChanged = await TigerPlaceSchema.EnsureMunicipalityLookupContractAsync(conn);
            if (placeSchemaChanged)
            {
                _logger.LogWarning(
                    "TigerSeeder: Existing TIGER PLACE schema was missing municipality lookup fields. Required states will be reingested before startup.");
            }

            foreach (var stateFips in requiredStateFips)
            {
                var needsRefresh = placeSchemaChanged || !await HasUsablePlaceDataForStateAsync(conn, stateFips);
                if (needsRefresh)
                {
                    _logger.LogInformation(
                        "TigerSeeder: Required municipality PLACE data for state {StateFips} is not endpoint-ready. Reingesting before application startup...",
                        stateFips);
                    await _ingestionService.IngestPlacesForState(stateFips);
                }

                if (!await HasUsablePlaceDataForStateAsync(conn, stateFips))
                {
                    throw new InvalidOperationException(
                        $"TIGER PLACE data for required state {stateFips} is still not endpoint-ready after ingestion. " +
                        "The application cannot safely evaluate municipal tax-allocation containment.");
                }

                await VerifyMunicipalityLookupContractAsync(conn, stateFips);

                _logger.LogInformation(
                    "TigerSeeder: Required municipality PLACE data for state {StateFips} is endpoint-ready.",
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
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM tiger_places
                        WHERE state_fp = @fips
                          AND funcstat = 'A'
                          AND geoid IS NOT NULL
                          AND name IS NOT NULL
                          AND aland IS NOT NULL
                          AND geom IS NOT NULL
                          AND NOT ST_IsEmpty(geom)
                          AND ST_SRID(geom) = 4326
                          AND ST_IsValid(geom)
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM tiger_places
                        WHERE state_fp = @fips
                          AND funcstat = 'A'
                          AND (
                              geoid IS NULL
                              OR name IS NULL
                              OR aland IS NULL
                              OR geom IS NULL
                              OR ST_IsEmpty(geom)
                              OR ST_SRID(geom) <> 4326
                              OR NOT ST_IsValid(geom)
                          )
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
