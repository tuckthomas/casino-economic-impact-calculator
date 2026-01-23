using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveFW.Server.Data;
using System.Data;

namespace SaveFW.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImpactController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ImpactController> _logger;

    public ImpactController(AppDbContext db, IConfiguration config, ILogger<ImpactController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpGet("calculate")]
    public async Task<IActionResult> CalculateImpact(double lat, double lon)
    {
        // ... (existing code) ...
        var connString = _config.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // 10 miles = 16093.4 meters
        // 20 miles = 32186.9 meters
        var sql = @"
            WITH point AS (
                SELECT ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography AS pt
            ),
            buffers AS (
                SELECT
                    ST_Buffer(pt, 16093.4)::geometry as geom_10,
                    ST_Buffer(pt, 32186.9)::geometry as geom_20
                FROM point
            ),
            -- Identify the county FIPS (State+County) that contains the center point
            center_county AS (
                SELECT SUBSTRING(geoid, 1, 5) as fips
                FROM census_block_groups
                WHERE ST_Intersects(geom, ST_SetSRID(ST_MakePoint(@lon, @lat), 4326))
                LIMIT 1
            ),
            -- Aggregate total county stats
            county_stats AS (
                SELECT 
                    SUM(pop_total) as c_total, 
                    SUM(pop_18_plus) as c_adults
                FROM census_block_groups
                WHERE geoid LIKE (SELECT fips FROM center_county) || '%'
            )
            SELECT
                -- Zone 1 (0-10 miles) - Adults
                COALESCE(SUM(
                    CASE 
                        WHEN ST_Intersects(b.geom, buf.geom_10) THEN
                            b.pop_18_plus * (ST_Area(ST_Intersection(b.geom, buf.geom_10)) / ST_Area(b.geom))
                        ELSE 0 
                    END
                ), 0) as pop_10,
                
                -- Zone 2 (0-20 miles) - Adults (for later subtraction)
                COALESCE(SUM(
                    CASE 
                        WHEN ST_Intersects(b.geom, buf.geom_20) THEN
                            b.pop_18_plus * (ST_Area(ST_Intersection(b.geom, buf.geom_20)) / ST_Area(b.geom))
                        ELSE 0 
                    END
                ), 0) as pop_20_total,

                -- County Stats
                (SELECT c_total FROM county_stats) as county_total,
                (SELECT c_adults FROM county_stats) as county_adults

            FROM census_block_groups b, buffers buf
            WHERE ST_Intersects(b.geom, buf.geom_20);
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("lat", lat);
        cmd.Parameters.AddWithValue("lon", lon);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var pop10 = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
            var pop20Total = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
            var countyTotal = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            var countyAdults = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            
            // pop_20 in the UI acts as the "Elevated Risk" band (10-20 miles), so we subtract pop_10
            var pop10_20 = Math.Max(0, pop20Total - pop10);

            return Ok(new 
            { 
                t1 = (long)pop10, 
                t2 = (long)pop10_20,
                county_total = countyTotal,
                county_adults = countyAdults
            });
        }
        
        return Ok(new { t1 = 0, t2 = 0, county_total = 0, county_adults = 0 });
    }

    [HttpGet("county-context/{fips}")]
    public async Task<IActionResult> GetCountyContext(string fips, [FromQuery] bool lite = false)
    {
        _logger.LogInformation($"[ImpactController] 1. Request received for {fips} (Lite: {lite})");
        try
        {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        var connString = _config.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        _logger.LogInformation($"[ImpactController] 2. DB Connection Open ({sw.ElapsedMilliseconds}ms)");

        var sql = lite
            ? @"
                WITH county_center AS (
                    SELECT ST_Centroid(geom) as pt, geoid
                    FROM tiger_counties
                    WHERE geoid = @fips
                ),
                county_stats AS (
                    SELECT
                        COALESCE(SUM(pop_total), 0) as county_total,
                        COALESCE(SUM(pop_18_plus), 0) as county_adults
                    FROM census_block_groups
                    WHERE geoid LIKE @fips || '%'
                )
                SELECT json_build_object(
                    'fips', @fips,
                    'state_fips', SUBSTRING(@fips, 1, 2),
                    'lite', true,
                    'county_total', (SELECT county_total FROM county_stats),
                    'county_adults', (SELECT county_adults FROM county_stats),
                    'points', COALESCE(json_agg(
                        json_build_array(
                            ST_X(ST_Centroid(b.geom)),
                            ST_Y(ST_Centroid(b.geom)),
                            b.pop_18_plus,
                            SUBSTRING(b.geoid, 1, 5)
                        )
                    ), '[]'::json)
                )::text
                FROM census_block_groups b, county_center c
                WHERE
                    b.geoid LIKE SUBSTRING(@fips, 1, 2) || '%'
                    AND b.pop_18_plus > 0
                    AND ST_DWithin(b.geom, c.pt, 1.5) -- Use geometry index first (approx 100 miles)
                    AND ST_DWithin(b.geom::geography, c.pt::geography, 80467.2);
            "
            : @"
                SELECT json_build_object(
                    'type', 'FeatureCollection',
                    'lite', false,
                    'features', COALESCE(json_agg(
                        json_build_object(
                            'type', 'Feature',
                            'geometry', ST_AsGeoJSON(COALESCE(b.geom_simplified, b.geom))::json,
                            'properties', json_build_object(
                                'POPULATION', b.pop_total,
                                'POP_ADULT', b.pop_18_plus,
                                'GEOID', b.geoid,
                                'CX', ST_X(ST_PointOnSurface(b.geom)),
                                'CY', ST_Y(ST_PointOnSurface(b.geom))
                            )
                        )
                    ), '[]'::json)
                )::text
                FROM census_block_groups b
                WHERE b.geoid LIKE @fips || '%';
            ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("fips", fips);

        _logger.LogInformation($"[ImpactController] 3. Executing SQL... ({sw.ElapsedMilliseconds}ms)");
        // ExecuteScalar returns the JSON string directly
        var jsonResult = await cmd.ExecuteScalarAsync();
        
        sw.Stop();
        _logger.LogInformation($"[ImpactController] 4. Query finished in {sw.ElapsedMilliseconds}ms");

        if (jsonResult == null || jsonResult == DBNull.Value) 
        {
            _logger.LogWarning($"[ImpactController] No data found for {fips}");
            return NotFound();
        }

        var jsonString = jsonResult.ToString() ?? string.Empty;
        _logger.LogInformation($"[ImpactController] 5. JSON String Length: {jsonString.Length}");
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);
        _logger.LogInformation($"[ImpactController] 6. Bytes Converted: {bytes.Length}");

        // Use File result to ensure proper headers and flushing
        _logger.LogInformation($"[ImpactController] 7. Returning File Result.");
        return File(bytes, "application/json");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"[ImpactController] Error in GetCountyContext for {fips}");
        return StatusCode(500, new { error = ex.Message });
    }
}
    [HttpGet("grid-points")]
    public async Task<IActionResult> GetGridPoints([FromQuery] string? stateFips = "18", [FromQuery] string? title = "Allen")
    {
        // Defaults align with appsettings for now
        var connString = _config.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // Query distinct points from cache intersected with the requested county
        var sql = @"
            WITH target_county AS (
                SELECT geom 
                FROM tiger_counties 
                WHERE statefp = @stateFips AND name = @countyName
                LIMIT 1
            )
            SELECT DISTINCT c.lat, c.lon 
            FROM isochrone_cache c
            JOIN target_county t ON ST_Contains(t.geom, c.geom)
            LIMIT 5000; 
        ";
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("stateFips", stateFips ?? "18");
        cmd.Parameters.AddWithValue("countyName", title ?? "Allen");

        await using var reader = await cmd.ExecuteReaderAsync();
        
        var points = new List<object>();
        while (await reader.ReadAsync())
        {
            points.Add(new { lat = reader.GetDouble(0), lon = reader.GetDouble(1) });
        }

        return Ok(points);
    }

    [HttpGet("cached-isochrone")]
    public async Task<IActionResult> GetCachedIsochrone(double lat, double lon)
    {
        var connString = _config.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // Query both the GeoJSON and the population statistics for the 3 tiers
        var sql = @"
            WITH isochrones AS (
                SELECT geom, minutes
                FROM isochrone_cache
                WHERE lat = @lat AND lon = @lon
            ),
            intersection_stats AS (
                SELECT 
                    i.minutes,
                    SUBSTRING(b.geoid, 1, 5) as fips,
                    SUM(b.pop_18_plus * (ST_Area(ST_Intersection(b.geom, i.geom)) / ST_Area(b.geom))) as pop
                FROM census_block_groups b
                JOIN isochrones i ON ST_Intersects(b.geom, i.geom)
                GROUP BY i.minutes, SUBSTRING(b.geoid, 1, 5)
            ),
            pop_stats AS (
                SELECT 
                    minutes,
                    SUM(pop) as pop_adult_total,
                    json_object_agg(fips, pop) as by_county
                FROM intersection_stats
                GROUP BY minutes
            )
            SELECT 
                (SELECT json_build_object(
                    'type', 'FeatureCollection',
                    'features', json_agg(
                        json_build_object(
                            'type', 'Feature',
                            'properties', json_build_object('contour', minutes),
                            'geometry', ST_AsGeoJSON(geom)::json
                        )
                    )
                ) FROM isochrones) as geojson,
                
                (SELECT 
                    json_object_agg(minutes, json_build_object(
                        'total', pop_adult_total,
                        'by_county', by_county
                    ))
                 FROM pop_stats) as stats;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("lat", lat);
        cmd.Parameters.AddWithValue("lon", lon);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var geoJsonStr = reader.IsDBNull(0) ? null : reader.GetString(0);
            var statsJsonStr = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
            
            if (string.IsNullOrEmpty(geoJsonStr)) return NotFound();

            // We need to parse the stats to structure the response for the frontend
            // Expected contours: 15, 30, 45 (or similar)
            // We'll return them mapped to t1, t2, t3 logic if possible, or just raw
            
            // For now, let's return a composite JSON
            var result = new 
            {
                geoJson = System.Text.Json.JsonDocument.Parse(geoJsonStr).RootElement,
                stats = System.Text.Json.JsonDocument.Parse(statsJsonStr).RootElement
            };

            return Ok(result);
        }

        return NotFound();
    }
}
