// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Providers;
using SaveNEIN.Server.Services.Reports;
using SaveNEIN.Server.Services.Validation;
using SaveNEIN.Server.Services.Valhalla;
using QuestPDF.Infrastructure;

if (args is ["--probe-zcta-origins"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var dataset = await new CensusZctaOriginProvider(
            providerProbeHttp,
            Options.Create(new CensusZctaOriginProviderOptions()))
        .FetchAsync(new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = "46802,46204" }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        dataset.Source.Url,
        dataset.Period,
        RowCount = dataset.Rows.Count,
        Origins = dataset.Rows.Select(row => new
        {
            row.StableOriginId,
            row.OriginType,
            row.StateOrTerritoryCode,
            row.CountyEquivalentCode,
            row.RepresentativeLatitude,
            row.RepresentativeLongitude,
            WktLength = row.AreaWkt.Length
        }),
        dataset.Warnings,
        dataset.ContentChecksum
    }));
    return;
}

if (args is ["--probe-irs-soi"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    var dataset = await new IrsSoiExactCodeZctaIncomeProvider(
            providerProbeHttp,
            Options.Create(new IrsSoiProviderOptions()))
        .FetchAsync(new ProviderFetchRequest(
            "US-STATES",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            new Dictionary<string, string> { ["state-codes"] = "IL,IN,KY,MI,OH" }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        dataset.Source.Url,
        dataset.Source.Notes,
        dataset.Period,
        RowCount = dataset.Rows.Count,
        ReturnCount = dataset.Rows.Sum(row => row.ReturnCount ?? 0),
        AdjustedGrossIncome = dataset.Rows.Sum(row => row.AdjustedGrossIncome ?? 0),
        dataset.Warnings,
        dataset.ContentChecksum
    }));
    return;
}

if (args is ["--probe-indiana-providers"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    var gamingProvider = new IndianaGamingCommissionMonthlyRevenueProvider(
        providerProbeHttp,
        Options.Create(new IndianaGamingCommissionProviderOptions()));
    var gaming = await gamingProvider.FetchAsync(new ProviderFetchRequest(
        "US-IN",
        new DateOnly(2025, 12, 1),
        new DateOnly(2025, 12, 31)));
    var annualGaming = await gamingProvider.FetchAsync(new ProviderFetchRequest(
        "US-IN",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31)));
    var facilityProvider = new IndianaGamingCommissionFacilityInventoryProvider(
        providerProbeHttp,
        Options.Create(new IndianaGamingCommissionProviderOptions()));
    var facilityDataset = await facilityProvider.FetchAsync(new ProviderFetchRequest(
        "US-IN",
        new DateOnly(2025, 12, 1),
        new DateOnly(2025, 12, 31)));
    var trafficProvider = new IndianaDepartmentOfTransportationAadtProvider(
        providerProbeHttp,
        Options.Create(new IndianaDepartmentOfTransportationProviderOptions()));
    var trafficDataset = await trafficProvider.FetchAsync(new ProviderFetchRequest(
        "US-IN",
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-numbers"] = "970200"
        }));
    var tourismDataset = await new IndianaDestinationDevelopmentPersonTripsProvider(
            providerProbeHttp,
            Options.Create(new IndianaTourismProviderOptions()))
        .FetchAsync(new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31)));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Gaming = new
        {
            gaming.Source.Url,
            gaming.Period,
            RowCount = gaming.Rows.Count,
            MetricKeys = gaming.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
            FacilityCount = gaming.Rows.Select(row => row.StableVenueId).Distinct().Count(),
            gaming.ContentChecksum
        },
        AnnualGaming = new
        {
            annualGaming.Source.Url,
            annualGaming.Period,
            RowCount = annualGaming.Rows.Count,
            MonthCount = annualGaming.Rows.Select(row => row.PeriodStart).Distinct().Count(),
            MetricKeys = annualGaming.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
            FacilityCount = annualGaming.Rows.Select(row => row.StableVenueId).Distinct().Count(),
            annualGaming.ContentChecksum
        },
        Facilities = new
        {
            facilityDataset.Source.Url,
            facilityDataset.Period,
            RowCount = facilityDataset.Rows.Count,
            RacinoCount = facilityDataset.Rows.Count(row => row.HasRacetrack == true),
            UnknownHotelCount = facilityDataset.Rows.Count(row => row.HasHotel is null),
            facilityDataset.ContentChecksum
        },
        Traffic = new
        {
            trafficDataset.Source.Url,
            trafficDataset.Period,
            RowCount = trafficDataset.Rows.Count,
            Observation = trafficDataset.Rows.Single(),
            trafficDataset.ContentChecksum
        },
        Tourism = new
        {
            tourismDataset.Source.Url,
            tourismDataset.Period,
            RowCount = tourismDataset.Rows.Count,
            Observation = tourismDataset.Rows.Single(),
            tourismDataset.Warnings,
            tourismDataset.ContentChecksum
        }
    }));
    return;
}

if (args is ["--probe-illinois-providers"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var configured = Options.Create(new IllinoisGamingBoardProviderOptions());
    var revenueProvider = new IllinoisGamingBoardRevenueProvider(providerProbeHttp, configured);
    var annualRevenue = await revenueProvider.FetchAsync(new ProviderFetchRequest(
        "US-IL",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31)));
    var facilities = await new IllinoisGamingBoardFacilityInventoryProvider(
            providerProbeHttp,
            revenueProvider,
            configured)
        .FetchAsync(new ProviderFetchRequest(
            "US-IL",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));
    var revenueIds = annualRevenue.Rows
        .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .Select(row => row.StableVenueId)
        .ToHashSet(StringComparer.Ordinal);
    var facilityIds = facilities.Rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    if (!revenueIds.SetEquals(facilityIds))
    {
        throw new InvalidOperationException("Live IGB revenue and facility stable-ID sets did not reconcile.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Revenue = new
        {
            annualRevenue.Source.Url,
            annualRevenue.Period,
            RowCount = annualRevenue.Rows.Count,
            FacilityCount = revenueIds.Count,
            MetricKeys = annualRevenue.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
            TotalComparableRevenue = annualRevenue.Rows
                .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
                .Sum(row => row.ReportedAmount),
            annualRevenue.Warnings,
            annualRevenue.ContentChecksum
        },
        Facilities = new
        {
            facilities.Source.Url,
            facilities.Period,
            RowCount = facilities.Rows.Count,
            GeocodeMinimumLatitude = facilities.Rows.Min(row => row.Latitude),
            GeocodeMaximumLatitude = facilities.Rows.Max(row => row.Latitude),
            GeocodeMinimumLongitude = facilities.Rows.Min(row => row.Longitude),
            GeocodeMaximumLongitude = facilities.Rows.Max(row => row.Longitude),
            OrganizationGamingCount = facilities.Rows.Count(row => row.HasRacetrack == true),
            facilities.Warnings,
            facilities.ContentChecksum
        }
    }));
    return;
}

if (args is ["--probe-michigan-facilities"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var dataset = await new MichiganGamingFacilityInventoryProvider(
            providerProbeHttp,
            Options.Create(new MichiganGamingFacilityProviderOptions()))
        .FetchAsync(new ProviderFetchRequest(
            "US-MI",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        dataset.Source.Url,
        dataset.Period,
        RowCount = dataset.Rows.Count,
        TribalCount = dataset.Rows.Count(row => row.TribalNationName is not null),
        CommercialCount = dataset.Rows.Count(row => row.TribalNationName is null),
        SouthernMichigan = dataset.Rows
            .Where(row => row.Latitude < 43)
            .OrderBy(row => row.Name)
            .Select(row => new { row.StableVenueId, row.Name, row.City, row.Latitude, row.Longitude }),
        dataset.Warnings,
        dataset.ContentChecksum
    }));
    return;
}

if (args is ["--probe-michigan-performance"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var dataset = await new MichiganGamingControlBoardRevenueProvider(
            providerProbeHttp,
            Options.Create(new MichiganGamingFacilityProviderOptions()))
        .FetchAsync(new ProviderFetchRequest(
            "US-MI",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));
    var comparable = dataset.Rows
        .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        dataset.Source.Url,
        dataset.Period,
        RowCount = dataset.Rows.Count,
        FacilityCount = comparable.Select(row => row.StableVenueId).Distinct().Count(),
        MonthCount = comparable.Select(row => row.PeriodStart).Distinct().Count(),
        MetricKeys = dataset.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
        TotalComparableRevenue = comparable.Sum(row => row.ReportedAmount),
        AnnualByFacility = comparable
            .GroupBy(row => row.StableVenueId)
            .OrderBy(group => group.Key)
            .Select(group => new { StableVenueId = group.Key, Revenue = group.Sum(row => row.ReportedAmount) }),
        dataset.Warnings,
        dataset.ContentChecksum
    }));
    return;
}

if (args is ["--probe-ohio-providers"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var configured = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var revenueProvider = new OhioCasinoControlCommissionRevenueProvider(providerProbeHttp, configured);
    var request = new ProviderFetchRequest(
        "US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var revenue = await revenueProvider.FetchAsync(request);
    var facilities = await new OhioCasinoControlCommissionFacilityInventoryProvider(
            providerProbeHttp,
            revenueProvider,
            configured)
        .FetchAsync(request);
    var comparable = revenue.Rows
        .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .ToArray();
    var revenueIds = comparable.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    var facilityIds = facilities.Rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    if (!revenueIds.SetEquals(facilityIds))
    {
        throw new InvalidOperationException("Live OCCC revenue and facility stable-ID sets did not reconcile.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Revenue = new
        {
            revenue.Source.Url,
            revenue.Period,
            RowCount = revenue.Rows.Count,
            FacilityCount = revenueIds.Count,
            MonthCount = comparable.Select(row => row.PeriodStart).Distinct().Count(),
            MetricKeys = revenue.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
            TotalComparableRevenue = comparable.Sum(row => row.ReportedAmount),
            AnnualByFacility = comparable
                .GroupBy(row => row.StableVenueId)
                .OrderBy(group => group.Key)
                .Select(group => new { StableVenueId = group.Key, Revenue = group.Sum(row => row.ReportedAmount) }),
            revenue.Warnings,
            revenue.ContentChecksum
        },
        Facilities = new
        {
            facilities.Source.Url,
            facilities.Period,
            RowCount = facilities.Rows.Count,
            TotalTables = facilities.Rows.Sum(row => row.TableGameCount),
            TotalSlots = facilities.Rows.Sum(row => row.SlotOrVltPositions),
            facilities.Warnings,
            facilities.ContentChecksum
        }
    }));
    return;
}

if (args is ["--export-michigan-provider-bundle", var bundleOutputPath])
{
    var fullOutputPath = Path.GetFullPath(bundleOutputPath);
    if (!string.Equals(Path.GetExtension(fullOutputPath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !Directory.Exists(Path.GetDirectoryName(fullOutputPath)))
    {
        throw new ArgumentException("Michigan provider bundle output must be a .json file in an existing directory.");
    }
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var configured = Options.Create(new MichiganGamingFacilityProviderOptions());
    var request = new ProviderFetchRequest(
        "US-MI",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var facilities = await new MichiganGamingFacilityInventoryProvider(providerProbeHttp, configured)
        .FetchAsync(request);
    var performance = await new MichiganGamingControlBoardRevenueProvider(providerProbeHttp, configured)
        .FetchAsync(request);
    var bundle = new MichiganProviderValidationBundle(facilities, performance);
    await File.WriteAllTextAsync(
        fullOutputPath,
        JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        BundlePath = fullOutputPath,
        FacilityRows = facilities.Rows.Count,
        PerformanceRows = performance.Rows.Count,
        facilities.ContentChecksum,
        PerformanceChecksum = performance.ContentChecksum
    }));
    return;
}

var validateIncumbentCalibration = args is ["--validate-incumbent-calibration", _, _];
var validateMichiganProviderBundle = args is ["--validate-michigan-provider-bundle", _, _];
var validateOhioProviderIngestion = args is ["--validate-ohio-provider-ingestion", _];
var validateProviderIngestion = args is ["--validate-provider-ingestion", _] ||
                                 validateIncumbentCalibration ||
                                 validateMichiganProviderBundle ||
                                 validateOhioProviderIngestion;
if ((!validateIncumbentCalibration && !validateMichiganProviderBundle && args.Length != 2) ||
    ((validateIncumbentCalibration || validateMichiganProviderBundle) && args.Length != 3))
{
    throw new ArgumentException(
        "Usage: GravityModelIntegrationHarness <validation-db> <valhalla-base-url> | --probe-zcta-origins | --probe-irs-soi | --probe-indiana-providers | --probe-illinois-providers | --probe-michigan-facilities | --probe-michigan-performance | --probe-ohio-providers | --export-michigan-provider-bundle <output-json> | --validate-michigan-provider-bundle <validation-db> <bundle-json> | --validate-ohio-provider-ingestion <validation-db> | --validate-provider-ingestion <validation-db> | --validate-incumbent-calibration <validation-db> <valhalla-base-url>");
}

var validationDatabase = validateProviderIngestion ? args[1] : args[0];
var requiredDatabasePrefix = validateProviderIngestion
    ? validateIncumbentCalibration
        ? "savenein_calibration_validation_"
        : "savenein_provider_validation_"
    : "savenein_gravity_validation_";
if (!validationDatabase.StartsWith(requiredDatabasePrefix, StringComparison.Ordinal) ||
    validationDatabase.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
{
    throw new ArgumentException("Unsafe validation database name.");
}

var configuredConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("The app container did not provide ConnectionStrings__DefaultConnection.");
var connectionBuilder = new NpgsqlConnectionStringBuilder(configuredConnection)
{
    Database = validationDatabase
};
var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionBuilder.ConnectionString, provider => provider.UseNetTopologySuite())
    .Options;

await using var db = new AppDbContext(dbOptions);
await ModelFoundationInitializer.ApplySchemaAsync(db);
await ModelFoundationInitializer.SeedAsync(db);

if (validateMichiganProviderBundle)
{
    var bundlePath = Path.GetFullPath(args[2]);
    if (!File.Exists(bundlePath) || !string.Equals(Path.GetExtension(bundlePath), ".json", StringComparison.OrdinalIgnoreCase))
    {
        throw new FileNotFoundException("The Michigan provider validation bundle was not found.", bundlePath);
    }
    var bundle = JsonSerializer.Deserialize<MichiganProviderValidationBundle>(
            await File.ReadAllTextAsync(bundlePath))
        ?? throw new InvalidDataException("The Michigan provider validation bundle is empty or invalid.");
    var request = new ProviderFetchRequest(
        "US-MI",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var snapshots = new DataSnapshotService(db);
    var rows = new ModelDataIngestionService(db);
    var bundleIngestion = new ProviderSnapshotIngestionService(snapshots, rows);
    var facilitySnapshotId = await bundleIngestion.IngestGamingFacilitiesAsync(
        new FrozenMichiganFacilityProvider(bundle.Facilities),
        request);
    var performanceSnapshotId = await bundleIngestion.IngestGamingPerformanceAsync(
        new FrozenMichiganPerformanceProvider(bundle.Performance),
        request,
        facilitySnapshotId);
    var facilityCount = await db.CasinoCompetitors.CountAsync(row => row.DatasetSnapshotId == facilitySnapshotId);
    var performanceCount = await db.CasinoGamingRevenuePeriods.CountAsync(row => row.DatasetSnapshotId == performanceSnapshotId);
    var comparableTotal = await db.CasinoGamingRevenuePeriods
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId &&
                      row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .SumAsync(row => row.ReportedAmount);
    var snapshotStates = await db.DatasetSnapshots
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .Select(snapshot => new { snapshot.DatasetKey, snapshot.IsSealed, snapshot.ValidationState, snapshot.Checksum })
        .OrderBy(snapshot => snapshot.DatasetKey)
        .ToArrayAsync();
    if (facilityCount != 27 || performanceCount != 72 || comparableTotal != 1_265_324_361.46m ||
        snapshotStates.Length != 2 || snapshotStates.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("The Michigan provider bundle did not persist the expected sealed regulator rows.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        facilitySnapshotId,
        performanceSnapshotId,
        facilityCount,
        performanceCount,
        comparableTotal,
        snapshotStates,
        SourceMode = "Official MGCB provider outputs fetched locally because michigan.gov returned HTTP 403 to the VPS source IP; exact provider checksums and rows were transferred for disposable PostGIS ingestion validation."
    }));
    return;
}

if (validateOhioProviderIngestion)
{
    var request = new ProviderFetchRequest(
        "US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var configured = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var revenueProvider = new OhioCasinoControlCommissionRevenueProvider(providerHttp, configured);
    var facilityProvider = new OhioCasinoControlCommissionFacilityInventoryProvider(
        providerHttp,
        revenueProvider,
        configured);
    var snapshots = new DataSnapshotService(db);
    var rows = new ModelDataIngestionService(db);
    var ohioIngestion = new ProviderSnapshotIngestionService(snapshots, rows);
    var facilitySnapshotId = await ohioIngestion.IngestGamingFacilitiesAsync(facilityProvider, request);
    var performanceSnapshotId = await ohioIngestion.IngestGamingPerformanceAsync(
        revenueProvider,
        request,
        facilitySnapshotId);
    var facilityCount = await db.CasinoCompetitors.CountAsync(row => row.DatasetSnapshotId == facilitySnapshotId);
    var performanceCount = await db.CasinoGamingRevenuePeriods.CountAsync(row => row.DatasetSnapshotId == performanceSnapshotId);
    var comparableTotal = await db.CasinoGamingRevenuePeriods
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId &&
                      row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .SumAsync(row => row.ReportedAmount);
    var snapshotStates = await db.DatasetSnapshots
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .Select(snapshot => new { snapshot.DatasetKey, snapshot.IsSealed, snapshot.ValidationState, snapshot.Checksum })
        .OrderBy(snapshot => snapshot.DatasetKey)
        .ToArrayAsync();
    if (facilityCount != 4 || performanceCount != 96 || comparableTotal != 1_033_920_366m ||
        snapshotStates.Length != 2 || snapshotStates.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("The live Ohio provider ingestion did not persist the expected sealed regulator rows.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        facilitySnapshotId,
        performanceSnapshotId,
        facilityCount,
        performanceCount,
        comparableTotal,
        snapshotStates
    }));
    return;
}

if (validateProviderIngestion)
{
    var providerSnapshots = new DataSnapshotService(db);
    var providerRows = new ModelDataIngestionService(db);
    var providerIngestion = new ProviderSnapshotIngestionService(providerSnapshots, providerRows);
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var igcOptions = Options.Create(new IndianaGamingCommissionProviderOptions());
    var igbOptions = Options.Create(new IllinoisGamingBoardProviderOptions());
    var mgcbOptions = Options.Create(new MichiganGamingFacilityProviderOptions());
    var occcOptions = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var igcRevenueProvider = new IndianaGamingCommissionMonthlyRevenueProvider(providerHttp, igcOptions);
    var igcFacilityProvider = new IndianaGamingCommissionFacilityInventoryProvider(providerHttp, igcOptions);
    var igbRevenueProvider = new IllinoisGamingBoardRevenueProvider(providerHttp, igbOptions);
    var igbFacilityProvider = new IllinoisGamingBoardFacilityInventoryProvider(
        providerHttp,
        igbRevenueProvider,
        igbOptions);
    var mgcbRevenueProvider = new MichiganGamingControlBoardRevenueProvider(providerHttp, mgcbOptions);
    var mgcbFacilityProvider = new MichiganGamingFacilityInventoryProvider(providerHttp, mgcbOptions);
    var occcRevenueProvider = new OhioCasinoControlCommissionRevenueProvider(providerHttp, occcOptions);
    var occcFacilityProvider = new OhioCasinoControlCommissionFacilityInventoryProvider(
        providerHttp,
        occcRevenueProvider,
        occcOptions);
    var igcFacilityPeriod = new ProviderFetchRequest(
        "US-IN,US-IL,US-MI,US-OH",
        new DateOnly(2025, 12, 1),
        new DateOnly(2025, 12, 31));
    var igcPerformancePeriod = new ProviderFetchRequest(
        "US-IN,US-IL,US-MI,US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var facilitySnapshotId = await providerIngestion.IngestGamingFacilitiesAsync(
        new CompositeGamingFacilityInventoryProvider(
            new IGamingFacilityInventoryProvider[] { igcFacilityProvider, igbFacilityProvider, mgcbFacilityProvider, occcFacilityProvider }),
        igcFacilityPeriod);
    var performanceSnapshotId = await providerIngestion.IngestGamingPerformanceAsync(
        new CompositeGamingRegulatorPerformanceProvider(
            new IGamingRegulatorPerformanceProvider[] { igcRevenueProvider, igbRevenueProvider, mgcbRevenueProvider, occcRevenueProvider }),
        igcPerformancePeriod,
        facilitySnapshotId);
    var trafficSnapshotId = await providerIngestion.IngestTrafficAsync(
        new IndianaDepartmentOfTransportationAadtProvider(
            providerHttp,
            Options.Create(new IndianaDepartmentOfTransportationProviderOptions())),
        new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["site-numbers"] = "970200"
            }));
    var irsPeriod = new ProviderFetchRequest(
        "US-STATES",
        new DateOnly(2022, 1, 1),
        new DateOnly(2022, 12, 31),
        new Dictionary<string, string> { ["state-codes"] = "IL,IN,KY,MI,OH" });
    var irsDataset = await new IrsSoiExactCodeZctaIncomeProvider(
            providerHttp,
            Options.Create(new IrsSoiProviderOptions()))
        .FetchAsync(irsPeriod);
    var zctaCodes = string.Join(',', irsDataset.Rows
        .Select(row => row.StableOriginId["USA-ZCTA-".Length..])
        .Order(StringComparer.Ordinal));
    var originSnapshotId = await providerIngestion.IngestOriginsAsync(
        new CensusZctaOriginProvider(
            providerHttp,
            Options.Create(new CensusZctaOriginProviderOptions())),
        new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = zctaCodes }));
    var incomeSnapshotId = await providerIngestion.IngestIncomeAsync(
        new FrozenOriginIncomeProvider(irsDataset),
        irsPeriod,
        originSnapshotId);
    var censusApiKey = Environment.GetEnvironmentVariable("CensusAcs__ApiKey");
    var ageSnapshotId = await providerIngestion.IngestAgePopulationAsync(
        new CensusAcsAgePopulationProvider(
            providerHttp,
            Options.Create(new CensusAcsProviderOptions { ApiKey = censusApiKey })),
        new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = zctaCodes }),
        originSnapshotId);
    var tourismSnapshotId = await providerIngestion.IngestTourismAsync(
        new IndianaDestinationDevelopmentPersonTripsProvider(
            providerHttp,
            Options.Create(new IndianaTourismProviderOptions())),
        new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31)));

    var facilityCount = await db.CasinoCompetitors.CountAsync(row => row.DatasetSnapshotId == facilitySnapshotId);
    var unknownHotelCount = await db.CasinoCompetitors.CountAsync(
        row => row.DatasetSnapshotId == facilitySnapshotId && row.HasHotel == null);
    var performanceCount = await db.CasinoGamingRevenuePeriods.CountAsync(
        row => row.DatasetSnapshotId == performanceSnapshotId);
    var trafficCount = await db.TrafficCorridorObservations.CountAsync(
        row => row.DatasetSnapshotId == trafficSnapshotId);
    var originCount = await db.OriginZones.CountAsync(row => row.DatasetSnapshotId == originSnapshotId);
    var incomeCount = await db.OriginZoneIncomePeriods.CountAsync(row => row.DatasetSnapshotId == incomeSnapshotId);
    var ageBinCount = await db.OriginZoneAgeBins.CountAsync(row => row.DatasetSnapshotId == ageSnapshotId);
    var tourismCount = await db.TourismMarketObservations.CountAsync(row => row.DatasetSnapshotId == tourismSnapshotId);
    var metricKeys = await db.CasinoGamingRevenuePeriods
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId)
        .Select(row => row.ReportedMetricKey)
        .Distinct()
        .Order()
        .ToArrayAsync();
    var snapshotStates = await db.DatasetSnapshots
        .Where(snapshot =>
            snapshot.Id == facilitySnapshotId ||
            snapshot.Id == performanceSnapshotId ||
            snapshot.Id == trafficSnapshotId ||
            snapshot.Id == originSnapshotId ||
            snapshot.Id == incomeSnapshotId ||
            snapshot.Id == ageSnapshotId ||
            snapshot.Id == tourismSnapshotId)
        .Select(snapshot => new { snapshot.Id, snapshot.DatasetKey, snapshot.IsSealed, snapshot.ValidationState })
        .OrderBy(snapshot => snapshot.DatasetKey)
        .ToArrayAsync();
    if (facilityCount != 61 || unknownHotelCount != 61 || performanceCount != 670 || trafficCount != 1 ||
        originCount != irsDataset.Rows.Count || incomeCount != irsDataset.Rows.Count ||
        ageBinCount != irsDataset.Rows.Count * 23 || tourismCount != 1 ||
        snapshotStates.Length != 7 || snapshotStates.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("Live multi-jurisdiction provider ingestion did not persist the expected sealed rows.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        facilitySnapshotId,
        performanceSnapshotId,
        trafficSnapshotId,
        originSnapshotId,
        incomeSnapshotId,
        ageSnapshotId,
        tourismSnapshotId,
        facilityCount,
        unknownHotelCount,
        performanceCount,
        trafficCount,
        originCount,
        incomeCount,
        ageBinCount,
        tourismCount,
        metricKeys,
        snapshotStates
    }));

    if (validateIncumbentCalibration)
    {
        var indianaOriginCount = await db.OriginZones.CountAsync(origin =>
            origin.DatasetSnapshotId == originSnapshotId && origin.StateOrTerritoryCode == "IN");
        if (indianaOriginCount == 0)
        {
            throw new InvalidOperationException(
                "The live ZCTA snapshot did not preserve Census relationship-based Indiana geography attributes.");
        }

        var validationIndiana = await db.Jurisdictions.SingleAsync(jurisdiction => jurisdiction.Code == "US-IN");
        db.JurisdictionRules.AddRange(
            new JurisdictionRule
            {
                JurisdictionId = validationIndiana.Id,
                RuleType = JurisdictionRuleTypes.GamingTaxSchedule,
                RuleValueJson = JsonSerializer.Serialize(new GamingTaxSchedulePayload(
                    "commercial-casino",
                    "Disposable calibration taxable GGR",
                    [new GamingTaxBracketPayload(null, 0.10m)])),
                ValidationState = JurisdictionRuleValidationStates.Validated,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SourceUrl = "https://example.invalid/disposable-calibration-gaming-tax",
                ProvenanceNotes = "Disposable database-only validation fixture; not an Indiana fiscal calibration."
            },
            new JurisdictionRule
            {
                JurisdictionId = validationIndiana.Id,
                RuleType = JurisdictionRuleTypes.LocalRevenueShare,
                RuleValueJson = JsonSerializer.Serialize(new LocalRevenueSharePayload("commercial-casino", 0.20m)),
                ValidationState = JurisdictionRuleValidationStates.Validated,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SourceUrl = "https://example.invalid/disposable-calibration-local-share",
                ProvenanceNotes = "Disposable database-only validation fixture; not an Indiana fiscal calibration."
            },
            new JurisdictionRule
            {
                JurisdictionId = validationIndiana.Id,
                RuleType = JurisdictionRuleTypes.GeneralFiscalRates,
                RuleValueJson = JsonSerializer.Serialize(new GeneralFiscalRulePayload(
                    "commercial-casino", 0.07m, 0.05m, 0.03m, 25_000m, 0.10m)),
                ValidationState = JurisdictionRuleValidationStates.Validated,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SourceUrl = "https://example.invalid/disposable-calibration-general-fiscal",
                ProvenanceNotes = "Disposable database-only validation fixture; not an Indiana fiscal calibration."
            });
        await db.SaveChangesAsync();

        var originIds = await db.OriginZones.AsNoTracking()
            .Where(origin => origin.DatasetSnapshotId == originSnapshotId)
            .OrderBy(origin => origin.StableOriginId)
            .Select(origin => origin.StableOriginId)
            .ToArrayAsync();
        var liveCompetitors = await db.CasinoCompetitors.AsNoTracking()
            .Where(competitor => competitor.DatasetSnapshotId == facilitySnapshotId)
            .OrderBy(competitor => competitor.StableVenueId)
            .ToArrayAsync();
        var competitorByStableId = liveCompetitors.ToDictionary(
            competitor => competitor.StableVenueId,
            StringComparer.OrdinalIgnoreCase);
        IncumbentBacktestTarget Target(string stableId, string partition, string group) => new(
            competitorByStableId[stableId].Id,
            $"live-2025-{stableId["USA-IN-IGC-".Length..]}",
            partition,
            group);
        var targets = new[]
        {
            Target("USA-IN-IGC-french-lick-resort", ValidationPartitions.Training, "southern-indiana"),
            Target("USA-IN-IGC-caesars-southern-indiana", ValidationPartitions.Training, "southern-indiana-border"),
            Target("USA-IN-IGC-terre-haute-casino", ValidationPartitions.Training, "western-indiana"),
            Target("USA-IN-IGC-ballys-evansville", ValidationPartitions.Holdout, "southwestern-indiana"),
            Target("USA-IN-IGC-hollywood-lawrenceburg", ValidationPartitions.Holdout, "cincinnati-border-market")
        };
        var sourceParameterSet = await db.ModelParameterSets.AsNoTracking().SingleAsync(
            set => set.Key == "national-base" && set.Version == "0.1.0-provisional");
        using var calibrationHttp = new HttpClient
        {
            BaseAddress = new Uri(args[2], UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(2)
        };
        var calibrationValhalla = new ValhallaClient(
            calibrationHttp,
            NullLogger<ValhallaClient>.Instance);
        var profiles = new JurisdictionProfileService(db);
        var calibrationExecution = new GravityModelExecutionService(
            db,
            new ModelParameterService(db),
            new GamingAgeResolver(profiles),
            new CompetitiveUniverseService(db),
            new OriginDemandService(),
            new FacilityAttractivenessService(),
            new TravelMatrixService(db, calibrationValhalla),
            new MarketEquilibriumService(new GravityModelService()),
            new AccessibilityExpansionService(),
            new TourismDemandService(),
            new TrafficInterceptService(),
            new CapacityDiagnosticService(),
            new RampScheduleService(),
            new GamingTaxCalculator(profiles),
            new LocalRevenueShareCalculator(profiles),
            new GeneralFiscalRuleResolver(profiles),
            new CannibalizationAccountingService(),
            new LocalEconomicInventoryWeightService(),
            new DisplacementModelService(),
            new EmploymentImpactService(),
            new FiscalImpactService(),
            new SocialCostService(),
            new NetImpactService());
        var baseRequest = new GravityModelRunRequest(
            "Live 2025 Indiana incumbent backtest",
            "US-IN",
            Guid.Empty,
            0,
            0,
            originSnapshotId,
            ageSnapshotId,
            incomeSnapshotId,
            facilitySnapshotId,
            performanceSnapshotId,
            originIds,
            [],
            2022,
            2022,
            new DateOnly(2025, 12, 31),
            "commercial-casino",
            GravityDemandSpecifications.AgiShare,
            FacilityAttractionSpecifications.HybridObservedGgr,
            "inverse-power",
            GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            sourceParameterSet.Id,
            null,
            null,
            [],
            CompetitorPrefilterMiles: 50,
            ImpactGeography: new ImpactGeographyDefinition(ImpactScopeKinds.HostState, "US-IN"),
            MissingRoutePolicy: MissingRoutePolicies.ExcludeFacility);
        var metricsService = new ValidationMetricsService();
        var calibration = await new IncumbentBacktestCalibrationService(
            db,
            new DevelopmentProgramService(db),
            calibrationExecution,
            metricsService,
            new ValidationEvaluationService(
                db,
                metricsService,
                new ComparableMarketModelService(),
                new ModelParameterSetService(db)))
            .CalibrateAsync(new IncumbentBacktestCalibrationRequest(
                "live-indiana-incumbent-backtest",
                "2025-disposable-validation",
                ValidationObjectiveFunctions.Smape,
                baseRequest,
                targets,
                [
                    new IncumbentCalibrationCandidate("beta-1.4-outside-0.001", new Dictionary<string, double> { ["gravity.beta"] = 1.4, ["gravity.outside_option_weight"] = 0.001 }),
                    new IncumbentCalibrationCandidate("beta-1.4-outside-0.01", new Dictionary<string, double> { ["gravity.beta"] = 1.4, ["gravity.outside_option_weight"] = 0.01 }),
                    new IncumbentCalibrationCandidate("beta-1.4-outside-0.1", new Dictionary<string, double> { ["gravity.beta"] = 1.4, ["gravity.outside_option_weight"] = 0.1 }),
                    new IncumbentCalibrationCandidate("beta-1.5-outside-0.001", new Dictionary<string, double> { ["gravity.beta"] = 1.5, ["gravity.outside_option_weight"] = 0.001 }),
                    new IncumbentCalibrationCandidate("beta-1.5-outside-0.01", new Dictionary<string, double> { ["gravity.beta"] = 1.5, ["gravity.outside_option_weight"] = 0.01 }),
                    new IncumbentCalibrationCandidate("beta-1.5-outside-0.1", new Dictionary<string, double> { ["gravity.beta"] = 1.5, ["gravity.outside_option_weight"] = 0.1 }),
                    new IncumbentCalibrationCandidate("beta-1.6-outside-0.001", new Dictionary<string, double> { ["gravity.beta"] = 1.6, ["gravity.outside_option_weight"] = 0.001 }),
                    new IncumbentCalibrationCandidate("beta-1.6-outside-0.01", new Dictionary<string, double> { ["gravity.beta"] = 1.6, ["gravity.outside_option_weight"] = 0.01 }),
                    new IncumbentCalibrationCandidate("beta-1.6-outside-0.1", new Dictionary<string, double> { ["gravity.beta"] = 1.6, ["gravity.outside_option_weight"] = 0.1 })
                ],
                ["total-resident-demand", "gaming-positions"],
                JsonSerializer.Serialize(new
                {
                    purpose = "Disposable execution proof against authoritative live source snapshots.",
                    limitation = "Indiana, Illinois, and Michigan commercial regulator coverage is live; Ohio, Kentucky, tribal performance, local tourism, and corridor-complete traffic remain incomplete. Results are not production calibration evidence.",
                    zctaAssignment = "Dominant 2020 Census county by land-area overlap."
                }),
                sourceParameterSet.Id,
                "0.1.0-live-backtest-disposable",
                OriginPrefilterMiles: 50));
        var evaluation = await db.ValidationEvaluations.AsNoTracking()
            .SingleAsync(item => item.Id == calibration.Evaluation.ValidationEvaluationId);
        var caseCount = await db.ValidationCases.CountAsync(item =>
            item.CaseKey.StartsWith("live-2025-"));
        var finalizedRunCount = await db.ModelRuns.CountAsync(run =>
            run.Status == ModelRunStatuses.Finalized);
        if (evaluation.Status != ValidationEvaluationStatuses.Finalized || !evaluation.IsImmutable ||
            calibration.Evaluation.PublishedParameterSetId is null || caseCount != targets.Length ||
            finalizedRunCount != targets.Length * 9)
        {
            throw new InvalidOperationException("Live incumbent calibration did not persist complete immutable evidence.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Calibration = new
            {
                calibration.SelectedCandidateKey,
                calibration.SelectedParameters,
                CandidateResults = calibration.CandidateResults.Select(candidate => new
                {
                    candidate.CandidateKey,
                    candidate.ObjectiveValue,
                    candidate.TrainingMetrics
                }),
                calibration.Evaluation.ValidationEvaluationId,
                calibration.Evaluation.PublishedParameterSetId,
                calibration.Evaluation.TrainingMetrics,
                calibration.Evaluation.HoldoutMetrics,
                calibration.Evaluation.ComparableTrainingMetrics,
                calibration.Evaluation.ComparableHoldoutMetrics,
                caseCount,
                finalizedRunCount,
                indianaOriginCount,
                limitation = "Disposable pipeline proof only: Indiana, Illinois, and Michigan commercial coverage is live; Ohio, Kentucky, tribal performance, and local visitor/traffic capture remain incomplete."
            }
        }));
    }
    return;
}

var indiana = await db.Jurisdictions.SingleAsync(jurisdiction => jurisdiction.Code == "US-IN");
db.JurisdictionRules.AddRange(
    new JurisdictionRule
    {
        JurisdictionId = indiana.Id,
        RuleType = JurisdictionRuleTypes.GamingTaxSchedule,
        RuleValueJson = JsonSerializer.Serialize(new GamingTaxSchedulePayload(
            "commercial-casino",
            "Disposable integration taxable GGR",
            [new GamingTaxBracketPayload(null, 0.10m)])),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/integration-gaming-tax",
        ProvenanceNotes = "Disposable integration fixture only."
    },
    new JurisdictionRule
    {
        JurisdictionId = indiana.Id,
        RuleType = JurisdictionRuleTypes.LocalRevenueShare,
        RuleValueJson = JsonSerializer.Serialize(new LocalRevenueSharePayload("commercial-casino", 0.20m)),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/integration-local-share",
        ProvenanceNotes = "Disposable integration fixture only."
    },
    new JurisdictionRule
    {
        JurisdictionId = indiana.Id,
        RuleType = JurisdictionRuleTypes.GeneralFiscalRates,
        RuleValueJson = JsonSerializer.Serialize(new GeneralFiscalRulePayload(
            "commercial-casino", 0.07m, 0.05m, 0.03m, 25_000m, 0.10m)),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/integration-general-fiscal",
        ProvenanceNotes = "Disposable integration fixture only."
    });
await db.SaveChangesAsync();

var unitedStates = await db.Jurisdictions.SingleAsync(jurisdiction => jurisdiction.Code == "US");
var ohio = new Jurisdiction
{
    Code = "US-OH",
    Name = "Ohio synthetic validation jurisdiction",
    Kind = "state",
    ParentJurisdictionId = unitedStates.Id,
    IsActive = true
};
db.Jurisdictions.Add(ohio);
await db.SaveChangesAsync();
db.JurisdictionRules.AddRange(
    new JurisdictionRule
    {
        JurisdictionId = ohio.Id,
        RuleType = JurisdictionRuleTypes.LegalGamingAge,
        RuleValueJson = JsonSerializer.Serialize(new GamingAgeRulePayload("commercial-casino", 21)),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/ohio-synthetic-gaming-age",
        ProvenanceNotes = "Disposable non-Indiana portability fixture only."
    },
    new JurisdictionRule
    {
        JurisdictionId = ohio.Id,
        RuleType = JurisdictionRuleTypes.GamingTaxSchedule,
        RuleValueJson = JsonSerializer.Serialize(new GamingTaxSchedulePayload(
            "commercial-casino",
            "Synthetic Ohio taxable GGR",
            [new GamingTaxBracketPayload(null, 0.07m)])),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/ohio-synthetic-gaming-tax",
        ProvenanceNotes = "Disposable non-Indiana portability fixture only."
    },
    new JurisdictionRule
    {
        JurisdictionId = ohio.Id,
        RuleType = JurisdictionRuleTypes.LocalRevenueShare,
        RuleValueJson = JsonSerializer.Serialize(new LocalRevenueSharePayload("commercial-casino", 0.15m)),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/ohio-synthetic-local-share",
        ProvenanceNotes = "Disposable non-Indiana portability fixture only."
    },
    new JurisdictionRule
    {
        JurisdictionId = ohio.Id,
        RuleType = JurisdictionRuleTypes.GeneralFiscalRates,
        RuleValueJson = JsonSerializer.Serialize(new GeneralFiscalRulePayload(
            "commercial-casino", 0.055m, 0.04m, 0.025m, 23_000m, 0.08m)),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/ohio-synthetic-general-fiscal",
        ProvenanceNotes = "Disposable non-Indiana portability fixture only."
    });
await db.SaveChangesAsync();

var snapshotService = new DataSnapshotService(db);
var ingestion = new ModelDataIngestionService(db);
var source = await snapshotService.RegisterSourceAsync(new RegisterDataSourceRequest(
    "Canonical gravity integration fixture",
    "SaveNEIN validation",
    "https://example.invalid/canonical-gravity-integration",
    "integration-fixture",
    "Fort Wayne and South Bend, Indiana",
    "2025",
    DateTime.UtcNow,
    "gravity-integration-fixture-v1",
    false,
    "Disposable validation fixture only.",
    "Exercises sealed snapshots and live Valhalla routing."));

async Task<DatasetSnapshot> BeginAsync(string kind, long rows, string checksum) =>
    await snapshotService.BeginSnapshotAsync(new BeginDatasetSnapshotRequest(
        source.Id,
        kind,
        "2025",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31),
        rows,
        checksum,
        "integration-v1"));

async Task SealAsync(DatasetSnapshot snapshot) =>
    _ = await snapshotService.SealSnapshotAsync(new SealDatasetSnapshotRequest(
        snapshot.Id,
        DatasetValidationStates.Validated,
        [],
        []));

var geography = await BeginAsync(DatasetSnapshotKinds.OriginGeography, 1, "gravity-integration-geography-v1");
await ingestion.AppendOriginsAsync(geography.Id,
[
    new OriginZoneImportRow(
        "USA-ZCTA-46802",
        "zcta",
        "46802",
        "USA",
        "IN",
        "18003",
        "23060",
        "258",
        41.075,
        -85.142,
        "POLYGON((-85.20 41.02,-85.08 41.02,-85.08 41.13,-85.20 41.13,-85.20 41.02))")
]);
await SealAsync(geography);

var age = await BeginAsync(DatasetSnapshotKinds.AgePopulation, 3, "gravity-integration-age-v1");
await ingestion.AppendAgeBinsAsync(age.Id, new OriginAgeBinImportRequest(
    geography.Id,
    [
        new OriginAgeBinImportRow("USA-ZCTA-46802", 2025, 0, 17, 12_000, DatasetValidationStates.Validated),
        new OriginAgeBinImportRow("USA-ZCTA-46802", 2025, 18, 20, 2_000, DatasetValidationStates.Validated),
        new OriginAgeBinImportRow("USA-ZCTA-46802", 2025, 21, null, 32_000, DatasetValidationStates.Validated)
    ]));
await SealAsync(age);

var income = await BeginAsync(DatasetSnapshotKinds.Income, 1, "gravity-integration-income-v1");
await ingestion.AppendIncomeAsync(income.Id, new OriginIncomeImportRequest(
    geography.Id,
    [
        new OriginIncomeImportRow("USA-ZCTA-46802", 2025, 20_000, 1_100_000_000m, 1_100_000_000m, 58_000m, 2025, null)
    ]));
await SealAsync(income);

var competitors = await BeginAsync(DatasetSnapshotKinds.Competitors, 1, "gravity-integration-competitors-v1");
await ingestion.AppendCompetitorsAsync(competitors.Id,
[
    new CasinoCompetitorImportRow(
        "USA-IN-SOUTH-BEND-SMOKE",
        "South Bend integration competitor",
        "IN",
        "USA",
        "commercial-casino",
        "commercial-casino",
        "active",
        null,
        "Integration regulator",
        "integration-license",
        null,
        new DateOnly(2018, 1, 1),
        null,
        "St. Joseph",
        "South Bend",
        41.7075,
        -86.2600,
        true,
        "Integration operator",
        "https://example.invalid/venue",
        DateTime.UtcNow,
        true,
        true,
        false,
        false,
        false,
        true,
        true,
        true,
        true,
        true,
        1_900,
        1_850,
        45,
        0,
        55_000,
        300,
        1_000,
        5,
        500_000_000m,
        2025,
        "interstate-adjacent",
        1.5,
        true,
        "regional",
        false,
        "Disposable integration fixture.")
]);
await SealAsync(competitors);

var observed = await BeginAsync(DatasetSnapshotKinds.ObservedPerformance, 1, "gravity-integration-observed-v1");
await ingestion.AppendGamingRevenueAsync(observed.Id, new CasinoGamingRevenueImportRequest(
    competitors.Id,
    [
        new CasinoGamingRevenueImportRow(
            "USA-IN-SOUTH-BEND-SMOKE",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            "annual",
            "agr",
            "Annual adjusted gross receipts integration fixture",
            150_000_000m,
            150_000_000m,
            2025,
            [],
            null)
    ]));
await SealAsync(observed);

var tourism = await BeginAsync(DatasetSnapshotKinds.Tourism, 1, "gravity-integration-tourism-v1");
await ingestion.AppendTourismObservationsAsync(tourism.Id,
[
    new TourismMarketObservationImportRow(
        "ALLEN-VISITOR-TRIPS-2025",
        "US-IN-ALLEN",
        "county",
        "18003",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31),
        "visitor-person-trips",
        100_000m,
        100_000m,
        "provider-direct-person-trips",
        "Disposable integration fixture.")
]);
await SealAsync(tourism);

var traffic = await BeginAsync(DatasetSnapshotKinds.Traffic, 1, "gravity-integration-traffic-v1");
await ingestion.AppendTrafficObservationsAsync(traffic.Id,
[
    new TrafficCorridorObservationImportRow(
        "I69-ALLEN-2025",
        "I-69",
        "US-IN",
        41.08,
        -85.19,
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31),
        10_000,
        365,
        "aadt",
        "bidirectional",
        "Disposable integration fixture.")
]);
await SealAsync(traffic);

var localEconomicInventory = await BeginAsync(
    DatasetSnapshotKinds.LocalEconomicInventory,
    3,
    "gravity-integration-local-economic-v1");
await ingestion.AppendLocalEconomicSectorObservationsAsync(localEconomicInventory.Id,
[
    new LocalEconomicSectorObservationImportRow(
        "US-IN-RESTAURANT-HOSPITALITY-2025", "host-state", "US-IN",
        DisplacementSectorKeys.RestaurantHospitality, ["72"],
        new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
        8_000, 160_000, 5_000_000_000m, 12_000_000_000m,
        "Disposable provider-style sector inventory fixture.", null),
    new LocalEconomicSectorObservationImportRow(
        "US-IN-RETAIL-2025", "host-state", "US-IN",
        DisplacementSectorKeys.Retail, ["44", "45"],
        new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
        6_000, 120_000, 4_000_000_000m, 6_000_000_000m,
        "Disposable provider-style sector inventory fixture.", null),
    new LocalEconomicSectorObservationImportRow(
        "US-IN-ARTS-ENTERTAINMENT-2025", "host-state", "US-IN",
        DisplacementSectorKeys.ArtsEntertainmentRecreation, ["71"],
        new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
        2_000, 40_000, 1_000_000_000m, 2_000_000_000m,
        "Disposable provider-style sector inventory fixture.", null)
]);
await SealAsync(localEconomicInventory);

var developmentProgram = await new DevelopmentProgramService(db).CreateAsync(new DevelopmentProgramDefinition(
    "fort-wayne-integration-program",
    "1",
    "Fort Wayne integration program",
    1_500,
    50,
    10,
    true,
    250,
    60_000,
    6,
    1_500,
    4,
    500_000_000m,
    2025,
    new DateOnly(2028, 1, 1),
    3,
    "Disposable integration fixture."));

using var http = new HttpClient { BaseAddress = new Uri(args[1], UriKind.Absolute), Timeout = TimeSpan.FromSeconds(60) };
var valhalla = new ValhallaClient(http, NullLogger<ValhallaClient>.Instance);
var gravity = new GravityModelService();
var execution = new GravityModelExecutionService(
    db,
    new ModelParameterService(db),
    new GamingAgeResolver(new JurisdictionProfileService(db)),
    new CompetitiveUniverseService(db),
    new OriginDemandService(),
    new FacilityAttractivenessService(),
    new TravelMatrixService(db, valhalla),
    new MarketEquilibriumService(gravity),
    new AccessibilityExpansionService(),
    new TourismDemandService(),
    new TrafficInterceptService(),
    new CapacityDiagnosticService(),
    new RampScheduleService(),
    new GamingTaxCalculator(new JurisdictionProfileService(db)),
    new LocalRevenueShareCalculator(new JurisdictionProfileService(db)),
    new GeneralFiscalRuleResolver(new JurisdictionProfileService(db)),
    new CannibalizationAccountingService(),
    new LocalEconomicInventoryWeightService(),
    new DisplacementModelService(),
    new EmploymentImpactService(),
    new FiscalImpactService(),
    new SocialCostService(),
    new NetImpactService());

var integrationRequest = new GravityModelRunRequest(
    "Fort Wayne canonical integration",
    "US-IN",
    developmentProgram.Id,
    41.0793,
    -85.1394,
    geography.Id,
    age.Id,
    income.Id,
    competitors.Id,
    observed.Id,
    ["USA-ZCTA-46802"],
    [],
    2025,
    2025,
    new DateOnly(2025, 12, 31),
    "commercial-casino",
    GravityDemandSpecifications.AgiShare,
    FacilityAttractionSpecifications.ObservedGgr,
    "inverse-power",
    "agr",
    new DateOnly(2025, 1, 1),
    new DateOnly(2025, 12, 31),
    null,
    null,
    null,
    [
        new ParameterOverride("market_expansion.accessibility_elasticity", 0.2),
        new ParameterOverride("tourism.resident_origin_overlap_share", 0.2),
        new ParameterOverride("tourism.eligible_visitor_share", 0.8),
        new ParameterOverride("tourism.participation_rate", 0.1),
        new ParameterOverride("tourism.capture_rate", 0.2),
        new ParameterOverride("tourism.ggr_per_captured_participant", 100),
        new ParameterOverride("traffic.intercept_rate", 0.01),
        new ParameterOverride("traffic.resident_origin_overlap_share", 0.1),
        new ParameterOverride("traffic.overlap_deduplication_share", 0.1),
        new ParameterOverride("traffic.ggr_per_intercepted_traveler", 50),
        new ParameterOverride("capacity.diagnostic_enabled", 1),
        new ParameterOverride("capacity.slot_win_per_unit_day_minimum", 1),
        new ParameterOverride("capacity.slot_win_per_unit_day_maximum", 1000),
        new ParameterOverride("capacity.table_win_per_table_day_minimum", 1),
        new ParameterOverride("capacity.table_win_per_table_day_maximum", 5000),
        new ParameterOverride("displacement.eligible_base_share", 0.75),
        new ParameterOverride("displacement.coefficient", 0.5),
        new ParameterOverride("employment.direct_jobs_per_million_ggr", 4),
        new ParameterOverride("employment.construction_job_years_per_million_capital_cost", 2),
        new ParameterOverride("employment.indirect_induced_jobs_per_direct_job", 0.5),
        new ParameterOverride("employment.incumbent_jobs_per_million_lost_ggr", 3),
        new ParameterOverride("employment.direct_average_annual_wage", 50_000),
        new ParameterOverride("employment.indirect_average_annual_wage", 40_000),
        new ParameterOverride("employment.incumbent_average_annual_wage", 45_000),
        new ParameterOverride("fiscal.non_gaming_business_margin", 0.2),
        new ParameterOverride("social_cost.prevalence", 0.02),
        new ParameterOverride("social_cost.exposure_response", 0.1),
        new ParameterOverride("social_cost.treatment_health_per_case", 1_000),
        new ParameterOverride("social_cost.bankruptcy_debt_per_case", 500),
        new ParameterOverride("social_cost.crime_public_safety_per_case", 250),
        new ParameterOverride("social_cost.productivity_employment_per_case", 750),
        new ParameterOverride("social_cost.family_household_per_case", 250),
        new ParameterOverride("social_cost.public_assistance_administration_per_case", 250)
    ],
    TourismSnapshotId: tourism.Id,
    TourismObservationIds: ["ALLEN-VISITOR-TRIPS-2025"],
    TrafficSnapshotId: traffic.Id,
    TrafficCorridors: [new TrafficCorridorRunSelection("I69-ALLEN-2025", 0.5, 0.8)],
    LocalEconomicInventorySnapshotId: localEconomicInventory.Id);
var result = await execution.ExecuteAsync(integrationRequest);

var persistedRun = await db.ModelRuns.AsNoTracking().SingleAsync(run => run.Id == result.ModelRunId);
var originResult = await db.ModelRunOriginResults.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
var allocations = await db.ModelRunOriginFacilityAllocations.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).ToArrayAsync();
var referencedRouteIds = allocations.Select(row => row.OriginFacilityTravelId).Distinct().ToArray();
var routes = await db.OriginFacilityTravel.AsNoTracking().Where(row => referencedRouteIds.Contains(row.Id)).ToArrayAsync();
var parameterCount = await db.ModelRunParameterValues.AsNoTracking().CountAsync(row => row.ModelRunId == result.ModelRunId);
var snapshotReferenceCount = await db.ModelRunDatasetSnapshotReferences.AsNoTracking().CountAsync(row => row.ModelRunId == result.ModelRunId);
var demandComponents = await db.ModelRunDemandComponents.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).ToArrayAsync();
var capacity = await db.ModelRunCapacityDiagnostics.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
var ramp = await db.ModelRunRampResults.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).OrderBy(row => row.CalendarYear).ToArrayAsync();
var geographicAccounting = await db.ModelRunGeographicAccounting.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
var sectorDisplacement = await db.ModelRunSectorDisplacement.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).ToArrayAsync();
var employment = await db.ModelRunEmploymentImpacts.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
var fiscal = await db.ModelRunFiscalImpacts.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
var socialCosts = await db.ModelRunSocialCosts.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).ToArrayAsync();
var netImpact = await db.ModelRunNetImpacts.AsNoTracking().SingleAsync(row => row.ModelRunId == result.ModelRunId);
QuestPDF.Settings.License = LicenseType.Community;
var reportArtifacts = new ReportArtifactService(
    db,
    new CasinoImpactReportModelFactory(db),
    new HtmlReportRenderer(),
    new PdfReportRenderer(),
    new CsvReportRenderer());
var reportArtifact = await reportArtifacts.GetOrCreateAsync(
    result.ModelRunId,
    new ReportPresentationOptions("Canonical integration report", "Disposable validation", 20, "USD"));
var repeatedReportArtifact = await reportArtifacts.GetOrCreateAsync(
    result.ModelRunId,
    new ReportPresentationOptions("Canonical integration report", "Disposable validation", 20, "USD"));
var sensitivity = await new SensitivityAnalysisService(db, new ModelParameterService(db), execution).ExecuteAsync(
    new SensitivityAnalysisRequest(
        "canonical-integration-oat",
        "1",
        "Canonical integration one-at-a-time sensitivity",
        SensitivityOutputMetrics.NetHostLocalImpact,
        integrationRequest,
        [
            new SensitivityParameterRange("gravity.beta", 1.4, 1.6),
            new SensitivityParameterRange("displacement.coefficient", 0.25, 0.75)
        ]));
var sensitivityRunIds = sensitivity.Points.Select(point => point.ModelRunId).ToArray();
var finalizedSensitivityRunCount = await db.ModelRuns.AsNoTracking()
    .CountAsync(run => sensitivityRunIds.Contains(run.Id) && run.Status == ModelRunStatuses.Finalized);
var sensitivityValues = await db.ModelRunParameterValues.AsNoTracking()
    .Where(value => sensitivityRunIds.Contains(value.ModelRunId))
    .Join(
        db.ModelParameterDefinitions.AsNoTracking(),
        value => value.ParameterDefinitionId,
        definition => definition.Id,
        (value, definition) => new { value.ModelRunId, definition.Key, value.FinalValue })
    .ToListAsync();
var sensitivityReportOptions = new ReportPresentationOptions(
    "Canonical sensitivity report", "Disposable validation", 20, "USD", sensitivity.SensitivityAnalysisId);
var sensitivityReportArtifact = await reportArtifacts.GetOrCreateAsync(
    sensitivity.BaselineModelRunId,
    sensitivityReportOptions);
var repeatedSensitivityReportArtifact = await reportArtifacts.GetOrCreateAsync(
    sensitivity.BaselineModelRunId,
    sensitivityReportOptions);

if (persistedRun.Status != ModelRunStatuses.Finalized || result.Status != ModelRunStatuses.Finalized)
{
    throw new InvalidOperationException("The integration model run did not finalize.");
}
if (routes.Length != 2 || routes.Any(route => !route.RouteFound || route.TravelTimeMinutes is null))
{
    throw new InvalidOperationException("Expected two successful persisted Valhalla routes (incumbent and proposed).");
}
if (allocations.Length != 3)
{
    throw new InvalidOperationException("Expected one baseline and two with-project facility allocations to be persisted.");
}
if (parameterCount == 0 || snapshotReferenceCount != 8)
{
    throw new InvalidOperationException("Resolved parameter or dataset provenance was not persisted.");
}
if (demandComponents.Length != 2 || result.TourismGgr <= 0 || result.TrafficGgr <= 0 ||
    result.StabilizedTotalGgr != result.ProposedResidentGgr + result.TourismGgr + result.TrafficGgr)
{
    throw new InvalidOperationException("Tourism/traffic components did not remain separately persisted and reconcile to stabilized GGR.");
}
if (capacity.Status == CapacityDiagnosticStatuses.NotEvaluated || ramp.Length != 6)
{
    throw new InvalidOperationException("Capacity and opening-through-stabilization ramp outputs were not persisted.");
}
if (sectorDisplacement.Length != 3 || socialCosts.Length != 6 ||
    geographicAccounting.StabilizedGgr != result.StabilizedTotalGgr ||
    Math.Abs(sectorDisplacement.Sum(row => row.DisplacedSales) - result.LocalDiscretionaryDisplacement) > 0.02m ||
    employment.NetPermanentJobs != result.NetPermanentJobs ||
    fiscal.GrossGamingTax != result.GrossGamingTax ||
    Math.Abs(socialCosts.Sum(row => row.AnnualCost) - result.GrossSocialCost) > 0.02m ||
    netImpact.NetHostLocalImpact != result.NetHostLocalImpact ||
    netImpact.NetHostStateImpact != result.NetHostStateImpact)
{
    throw new InvalidOperationException("Comprehensive impact outputs were not separately persisted and reconciled.");
}
var expectedInventoryWeights = new Dictionary<string, decimal>(StringComparer.Ordinal)
{
    [DisplacementSectorKeys.RestaurantHospitality] = 0.6m,
    [DisplacementSectorKeys.Retail] = 0.3m,
    [DisplacementSectorKeys.ArtsEntertainmentRecreation] = 0.1m
};
if (sectorDisplacement.Any(row =>
        !expectedInventoryWeights.TryGetValue(row.SectorKey, out var expectedWeight) ||
        Math.Abs((decimal)row.NormalizedWeight - expectedWeight) > 0.000001m) ||
    !persistedRun.ResolvedInputJson.Contains("provider-snapshot:annual-receipts-or-sales:host-state:US-IN", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Provider-backed local economic inventory did not govern persisted displacement weights.");
}
if (reportArtifact.Id != repeatedReportArtifact.Id ||
    await db.ModelRunReportArtifacts.CountAsync(row => row.ModelRunId == result.ModelRunId) != 1 ||
    reportArtifact.PdfContent.Length < 1_000 ||
    System.Text.Encoding.ASCII.GetString(reportArtifact.PdfContent, 0, 4) != "%PDF" ||
    !reportArtifact.HtmlContent.Contains(result.ModelRunId.ToString(), StringComparison.Ordinal) ||
    !reportArtifact.CsvContent.Contains("revenue,stabilized,total_ggr", StringComparison.Ordinal) ||
    reportArtifact.ReportModelHash.Length != 64 || reportArtifact.PdfContentHash.Length != 64)
{
    throw new InvalidOperationException("Stored report artifacts were not deterministic, complete, and server-rendered.");
}
if (sensitivity.Status != SensitivityAnalysisStatuses.Finalized ||
    sensitivity.Points.Count != 4 || sensitivity.Tornado.Count != 2 ||
    finalizedSensitivityRunCount != 4 ||
    sensitivity.Points.Any(point => !sensitivityValues.Any(value =>
        value.ModelRunId == point.ModelRunId && value.Key == point.ParameterKey && value.FinalValue == point.ParameterValue)))
{
    throw new InvalidOperationException("One-at-a-time sensitivity did not persist complete backend runs and tornado values.");
}
if (sensitivityReportArtifact.Id != repeatedSensitivityReportArtifact.Id ||
    sensitivityReportArtifact.PdfContent.Length < 1_000 ||
    !sensitivityReportArtifact.HtmlContent.Contains("One-at-a-time sensitivity", StringComparison.Ordinal) ||
    !sensitivityReportArtifact.CsvContent.Contains(sensitivity.SensitivityAnalysisId.ToString(), StringComparison.Ordinal) ||
    !sensitivityReportArtifact.CsvContent.Contains("sensitivity,gravity.beta,low_model_run_id", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Sensitivity report exhibit was not deterministic and linked to its complete point runs.");
}
var baselineAllocated = allocations.Where(row => row.MarketState == MarketStates.Baseline).Sum(row => row.AllocatedResidentGgr);
var projectAllocated = allocations.Where(row => row.MarketState == MarketStates.WithProject).Sum(row => row.AllocatedResidentGgr);
var baselineExpected = originResult.ResidentDemand * (1 - (decimal)originResult.BaselineOutsideShare);
var projectExpected = originResult.ResidentDemand * (1 - (decimal)originResult.WithProjectOutsideShare);
if (Math.Abs(baselineAllocated - baselineExpected) > 0.02m || Math.Abs(projectAllocated - projectExpected) > 0.02m)
{
    throw new InvalidOperationException("Persisted allocation totals do not reconcile to origin demand and the outside option.");
}
var inducedAllocated = allocations.Sum(row => row.AllocatedInducedResidentGgr);
if (originResult.InducedResidentDemand <= 0 ||
    Math.Abs(inducedAllocated + originResult.InducedOutsideOptionGgr - originResult.InducedResidentDemand) > 0.02m ||
    originResult.TotalProposedResidentGgr != originResult.ProposedResidentGgr + originResult.ProposedInducedResidentGgr)
{
    throw new InvalidOperationException("Accessibility-induced resident demand did not reconcile through facility and outside-option allocations.");
}

var ohioGeography = await BeginAsync(DatasetSnapshotKinds.OriginGeography, 1, "gravity-integration-ohio-geography-v1");
await ingestion.AppendOriginsAsync(ohioGeography.Id,
[
    new OriginZoneImportRow(
        "USA-ZCTA-43215", "zcta", "43215", "USA", "OH", "39049", "18140", null,
        39.965, -83.010,
        "POLYGON((-83.08 39.90,-82.94 39.90,-82.94 40.03,-83.08 40.03,-83.08 39.90))")
]);
await SealAsync(ohioGeography);

var ohioAge = await BeginAsync(DatasetSnapshotKinds.AgePopulation, 3, "gravity-integration-ohio-age-v1");
await ingestion.AppendAgeBinsAsync(ohioAge.Id, new OriginAgeBinImportRequest(
    ohioGeography.Id,
    [
        new OriginAgeBinImportRow("USA-ZCTA-43215", 2025, 0, 17, 8_000, DatasetValidationStates.Validated),
        new OriginAgeBinImportRow("USA-ZCTA-43215", 2025, 18, 20, 1_500, DatasetValidationStates.Validated),
        new OriginAgeBinImportRow("USA-ZCTA-43215", 2025, 21, null, 28_000, DatasetValidationStates.Validated)
    ]));
await SealAsync(ohioAge);

var ohioIncome = await BeginAsync(DatasetSnapshotKinds.Income, 1, "gravity-integration-ohio-income-v1");
await ingestion.AppendIncomeAsync(ohioIncome.Id, new OriginIncomeImportRequest(
    ohioGeography.Id,
    [new OriginIncomeImportRow("USA-ZCTA-43215", 2025, 18_000, 950_000_000m, 950_000_000m, 62_000m, 2025, null)]));
await SealAsync(ohioIncome);

var ohioCompetitors = await BeginAsync(DatasetSnapshotKinds.Competitors, 1, "gravity-integration-ohio-competitors-v1");
await ingestion.AppendCompetitorsAsync(ohioCompetitors.Id,
[
    new CasinoCompetitorImportRow(
        "USA-OH-CINCINNATI-SYNTHETIC",
        "Cincinnati synthetic validation competitor",
        "OH", "USA", "commercial-casino", "commercial-casino", "active", ohio.Id,
        "Synthetic regulator", "synthetic-ohio-license", null, new DateOnly(2013, 1, 1), null,
        "Hamilton", "Cincinnati", 39.1031, -84.5120, true, "Synthetic operator",
        "https://example.invalid/ohio-synthetic-venue", DateTime.UtcNow,
        true, true, false, false, false, true, true, true, true, false,
        1_700, 1_650, 40, 0, 50_000, 150, 800, 4, 400_000_000m, 2025,
        "urban", 2, true, "regional", false, "Disposable non-Indiana portability fixture only.")
]);
await SealAsync(ohioCompetitors);

var ohioObserved = await BeginAsync(DatasetSnapshotKinds.ObservedPerformance, 1, "gravity-integration-ohio-observed-v1");
await ingestion.AppendGamingRevenueAsync(ohioObserved.Id, new CasinoGamingRevenueImportRequest(
    ohioCompetitors.Id,
    [
        new CasinoGamingRevenueImportRow(
            "USA-OH-CINCINNATI-SYNTHETIC", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
            "annual", "ggr", "Synthetic annual gross gaming revenue", 175_000_000m, 175_000_000m,
            2025, [], "Disposable non-Indiana portability fixture only.")
    ]));
await SealAsync(ohioObserved);

var ohioProgram = await new DevelopmentProgramService(db).CreateAsync(new DevelopmentProgramDefinition(
    "columbus-synthetic-validation-program", "1", "Columbus synthetic validation program",
    1_300, 45, 8, true, 180, 55_000, 5, 1_200, 3, 425_000_000m, 2025,
    new DateOnly(2028, 1, 1), 3, "Disposable non-Indiana portability fixture only."));

var ohioResult = await execution.ExecuteAsync(new GravityModelRunRequest(
    "Columbus non-Indiana portability validation",
    "US-OH",
    ohioProgram.Id,
    39.9700,
    -83.0000,
    ohioGeography.Id,
    ohioAge.Id,
    ohioIncome.Id,
    ohioCompetitors.Id,
    ohioObserved.Id,
    ["USA-ZCTA-43215"],
    [],
    2025,
    2025,
    new DateOnly(2025, 12, 31),
    "commercial-casino",
    GravityDemandSpecifications.AgiShare,
    FacilityAttractionSpecifications.ObservedGgr,
    "inverse-power",
    "ggr",
    new DateOnly(2025, 1, 1),
    new DateOnly(2025, 12, 31),
    null,
    null,
    null,
    [new ParameterOverride("market_expansion.accessibility_elasticity", 0.1)],
    ImpactGeography: new ImpactGeographyDefinition("host-state", "US-OH")));
var ohioRun = await db.ModelRuns.AsNoTracking().SingleAsync(run => run.Id == ohioResult.ModelRunId);
var ohioAccounting = await db.ModelRunGeographicAccounting.AsNoTracking()
    .SingleAsync(row => row.ModelRunId == ohioResult.ModelRunId);
var ohioFiscal = await db.ModelRunFiscalImpacts.AsNoTracking()
    .SingleAsync(row => row.ModelRunId == ohioResult.ModelRunId);
var expectedOhioGamingTax = decimal.Round(ohioResult.StabilizedTotalGgr * 0.07m, 2, MidpointRounding.AwayFromZero);
if (ohioResult.Status != ModelRunStatuses.Finalized || ohioRun.JurisdictionId != ohio.Id ||
    ohioResult.ComputationalOriginType != "zcta" || ohioAccounting.ScopeCode != "US-OH" ||
    ohioFiscal.GrossGamingTax != expectedOhioGamingTax)
{
    throw new InvalidOperationException("The non-Indiana portability run did not use Ohio geography and jurisdiction rules.");
}
db.ValidationCases.Add(new ValidationCase
{
    CaseKey = "synthetic-ohio-columbus-holdout",
    Name = "Synthetic Columbus non-Indiana holdout",
    MarketCode = "US-OH-COLUMBUS",
    JurisdictionCode = "US-OH",
    CaseKind = ValidationCaseKinds.SyntheticNational,
    DatasetPartition = ValidationPartitions.Holdout,
    HoldoutGroup = "non-indiana-synthetic",
    ModelRunId = ohioResult.ModelRunId,
    ObservedRevenue = decimal.Round(ohioResult.StabilizedTotalGgr * 1.03m, 2, MidpointRounding.AwayFromZero),
    ObservedMetricKey = "synthetic-ggr",
    ObservedMetricDefinition = "Synthetic holdout target used only to prove national validation plumbing.",
    TrainingPeriodStart = new DateOnly(2024, 1, 1),
    TrainingPeriodEnd = new DateOnly(2024, 12, 31),
    ValidationPeriodStart = new DateOnly(2025, 1, 1),
    ValidationPeriodEnd = new DateOnly(2025, 12, 31),
    InclusionRulesJson = "{\"synthetic\":true,\"indianaExcluded\":true}",
    PredictorValuesJson = "{\"accessible-population\":28000,\"gaming-positions\":1300}",
    ExecutionRequestJson = "{}",
    Notes = "Disposable integration fixture."
});
await db.SaveChangesAsync();

Console.WriteLine(JsonSerializer.Serialize(new
{
    result.ModelRunId,
    result.Status,
    result.ComputationalOriginType,
    result.RoutingGraphHash,
    result.TotalResidentDemand,
    result.InducedResidentDemand,
    result.ProposedRedistributedResidentGgr,
    result.ProposedInducedResidentGgr,
    result.ProposedResidentGgr,
    result.TourismGgr,
    result.TrafficGgr,
    result.StabilizedTotalGgr,
    RouteCount = routes.Length,
    AllocationCount = allocations.Length,
    ParameterCount = parameterCount,
    SnapshotReferenceCount = snapshotReferenceCount,
    DemandComponentCount = demandComponents.Length,
    CapacityStatus = capacity.Status,
    RampYearCount = ramp.Length,
    result.LocalDiscretionaryDisplacement,
    result.GrossGamingTax,
    result.GrossSocialCost,
    result.NetPermanentJobs,
    result.NetHostLocalImpact,
    result.NetHostStateImpact,
    ImpactScope = $"{geographicAccounting.ScopeKind}:{geographicAccounting.ScopeCode}",
    SectorDisplacementCount = sectorDisplacement.Length,
    SocialCostDomainCount = socialCosts.Length,
    ReportArtifactId = reportArtifact.Id,
    ReportPdfBytes = reportArtifact.PdfContent.Length,
    reportArtifact.ReportModelHash,
    Sensitivity = new
    {
        sensitivity.SensitivityAnalysisId,
        sensitivity.Status,
        sensitivity.OutputMetric,
        PointCount = sensitivity.Points.Count,
        TornadoCount = sensitivity.Tornado.Count,
        sensitivity.BaselineModelRunId,
        ReportArtifactId = sensitivityReportArtifact.Id,
        ReportPdfBytes = sensitivityReportArtifact.PdfContent.Length
    },
    NonIndianaRun = new
    {
        ohioResult.ModelRunId,
        ohioResult.Status,
        ohioResult.ComputationalOriginType,
        Jurisdiction = ohio.Code,
        ImpactScope = $"{ohioAccounting.ScopeKind}:{ohioAccounting.ScopeCode}",
        ohioResult.StabilizedTotalGgr,
        ohioFiscal.GrossGamingTax
    },
    Travel = routes.OrderBy(route => route.FacilityKind).Select(route => new
    {
        route.FacilityKind,
        route.TravelTimeMinutes,
        route.RoutedDistanceMeters,
        route.RouteFound
    })
}));

file sealed class FrozenOriginIncomeProvider(
    ProviderDataset<OriginIncomeImportRow> dataset) : IOriginIncomeProvider
{
    public string ProviderKey => "frozen-live-irs-soi-validation";

    public Task<ProviderDataset<OriginIncomeImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}

file sealed record MichiganProviderValidationBundle(
    ProviderDataset<CasinoCompetitorImportRow> Facilities,
    ProviderDataset<CasinoGamingRevenueImportRow> Performance);

file sealed class FrozenMichiganFacilityProvider(
    ProviderDataset<CasinoCompetitorImportRow> dataset) : IGamingFacilityInventoryProvider
{
    public string ProviderKey => "frozen-official-michigan-facilities-validation";
    public string GeographicCoverage => "US-MI";

    public Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}

file sealed class FrozenMichiganPerformanceProvider(
    ProviderDataset<CasinoGamingRevenueImportRow> dataset) : IGamingRegulatorPerformanceProvider
{
    public string ProviderKey => "frozen-official-michigan-performance-validation";
    public string GeographicCoverage => "US-MI";

    public Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}
