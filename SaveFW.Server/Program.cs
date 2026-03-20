using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaveFW.Server.Data;
using SaveFW.Shared;
using QuestPDF.Infrastructure;

// Set QuestPDF License
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddRazorPages();
builder.Services.AddOpenApi();

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), 
        o => o.UseNetTopologySuite()));

// Register Valhalla Client
builder.Services.AddHttpClient<SaveFW.Server.Services.Valhalla.ValhallaClient>(client =>
{
    var baseUrl = builder.Configuration["Valhalla:BaseUrl"];
    if (string.IsNullOrEmpty(baseUrl))
    {
        throw new InvalidOperationException("Valhalla:BaseUrl configuration is missing.");
    }
    client.BaseAddress = new Uri(baseUrl);
});

// Register Tiger Services
builder.Services.AddHttpClient<SaveFW.Server.Services.TigerIngestionService>();
builder.Services.AddScoped<TigerSeeder>();

// Register Census Ingestion Service
builder.Services.AddHttpClient<SaveFW.Server.Services.CensusIngestionService>();

// Register Isochrone Seeding Service
builder.Services.AddScoped<SaveFW.Server.Services.IsochroneSeedingService>();

// Register Workers
// builder.Services.AddHostedService<SaveFW.Server.Workers.ScoringWorker>();

var app = builder.Build();

if (args.Contains("--seed-isochrones") || args.Contains("--run-allen-isochrones"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SaveFW.Server.Services.IsochroneSeedingService>();
    // High-res (1km) grid for Northeast Indiana Region
    var counties = new[] 
    { 
        "Steuben", "Allen", "Adams", "DeKalb", "Huntington", "LaGrange", 
        "Noble", "Wabash", "Wells", "Whitley" 
    };
    var gridMeters = 1000; 
    await seeder.RunSeedingJobAsync(counties, gridMeters, CancellationToken.None);
    return;
}

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Wait for DB to be ready
    try 
    {
        if (db.Database.GetPendingMigrations().Any())
        {
            Console.WriteLine("Applying pending migrations...");
            db.Database.Migrate();
            Console.WriteLine("Migrations applied successfully.");
        }

    }
    catch (Exception ex)
    {
        Console.WriteLine($"Migration failed: {ex.Message}");
    }
}

// Seed TIGER Data on startup and warm caches (fire-and-forget to avoid blocking startup)
_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    // var seeder = scope.ServiceProvider.GetRequiredService<TigerSeeder>();
    try
    {
        // Console.WriteLine("Starting TIGER Data Seeding Check...");
        // await seeder.EnsureSeededAsync();
        // Console.WriteLine("TIGER Data Seeding Check Complete.");

        await WarmStateCacheAsync(scope.ServiceProvider);
        await WarmMvtTilesAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Tiger Seeding Failed: {ex}");
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.MapOpenApi();
}

// app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

// API Endpoints
app.MapGet("/api/legislators", async (AppDbContext db) =>
    await db.Legislators.ToListAsync());

app.MapGet("/api/impacts", async (AppDbContext db) =>
    await db.ImpactFacts.ToListAsync());

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

static async Task WarmStateCacheAsync(IServiceProvider services)
{
    var cache = services.GetRequiredService<IMemoryCache>();
    var db = services.GetRequiredService<AppDbContext>();

    try
    {
        Console.WriteLine("Warming state boundaries cache...");
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120; // Allow sufficient time for the initial large aggregate query

        // MUST match the query in CensusController.GetStates()
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
            cache.Set("tiger_states_geojson", json, TimeSpan.FromHours(24));
            Console.WriteLine("State boundaries cache warmed successfully.");
        }
        else
        {
            Console.WriteLine("State boundaries cache warm skipped (no data).");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"State cache warm failed: {ex.Message}");
    }
}

/// <summary>
/// Pre-warm MVT tiles for the initial map view (continental US at low zoom).
/// This ensures first-time visitors get instant state borders.
/// </summary>
static async Task WarmMvtTilesAsync(IServiceProvider services)
{
    var cache = services.GetRequiredService<IMemoryCache>();
    var db = services.GetRequiredService<AppDbContext>();

    try
    {
        Console.WriteLine("Pre-warming MVT state tiles...");
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        // Tiles covering continental US at zoom levels 2, 3, and 4 (state-only, no counties)
        // These are the tiles that will be requested when the map first loads
        var tilesToWarm = new List<(int z, int x, int y)>
        {
            // Z2 - continental US overview
            (2, 0, 1), (2, 1, 1),
            // Z3 - main US tiles
            (3, 0, 2), (3, 1, 2), (3, 2, 2), (3, 3, 2),
            (3, 0, 3), (3, 1, 3), (3, 2, 3), (3, 3, 3),
            // Z4 - more detailed state view (common zoom for state selection)
            (4, 2, 4), (4, 3, 4), (4, 4, 4), (4, 5, 4), (4, 6, 4),
            (4, 2, 5), (4, 3, 5), (4, 4, 5), (4, 5, 5), (4, 6, 5),
            (4, 2, 6), (4, 3, 6), (4, 4, 6), (4, 5, 6), (4, 6, 6),
        };

        int warmed = 0;
        foreach (var (z, x, y) in tilesToWarm)
        {
            var cacheKey = $"mvt_tile_{z}_{x}_{y}";
            
            // Skip if already cached
            if (cache.TryGetValue(cacheKey, out byte[]? _))
            {
                warmed++;
                continue;
            }

            try
            {
                using var cmd = conn.CreateCommand();
                
                // State-only MVT query (z < 4 doesn't include counties anyway)
                bool includesCounties = z >= 4;
                var simplifyTolerance = includesCounties 
                    ? Math.Max(100, 5000 / Math.Pow(2, z - 4)) 
                    : 500;

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

                if (includesCounties)
                {
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
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@z", z));
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@x", x));
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@y", y));

                var mvt = await cmd.ExecuteScalarAsync();
                if (mvt != null && mvt != DBNull.Value)
                {
                    var tileData = (byte[])mvt;
                    var cacheDuration = includesCounties 
                        ? TimeSpan.FromMinutes(30) 
                        : TimeSpan.FromHours(2);
                    cache.Set(cacheKey, tileData, cacheDuration);
                    warmed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to warm tile z={z} x={x} y={y}: {ex.Message}");
            }
        }

        Console.WriteLine($"MVT tile cache warmed: {warmed}/{tilesToWarm.Count} tiles.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MVT tile cache warm failed: {ex.Message}");
    }
}

