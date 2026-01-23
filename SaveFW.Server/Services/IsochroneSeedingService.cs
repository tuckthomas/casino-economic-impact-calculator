using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveFW.Server.Data;
using SaveFW.Server.Services.Valhalla;

namespace SaveFW.Server.Services;

public class IsochroneSeedingService
{
    private readonly AppDbContext _db;
    private readonly ValhallaClient _valhalla;
    private readonly IConfiguration _config;
    private readonly ILogger<IsochroneSeedingService> _logger;

    public IsochroneSeedingService(
        AppDbContext db,
        ValhallaClient valhalla,
        IConfiguration config,
        ILogger<IsochroneSeedingService> logger)
    {
        _db = db;
        _valhalla = valhalla;
        _config = config;
        _logger = logger;
    }

    public async Task RunSeedingJobAsync(string[] counties, int gridMeters, CancellationToken ct)
    {
        var stateFips = _config["IsochroneSeeding:StateFips"] ?? "18";
        // var gridMeters = int.TryParse(_config["IsochroneSeeding:GridMeters"], out var meters) ? meters : 10000;
        var contours = _config.GetSection("IsochroneSeeding:ContoursMinutes").Get<int[]>() ?? new[] { 60, 90 };
        var maxContoursPerRequest = int.TryParse(_config["IsochroneSeeding:MaxContoursPerRequest"], out var maxContours)
            ? Math.Max(1, maxContours)
            : 4;
        var hardwareNote = _config["IsochroneSeeding:HardwareNote"];
        var valhallaBaseUrl = _config["Valhalla:BaseUrl"];

        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await EnsureIsochroneCacheTableAsync(ct);
            await EnsureIsochroneRunTableAsync(ct);

            foreach (var countyName in counties)
            {
                _logger.LogInformation("Starting seeding job for {CountyName} County...", countyName);

                var points = await GetCountyGridPointsAsync(stateFips, countyName, gridMeters, ct);
                if (points.Count == 0)
                {
                    _logger.LogWarning("No grid points found for {CountyName} County, state {StateFips}.", countyName, stateFips);
                    continue;
                }

                var countyAreaSqMiles = await GetCountyAreaSqMilesAsync(stateFips, countyName, ct);
                var (cpuModel, cpuCores, memoryGb) = GetHardwareInfo(hardwareNote);

                _logger.LogInformation("Seeding {PointCount} grid points for {CountyName} County ({GridMeters}m).", points.Count, countyName, gridMeters);

                var startUtc = DateTime.UtcNow;
                var requestCount = 0;
                var totalRequestMs = 0.0;
                var minRequestMs = double.MaxValue;
                var maxRequestMs = 0.0;
                var insertedIsochrones = 0;

                foreach (var point in points)
                {
                    ct.ThrowIfCancellationRequested();
                    var lat = Math.Round(point.Lat, 6);
                    var lon = Math.Round(point.Lon, 6);
                    var sourceHash = ComputeSourceHash(lat, lon, contours);

                    var missing = await GetMissingContoursAsync(lat, lon, contours, ct);
                    if (missing.Count == 0)
                    {
                        continue;
                    }

                    foreach (var batch in BatchContours(missing, maxContoursPerRequest))
                    {
                        _logger.LogInformation("Fetching isochrones for {Lat},{Lon} ({Minutes}).", lat, lon, string.Join(",", batch));
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var json = await _valhalla.GetIsochroneJsonAsync(lat, lon, batch, ct);
                        stopwatch.Stop();

                        requestCount++;
                        totalRequestMs += stopwatch.Elapsed.TotalMilliseconds;
                        minRequestMs = Math.Min(minRequestMs, stopwatch.Elapsed.TotalMilliseconds);
                        maxRequestMs = Math.Max(maxRequestMs, stopwatch.Elapsed.TotalMilliseconds);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            _logger.LogWarning("Valhalla returned empty response for {Lat},{Lon}.", lat, lon);
                            continue;
                        }

                        var inserts = ExtractContourGeometries(json, batch);
                        if (inserts.Count == 0)
                        {
                            _logger.LogWarning("No contours parsed for {Lat},{Lon}.", lat, lon);
                            continue;
                        }

                        insertedIsochrones += await InsertIsochronesAsync(lat, lon, sourceHash, inserts, ct);
                    }
                }

                var completedUtc = DateTime.UtcNow;
                var avgRequestMs = requestCount > 0 ? totalRequestMs / requestCount : 0.0;
                if (minRequestMs == double.MaxValue)
                {
                    minRequestMs = 0.0;
                }

                await InsertRunMetadataAsync(
                    stateFips,
                    countyName,
                    gridMeters,
                    contours,
                    points.Count,
                    requestCount,
                    insertedIsochrones,
                    avgRequestMs,
                    minRequestMs,
                    maxRequestMs,
                    countyAreaSqMiles,
                    cpuModel,
                    cpuCores,
                    memoryGb,
                    hardwareNote,
                    valhallaBaseUrl,
                    startUtc,
                    completedUtc,
                    ct);
                
                _logger.LogInformation("Completed seeding for {CountyName}.", countyName);
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task EnsureIsochroneCacheTableAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE EXTENSION IF NOT EXISTS postgis;

            CREATE TABLE IF NOT EXISTS isochrone_cache (
                id BIGSERIAL PRIMARY KEY,
                lat DOUBLE PRECISION NOT NULL,
                lon DOUBLE PRECISION NOT NULL,
                minutes INTEGER NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                source_hash TEXT,
                geom geometry(MultiPolygon, 4326) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_isochrone_cache_geom ON isochrone_cache USING gist (geom);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_isochrone_cache_unique
                ON isochrone_cache (lat, lon, minutes, source_hash);
        ";

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureIsochroneRunTableAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS isochrone_runs (
                id BIGSERIAL PRIMARY KEY,
                started_at TIMESTAMPTZ NOT NULL,
                completed_at TIMESTAMPTZ NOT NULL,
                state_fips TEXT NOT NULL,
                county_name TEXT NOT NULL,
                grid_meters INTEGER NOT NULL,
                contours_minutes TEXT NOT NULL,
                point_count INTEGER NOT NULL,
                request_count INTEGER NOT NULL,
                inserted_isochrones INTEGER NOT NULL,
                avg_request_ms DOUBLE PRECISION NOT NULL,
                min_request_ms DOUBLE PRECISION NOT NULL,
                max_request_ms DOUBLE PRECISION NOT NULL,
                county_area_sq_miles DOUBLE PRECISION,
                hardware_cpu_model TEXT,
                hardware_cpu_cores INTEGER,
                hardware_memory_gb DOUBLE PRECISION,
                hardware_note TEXT,
                valhalla_base_url TEXT
            );
        ";

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<List<(double Lat, double Lon)>> GetCountyGridPointsAsync(string stateFips, string countyName, int gridMeters, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            WITH county AS (
                SELECT COALESCE(geom_simplified, geom) AS geom
                FROM tiger_counties
                WHERE state_fp = @state_fips AND name = @county_name
            ),
            grid AS (
                SELECT grid.geom AS cell
                FROM county,
                LATERAL ST_SquareGrid(@grid_meters, ST_Transform(county.geom, 3857)) AS grid
            ),
            points AS (
                SELECT ST_Transform(ST_Centroid(cell), 4326) AS pt
                FROM grid
            )
            SELECT ST_Y(pt) AS lat, ST_X(pt) AS lon
            FROM points, county
            WHERE ST_Intersects(pt, county.geom);
        ";
        cmd.Parameters.AddWithValue("state_fips", stateFips);
        cmd.Parameters.AddWithValue("county_name", countyName);
        cmd.Parameters.AddWithValue("grid_meters", gridMeters);

        var results = new List<(double Lat, double Lon)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetDouble(0), reader.GetDouble(1)));
        }

        return results;
    }

    private async Task<double?> GetCountyAreaSqMilesAsync(string stateFips, string countyName, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ST_Area(geography(COALESCE(geom_simplified, geom))) / 2589988.110336
            FROM tiger_counties
            WHERE state_fp = @state_fips AND name = @county_name;
        ";
        cmd.Parameters.AddWithValue("state_fips", stateFips);
        cmd.Parameters.AddWithValue("county_name", countyName);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is double area)
        {
            return area;
        }

        return null;
    }

    private async Task<HashSet<int>> GetMissingContoursAsync(double lat, double lon, int[] contours, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT minutes
            FROM isochrone_cache
            WHERE lat = @lat AND lon = @lon AND minutes = ANY(@contours);
        ";
        cmd.Parameters.AddWithValue("lat", lat);
        cmd.Parameters.AddWithValue("lon", lon);
        cmd.Parameters.AddWithValue("contours", contours);

        var existing = new HashSet<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            existing.Add(reader.GetInt32(0));
        }

        return new HashSet<int>(contours.Except(existing));
    }

    private static Dictionary<int, string> ExtractContourGeometries(string json, int[] contours)
    {
        var contourSet = new HashSet<int>(contours);
        var results = new Dictionary<int, string>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            if (!TryGetContourMinutes(properties, out var minutes) || !contourSet.Contains(minutes))
            {
                continue;
            }

            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            results[minutes] = geometry.GetRawText();
        }

        return results;
    }

    private async Task<int> InsertIsochronesAsync(double lat, double lon, string sourceHash, Dictionary<int, string> geometries, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var inserted = 0;
        foreach (var (minutes, geometryJson) in geometries)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO isochrone_cache (lat, lon, minutes, source_hash, geom)
                VALUES (@lat, @lon, @minutes, @source_hash,
                        ST_Multi(ST_SetSRID(ST_GeomFromGeoJSON(@geom_json), 4326)))
                ON CONFLICT DO NOTHING;
            ";
            cmd.Parameters.AddWithValue("lat", lat);
            cmd.Parameters.AddWithValue("lon", lon);
            cmd.Parameters.AddWithValue("minutes", minutes);
            cmd.Parameters.AddWithValue("source_hash", sourceHash);
            cmd.Parameters.AddWithValue("geom_json", geometryJson);

            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }

        return inserted;
    }

    private static bool TryGetContourMinutes(JsonElement properties, out int minutes)
    {
        minutes = 0;
        if (properties.TryGetProperty("contour", out var contourElement))
        {
            if (contourElement.ValueKind == JsonValueKind.Number && contourElement.TryGetInt32(out minutes))
            {
                return true;
            }
        }

        if (properties.TryGetProperty("time", out var timeElement))
        {
            if (timeElement.ValueKind == JsonValueKind.Number && timeElement.TryGetInt32(out minutes))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeSourceHash(double lat, double lon, int[] contours)
    {
        var payload = $"{lat:F6}|{lon:F6}|{string.Join(",", contours.OrderBy(c => c))}|auto|polygons=true|denoise=0.1";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task InsertRunMetadataAsync(
        string stateFips,
        string countyName,
        int gridMeters,
        int[] contours,
        int pointCount,
        int requestCount,
        int insertedIsochrones,
        double avgRequestMs,
        double minRequestMs,
        double maxRequestMs,
        double? countyAreaSqMiles,
        string? cpuModel,
        int? cpuCores,
        double? memoryGb,
        string? hardwareNote,
        string? valhallaBaseUrl,
        DateTime startedUtc,
        DateTime completedUtc,
        CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO isochrone_runs (
                started_at,
                completed_at,
                state_fips,
                county_name,
                grid_meters,
                contours_minutes,
                point_count,
                request_count,
                inserted_isochrones,
                avg_request_ms,
                min_request_ms,
                max_request_ms,
                county_area_sq_miles,
                hardware_cpu_model,
                hardware_cpu_cores,
                hardware_memory_gb,
                hardware_note,
                valhalla_base_url
            ) VALUES (
                @started_at,
                @completed_at,
                @state_fips,
                @county_name,
                @grid_meters,
                @contours_minutes,
                @point_count,
                @request_count,
                @inserted_isochrones,
                @avg_request_ms,
                @min_request_ms,
                @max_request_ms,
                @county_area_sq_miles,
                @hardware_cpu_model,
                @hardware_cpu_cores,
                @hardware_memory_gb,
                @hardware_note,
                @valhalla_base_url
            );
        ";
        cmd.Parameters.AddWithValue("started_at", startedUtc);
        cmd.Parameters.AddWithValue("completed_at", completedUtc);
        cmd.Parameters.AddWithValue("state_fips", stateFips);
        cmd.Parameters.AddWithValue("county_name", countyName);
        cmd.Parameters.AddWithValue("grid_meters", gridMeters);
        cmd.Parameters.AddWithValue("contours_minutes", string.Join(",", contours.OrderBy(c => c)));
        cmd.Parameters.AddWithValue("point_count", pointCount);
        cmd.Parameters.AddWithValue("request_count", requestCount);
        cmd.Parameters.AddWithValue("inserted_isochrones", insertedIsochrones);
        cmd.Parameters.AddWithValue("avg_request_ms", avgRequestMs);
        cmd.Parameters.AddWithValue("min_request_ms", minRequestMs);
        cmd.Parameters.AddWithValue("max_request_ms", maxRequestMs);
        cmd.Parameters.AddWithValue("county_area_sq_miles", (object?)countyAreaSqMiles ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hardware_cpu_model", (object?)cpuModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hardware_cpu_cores", (object?)cpuCores ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hardware_memory_gb", (object?)memoryGb ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hardware_note", (object?)hardwareNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("valhalla_base_url", (object?)valhallaBaseUrl ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string? CpuModel, int? CpuCores, double? MemoryGb) GetHardwareInfo(string? fallbackNote)
    {
        string? cpuModel = null;
        int? cpuCores = null;
        double? memoryGb = null;

        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var cpuLines = File.ReadAllLines("/proc/cpuinfo");
                cpuModel = cpuLines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase))?
                    .Split(':', 2).LastOrDefault()?.Trim();
                cpuCores = cpuLines.Count(l => l.StartsWith("processor", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
        }

        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                var memLine = File.ReadAllLines("/proc/meminfo")
                    .FirstOrDefault(l => l.StartsWith("MemTotal", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(memLine))
                {
                    var parts = memLine.Split(':', 2);
                    var kbString = parts.Length > 1 ? parts[1].Trim().Split(' ').FirstOrDefault() : null;
                    if (double.TryParse(kbString, out var kb))
                    {
                        memoryGb = Math.Round(kb / 1024.0 / 1024.0, 2);
                    }
                }
            }
        }
        catch
        {
        }

        if (cpuModel == null && cpuCores == null && memoryGb == null && !string.IsNullOrWhiteSpace(fallbackNote))
        {
            return (fallbackNote, null, null);
        }

        return (cpuModel, cpuCores, memoryGb);
    }

    private static IEnumerable<int[]> BatchContours(IEnumerable<int> contours, int batchSize)
    {
        var list = contours.OrderBy(c => c).ToList();
        for (var i = 0; i < list.Count; i += batchSize)
        {
            yield return list.Skip(i).Take(batchSize).ToArray();
        }
    }
}
