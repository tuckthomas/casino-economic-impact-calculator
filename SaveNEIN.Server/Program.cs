using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.HttpOverrides;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Services.Email;
using SaveNEIN.Shared;
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
builder.Services.AddSingleton<SaveNEIN.Server.Services.IFactCheckShareImageService, SaveNEIN.Server.Services.FactCheckShareImageService>();
builder.Services.Configure<TaxAllocationOptions>(builder.Configuration.GetSection("TaxAllocation"));
builder.Services.Configure<DailySignupDigestOptions>(builder.Configuration.GetSection(DailySignupDigestOptions.ConfigurationSection));
builder.Services.Configure<ZohoMailOptions>(builder.Configuration.GetSection(ZohoMailOptions.ConfigurationSection));
builder.Services.Configure<ArchiveBoxOptions>(builder.Configuration.GetSection(ArchiveBoxOptions.ConfigurationSection));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The application port is private to Docker, so forwarded headers can only
    // arrive through the reverse proxy on the production network.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), 
        o => o.UseNetTopologySuite()));

builder.Services.AddHttpClient<IZohoMailSender, ZohoMailSender>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ZohoMailOptions>>().Value;
    client.BaseAddress = new Uri(options.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<DailySignupDigestWorker>();
builder.Services.AddSingleton<SaveNEIN.Server.Services.IArchiveSourceUrlValidator, SaveNEIN.Server.Services.ArchiveSourceUrlValidator>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.IArchiveBoxCaptureService, SaveNEIN.Server.Services.ArchiveBoxCaptureService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ArchiveBoxOptions>>().Value;
    client.BaseAddress = new Uri(options.InternalBaseUrl.TrimEnd('/') + "/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});

// Register Valhalla Client
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Valhalla.ValhallaClient>(client =>
{
    var baseUrl = builder.Configuration["Valhalla:BaseUrl"];
    if (string.IsNullOrEmpty(baseUrl))
    {
        throw new InvalidOperationException("Valhalla:BaseUrl configuration is missing.");
    }
    client.BaseAddress = new Uri(baseUrl);
});

// Register Tiger Services
builder.Services.AddHttpClient<SaveNEIN.Server.Services.TigerIngestionService>();
builder.Services.AddScoped<TigerSeeder>();

// Register Census Ingestion Service
builder.Services.AddHttpClient<SaveNEIN.Server.Services.CensusIngestionService>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.CensusAcsProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.CensusAcsProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.CensusAcsAgePopulationProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.CensusAcsMedianIncomeProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.CensusCountyBusinessPatternsProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.CensusCountyBusinessPatternsProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.CensusCountyBusinessPatternsProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.CensusZctaOriginProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.CensusZctaOriginProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.CensusZctaOriginProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IrsSoiProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IrsSoiProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IrsSoiExactCodeZctaIncomeProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IndianaGamingCommissionProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IndianaGamingCommissionProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IndianaGamingCommissionMonthlyRevenueProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IndianaGamingCommissionFacilityInventoryProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IndianaTribalGamingFacilityProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IndianaTribalGamingFacilityProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IndianaTribalGamingFacilityInventoryProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IllinoisGamingBoardProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IllinoisGamingBoardProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IllinoisGamingBoardRevenueProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IllinoisGamingBoardFacilityInventoryProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.MichiganGamingFacilityProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.MichiganGamingFacilityProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.MichiganGamingFacilityInventoryProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.MichiganGamingControlBoardRevenueProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionRevenueProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionFacilityInventoryProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryRevenueProvider>();
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryFacilityInventoryProvider>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingRegulatorPerformanceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.IndianaGamingCommissionMonthlyRevenueProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingRegulatorPerformanceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.IllinoisGamingBoardRevenueProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingRegulatorPerformanceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.MichiganGamingControlBoardRevenueProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingRegulatorPerformanceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionRevenueProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingRegulatorPerformanceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryRevenueProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.IndianaGamingCommissionFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.IndianaTribalGamingFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.IllinoisGamingBoardFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.MichiganGamingFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.OhioCasinoControlCommissionFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFacilityInventoryProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<SaveNEIN.Server.Services.Providers.OhioLotteryVideoLotteryFacilityInventoryProvider>());
builder.Services.AddScoped<SaveNEIN.Server.Services.Providers.CompositeGamingRegulatorPerformanceProvider>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Providers.CompositeGamingFacilityInventoryProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IndianaDepartmentOfTransportationProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IndianaDepartmentOfTransportationProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IndianaDepartmentOfTransportationAadtProvider>();
builder.Services.Configure<SaveNEIN.Server.Services.Providers.IndianaTourismProviderOptions>(
    builder.Configuration.GetSection(SaveNEIN.Server.Services.Providers.IndianaTourismProviderOptions.ConfigurationSection));
builder.Services.AddHttpClient<SaveNEIN.Server.Services.Providers.IndianaDestinationDevelopmentPersonTripsProvider>();

// Register Isochrone Seeding Service
builder.Services.AddScoped<SaveNEIN.Server.Services.IsochroneSeedingService>();

builder.Services.AddScoped<SaveNEIN.Server.Services.IModelParameterService, SaveNEIN.Server.Services.ModelParameterService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IJurisdictionProfileService, SaveNEIN.Server.Services.JurisdictionProfileService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingAgeResolver, SaveNEIN.Server.Services.GamingAgeResolver>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IProblemGamblingPrevalenceResolver, SaveNEIN.Server.Services.ProblemGamblingPrevalenceResolver>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingTaxCalculator, SaveNEIN.Server.Services.GamingTaxCalculator>();
builder.Services.AddScoped<SaveNEIN.Server.Services.ICandidateFiscalLocationResolver, SaveNEIN.Server.Services.CandidateFiscalLocationResolver>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IGamingFiscalAllocationCalculator, SaveNEIN.Server.Services.GamingFiscalAllocationCalculator>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IGeneralFiscalRuleResolver, SaveNEIN.Server.Services.GeneralFiscalRuleResolver>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IModelParameterSetService, SaveNEIN.Server.Services.ModelParameterSetService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IModelRunService, SaveNEIN.Server.Services.ModelRunService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IDataSnapshotService, SaveNEIN.Server.Services.DataSnapshotService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IModelDataIngestionService, SaveNEIN.Server.Services.ModelDataIngestionService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IProviderSnapshotIngestionService, SaveNEIN.Server.Services.ProviderSnapshotIngestionService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.IOriginEligiblePopulationService, SaveNEIN.Server.Services.OriginEligiblePopulationService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IOriginDemandService, SaveNEIN.Server.Services.Gravity.OriginDemandService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IDevelopmentProgramService, SaveNEIN.Server.Services.Gravity.DevelopmentProgramService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ICompetitiveUniverseService, SaveNEIN.Server.Services.Gravity.CompetitiveUniverseService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IFacilityAttractivenessService, SaveNEIN.Server.Services.Gravity.FacilityAttractivenessService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ITravelMatrixService, SaveNEIN.Server.Services.Gravity.TravelMatrixService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IGravityModelService, SaveNEIN.Server.Services.Gravity.GravityModelService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IMarketEquilibriumService, SaveNEIN.Server.Services.Gravity.MarketEquilibriumService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IAccessibilityExpansionService, SaveNEIN.Server.Services.Gravity.AccessibilityExpansionService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ITourismDemandService, SaveNEIN.Server.Services.Gravity.TourismDemandService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ITrafficInterceptService, SaveNEIN.Server.Services.Gravity.TrafficInterceptService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ICapacityDiagnosticService, SaveNEIN.Server.Services.Gravity.CapacityDiagnosticService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ICapacityProductivityBenchmarkService, SaveNEIN.Server.Services.Gravity.CapacityProductivityBenchmarkService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IRampScheduleService, SaveNEIN.Server.Services.Gravity.RampScheduleService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ICannibalizationAccountingService, SaveNEIN.Server.Services.Gravity.CannibalizationAccountingService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ILocalEconomicInventoryWeightService, SaveNEIN.Server.Services.Gravity.LocalEconomicInventoryWeightService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IDisplacementModelService, SaveNEIN.Server.Services.Gravity.DisplacementModelService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IEmploymentImpactService, SaveNEIN.Server.Services.Gravity.EmploymentImpactService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IEmploymentProductivityBenchmarkService, SaveNEIN.Server.Services.Gravity.EmploymentProductivityBenchmarkService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IFiscalImpactService, SaveNEIN.Server.Services.Gravity.FiscalImpactService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.ISocialCostService, SaveNEIN.Server.Services.Gravity.SocialCostService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.INetImpactService, SaveNEIN.Server.Services.Gravity.NetImpactService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IOriginSummaryService, SaveNEIN.Server.Services.Gravity.OriginSummaryService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Gravity.IGravityModelExecutionService, SaveNEIN.Server.Services.Gravity.GravityModelExecutionService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IValidationMetricsService, SaveNEIN.Server.Services.Validation.ValidationMetricsService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IGeographicResidualPatternService, SaveNEIN.Server.Services.Validation.GeographicResidualPatternService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.ICalibrationSearchService, SaveNEIN.Server.Services.Validation.CalibrationSearchService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IComparableMarketModelService, SaveNEIN.Server.Services.Validation.ComparableMarketModelService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IBenchmarkOutputReader, SaveNEIN.Server.Services.Validation.BenchmarkOutputReader>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IValidationEvaluationService, SaveNEIN.Server.Services.Validation.ValidationEvaluationService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.IIncumbentBacktestCalibrationService, SaveNEIN.Server.Services.Validation.IncumbentBacktestCalibrationService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Validation.ISensitivityAnalysisService, SaveNEIN.Server.Services.Validation.SensitivityAnalysisService>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Reports.ICasinoImpactReportModelFactory, SaveNEIN.Server.Services.Reports.CasinoImpactReportModelFactory>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Reports.IHtmlReportRenderer, SaveNEIN.Server.Services.Reports.HtmlReportRenderer>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Reports.IPdfReportRenderer, SaveNEIN.Server.Services.Reports.PdfReportRenderer>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Reports.ICsvReportRenderer, SaveNEIN.Server.Services.Reports.CsvReportRenderer>();
builder.Services.AddScoped<SaveNEIN.Server.Services.Reports.IReportArtifactService, SaveNEIN.Server.Services.Reports.ReportArtifactService>();

var app = builder.Build();
var databaseInitializationEnabled = app.Configuration.GetValue("DatabaseInitialization:Enabled", true);
var factCheckAssetDirectory = ResolveFactCheckAssetDirectory(app.Environment);
app.Services.GetRequiredService<SaveNEIN.Server.Services.IFactCheckShareImageService>()
    .GenerateStaticAssets(factCheckAssetDirectory);

app.UseForwardedHeaders();

if (databaseInitializationEnabled &&
    (args.Contains("--seed-isochrones") || args.Contains("--run-allen-isochrones")))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SaveNEIN.Server.Services.IsochroneSeedingService>();
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

if (databaseInitializationEnabled)
{
    // Auto-migrate database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            if (db.Database.GetPendingMigrations().Any())
            {
                Console.WriteLine("Applying pending migrations...");
                db.Database.Migrate();
                Console.WriteLine("Migrations applied successfully.");
            }

            // Initialize and seed database
            await SaveNEIN.Server.Data.DbInitializer.Seed(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration or Seeding failed: {ex.Message}");
        }
    }

    // Validated fiscal rules can require incorporated-place containment. Derive the
    // necessary PLACE states from those rules and make them a startup dependency.
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<TigerSeeder>();
        await seeder.EnsureRequiredFiscalPlaceDataAsync();
    }

    // Seed TIGER datasets and warm visualization caches in the background. Fiscal model runs
    // resolve and require their own exact candidate county/place evidence at execution time.
    if (app.Configuration.GetValue("TigerSeeding:Enabled", true))
    {
        _ = Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<TigerSeeder>();
            try
            {
                Console.WriteLine("Starting TIGER Data Seeding Check...");
                await seeder.EnsureSeededAsync();
                Console.WriteLine("TIGER Data Seeding Check Complete.");

                await WarmStateCacheAsync(scope.ServiceProvider);
                await WarmMvtTilesAsync(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tiger Seeding Failed: {ex}");
            }
        });
    }
    else
    {
        Console.WriteLine("Background TIGER seeding is disabled for this runtime.");
    }
}
else
{
    Console.WriteLine("Database initialization is disabled for this runtime.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var accept = context.Request.Headers.Accept.ToString();
        var isHtmlRequest =
            path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        var shouldDisableCache =
            isHtmlRequest ||
            path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);

        if (shouldDisableCache)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";
                return Task.CompletedTask;
            });
        }

        await next();
    });
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(factCheckAssetDirectory),
    RequestPath = "/assets/fact-checks"
});
app.UseStaticFiles();
app.UseRouting();

// API Endpoints
app.MapGet("/api/legislators", async (AppDbContext db) =>
    await db.Legislators.ToListAsync());

app.MapGet("/api/impacts", async (AppDbContext db) =>
    await db.ImpactFacts.ToListAsync());

app.MapGet("/fact-checks/{slug}/share.png", (string slug) =>
    Results.Redirect($"/assets/fact-checks/{Uri.EscapeDataString(slug)}.png", permanent: false));

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToPage("/Index");

app.Run();

static string ResolveFactCheckAssetDirectory(IWebHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var clientRoots = new[]
        {
            Path.Combine(contentRoot, "SaveNEIN.Client"),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "SaveNEIN.Client"))
        };

        var clientRoot = clientRoots.FirstOrDefault(Directory.Exists);
        if (clientRoot is not null)
        {
            return Path.Combine(clientRoot, "wwwroot", "assets", "fact-checks");
        }
    }

    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    return Path.Combine(webRoot, "assets", "fact-checks");
}

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
