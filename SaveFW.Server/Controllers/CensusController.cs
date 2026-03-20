using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using SaveFW.Server.Data;
using SaveFW.Server.Services;

namespace SaveFW.Server.Controllers
{
    [ApiController]
    [Route("api/census")]
    public class CensusController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly TigerSeeder _seeder;
        private readonly IMemoryCache _cache;

        public CensusController(AppDbContext db, TigerSeeder seeder, IMemoryCache cache)
        {
            _db = db;
            _seeder = seeder;
            _cache = cache;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            // We use raw SQL because we haven't mapped these tables to EF entities fully yet
            var stateCount = -1;
            var countyCount = -1;
            var bgCount = -1;

            try 
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM tiger_states";
                    stateCount = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM tiger_counties";
                    countyCount = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                }
                 using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM census_block_groups";
                    bgCount = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                }
            }
            catch (Exception ex)
            {
                 return Ok(new { Error = ex.Message });
            }

            return Ok(new 
            { 
                States = stateCount,
                Counties = countyCount,
                BlockGroups = bgCount,
                Status = (stateCount > 0 && countyCount > 0) ? "Seeded" : "Incomplete"
            });
        }

        [HttpGet("states")]
        public async Task<IActionResult> GetStates()
        {
            var cacheKey = "tiger_states_geojson";
            if (_cache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                return Content(cachedJson, "application/json");
            }

            try 
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 120; // Allow sufficient time for the large aggregate query
                cmd.CommandText = @"
                    WITH state_pop AS (
                        SELECT substring(geoid, 1, 2) AS state_fips,
                               SUM(pop_total) AS pop_total,
                               SUM(pop_18_plus) AS pop_adult
                        FROM census_block_groups
                        GROUP BY 1
                    )
                    SELECT json_build_object(
                        'type', 'FeatureCollection',
                        'features', COALESCE(json_agg(
                            json_build_object(
                                'type', 'Feature',
                                'geometry', ST_AsGeoJSON(COALESCE(geom_simplified, geom))::json,
                                'properties', json_build_object(
                                    'geoid', geoid,
                                    'name', name,
                                    'stusps', stusps,
                                    'pop_total', COALESCE(sp.pop_total, 0),
                                    'pop_adult', COALESCE(sp.pop_adult, 0)
                                )
                            )
                        ), '[]'::json)
                    )::text
                    FROM tiger_states ts
                    LEFT JOIN state_pop sp ON sp.state_fips = ts.geoid;
                ";

                var json = (string?)await cmd.ExecuteScalarAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    _cache.Set(cacheKey, json, TimeSpan.FromHours(24));
                    return Content(json, "application/json");
                }
                return NotFound("No state data found.");
            }
            catch (Exception ex)
            {
                 return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("county/{countyFips}")]
        public async Task<IActionResult> GetCounty(string countyFips)
        {
            if (string.IsNullOrEmpty(countyFips) || countyFips.Length != 5)
                return BadRequest("County FIPS must be 5 digits");

            var cacheKey = $"tiger_county_{countyFips}_geojson";
            if (_cache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                return Content(cachedJson, "application/json");
            }

            try 
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT json_build_object(
                        'type', 'Feature',
                        'geometry', ST_AsGeoJSON(COALESCE(geom_simplified, geom))::json,
                        'properties', json_build_object(
                            'geoid', geoid,
                            'name', name,
                            'state_fp', state_fp,
                            'centroid', json_build_array(ST_X(ST_Centroid(geom)), ST_Y(ST_Centroid(geom))),
                            'bbox', json_build_array(ST_XMin(geom), ST_YMin(geom), ST_XMax(geom), ST_YMax(geom))
                        )
                    )::text
                    FROM tiger_counties
                    WHERE geoid = @fips;
                ";
                
                var p = cmd.CreateParameter();
                p.ParameterName = "fips";
                p.Value = countyFips;
                cmd.Parameters.Add(p);

                var json = (string?)await cmd.ExecuteScalarAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    _cache.Set(cacheKey, json, TimeSpan.FromHours(24));
                    return Content(json, "application/json");
                }
                return NotFound($"No county found for FIPS {countyFips}.");
            }
            catch (Exception ex)
            {
                 return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("counties/{stateFips}")]
        public async Task<IActionResult> GetCounties(string stateFips)
        {
            var cacheKey = $"tiger_counties_{stateFips}_geojson";
            if (_cache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                return Content(cachedJson, "application/json");
            }

            try 
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    WITH county_pop AS (
                        SELECT substring(geoid, 1, 5) AS county_geoid,
                               SUM(pop_total) AS pop_total,
                               SUM(pop_18_plus) AS pop_adult
                        FROM census_block_groups
                        WHERE geoid LIKE @fips || '%'
                        GROUP BY 1
                    )
                    SELECT json_build_object(
                        'type', 'FeatureCollection',
                        'features', COALESCE(json_agg(
                            json_build_object(
                                'type', 'Feature',
                                'geometry', ST_AsGeoJSON(COALESCE(geom_simplified, geom))::json,
                                'properties', json_build_object(
                                    'geoid', geoid,
                                    'name', name,
                                    'state_fp', state_fp,
                                    'pop_total', COALESCE(cp.pop_total, 0),
                                    'pop_adult', COALESCE(cp.pop_adult, 0)
                                )
                            )
                        ), '[]'::json)
                    )::text
                    FROM tiger_counties tc
                    LEFT JOIN county_pop cp ON cp.county_geoid = tc.geoid
                    WHERE tc.state_fp = @fips;
                ";
                
                var p = cmd.CreateParameter();
                p.ParameterName = "fips";
                p.Value = stateFips;
                cmd.Parameters.Add(p);

                var json = (string?)await cmd.ExecuteScalarAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    _cache.Set(cacheKey, json, TimeSpan.FromHours(24));
                    return Content(json, "application/json");
                }
                return NotFound($"No county data found for state {stateFips}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CensusController] GetCounties Error: {ex}");
                 return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("seed/force")]
        public async Task<IActionResult> ForceSeed()
        {
            await _seeder.EnsureSeededAsync();
            return Ok("Seeding triggered.");
        }

        /// <summary>
        /// Get census tract boundaries for a county, dissolved from block groups.
        /// Tract GEOID = first 11 characters of block group GEOID (state 2 + county 3 + tract 6).
        /// </summary>
        [HttpGet("tracts/{countyFips}")]
        public async Task<IActionResult> GetTracts(string countyFips)
        {
            var cacheKey = $"census_tracts_{countyFips}_geojson";
            if (_cache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                return Content(cachedJson, "application/json");
            }

            try 
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                // Dissolve block groups into tracts using ST_Union grouped by tract GEOID (first 11 chars)
                // Returns boundaries as LineStrings using ST_Boundary
                cmd.CommandText = @"
                    WITH tract_geoms AS (
                        SELECT 
                            substring(geoid, 1, 11) AS tract_geoid,
                            SUM(pop_total) AS pop_total,
                            SUM(pop_18_plus) AS pop_adult,
                            ST_Union(geom) AS geom
                        FROM census_block_groups
                        WHERE substring(geoid, 1, 5) = @fips
                        GROUP BY 1
                    )
                    SELECT json_build_object(
                        'type', 'FeatureCollection',
                        'features', COALESCE(json_agg(
                            json_build_object(
                                'type', 'Feature',
                                'geometry', ST_AsGeoJSON(ST_Boundary(geom))::json,
                                'properties', json_build_object(
                                    'GEOID', tract_geoid,
                                    'POP_TOTAL', COALESCE(pop_total, 0),
                                    'POP_ADULT', COALESCE(pop_adult, 0)
                                )
                            )
                        ), '[]'::json)
                    )::text
                    FROM tract_geoms;
                ";
                
                var p = cmd.CreateParameter();
                p.ParameterName = "fips";
                p.Value = countyFips;
                cmd.Parameters.Add(p);

                var json = (string?)await cmd.ExecuteScalarAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    _cache.Set(cacheKey, json, TimeSpan.FromHours(24));
                    return Content(json, "application/json");
                }
                return NotFound($"No tract data found for county {countyFips}.");
            }
            catch (Exception ex)
            {
                 return StatusCode(500, new { error = ex.Message });
            }
        }
        /// <summary>
        /// Serve vector tiles dynamically from PostGIS using ST_AsMVT.
        /// Includes 'states' layer (always) and 'counties' layer (z >= 4).
        /// Tiles are cached in memory for performance.
        /// </summary>
        [HttpGet("tiles/{z}/{x}/{y}")]
        public async Task<IActionResult> GetTiles(int z, int x, int y)
        {
            // Cache key based on tile coordinates
            var cacheKey = $"mvt_tile_{z}_{x}_{y}";
            
            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out byte[]? cachedTile) && cachedTile != null)
            {
                return File(cachedTile, "application/vnd.mapbox-vector-tile");
            }

            try
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                // Build query dynamically based on zoom level to optimize performance
                // ST_TileEnvelope returns 3857, but our data is 4326, so we transform for intersection
                // and use ST_AsMVTGeom with the 3857 envelope for proper MVT output
                // We apply ST_Simplify to reduce vertex count for faster rendering and hover detection
                var sql = @"
                    WITH 
                    bounds_3857 AS (
                        SELECT ST_TileEnvelope(@z, @x, @y) AS geom
                    ),
                    bounds_4326 AS (
                        SELECT ST_Transform(geom, 4326) AS geom FROM bounds_3857
                    ),
                    mvt_states AS (
                        SELECT ST_AsMVT(q, 'states', 4096, 'geom') AS mvt
                        FROM (
                            SELECT
                                row_number() OVER () AS id,
                                geoid,
                                name,
                                stusps,
                                ST_AsMVTGeom(
                                    ST_Simplify(ST_Transform(ts.geom, 3857), 500), 
                                    (SELECT geom FROM bounds_3857),
                                    4096, 256, true
                                ) AS geom
                            FROM tiger_states ts, bounds_4326 b
                            WHERE ts.geom && b.geom
                        ) q
                        WHERE geom IS NOT NULL
                    )";

                // Only fetch counties if zoom is high enough to matter (approx z4+)
                // This saves DB load on global views
                bool includesCounties = z >= 4;
                if (includesCounties) 
                {
                    // More aggressive simplification for lower zoom, less for higher zoom
                    // Tolerance in meters (3857): ~1000m at z4, ~100m at z10
                    var simplifyTolerance = Math.Max(100, 5000 / Math.Pow(2, z - 4));
                    
                    sql += $@",
                    mvt_counties AS (
                        SELECT ST_AsMVT(q, 'counties', 4096, 'geom') AS mvt
                        FROM (
                            SELECT
                                row_number() OVER () AS id,
                                geoid,
                                name,
                                state_fp,
                                ST_AsMVTGeom(
                                    ST_Simplify(ST_Transform(tc.geom, 3857), {simplifyTolerance}), 
                                    (SELECT geom FROM bounds_3857),
                                    4096, 256, true
                                ) AS geom
                            FROM tiger_counties tc, bounds_4326 b
                            WHERE tc.geom && b.geom
                        ) q
                        WHERE geom IS NOT NULL
                    )
                    SELECT mvt_states.mvt || mvt_counties.mvt FROM mvt_states, mvt_counties";
                }
                else
                {
                    sql += " SELECT mvt FROM mvt_states";
                }

                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("@z", z));
                cmd.Parameters.Add(new NpgsqlParameter("@x", x));
                cmd.Parameters.Add(new NpgsqlParameter("@y", y));

                var mvt = await cmd.ExecuteScalarAsync();

                if (mvt == null || mvt == DBNull.Value) 
                {
                    return NotFound();
                }

                var tileData = (byte[])mvt;

                // Cache the tile: longer TTL for state-only tiles (low zoom), shorter for county tiles
                // State tiles at low zoom are universal and rarely change
                var cacheDuration = includesCounties 
                    ? TimeSpan.FromMinutes(30)  // County tiles: 30 minutes
                    : TimeSpan.FromHours(2);    // State-only tiles: 2 hours

                _cache.Set(cacheKey, tileData, cacheDuration);

                // Log tile request for analytics (can be used to identify popular regions for pre-warming)
                Console.WriteLine($"[MVT] Generated tile z={z} x={x} y={y} (counties={includesCounties}, size={tileData.Length} bytes)");

                return File(tileData, "application/vnd.mapbox-vector-tile");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating tile {z}/{x}/{y}: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
