using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using SaveFW.Server.Data;

namespace SaveFW.Server.Services
{
    public class TigerIngestionService
    {
        private readonly ILogger<TigerIngestionService> _logger;
        private readonly IConfiguration _config;
        private readonly HttpClient _http;
        private const string TigerYear = "2025";
        private const string BaseTigerUrl = "https://www2.census.gov/geo/tiger/TIGER2025";

        public TigerIngestionService(ILogger<TigerIngestionService> logger, IConfiguration config, HttpClient http)
        {
            _logger = logger;
            _config = config;
            _http = http;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task IngestState(string fips)
        {
            // Logic to download and ingest a specific state's Block Groups (e.g. tl_2020_18_bg.zip)
            fips = fips.PadLeft(2, '0');
            var fileName = $"tl_{TigerYear}_{fips}_bg.zip";
            var url = $"{BaseTigerUrl}/BG/{fileName}";
            
            await ProcessTigerFile(url, fileName, "census_block_groups", fips);
        }

        public async Task IngestNationalCounties()
        {
            // Logic to download and ingest national counties (tl_2025_us_county.zip)
            var fileName = $"tl_{TigerYear}_us_county.zip";
            var localZipPath = Path.Combine("/root", fileName);
            if (File.Exists(localZipPath))
            {
                _logger.LogInformation($"Using local county zip: {localZipPath}");
                await ProcessLocalZip(localZipPath, fileName, "tiger_counties");
                return;
            }

            var url = $"{BaseTigerUrl}/COUNTY/{fileName}";
            await ProcessTigerFile(url, fileName, "tiger_counties", null);
        }

        public async Task IngestNationalStates()
        {
             // Logic to download and ingest national states (tl_2020_us_state.zip)
            var fileName = $"tl_{TigerYear}_us_state.zip";
            var url = $"{BaseTigerUrl}/STATE/{fileName}";
            
            await ProcessTigerFile(url, fileName, "tiger_states", null);
        }

        public async Task IngestPlacesForState(string stateFips)
        {
            stateFips = stateFips.PadLeft(2, '0');
            var fileName = $"tl_{TigerYear}_{stateFips}_place.zip";
            var localZipPath = Path.Combine("/root", fileName);
            if (File.Exists(localZipPath))
            {
                _logger.LogInformation($"Using local place zip: {localZipPath}");
                await ProcessLocalZip(localZipPath, fileName, "tiger_places");
                return;
            }

            var url = $"{BaseTigerUrl}/PLACE/{fileName}";
            await ProcessTigerFile(url, fileName, "tiger_places", null);
        }

        public async Task IngestAddressRanges(string stateFips)
        {
            stateFips = stateFips.PadLeft(2, '0');
            _logger.LogInformation($"Ingesting Address Ranges for State {stateFips}...");

            // Get counties for this state from DB
            var connString = _config.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var countyFipsList = new System.Collections.Generic.List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT geoid FROM tiger_counties WHERE state_fp = @state_fp";
                cmd.Parameters.AddWithValue("state_fp", stateFips);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    countyFipsList.Add(reader.GetString(0));
                }
            }

            foreach (var countyFips in countyFipsList)
            {
                var fileName = $"tl_{TigerYear}_{countyFips}_addrfeat.zip";
                var url = $"{BaseTigerUrl}/ADDRFEAT/{fileName}";
                try 
                {
                    await ProcessTigerFile(url, fileName, "tiger_address_ranges", null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to ingest address ranges for county {countyFips}");
                    // Continue to next county
                }
            }
        }

        private async Task ProcessTigerFile(string url, string fileName, string tableName, string? stateFipsFilter)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "savefw_tiger_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, fileName);

            int maxRetries = 3;
            int attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    _logger.LogInformation($"Downloading {fileName} (Attempt {attempt}/{maxRetries})...");
                    
                    // 1. Download with Interstitial Bypass
                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    {
                        await DownloadFileWithBypassAsync(url, fs);
                    }

                    // 2. Extract
                    _logger.LogInformation($"Extracting {fileName}...");
                    // Use overload with overwriteFiles: true just in case
                    ZipFile.ExtractToDirectory(zipPath, tempDir, true);

                    // 3. Find Shapefile (.shp)
                    var shpFile = Directory.GetFiles(tempDir, "*.shp").FirstOrDefault();
                    if (shpFile == null) throw new FileNotFoundException("No .shp found in archive");

                    // 4. Read & Ingest
                    _logger.LogInformation($"Reading shapefile: {Path.GetFileName(shpFile)}");
                    await IngestShapefileToPostGIS(shpFile, tableName);

                    _logger.LogInformation($"Ingestion complete for {fileName}");
                    break; // Success, exit loop
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to process {fileName} on attempt {attempt}");
                    
                    if (attempt >= maxRetries)
                    {
                        throw; // Give up
                    }
                    
                    // Cleanup before retry
                    await Task.Delay(2000);
                }
            }
            
            // Cleanup final directory since we used a unique GUID
            try 
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }

        private async Task ProcessLocalZip(string sourceZipPath, string fileName, string tableName)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "savefw_tiger_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, fileName);

            try
            {
                _logger.LogInformation($"Copying local zip {sourceZipPath}...");
                File.Copy(sourceZipPath, zipPath, true);

                _logger.LogInformation($"Extracting {fileName}...");
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);

                var shpFile = Directory.GetFiles(tempDir, "*.shp").FirstOrDefault();
                if (shpFile == null) throw new FileNotFoundException("No .shp found in archive");

                _logger.LogInformation($"Reading shapefile: {Path.GetFileName(shpFile)}");
                await IngestShapefileToPostGIS(shpFile, tableName);

                _logger.LogInformation($"Ingestion complete for {fileName}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private async Task DownloadFileWithBypassAsync(string url, FileStream destination)
        {
            // First Request: Allows us to capture valid cookies if there's an interstitial
            // We use a separate HttpClientHandler to manage cookies manually if needed, 
            // but the shared _http might already have cookies if we reuse it? 
            // Better to force the flow.
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            // Check if we got the Interstitial Page (Small HTML file disguised as 200 OK)
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var length = response.Content.Headers.ContentLength;

            bool isInterstitial = (contentType != null && contentType.Contains("text/html")) || (length.HasValue && length < 50000); // 50KB heuristic

            if (isInterstitial)
            {
                _logger.LogWarning("Detected Census Interstitial Page. Waiting 12 seconds to bypass...");
                
                // Grab cookies from the first response
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    var cookieHeader = string.Join("; ", cookies.Select(c => c.Split(';')[0]));

                    // Wait for the JS redirect timer (10s) + buffer
                    await Task.Delay(12000); 

                    var request2 = new HttpRequestMessage(HttpMethod.Get, url);
                    request2.Headers.Add("Cookie", cookieHeader); // Replay cookies without attributes
                    
                    _logger.LogInformation("Retrying download with cookies...");
                    var response2 = await _http.SendAsync(request2, HttpCompletionOption.ResponseHeadersRead);
                    if (!IsValidZipResponse(response2))
                    {
                        throw new InvalidDataException("Download returned non-zip content after cookie bypass.");
                    }
                    await response2.Content.CopyToAsync(destination);
                    return;
                }
                else 
                {
                     // If no cookies, maybe just the delay allows the IP through? Or we can't bypass.
                     // Try anyway after delay.
                     await Task.Delay(12000);
                     var request3 = new HttpRequestMessage(HttpMethod.Get, url);
                     var response3 = await _http.SendAsync(request3, HttpCompletionOption.ResponseHeadersRead);
                     if (!IsValidZipResponse(response3))
                     {
                         throw new InvalidDataException("Download returned non-zip content after bypass delay.");
                     }
                     await response3.Content.CopyToAsync(destination);
                     return;
                }
            }

            // Normal file download
            if (!IsValidZipResponse(response))
            {
                throw new InvalidDataException("Download returned non-zip content.");
            }
            await response.Content.CopyToAsync(destination);
        }

        private static bool IsValidZipResponse(HttpResponseMessage response)
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrEmpty(contentType) && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var length = response.Content.Headers.ContentLength;
            if (length.HasValue && length.Value > 0 && length.Value < 50000)
            {
                return false;
            }

            return true;
        }

        private async Task IngestShapefileToPostGIS(string shpFile, string tableName)
        {
            var factory = new GeometryFactory();
            using var reader = new ShapefileDataReader(shpFile, factory);

            var connString = _config.GetConnectionString("DefaultConnection");
            
            // Configure Npgsql to use NetTopologySuite for this connection
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
            dataSourceBuilder.UseNetTopologySuite();
            await using var dataSource = dataSourceBuilder.Build();
            await using var conn = await dataSource.OpenConnectionAsync();

            // 1. Ensure Table Exists
            await EnsureTableExists(conn, tableName);

            // 2. Prepare Insert Command
            // We reuse the command for performance, just swapping parameters
            using var cmd = conn.CreateCommand();
            
            if (tableName == "tiger_counties")
            {
                cmd.CommandText = @"
                    INSERT INTO tiger_counties (geoid, name, state_fp, geom)
                    VALUES (@geoid, @name, @state_fp, ST_Transform(@geom, 4326))
                    ON CONFLICT (geoid) DO UPDATE 
                    SET geom = EXCLUDED.geom, name = EXCLUDED.name, state_fp = EXCLUDED.state_fp;
                ";
                cmd.Parameters.Add(new NpgsqlParameter("geoid", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("state_fp", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("geom", NpgsqlTypes.NpgsqlDbType.Geometry));
            }
            else if (tableName == "tiger_states")
            {
                cmd.CommandText = @"
                    INSERT INTO tiger_states (geoid, name, stusps, geom)
                    VALUES (@geoid, @name, @stusps, ST_Transform(@geom, 4326))
                    ON CONFLICT (geoid) DO UPDATE 
                    SET geom = EXCLUDED.geom, name = EXCLUDED.name, stusps = EXCLUDED.stusps;
                ";
                cmd.Parameters.Add(new NpgsqlParameter("geoid", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("stusps", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("geom", NpgsqlTypes.NpgsqlDbType.Geometry));
            }
            else if (tableName == "tiger_places")
            {
                cmd.CommandText = @"
                    INSERT INTO tiger_places (geoid, name, state_fp, place_fp, funcstat, lsad, aland, awater, geom)
                    VALUES (
                        @geoid,
                        @name,
                        @state_fp,
                        @place_fp,
                        @funcstat,
                        @lsad,
                        @aland,
                        @awater,
                        ST_Multi(ST_Transform(@geom, 4326))
                    )
                    ON CONFLICT (geoid) DO UPDATE
                    SET name = EXCLUDED.name,
                        state_fp = EXCLUDED.state_fp,
                        place_fp = EXCLUDED.place_fp,
                        funcstat = EXCLUDED.funcstat,
                        lsad = EXCLUDED.lsad,
                        aland = EXCLUDED.aland,
                        awater = EXCLUDED.awater,
                        geom = EXCLUDED.geom;
                ";
                cmd.Parameters.Add(new NpgsqlParameter("geoid", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("state_fp", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("place_fp", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("funcstat", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("lsad", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("aland", NpgsqlTypes.NpgsqlDbType.Bigint));
                cmd.Parameters.Add(new NpgsqlParameter("awater", NpgsqlTypes.NpgsqlDbType.Bigint));
                cmd.Parameters.Add(new NpgsqlParameter("geom", NpgsqlTypes.NpgsqlDbType.Geometry));
            }
            else if (tableName == "census_block_groups")
            {
                 // We only update geometry here, assuming population data comes from a different ingestion process (Census API)
                 // or we initialize with 0 pop. We also compute and store cx/cy centroids for fast queries.
                 cmd.CommandText = @"
                    INSERT INTO census_block_groups (geoid, geom, pop_total, pop_18_plus, cx, cy)
                    VALUES (
                        @geoid, 
                        ST_Transform(@geom, 4326), 
                        0, 
                        0,
                        ST_X(ST_Centroid(ST_Transform(@geom, 4326))),
                        ST_Y(ST_Centroid(ST_Transform(@geom, 4326)))
                    )
                    ON CONFLICT (geoid) DO UPDATE 
                    SET geom = EXCLUDED.geom,
                        cx = EXCLUDED.cx,
                        cy = EXCLUDED.cy;
                ";
                cmd.Parameters.Add(new NpgsqlParameter("geoid", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("geom", NpgsqlTypes.NpgsqlDbType.Geometry));
            }
            else if (tableName == "tiger_address_ranges")
            {
                 cmd.CommandText = @"
                    INSERT INTO tiger_address_ranges (tlid, side, from_hn, to_hn, zip, street_name, geom)
                    VALUES (@tlid, @side, @from_hn, @to_hn, @zip, @street_name, ST_Transform(@geom, 4326))
                    ON CONFLICT (tlid, side, from_hn, to_hn, zip) DO NOTHING;
                ";
                cmd.Parameters.Add(new NpgsqlParameter("tlid", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("side", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("from_hn", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("to_hn", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("zip", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("street_name", NpgsqlTypes.NpgsqlDbType.Text));
                cmd.Parameters.Add(new NpgsqlParameter("geom", NpgsqlTypes.NpgsqlDbType.Geometry));
            }

            int count = 0;
            while (reader.Read())
            {
                var geometry = reader.Geometry;
                if (geometry == null) continue;
                
                // Set SRID to 4269 (NAD83) so PostGIS knows what to transform FROM
                geometry.SRID = 4269; 

                // Populate Parameters
                if (tableName == "tiger_counties")
                {
                    cmd.Parameters["geoid"].Value = reader["GEOID"];
                    cmd.Parameters["name"].Value = reader["NAME"];
                    cmd.Parameters["state_fp"].Value = reader["STATEFP"];
                    cmd.Parameters["geom"].Value = geometry;
                }
                else if (tableName == "tiger_states")
                {
                    cmd.Parameters["geoid"].Value = reader["GEOID"];
                    cmd.Parameters["name"].Value = reader["NAME"];
                    cmd.Parameters["stusps"].Value = reader["STUSPS"];
                    cmd.Parameters["geom"].Value = geometry;
                }
                else if (tableName == "tiger_places")
                {
                    cmd.Parameters["geoid"].Value = reader["GEOID"];
                    cmd.Parameters["name"].Value = reader["NAME"];
                    cmd.Parameters["state_fp"].Value = reader["STATEFP"];
                    cmd.Parameters["place_fp"].Value = reader["PLACEFP"];
                    cmd.Parameters["funcstat"].Value = reader["FUNCSTAT"];
                    cmd.Parameters["lsad"].Value = reader["LSAD"] == DBNull.Value ? DBNull.Value : reader["LSAD"];
                    cmd.Parameters["aland"].Value = reader["ALAND"] == DBNull.Value ? DBNull.Value : reader["ALAND"];
                    cmd.Parameters["awater"].Value = reader["AWATER"] == DBNull.Value ? DBNull.Value : reader["AWATER"];
                    cmd.Parameters["geom"].Value = geometry;
                }
                else if (tableName == "census_block_groups")
                {
                    cmd.Parameters["geoid"].Value = reader["GEOID"];
                    cmd.Parameters["geom"].Value = geometry;
                }
                else if (tableName == "tiger_address_ranges")
                {
                    // TIGER ADDRFEAT has separate fields for Left/Right
                    // We need to insert TWO rows per feature: one for Left side, one for Right side (if they have ranges)
                    
                    var tlid = reader["TLID"].ToString();
                    var name = reader["FULLNAME"].ToString();
                    
                    // Left Side
                    var fromL = reader["LFROMHN"]?.ToString();
                    var toL = reader["LTOHN"]?.ToString();
                    var zipL = reader["ZIPL"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(fromL) && !string.IsNullOrWhiteSpace(toL))
                    {
                        cmd.Parameters["tlid"].Value = tlid;
                        cmd.Parameters["side"].Value = "L";
                        cmd.Parameters["from_hn"].Value = fromL;
                        cmd.Parameters["to_hn"].Value = toL;
                        cmd.Parameters["zip"].Value = zipL ?? "";
                        cmd.Parameters["street_name"].Value = name ?? "";
                        cmd.Parameters["geom"].Value = geometry;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Right Side
                    var fromR = reader["RFROMHN"]?.ToString();
                    var toR = reader["RTOHN"]?.ToString();
                    var zipR = reader["ZIPR"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(fromR) && !string.IsNullOrWhiteSpace(toR))
                    {
                        cmd.Parameters["tlid"].Value = tlid;
                        cmd.Parameters["side"].Value = "R";
                        cmd.Parameters["from_hn"].Value = fromR;
                        cmd.Parameters["to_hn"].Value = toR;
                        cmd.Parameters["zip"].Value = zipR ?? "";
                        cmd.Parameters["street_name"].Value = name ?? "";
                        cmd.Parameters["geom"].Value = geometry;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    
                    // We manually executed above, so skip the generic execute at the bottom
                    count++;
                    if (count % 1000 == 0) _logger.LogInformation($"Ingested {count} ranges...");
                    continue; 
                }

                await cmd.ExecuteNonQueryAsync();
                count++;
                if (count % 100 == 0) _logger.LogInformation($"Ingested {count} features into {tableName}...");
            }
            _logger.LogInformation($"Finished ingesting {count} features into {tableName}.");
        }

        private async Task EnsureTableExists(NpgsqlConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            if (tableName == "tiger_counties")
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS tiger_counties (
                        geoid text PRIMARY KEY,
                        name text,
                        state_fp text,
                        geom geometry(MultiPolygon, 4326)
                    );
                    CREATE INDEX IF NOT EXISTS idx_tiger_counties_geom ON tiger_counties USING GIST (geom);
                    CREATE INDEX IF NOT EXISTS idx_tiger_counties_state_fp ON tiger_counties (state_fp);
                ";
            }
            else if (tableName == "tiger_states")
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS tiger_states (
                        geoid text PRIMARY KEY,
                        name text,
                        stusps text,
                        geom geometry(MultiPolygon, 4326)
                    );
                    CREATE INDEX IF NOT EXISTS idx_tiger_states_geom ON tiger_states USING GIST (geom);
                ";
            }
            else if (tableName == "tiger_places")
            {
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
                    CREATE INDEX IF NOT EXISTS idx_tiger_places_geom ON tiger_places USING GIST (geom);
                    CREATE INDEX IF NOT EXISTS idx_tiger_places_state_fp ON tiger_places (state_fp);
                    CREATE INDEX IF NOT EXISTS idx_tiger_places_funcstat ON tiger_places (funcstat);
                    CREATE INDEX IF NOT EXISTS idx_tiger_places_name ON tiger_places (name);
                ";
            }
            // census_block_groups likely already exists from previous migrations, 
            // but for safety we can ensure it or skip. Let's skip recreation to avoid migration conflicts.
             else if (tableName == "census_block_groups")
            {
                 // Create if not exists logic for block groups is handled by EF migrations usually.
                 // We'll trust it exists or is simple enough to assume for now.
                 // To be safe for this script:
                 cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS census_block_groups (
                        geoid text PRIMARY KEY,
                        pop_total bigint DEFAULT 0,
                        pop_18_plus bigint DEFAULT 0,
                        geom geometry(MultiPolygon, 4326),
                        cx DOUBLE PRECISION,
                        cy DOUBLE PRECISION
                    );
                    CREATE INDEX IF NOT EXISTS idx_census_bg_geom ON census_block_groups USING GIST (geom);
                    
                    -- Add cx/cy columns if missing (for existing tables)
                    ALTER TABLE census_block_groups ADD COLUMN IF NOT EXISTS cx DOUBLE PRECISION;
                    ALTER TABLE census_block_groups ADD COLUMN IF NOT EXISTS cy DOUBLE PRECISION;
                 ";
            }
            else if (tableName == "tiger_address_ranges")
            {
                 // Handled by 003_tiger_address_ranges.sql script usually, but for robustness:
                 cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS tiger_address_ranges (
                        tlid text,
                        side text,
                        from_hn text,
                        to_hn text,
                        zip text,
                        street_name text,
                        geom geometry(LineString, 4326),
                        CONSTRAINT pk_tiger_address_ranges PRIMARY KEY (tlid, side, from_hn, to_hn, zip)
                    );
                    CREATE INDEX IF NOT EXISTS idx_tiger_addr_geom ON tiger_address_ranges USING GIST (geom);
                    CREATE INDEX IF NOT EXISTS idx_tiger_addr_zip_street ON tiger_address_ranges (zip, street_name);
                 ";
            }
            
            await cmd.ExecuteNonQueryAsync();
        }

    }
}
