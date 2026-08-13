// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;
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

if (args is ["--probe-census-cbp"])
{
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var provider = new CensusCountyBusinessPatternsProvider(
        providerProbeHttp,
        Options.Create(new CensusCountyBusinessPatternsProviderOptions()));
    static ProviderFetchRequest CbpRequest(
        string sourceGeography,
        string sourceFips,
        string impactScopeKind,
        string impactScopeCode) => new(
        "US-CBP",
        new DateOnly(2023, 1, 1),
        new DateOnly(2023, 12, 31),
        new Dictionary<string, string>
        {
            ["source-geography"] = sourceGeography,
            ["source-fips"] = sourceFips,
            ["impact-scope-kind"] = impactScopeKind,
            ["impact-scope-code"] = impactScopeCode
        });
    var state = await provider.FetchAsync(CbpRequest("state", "18", ImpactScopeKinds.HostState, "US-IN"));
    var county = await provider.FetchAsync(CbpRequest("county", "18003", ImpactScopeKinds.HostCounty, "18003"));
    var requiredSectors = new[]
    {
        DisplacementSectorKeys.RestaurantHospitality,
        DisplacementSectorKeys.Retail,
        DisplacementSectorKeys.ArtsEntertainmentRecreation
    };
    if (state.Rows.Count(row => requiredSectors.Contains(row.SectorKey, StringComparer.Ordinal)) != 3 ||
        county.Rows.Count(row => requiredSectors.Contains(row.SectorKey, StringComparer.Ordinal)) != 3 ||
        state.Rows.Any(row => row.AnnualReceiptsOrSales is not null) ||
        county.Rows.Any(row => row.AnnualReceiptsOrSales is not null))
    {
        throw new InvalidOperationException("The live CBP probe did not return the required non-synthetic local inventory sectors.");
    }
    static LocalEconomicSectorObservation CbpObservation(LocalEconomicSectorObservationImportRow row) => new()
    {
        StableObservationId = row.StableObservationId,
        GeographyType = row.GeographyType,
        GeographyCode = row.GeographyCode,
        SectorKey = row.SectorKey,
        NaicsCodesJson = JsonSerializer.Serialize(row.NaicsCodes),
        PeriodStart = row.PeriodStart,
        PeriodEnd = row.PeriodEnd,
        Establishments = row.Establishments,
        Employment = row.Employment,
        AnnualPayroll = row.AnnualPayroll,
        AnnualReceiptsOrSales = row.AnnualReceiptsOrSales,
        SourceMetricDefinition = row.SourceMetricDefinition,
        Notes = row.Notes
    };
    var laborService = new LocalEconomicInventoryWeightService();
    var stateLabor = laborService.ResolveLaborAssumptions(
        state.Rows.Select(CbpObservation).ToArray(),
        ImpactScopeKinds.HostState,
        "US-IN",
        1,
        1,
        1,
        true);
    var countyLabor = laborService.ResolveLaborAssumptions(
        county.Rows.Select(CbpObservation).ToArray(),
        ImpactScopeKinds.HostCounty,
        "18003",
        45_000,
        35_000,
        46_000,
        true);
    if (stateLabor.DirectAverageAnnualWage <= 0 || stateLabor.IndirectAverageAnnualWage <= 0 ||
        countyLabor.DirectAverageAnnualWage != 45_000 || countyLabor.IndirectAverageAnnualWage <= 0 ||
        countyLabor.Warnings.Count != 1)
    {
        throw new InvalidOperationException("The live CBP labor-assumption resolution did not preserve geography and fallback boundaries.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        State = new
        {
            state.Source.Url,
            state.ContentChecksum,
            Rows = state.Rows.Select(row => new
            {
                row.SectorKey,
                row.Employment,
                row.AnnualPayroll,
                row.Establishments,
                row.NaicsCodes
            }),
            LaborAssumptions = stateLabor,
            state.Warnings
        },
        County = new
        {
            county.Source.Url,
            county.ContentChecksum,
            Rows = county.Rows.Select(row => new
            {
                row.SectorKey,
                row.Employment,
                row.AnnualPayroll,
                row.Establishments,
                row.NaicsCodes
            }),
            LaborAssumptions = countyLabor,
            county.Warnings
        }
    }));
    return;
}

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
    var annualComparableByFacility = annualGaming.Rows
        .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .GroupBy(row => row.StableVenueId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Sum(row => row.ReportedAmount), StringComparer.Ordinal);
    var employmentProductivity = facilityDataset.Rows
        .Where(row => row.ReportedEmployment > 0 && annualComparableByFacility.ContainsKey(row.StableVenueId))
        .OrderBy(row => row.StableVenueId, StringComparer.Ordinal)
        .Select(row => new
        {
            row.StableVenueId,
            ReportedEmployment = row.ReportedEmployment!.Value,
            ObservedGgr = annualComparableByFacility[row.StableVenueId],
            JobsPerMillionGgr = row.ReportedEmployment.Value /
                                Convert.ToDouble(annualComparableByFacility[row.StableVenueId] / 1_000_000m)
        })
        .ToArray();
    if (employmentProductivity.Length != facilityDataset.Rows.Count)
    {
        throw new InvalidOperationException("Live IGC facility employment did not reconcile one-for-one to annual comparable revenue.");
    }
    var weightedJobsPerMillionGgr = employmentProductivity.Sum(row => row.ReportedEmployment) /
                                    Convert.ToDouble(employmentProductivity.Sum(row => row.ObservedGgr) / 1_000_000m);
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
            EmploymentCount = facilityDataset.Rows.Count(row => row.ReportedEmployment > 0),
            TotalReportedEmployment = facilityDataset.Rows.Sum(row => row.ReportedEmployment ?? 0),
            facilityDataset.ContentChecksum
        },
        EmploymentProductivity = new
        {
            Method = "reported-employment-per-observed-ggr-v1",
            FacilityCount = employmentProductivity.Length,
            WeightedJobsPerMillionGgr = weightedJobsPerMillionGgr,
            MinimumJobsPerMillionGgr = employmentProductivity.Min(row => row.JobsPerMillionGgr),
            MaximumJobsPerMillionGgr = employmentProductivity.Max(row => row.JobsPerMillionGgr),
            Facilities = employmentProductivity
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
    var lotteryConfigured = Options.Create(new OhioLotteryVideoLotteryProviderOptions());
    var lotteryRevenueProvider = new OhioLotteryVideoLotteryRevenueProvider(providerProbeHttp, lotteryConfigured);
    var lotteryRevenue = await lotteryRevenueProvider.FetchAsync(request);
    var lotteryFacilities = await new OhioLotteryVideoLotteryFacilityInventoryProvider(
            providerProbeHttp,
            lotteryRevenueProvider,
            lotteryConfigured)
        .FetchAsync(request);
    var lotteryComparable = lotteryRevenue.Rows
        .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .ToArray();
    var lotteryRevenueIds = lotteryComparable.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    var lotteryFacilityIds = lotteryFacilities.Rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    if (!lotteryRevenueIds.SetEquals(lotteryFacilityIds) ||
        revenueIds.Overlaps(lotteryRevenueIds))
    {
        throw new InvalidOperationException("Live Ohio Lottery revenue/facility stable IDs did not reconcile or overlapped OCCC casinos.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        CasinoRevenue = new
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
        CasinoFacilities = new
        {
            facilities.Source.Url,
            facilities.Period,
            RowCount = facilities.Rows.Count,
            TotalTables = facilities.Rows.Sum(row => row.TableGameCount),
            TotalSlots = facilities.Rows.Sum(row => row.SlotOrVltPositions),
            facilities.Warnings,
            facilities.ContentChecksum
        },
        VideoLotteryRevenue = new
        {
            lotteryRevenue.Source.Url,
            lotteryRevenue.Period,
            RowCount = lotteryRevenue.Rows.Count,
            FacilityCount = lotteryRevenueIds.Count,
            MonthCount = lotteryComparable.Select(row => row.PeriodStart).Distinct().Count(),
            MetricKeys = lotteryRevenue.Rows.Select(row => row.ReportedMetricKey).Distinct().Order().ToArray(),
            TotalComparableRevenue = lotteryComparable.Sum(row => row.ReportedAmount),
            AnnualByFacility = lotteryComparable
                .GroupBy(row => row.StableVenueId)
                .OrderBy(group => group.Key)
                .Select(group => new { StableVenueId = group.Key, Revenue = group.Sum(row => row.ReportedAmount) }),
            lotteryRevenue.Warnings,
            lotteryRevenue.ContentChecksum
        },
        VideoLotteryFacilities = new
        {
            lotteryFacilities.Source.Url,
            lotteryFacilities.Period,
            RowCount = lotteryFacilities.Rows.Count,
            TotalVlts = lotteryFacilities.Rows.Sum(row => row.SlotOrVltPositions),
            lotteryFacilities.Warnings,
            lotteryFacilities.ContentChecksum
        },
        Combined = new
        {
            FacilityCount = revenueIds.Count + lotteryRevenueIds.Count,
            TotalComparableRevenue = comparable.Sum(row => row.ReportedAmount) +
                                     lotteryComparable.Sum(row => row.ReportedAmount)
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
    var bundle = new ProviderValidationBundle(facilities, performance);
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

if (args is ["--export-four-state-provider-bundle", var fourStateBundleOutputPath])
{
    var fullOutputPath = Path.GetFullPath(fourStateBundleOutputPath);
    if (!string.Equals(Path.GetExtension(fullOutputPath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !Directory.Exists(Path.GetDirectoryName(fullOutputPath)))
    {
        throw new ArgumentException("Four-state provider bundle output must be a .json file in an existing directory.");
    }
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
    var request = new ProviderFetchRequest(
        "US-IL,US-IN,US-MI,US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));

    var indianaOptions = Options.Create(new IndianaGamingCommissionProviderOptions());
    var indianaTribalOptions = Options.Create(new IndianaTribalGamingFacilityProviderOptions());
    var illinoisOptions = Options.Create(new IllinoisGamingBoardProviderOptions());
    var illinoisRevenue = new IllinoisGamingBoardRevenueProvider(providerHttp, illinoisOptions);
    var michiganOptions = Options.Create(new MichiganGamingFacilityProviderOptions());
    var ohioOptions = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var ohioRevenue = new OhioCasinoControlCommissionRevenueProvider(providerHttp, ohioOptions);
    var ohioLotteryOptions = Options.Create(new OhioLotteryVideoLotteryProviderOptions());
    var ohioLotteryRevenue = new OhioLotteryVideoLotteryRevenueProvider(providerHttp, ohioLotteryOptions);

    var facilities = await new CompositeGamingFacilityInventoryProvider(
    [
        new IndianaGamingCommissionFacilityInventoryProvider(providerHttp, indianaOptions),
        new IndianaTribalGamingFacilityInventoryProvider(providerHttp, indianaTribalOptions),
        new IllinoisGamingBoardFacilityInventoryProvider(providerHttp, illinoisRevenue, illinoisOptions),
        new MichiganGamingFacilityInventoryProvider(providerHttp, michiganOptions),
        new OhioCasinoControlCommissionFacilityInventoryProvider(providerHttp, ohioRevenue, ohioOptions),
        new OhioLotteryVideoLotteryFacilityInventoryProvider(providerHttp, ohioLotteryRevenue, ohioLotteryOptions)
    ]).FetchAsync(request);
    var performance = await new CompositeGamingRegulatorPerformanceProvider(
    [
        new IndianaGamingCommissionMonthlyRevenueProvider(providerHttp, indianaOptions),
        illinoisRevenue,
        new MichiganGamingControlBoardRevenueProvider(providerHttp, michiganOptions),
        ohioRevenue,
        ohioLotteryRevenue
    ]).FetchAsync(request);
    var facilityIds = facilities.Rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    var performanceIds = performance.Rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal);
    if (!facilityIds.IsSupersetOf(performanceIds))
    {
        throw new InvalidOperationException(
            $"Four-state provider performance contains facility IDs absent from inventory: {string.Join(", ", performanceIds.Except(facilityIds, StringComparer.Ordinal).Order(StringComparer.Ordinal))}.");
    }

    var bundle = new ProviderValidationBundle(facilities, performance);
    await File.WriteAllTextAsync(
        fullOutputPath,
        JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        BundlePath = fullOutputPath,
        FacilityRows = facilities.Rows.Count,
        PerformanceRows = performance.Rows.Count,
        FacilityStates = facilities.Rows.GroupBy(row => row.State).OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count()),
        ComparableTotal = performance.Rows
            .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
            .Sum(row => row.ReportedAmount),
        facilities.ContentChecksum,
        PerformanceChecksum = performance.ContentChecksum
    }));
    return;
}

if (args is ["--refresh-four-state-indiana-facilities", var baseBundlePath, var refreshedBundleOutputPath])
{
    var fullBasePath = Path.GetFullPath(baseBundlePath);
    var fullOutputPath = Path.GetFullPath(refreshedBundleOutputPath);
    if (!File.Exists(fullBasePath) ||
        !string.Equals(Path.GetExtension(fullBasePath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Path.GetExtension(fullOutputPath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !Directory.Exists(Path.GetDirectoryName(fullOutputPath)))
    {
        throw new ArgumentException(
            "Indiana facility refresh requires an existing base .json bundle and an output .json path in an existing directory.");
    }
    var baseBytes = await File.ReadAllBytesAsync(fullBasePath);
    var baseChecksum = Convert.ToHexString(SHA256.HashData(baseBytes)).ToLowerInvariant();
    const string expectedBaseChecksum = "fccb73b93a777f49af7ed82b5ccba376d68631e5443038873a1cb29dcf4c9d50";
    if (!string.Equals(baseChecksum, expectedBaseChecksum, StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"Frozen four-state base bundle checksum '{baseChecksum}' does not match expected '{expectedBaseChecksum}'.");
    }
    var baseBundle = JsonSerializer.Deserialize<ProviderValidationBundle>(baseBytes)
        ?? throw new InvalidDataException("The frozen four-state base bundle is invalid.");
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
    var request = new ProviderFetchRequest(
        "US-IN",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var indianaFacilities = await new IndianaGamingCommissionFacilityInventoryProvider(
            providerHttp,
            Options.Create(new IndianaGamingCommissionProviderOptions()))
        .FetchAsync(request);
    var retainedRows = baseBundle.Facilities.Rows
        .Where(row => !row.StableVenueId.StartsWith("USA-IN-IGC-", StringComparison.Ordinal))
        .ToArray();
    var refreshedRows = retainedRows.Concat(indianaFacilities.Rows)
        .OrderBy(row => row.StableVenueId, StringComparer.Ordinal)
        .ToArray();
    var duplicate = refreshedRows.GroupBy(row => row.StableVenueId, StringComparer.Ordinal)
        .FirstOrDefault(group => group.Count() > 1);
    if (duplicate is not null || refreshedRows.Length != baseBundle.Facilities.Rows.Count)
    {
        throw new InvalidDataException(
            duplicate is null
                ? "Indiana facility refresh changed the four-state facility-row count."
                : $"Indiana facility refresh repeats stable venue ID '{duplicate.Key}'.");
    }
    var componentManifest = string.Join('\n',
        $"frozen-base|{baseChecksum}",
        $"indiana-commercial-facilities|{indianaFacilities.ContentChecksum}");
    var refreshedFacilityChecksum = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(componentManifest)))
        .ToLowerInvariant();
    var refreshedFacilities = new ProviderDataset<CasinoCompetitorImportRow>(
        new RegisterDataSourceRequest(
            "Checksum-pinned four-state gaming inventory with refreshed Indiana regulator facility profiles",
            "Multiple jurisdiction gaming regulators",
            $"urn:savenein:frozen-four-state-indiana-refresh:{refreshedFacilityChecksum}",
            "checksum-pinned-regulator-bundle-refresh",
            "US-IL,US-IN,US-MI,US-OH",
            "2025",
            DateTime.UtcNow,
            refreshedFacilityChecksum,
            true,
            "Each component regulator's public-record terms apply.",
            $"Frozen base bundle SHA-256 {baseChecksum}; Indiana commercial facility dataset SHA-256 " +
            $"{indianaFacilities.ContentChecksum}. Only rows with stable IDs prefixed USA-IN-IGC- were replaced; " +
            "all other frozen facility and performance rows are byte-derived from the checksum-pinned base bundle."),
        DatasetSnapshotKinds.Competitors,
        "2025",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31),
        refreshedFacilityChecksum,
        "frozen-four-state-indiana-facility-refresh-v1",
        refreshedRows,
        baseBundle.Facilities.Warnings.Concat(indianaFacilities.Warnings).Distinct(StringComparer.Ordinal).ToArray());
    var refreshedBundle = new ProviderValidationBundle(refreshedFacilities, baseBundle.Performance);
    await File.WriteAllTextAsync(
        fullOutputPath,
        JsonSerializer.Serialize(refreshedBundle, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        BundlePath = fullOutputPath,
        BaseBundleChecksum = baseChecksum,
        IndianaFacilityChecksum = indianaFacilities.ContentChecksum,
        RefreshedFacilityChecksum = refreshedFacilities.ContentChecksum,
        FacilityRows = refreshedFacilities.Rows.Count,
        IndianaCommercialRows = indianaFacilities.Rows.Count,
        UnknownIndianaHotelCount = indianaFacilities.Rows.Count(row => row.HotelRoomCount is null),
        PerformanceRows = refreshedBundle.Performance.Rows.Count,
        PerformanceChecksum = refreshedBundle.Performance.ContentChecksum
    }));
    return;
}

if (args is ["--export-ohio-capacity-provider-bundle", var ohioCapacityBundleOutputPath])
{
    var fullOutputPath = Path.GetFullPath(ohioCapacityBundleOutputPath);
    if (!string.Equals(Path.GetExtension(fullOutputPath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !Directory.Exists(Path.GetDirectoryName(fullOutputPath)))
    {
        throw new ArgumentException("Ohio capacity provider bundle output must be a .json file in an existing directory.");
    }
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var request = new ProviderFetchRequest(
        "US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var configured = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var revenue = new OhioCasinoControlCommissionRevenueProvider(providerProbeHttp, configured);
    var facilities = await new OhioCasinoControlCommissionFacilityInventoryProvider(
            providerProbeHttp,
            revenue,
            configured)
        .FetchAsync(request);
    var performance = await revenue.FetchAsync(request);
    var bundle = new ProviderValidationBundle(facilities, performance);
    await File.WriteAllTextAsync(
        fullOutputPath,
        JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        BundlePath = fullOutputPath,
        FacilityRows = facilities.Rows.Count,
        PerformanceRows = performance.Rows.Count,
        UnitCountRows = performance.Rows.Count(row => row.ReportedUnitCount is > 0),
        facilities.ContentChecksum,
        PerformanceChecksum = performance.ContentChecksum,
        performance.TransformVersion,
        ComparableTotal = performance.Rows
            .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
            .Sum(row => row.ReportedAmount)
    }));
    return;
}

if (args is ["--export-ohio-provider-bundle", var ohioBundleOutputPath])
{
    var fullOutputPath = Path.GetFullPath(ohioBundleOutputPath);
    if (!string.Equals(Path.GetExtension(fullOutputPath), ".json", StringComparison.OrdinalIgnoreCase) ||
        !Directory.Exists(Path.GetDirectoryName(fullOutputPath)))
    {
        throw new ArgumentException("Ohio provider bundle output must be a .json file in an existing directory.");
    }
    using var providerProbeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var request = new ProviderFetchRequest(
        "US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var casinoConfigured = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var casinoRevenue = new OhioCasinoControlCommissionRevenueProvider(providerProbeHttp, casinoConfigured);
    var lotteryConfigured = Options.Create(new OhioLotteryVideoLotteryProviderOptions());
    var lotteryRevenue = new OhioLotteryVideoLotteryRevenueProvider(providerProbeHttp, lotteryConfigured);
    var facilities = await new CompositeGamingFacilityInventoryProvider(
        [
            new OhioCasinoControlCommissionFacilityInventoryProvider(
                providerProbeHttp,
                casinoRevenue,
                casinoConfigured),
            new OhioLotteryVideoLotteryFacilityInventoryProvider(
                providerProbeHttp,
                lotteryRevenue,
                lotteryConfigured)
        ]).FetchAsync(request);
    var performance = await new CompositeGamingRegulatorPerformanceProvider(
        [casinoRevenue, lotteryRevenue]).FetchAsync(request);
    var bundle = new ProviderValidationBundle(facilities, performance);
    await File.WriteAllTextAsync(
        fullOutputPath,
        JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        BundlePath = fullOutputPath,
        FacilityRows = facilities.Rows.Count,
        PerformanceRows = performance.Rows.Count,
        facilities.ContentChecksum,
        PerformanceChecksum = performance.ContentChecksum,
        ComparableTotal = performance.Rows
            .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
            .Sum(row => row.ReportedAmount)
    }));
    return;
}

if (args is ["--ingest-four-state-provider-bundle", var fourStateDatabase, var fourStateBundlePath])
{
    if (!fourStateDatabase.StartsWith("savenein_ui_validation_", StringComparison.Ordinal) ||
        fourStateDatabase.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
    {
        throw new ArgumentException("Unsafe four-state validation database name.");
    }
    var fullBundlePath = Path.GetFullPath(fourStateBundlePath);
    if (!File.Exists(fullBundlePath) ||
        !string.Equals(Path.GetExtension(fullBundlePath), ".json", StringComparison.OrdinalIgnoreCase))
    {
        throw new FileNotFoundException("The four-state provider validation bundle was not found.", fullBundlePath);
    }
    var bundle = JsonSerializer.Deserialize<ProviderValidationBundle>(await File.ReadAllTextAsync(fullBundlePath))
        ?? throw new InvalidDataException("The four-state provider validation bundle is empty or invalid.");
    var fourStateConfiguredConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? throw new InvalidOperationException("The app container did not provide ConnectionStrings__DefaultConnection.");
    var fourStateConnectionBuilder = new NpgsqlConnectionStringBuilder(fourStateConfiguredConnection)
    {
        Database = fourStateDatabase
    };
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(fourStateConnectionBuilder.ConnectionString, provider => provider.UseNetTopologySuite())
        .Options;
    await using var bundleDb = new AppDbContext(options);
    await ModelFoundationInitializer.ApplySchemaAsync(bundleDb);
    await ModelFoundationInitializer.SeedAsync(bundleDb);
    var fourStateIngestion = new ProviderSnapshotIngestionService(
        new DataSnapshotService(bundleDb),
        new ModelDataIngestionService(bundleDb));
    var request = new ProviderFetchRequest(
        "US-IL,US-IN,US-MI,US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var facilitySnapshotId = await fourStateIngestion.IngestGamingFacilitiesAsync(
        new FrozenGamingFacilityProvider(
            bundle.Facilities,
            "frozen-official-four-state-facilities-validation",
            request.GeographicCoverage),
        request);
    var performanceSnapshotId = await fourStateIngestion.IngestGamingPerformanceAsync(
        new FrozenGamingPerformanceProvider(
            bundle.Performance,
            "frozen-official-four-state-performance-validation",
            request.GeographicCoverage),
        request,
        facilitySnapshotId);
    var facilityRows = await bundleDb.CasinoCompetitors.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == facilitySnapshotId)
        .ToArrayAsync();
    var performanceRows = await bundleDb.CasinoGamingRevenuePeriods.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId)
        .ToArrayAsync();
    var snapshots = await bundleDb.DatasetSnapshots.AsNoTracking()
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .ToArrayAsync();
    if (facilityRows.Length != bundle.Facilities.Rows.Count ||
        performanceRows.Length != bundle.Performance.Rows.Count ||
        snapshots.Length != 2 || snapshots.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("The four-state bundle did not persist as two complete sealed snapshots.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        facilitySnapshotId,
        performanceSnapshotId,
        FacilityRows = facilityRows.Length,
        PerformanceRows = performanceRows.Length,
        FacilityStates = facilityRows.GroupBy(row => row.State).OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count()),
        ComparableTotal = performanceRows
            .Where(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
            .Sum(row => row.ReportedAmount),
        Snapshots = snapshots.OrderBy(snapshot => snapshot.DatasetKey)
            .Select(snapshot => new { snapshot.DatasetKey, snapshot.Checksum, snapshot.ValidationState, snapshot.IsSealed })
    }));
    return;
}

var validateIncumbentCalibration = args is ["--validate-incumbent-calibration", _, _, _];
var validateMichiganProviderBundle = args is ["--validate-michigan-provider-bundle", _, _];
var validateOhioProviderBundle = args is ["--validate-ohio-provider-bundle", _, _];
var validateOhioProviderIngestion = args is ["--validate-ohio-provider-ingestion", _];
var validateIndianaFiscal = args is ["--validate-indiana-fiscal", _];
var validateIndianaEmployment = args is ["--validate-indiana-employment", _];
var validateIndianaSocial = args is ["--validate-indiana-social", _];
var validateProviderIngestion = args is ["--validate-provider-ingestion", _] ||
                                 validateIncumbentCalibration ||
                                 validateMichiganProviderBundle ||
                                 validateOhioProviderBundle ||
                                 validateOhioProviderIngestion;
if ((!validateIncumbentCalibration && !validateMichiganProviderBundle && !validateOhioProviderBundle && args.Length != 2) ||
    ((validateMichiganProviderBundle || validateOhioProviderBundle) && args.Length != 3) ||
    (validateIncumbentCalibration && args.Length != 4))
{
    throw new ArgumentException(
        "Usage: GravityModelIntegrationHarness <validation-db> <valhalla-base-url> | --probe-zcta-origins | --probe-irs-soi | --probe-indiana-providers | --probe-illinois-providers | --probe-michigan-facilities | --probe-michigan-performance | --export-michigan-provider-bundle <output-json> | --export-ohio-capacity-provider-bundle <output-json> | --export-ohio-provider-bundle <output-json> | --export-four-state-provider-bundle <output-json> | --refresh-four-state-indiana-facilities <base-bundle-json> <output-json> | --ingest-four-state-provider-bundle <validation-db> <bundle-json> | --validate-michigan-provider-bundle <validation-db> <bundle-json> | --validate-ohio-provider-bundle <validation-db> <bundle-json> | --validate-ohio-provider-ingestion <validation-db> | --validate-provider-ingestion <validation-db> | --validate-indiana-fiscal <validation-db> | --validate-indiana-employment <validation-db> | --validate-indiana-social <validation-db> | --validate-incumbent-calibration <validation-db> <valhalla-base-url> <four-state-provider-bundle-json>");
}

var namedDatabaseValidation = validateProviderIngestion || validateIndianaFiscal || validateIndianaEmployment || validateIndianaSocial;
var validationDatabase = namedDatabaseValidation ? args[1] : args[0];
var hasRequiredDatabasePrefix = namedDatabaseValidation
    ? validationDatabase.StartsWith(
        validateIncumbentCalibration
            ? "savenein_calibration_validation_"
            : validateIndianaFiscal
                ? "savenein_fiscal_validation_"
                : validateIndianaEmployment
                    ? "savenein_employment_validation_"
                    : validateIndianaSocial
                        ? "savenein_social_validation_"
                : "savenein_provider_validation_",
        StringComparison.Ordinal)
    : validationDatabase.StartsWith("savenein_gravity_validation_", StringComparison.Ordinal) ||
      validationDatabase.StartsWith("savenein_ui_validation_", StringComparison.Ordinal);
if (!hasRequiredDatabasePrefix ||
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

if (validateIndianaFiscal)
{
    var fiscalComponentColumnCount = await db.Database
        .SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'model_run_fiscal_impacts'
              AND column_name IN (
                  'base_gaming_tax',
                  'supplemental_gaming_tax',
                  'host_municipality_gaming_tax_share',
                  'host_county_gaming_tax_share',
                  'host_regional_gaming_tax_share',
                  'host_state_gaming_tax_share')
            """)
        .SingleAsync();
    var profiles = new JurisdictionProfileService(db);
    var taxCalculator = new GamingTaxCalculator(profiles);
    var allocationCalculator = new GamingFiscalAllocationCalculator(
        profiles,
        new CandidateFiscalLocationResolver(db));
    var ageResolver = new GamingAgeResolver(profiles);
    var effectiveOn = new DateOnly(2026, 8, 13);
    var lowPriorYearCasino = await taxCalculator.CalculateAsync(new GamingTaxRequest(
        "US-IN",
        "commercial-casino",
        effectiveOn,
        0,
        80_000_000m,
        PriorFiscalYearTaxableGamingRevenue: 0));
    var ordinaryCasino = await taxCalculator.CalculateAsync(new GamingTaxRequest(
        "US-IN",
        "commercial-casino",
        effectiveOn,
        0,
        80_000_000m,
        PriorFiscalYearTaxableGamingRevenue: 75_000_000m));
    var northeastAllocation = await allocationCalculator.CalculateAsync(new GamingFiscalAllocationRequest(
        "US-IN",
        "commercial-casino",
        effectiveOn,
        80_000_000m,
        ordinaryCasino.GamingTax,
        41.0793,
        -85.1394));
    var racino = await taxCalculator.CalculateAsync(new GamingTaxRequest(
        "US-IN",
        "commercial-racino",
        effectiveOn,
        0,
        150_000_000m));
    var casinoAge = await ageResolver.ResolveMinimumAgeAsync("US-IN", "commercial-casino", effectiveOn);
    var racinoAge = await ageResolver.ResolveMinimumAgeAsync("US-IN", "commercial-racino", effectiveOn);
    var fiscalRuleTypes = new[]
    {
        JurisdictionRuleTypes.GamingTaxSchedule,
        JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
        JurisdictionRuleTypes.GamingTaxDistribution
    };
    var fiscalRules = await db.JurisdictionRules
        .Where(rule => fiscalRuleTypes.Contains(rule.RuleType))
        .OrderBy(rule => rule.SourceUrl)
        .Select(rule => new { rule.ValidationState, rule.SourceUrl, rule.RuleValueJson })
        .ToArrayAsync();
    var legacyRuleCount = fiscalRules.Count(rule =>
        rule.ValidationState != JurisdictionRuleValidationStates.Validated ||
        rule.SourceUrl == "https://www.in.gov/igc/files/FY2025-Annual.pdf" ||
        rule.RuleValueJson.Contains("shareOfGamingTax"));
    if (lowPriorYearCasino.GamingTax != 12_125_000m ||
        ordinaryCasino.GamingTax != 15_250_000m ||
        racino.GamingTax != 40_000_000m ||
        northeastAllocation.SupplementalGamingTax != 2_800_000m ||
        northeastAllocation.GrossGamingTax != 18_050_000m ||
        northeastAllocation.HostMunicipalityShare != 1_260_000m ||
        northeastAllocation.HostCountyShare != 1_260_000m ||
        northeastAllocation.HostRegionalShare != 280_000m ||
        northeastAllocation.HostStateShare != 15_250_000m ||
        northeastAllocation.Location.CountyFips != "18003" ||
        northeastAllocation.Location.MunicipalityName != "Fort Wayne" ||
        fiscalComponentColumnCount != 6 ||
        casinoAge != 21 || racinoAge != 21 ||
        fiscalRules.Length != 5 || legacyRuleCount != 0)
    {
        throw new InvalidOperationException("The seeded Indiana fiscal rules did not reproduce the official effective schedules.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Database = validationDatabase,
        EffectiveOn = effectiveOn,
        CasinoLowPriorYearTax = lowPriorYearCasino.GamingTax,
        CasinoOrdinaryTax = ordinaryCasino.GamingTax,
        NortheastSupplementalTax = northeastAllocation.SupplementalGamingTax,
        NortheastGrossGamingTax = northeastAllocation.GrossGamingTax,
        northeastAllocation.HostMunicipalityShare,
        northeastAllocation.HostCountyShare,
        northeastAllocation.HostRegionalShare,
        northeastAllocation.HostStateShare,
        FiscalLocation = northeastAllocation.Location,
        FiscalComponentColumnCount = fiscalComponentColumnCount,
        RacinoTax = racino.GamingTax,
        CasinoMinimumAge = casinoAge,
        RacinoMinimumAge = racinoAge,
        LegacyRuleCount = legacyRuleCount,
        Rules = fiscalRules.Select(rule => new { rule.ValidationState, rule.SourceUrl })
    }));
    return;
}

if (validateIndianaEmployment)
{
    var providerSnapshots = new DataSnapshotService(db);
    var providerRows = new ModelDataIngestionService(db);
    var providerIngestion = new ProviderSnapshotIngestionService(providerSnapshots, providerRows);
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var igcOptions = Options.Create(new IndianaGamingCommissionProviderOptions());
    var facilitySnapshotId = await providerIngestion.IngestGamingFacilitiesAsync(
        new IndianaGamingCommissionFacilityInventoryProvider(providerHttp, igcOptions),
        new ProviderFetchRequest("US-IN", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
    var performanceSnapshotId = await providerIngestion.IngestGamingPerformanceAsync(
        new IndianaGamingCommissionMonthlyRevenueProvider(providerHttp, igcOptions),
        new ProviderFetchRequest("US-IN", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
        facilitySnapshotId);
    var employmentCompetitors = await db.CasinoCompetitors.AsNoTracking()
        .Where(competitor => competitor.DatasetSnapshotId == facilitySnapshotId)
        .OrderBy(competitor => competitor.StableVenueId)
        .ToArrayAsync();
    var performance = await db.CasinoGamingRevenuePeriods.AsNoTracking()
        .Where(period => period.DatasetSnapshotId == performanceSnapshotId)
        .OrderBy(period => period.CasinoCompetitorId)
        .ThenBy(period => period.PeriodStart)
        .ToArrayAsync();
    var resolution = new EmploymentProductivityBenchmarkService().Resolve(
        new EmploymentProductivityBenchmarkInput(
            performanceSnapshotId,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            employmentCompetitors,
            performance));
    var benchmark = resolution.Benchmark
        ?? throw new InvalidOperationException(string.Join(" ", resolution.Warnings));
    var schemaColumnCount = await db.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value"
        FROM information_schema.columns
        WHERE table_name = 'casino_competitors'
          AND column_name = 'reported_employment'
        """).SingleAsync();
    var validatedConstraintCount = await db.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value"
        FROM pg_constraint
        WHERE conname = 'ck_casino_competitors_reported_employment_positive'
          AND convalidated
        """).SingleAsync();
    var snapshots = await db.DatasetSnapshots.AsNoTracking()
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .OrderBy(snapshot => snapshot.DatasetKey)
        .Select(snapshot => new
        {
            snapshot.Id,
            snapshot.DatasetKey,
            snapshot.Checksum,
            snapshot.TransformVersion,
            snapshot.RowCount,
            snapshot.ValidationState,
            snapshot.IsSealed
        })
        .ToArrayAsync();
    if (employmentCompetitors.Length != 13 || employmentCompetitors.Any(competitor => competitor.ReportedEmployment is not > 0) ||
        employmentCompetitors.Sum(competitor => competitor.ReportedEmployment) != 10_112 ||
        performance.Length != 468 || benchmark.Facilities.Count != 13 ||
        schemaColumnCount != 1 || validatedConstraintCount != 1 ||
        snapshots.Length != 2 || snapshots.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("The live Indiana employment benchmark did not persist or reconcile expected official observations.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Database = validationDatabase,
        facilitySnapshotId,
        performanceSnapshotId,
        FacilityCount = employmentCompetitors.Length,
        TotalReportedEmployment = employmentCompetitors.Sum(competitor => competitor.ReportedEmployment),
        PerformanceRows = performance.Length,
        ComparableGgr = performance
            .Where(period => period.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
            .Sum(period => period.ReportedAmount),
        benchmark,
        resolution.Warnings,
        SchemaColumnCount = schemaColumnCount,
        ValidatedConstraintCount = validatedConstraintCount,
        snapshots
    }));
    return;
}

if (validateIndianaSocial)
{
    var effectiveOn = new DateOnly(2026, 8, 13);
    var resolver = new ProblemGamblingPrevalenceResolver(new JurisdictionProfileService(db));
    var assumption = await resolver.ResolveAsync("US-IN", effectiveOn)
        ?? throw new InvalidOperationException("The validated Indiana prevalence rule was not resolved.");
    var ruleCount = await db.JurisdictionRules.CountAsync(rule =>
        rule.RuleType == JurisdictionRuleTypes.ProblemGamblingPrevalence &&
        rule.ValidationState == JurisdictionRuleValidationStates.Validated &&
        rule.EffectiveFrom <= effectiveOn &&
        (rule.EffectiveTo == null || rule.EffectiveTo >= effectiveOn));
    if (ruleCount != 1 || assumption.Prevalence != 0.041 ||
        assumption.LowerConfidenceBound != 0.018 || assumption.UpperConfidenceBound != 0.090 ||
        assumption.ObservationYear != 2021 || assumption.Instrument != "DSM-V" ||
        assumption.SourceSha256 != "9414096e164ce4a68ba700a46e659e662328403aaa82ec0209c0d03a25a47ee3")
    {
        throw new InvalidOperationException("The seeded Indiana social-cost prevalence rule did not reproduce its official evidence.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Database = validationDatabase,
        EffectiveOn = effectiveOn,
        RuleCount = ruleCount,
        assumption
    }));
    return;
}

if (validateMichiganProviderBundle)
{
    var bundlePath = Path.GetFullPath(args[2]);
    if (!File.Exists(bundlePath) || !string.Equals(Path.GetExtension(bundlePath), ".json", StringComparison.OrdinalIgnoreCase))
    {
        throw new FileNotFoundException("The Michigan provider validation bundle was not found.", bundlePath);
    }
    var bundle = JsonSerializer.Deserialize<ProviderValidationBundle>(
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
        new FrozenGamingFacilityProvider(
            bundle.Facilities,
            "frozen-official-michigan-facilities-validation",
            "US-MI"),
        request);
    var performanceSnapshotId = await bundleIngestion.IngestGamingPerformanceAsync(
        new FrozenGamingPerformanceProvider(
            bundle.Performance,
            "frozen-official-michigan-performance-validation",
            "US-MI"),
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

if (validateOhioProviderBundle)
{
    var bundlePath = Path.GetFullPath(args[2]);
    if (!File.Exists(bundlePath) || !string.Equals(Path.GetExtension(bundlePath), ".json", StringComparison.OrdinalIgnoreCase))
    {
        throw new FileNotFoundException("The Ohio provider validation bundle was not found.", bundlePath);
    }
    var bundle = JsonSerializer.Deserialize<ProviderValidationBundle>(
            await File.ReadAllTextAsync(bundlePath))
        ?? throw new InvalidDataException("The Ohio provider validation bundle is empty or invalid.");
    var request = new ProviderFetchRequest(
        "US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var snapshots = new DataSnapshotService(db);
    var rows = new ModelDataIngestionService(db);
    var bundleIngestion = new ProviderSnapshotIngestionService(snapshots, rows);
    var facilitySnapshotId = await bundleIngestion.IngestGamingFacilitiesAsync(
        new FrozenGamingFacilityProvider(
            bundle.Facilities,
            "frozen-official-ohio-facilities-validation",
            "US-OH"),
        request);
    var performanceSnapshotId = await bundleIngestion.IngestGamingPerformanceAsync(
        new FrozenGamingPerformanceProvider(
            bundle.Performance,
            "frozen-official-ohio-performance-validation",
            "US-OH"),
        request,
        facilitySnapshotId);
    var facilityCount = await db.CasinoCompetitors.CountAsync(row => row.DatasetSnapshotId == facilitySnapshotId);
    var performanceCount = await db.CasinoGamingRevenuePeriods.CountAsync(row => row.DatasetSnapshotId == performanceSnapshotId);
    var comparableTotal = await db.CasinoGamingRevenuePeriods
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId &&
                      row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .SumAsync(row => row.ReportedAmount);
    var capacityCompetitors = await db.CasinoCompetitors.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == facilitySnapshotId)
        .ToArrayAsync();
    var capacityPeriods = await db.CasinoGamingRevenuePeriods.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId)
        .ToArrayAsync();
    var capacityResolution = new CapacityProductivityBenchmarkService().Resolve(
        new CapacityProductivityBenchmarkInput(
            performanceSnapshotId,
            request.PeriodStart,
            request.PeriodEnd,
            capacityCompetitors,
            capacityPeriods));
    var componentRowCount = capacityPeriods.Count(row =>
        row.ReportedMetricKey is GamingRevenueMetricKeys.SlotOrVltGamingRevenue or
            GamingRevenueMetricKeys.TableGameGamingRevenue);
    var isCapacityBundle = facilityCount == 4;
    var expectedPerformanceCount = isCapacityBundle ? 192 : componentRowCount == 0 ? 264 : 360;
    var expectedComparableTotal = isCapacityBundle ? 1_033_920_366m : 2_457_921_705m;
    var snapshotStates = await db.DatasetSnapshots
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .Select(snapshot => new { snapshot.DatasetKey, snapshot.IsSealed, snapshot.ValidationState, snapshot.Checksum })
        .OrderBy(snapshot => snapshot.DatasetKey)
        .ToArrayAsync();
    if (facilityCount is not (4 or 11) || performanceCount != expectedPerformanceCount ||
        comparableTotal != expectedComparableTotal ||
        (componentRowCount > 0 && capacityResolution.Benchmark?.Facilities.Count != 4) ||
        snapshotStates.Length != 2 || snapshotStates.Any(snapshot => !snapshot.IsSealed))
    {
        throw new InvalidOperationException("The Ohio provider bundle did not persist the expected sealed regulator rows.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        facilitySnapshotId,
        performanceSnapshotId,
        facilityCount,
        performanceCount,
        comparableTotal,
        componentRowCount,
        capacityResolution.Benchmark,
        capacityResolution.Warnings,
        snapshotStates,
        SourceMode = isCapacityBundle
            ? "Official OCCC capacity-provider outputs fetched locally and transferred by exact checksum for disposable PostGIS ingestion validation."
            : "Official OCCC and Ohio Lottery provider outputs fetched locally because ohiolottery.com returned HTTP 403 to the VPS source IP; exact composite provider checksums and rows were transferred for disposable PostGIS ingestion validation."
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
    var lotteryConfigured = Options.Create(new OhioLotteryVideoLotteryProviderOptions());
    var lotteryRevenueProvider = new OhioLotteryVideoLotteryRevenueProvider(providerHttp, lotteryConfigured);
    var lotteryFacilityProvider = new OhioLotteryVideoLotteryFacilityInventoryProvider(
        providerHttp,
        lotteryRevenueProvider,
        lotteryConfigured);
    var snapshots = new DataSnapshotService(db);
    var rows = new ModelDataIngestionService(db);
    var ohioIngestion = new ProviderSnapshotIngestionService(snapshots, rows);
    var facilitySnapshotId = await ohioIngestion.IngestGamingFacilitiesAsync(
        new CompositeGamingFacilityInventoryProvider([facilityProvider, lotteryFacilityProvider]),
        request);
    var performanceSnapshotId = await ohioIngestion.IngestGamingPerformanceAsync(
        new CompositeGamingRegulatorPerformanceProvider([revenueProvider, lotteryRevenueProvider]),
        request,
        facilitySnapshotId);
    var facilityCount = await db.CasinoCompetitors.CountAsync(row => row.DatasetSnapshotId == facilitySnapshotId);
    var performanceCount = await db.CasinoGamingRevenuePeriods.CountAsync(row => row.DatasetSnapshotId == performanceSnapshotId);
    var comparableTotal = await db.CasinoGamingRevenuePeriods
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId &&
                      row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue)
        .SumAsync(row => row.ReportedAmount);
    var capacityCompetitors = await db.CasinoCompetitors.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == facilitySnapshotId)
        .ToArrayAsync();
    var capacityPeriods = await db.CasinoGamingRevenuePeriods.AsNoTracking()
        .Where(row => row.DatasetSnapshotId == performanceSnapshotId)
        .ToArrayAsync();
    var capacityResolution = new CapacityProductivityBenchmarkService().Resolve(
        new CapacityProductivityBenchmarkInput(
            performanceSnapshotId,
            request.PeriodStart,
            request.PeriodEnd,
            capacityCompetitors,
            capacityPeriods));
    var capacityBenchmark = capacityResolution.Benchmark
        ?? throw new InvalidOperationException(string.Join(" ", capacityResolution.Warnings));
    var snapshotStates = await db.DatasetSnapshots
        .Where(snapshot => snapshot.Id == facilitySnapshotId || snapshot.Id == performanceSnapshotId)
        .Select(snapshot => new { snapshot.DatasetKey, snapshot.IsSealed, snapshot.ValidationState, snapshot.Checksum })
        .OrderBy(snapshot => snapshot.DatasetKey)
        .ToArrayAsync();
    if (facilityCount != 11 || performanceCount != 360 || comparableTotal != 2_457_921_705m ||
        capacityBenchmark.Facilities.Count != 4 ||
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
        capacityBenchmark,
        capacityResolution.Warnings,
        snapshotStates
    }));
    return;
}

if (validateProviderIngestion)
{
    ProviderValidationBundle? calibrationProviderBundle = null;
    if (validateIncumbentCalibration)
    {
        var bundlePath = Path.GetFullPath(args[3]);
        if (!File.Exists(bundlePath) || !string.Equals(Path.GetExtension(bundlePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The checksum-pinned four-state calibration provider bundle was not found.", bundlePath);
        }
        const string expectedBundleChecksum = "31d2fc3f3762ec02a97e18996ff680a276cc4d76b5016b3f388a5b287dd08396";
        await using (var bundleStream = File.OpenRead(bundlePath))
        {
            var actualBundleChecksum = Convert.ToHexString(
                    await SHA256.HashDataAsync(bundleStream))
                .ToLowerInvariant();
            if (!string.Equals(actualBundleChecksum, expectedBundleChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The four-state calibration provider bundle checksum was '{actualBundleChecksum}', expected '{expectedBundleChecksum}'.");
            }
        }
        calibrationProviderBundle = JsonSerializer.Deserialize<ProviderValidationBundle>(
                await File.ReadAllTextAsync(bundlePath))
            ?? throw new InvalidDataException("The four-state calibration provider bundle is empty or invalid.");
    }
    var providerSnapshots = new DataSnapshotService(db);
    var providerRows = new ModelDataIngestionService(db);
    var providerIngestion = new ProviderSnapshotIngestionService(providerSnapshots, providerRows);
    using var providerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var igcOptions = Options.Create(new IndianaGamingCommissionProviderOptions());
    var igbOptions = Options.Create(new IllinoisGamingBoardProviderOptions());
    var mgcbOptions = Options.Create(new MichiganGamingFacilityProviderOptions());
    var occcOptions = Options.Create(new OhioCasinoControlCommissionProviderOptions());
    var ohioLotteryOptions = Options.Create(new OhioLotteryVideoLotteryProviderOptions());
    var igcRevenueProvider = new IndianaGamingCommissionMonthlyRevenueProvider(providerHttp, igcOptions);
    var igcFacilityProvider = new IndianaGamingCommissionFacilityInventoryProvider(providerHttp, igcOptions);
    var indianaTribalFacilityProvider = new IndianaTribalGamingFacilityInventoryProvider(
        providerHttp,
        Options.Create(new IndianaTribalGamingFacilityProviderOptions()));
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
    var ohioLotteryRevenueProvider = new OhioLotteryVideoLotteryRevenueProvider(providerHttp, ohioLotteryOptions);
    var ohioLotteryFacilityProvider = new OhioLotteryVideoLotteryFacilityInventoryProvider(
        providerHttp,
        ohioLotteryRevenueProvider,
        ohioLotteryOptions);
    var igcFacilityPeriod = new ProviderFetchRequest(
        "US-IN,US-IL,US-MI,US-OH",
        new DateOnly(2025, 12, 1),
        new DateOnly(2025, 12, 31));
    var igcPerformancePeriod = new ProviderFetchRequest(
        "US-IN,US-IL,US-MI,US-OH",
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 12, 31));
    var existingFacilitySnapshotId = calibrationProviderBundle is null
        ? null
        : await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.DatasetKey == DatasetSnapshotKinds.Competitors &&
                snapshot.Checksum == calibrationProviderBundle.Facilities.ContentChecksum &&
                snapshot.IsSealed)
            .Select(snapshot => (Guid?)snapshot.Id)
            .SingleOrDefaultAsync();
    var facilitySnapshotId = existingFacilitySnapshotId ??
        await providerIngestion.IngestGamingFacilitiesAsync(
            calibrationProviderBundle is null
                ? new CompositeGamingFacilityInventoryProvider(
                    new IGamingFacilityInventoryProvider[] { igcFacilityProvider, indianaTribalFacilityProvider, igbFacilityProvider, mgcbFacilityProvider, occcFacilityProvider, ohioLotteryFacilityProvider })
                : new FrozenGamingFacilityProvider(
                    calibrationProviderBundle.Facilities,
                    "checksum-pinned-four-state-calibration-facilities",
                    "US-IN,US-IL,US-MI,US-OH"),
            igcFacilityPeriod);
    ProviderDataset<CasinoGamingRevenueImportRow>? linkedFrozenPerformance = null;
    if (calibrationProviderBundle is not null)
    {
        var linkedPerformanceManifest =
            $"performance={calibrationProviderBundle.Performance.ContentChecksum}\n" +
            $"facility={calibrationProviderBundle.Facilities.ContentChecksum}";
        var linkedPerformanceChecksum = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(linkedPerformanceManifest)))
            .ToLowerInvariant();
        linkedFrozenPerformance = calibrationProviderBundle.Performance with
        {
            ContentChecksum = linkedPerformanceChecksum,
            TransformVersion = calibrationProviderBundle.Performance.TransformVersion +
                               "+facility-link-v1",
            Warnings = calibrationProviderBundle.Performance.Warnings
                .Append(
                    $"Performance rows retain raw provider checksum {calibrationProviderBundle.Performance.ContentChecksum}; " +
                    $"snapshot transform checksum {linkedPerformanceChecksum} also binds refreshed facility checksum " +
                    $"{calibrationProviderBundle.Facilities.ContentChecksum} because performance foreign keys resolve inside that immutable facility snapshot.")
                .ToArray()
        };
    }
    var existingPerformanceSnapshotId = linkedFrozenPerformance is null
        ? null
        : await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.DatasetKey == DatasetSnapshotKinds.ObservedPerformance &&
                snapshot.Checksum == linkedFrozenPerformance.ContentChecksum &&
                snapshot.IsSealed)
            .Select(snapshot => (Guid?)snapshot.Id)
            .SingleOrDefaultAsync();
    var performanceSnapshotId = existingPerformanceSnapshotId ??
        await providerIngestion.IngestGamingPerformanceAsync(
            calibrationProviderBundle is null
                ? new CompositeGamingRegulatorPerformanceProvider(
                    new IGamingRegulatorPerformanceProvider[] { igcRevenueProvider, igbRevenueProvider, mgcbRevenueProvider, occcRevenueProvider, ohioLotteryRevenueProvider })
                : new FrozenGamingPerformanceProvider(
                    linkedFrozenPerformance!,
                    "checksum-pinned-four-state-calibration-performance",
                    "US-IN,US-IL,US-MI,US-OH"),
            igcPerformancePeriod,
            facilitySnapshotId);
    Guid trafficSnapshotId;
    Guid originSnapshotId;
    Guid incomeSnapshotId;
    Guid ageSnapshotId;
    Guid tourismSnapshotId;
    int expectedOriginCount;
    var reusableOriginSnapshot = validateIncumbentCalibration
        ? await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.DatasetKey == DatasetSnapshotKinds.OriginGeography && snapshot.IsSealed)
            .OrderByDescending(snapshot => snapshot.IngestedAtUtc)
            .Select(snapshot => (Guid?)snapshot.Id)
            .FirstOrDefaultAsync()
        : null;
    if (reusableOriginSnapshot is not null)
    {
        originSnapshotId = reusableOriginSnapshot.Value;
        trafficSnapshotId = await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.DatasetKey == DatasetSnapshotKinds.Traffic && snapshot.IsSealed)
            .OrderByDescending(snapshot => snapshot.IngestedAtUtc)
            .Select(snapshot => snapshot.Id)
            .FirstAsync();
        incomeSnapshotId = await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.DatasetKey == DatasetSnapshotKinds.Income && snapshot.IsSealed)
            .OrderByDescending(snapshot => snapshot.IngestedAtUtc)
            .Select(snapshot => snapshot.Id)
            .FirstAsync();
        ageSnapshotId = await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.DatasetKey == DatasetSnapshotKinds.AgePopulation && snapshot.IsSealed)
            .OrderByDescending(snapshot => snapshot.IngestedAtUtc)
            .Select(snapshot => snapshot.Id)
            .FirstAsync();
        tourismSnapshotId = await db.DatasetSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.DatasetKey == DatasetSnapshotKinds.Tourism && snapshot.IsSealed)
            .OrderByDescending(snapshot => snapshot.IngestedAtUtc)
            .Select(snapshot => snapshot.Id)
            .FirstAsync();
        expectedOriginCount = await db.OriginZones.CountAsync(row => row.DatasetSnapshotId == originSnapshotId);
    }
    else
    {
        trafficSnapshotId = await providerIngestion.IngestTrafficAsync(
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
        originSnapshotId = await providerIngestion.IngestOriginsAsync(
        new CensusZctaOriginProvider(
            providerHttp,
            Options.Create(new CensusZctaOriginProviderOptions())),
        new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = zctaCodes }));
        incomeSnapshotId = await providerIngestion.IngestIncomeAsync(
        new FrozenOriginIncomeProvider(irsDataset),
        irsPeriod,
        originSnapshotId);
        var censusApiKey = Environment.GetEnvironmentVariable("CensusAcs__ApiKey");
        ageSnapshotId = await providerIngestion.IngestAgePopulationAsync(
        new CensusAcsAgePopulationProvider(
            providerHttp,
            Options.Create(new CensusAcsProviderOptions { ApiKey = censusApiKey })),
        new ProviderFetchRequest(
            "US-ZCTA",
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            new Dictionary<string, string> { ["zcta-codes"] = zctaCodes }),
        originSnapshotId);
        tourismSnapshotId = await providerIngestion.IngestTourismAsync(
        new IndianaDestinationDevelopmentPersonTripsProvider(
            providerHttp,
            Options.Create(new IndianaTourismProviderOptions())),
        new ProviderFetchRequest(
            "US-IN",
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31)));
        expectedOriginCount = irsDataset.Rows.Count;
    }

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
    if (facilityCount != 69 || unknownHotelCount != 55 || performanceCount != 838 || trafficCount != 1 ||
        originCount != expectedOriginCount || incomeCount != expectedOriginCount ||
        ageBinCount != expectedOriginCount * 23 || tourismCount != 1 ||
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
        var obsoleteCalibrationFiscalFixtures = await db.JurisdictionRules
            .Where(rule => rule.JurisdictionId == validationIndiana.Id &&
                           rule.SourceUrl != null &&
                           rule.SourceUrl.StartsWith("https://example.invalid/disposable-calibration-"))
            .ToArrayAsync();
        if (obsoleteCalibrationFiscalFixtures.Length > 0)
        {
            db.JurisdictionRules.RemoveRange(obsoleteCalibrationFiscalFixtures);
            await db.SaveChangesAsync();
        }
        db.JurisdictionRules.AddRange(
            new JurisdictionRule
            {
                JurisdictionId = validationIndiana.Id,
                RuleType = JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
                RuleValueJson = JsonSerializer.Serialize(new SupplementalGamingTaxPayload("*", 0m, [])),
                ValidationState = JurisdictionRuleValidationStates.Validated,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SourceUrl = "https://example.invalid/disposable-calibration-supplemental-tax",
                ProvenanceNotes = "Disposable impact-accounting fixture only; no supplemental tax enters revenue calibration."
            },
            DisposableDistribution(validationIndiana.Id, "*", GamingTaxComponents.Base, 0.80m, 0.20m,
                "https://example.invalid/disposable-calibration-base-distribution"),
            DisposableDistribution(validationIndiana.Id, "*", GamingTaxComponents.Supplemental, 1m, 0m,
                "https://example.invalid/disposable-calibration-supplemental-distribution"),
            new JurisdictionRule
            {
                JurisdictionId = validationIndiana.Id,
                RuleType = JurisdictionRuleTypes.GeneralFiscalRates,
                RuleValueJson = JsonSerializer.Serialize(new GeneralFiscalRulePayload(
                    "*", 0.07m, 0.05m, 0.03m, 25_000m, 0.10m)),
                ValidationState = JurisdictionRuleValidationStates.Validated,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SourceUrl = "https://example.invalid/disposable-calibration-general-fiscal",
                ProvenanceNotes = "Disposable impact-accounting fixture only; not an Indiana general-fiscal calibration."
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
            $"live-2025-attributes-v2-{stableId["USA-IN-IGC-".Length..]}",
            partition,
            group);
        var targets = new[]
        {
            Target("USA-IN-IGC-horseshoe-indianapolis", ValidationPartitions.Training, "indianapolis-market"),
            Target("USA-IN-IGC-harrahs-hoosier-park", ValidationPartitions.Training, "indianapolis-market"),
            Target("USA-IN-IGC-blue-chip-casino", ValidationPartitions.Training, "michigan-city-market"),
            Target("USA-IN-IGC-terre-haute-casino", ValidationPartitions.Training, "terre-haute-market"),
            Target("USA-IN-IGC-hard-rock-casino-northern-indiana", ValidationPartitions.Holdout, "chicago-indiana-market"),
            Target("USA-IN-IGC-horseshoe-hammond", ValidationPartitions.Holdout, "chicago-indiana-market"),
            Target("USA-IN-IGC-ameristar-casino", ValidationPartitions.Holdout, "chicago-indiana-market")
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
            new CapacityProductivityBenchmarkService(),
            new RampScheduleService(),
            new GamingTaxCalculator(profiles),
            new GamingFiscalAllocationCalculator(profiles, new CandidateFiscalLocationResolver(db)),
            new GeneralFiscalRuleResolver(profiles),
            new ProblemGamblingPrevalenceResolver(profiles),
            new CannibalizationAccountingService(),
            new LocalEconomicInventoryWeightService(),
            new DisplacementModelService(),
            new EmploymentImpactService(),
            new EmploymentProductivityBenchmarkService(),
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
        var candidates = new[]
        {
            new { Key = "balanced", ComparableScale = 3d, GamingPositions = 0.75d, Tables = 0.2d, Hotels = 0.35d, GamingFloor = 0.25d, FoodBeverage = 0.25d, RegionalIntensity = 1d },
            new { Key = "amenity", ComparableScale = 2.5d, GamingPositions = 0.5d, Tables = 0.1d, Hotels = 0.6d, GamingFloor = 0.35d, FoodBeverage = 0.4d, RegionalIntensity = 1.1d }
        }.SelectMany(profile => new[] { 1.3d, 1.4d, 1.5d }.SelectMany(beta =>
            new[] { 0.75d, 1d, 1.25d }.Select(alpha => new IncumbentCalibrationCandidate(
                $"{profile.Key}-beta-{beta:0.0}-alpha-{alpha:0.00}",
                new Dictionary<string, double>
                {
                    ["gravity.beta"] = beta,
                    ["gravity.alpha"] = alpha,
                    ["gravity.outside_option_weight"] = 0.00000001,
                    ["facility.comparable_scale_multiplier"] = profile.ComparableScale,
                    ["facility.gaming_positions_coefficient"] = profile.GamingPositions,
                    ["facility.table_games_coefficient"] = profile.Tables,
                    ["facility.hotel_rooms_coefficient"] = profile.Hotels,
                    ["facility.gaming_floor_coefficient"] = profile.GamingFloor,
                    ["facility.food_beverage_coefficient"] = profile.FoodBeverage,
                    ["demand.regional_intensity_multiplier"] = profile.RegionalIntensity
                })))).ToArray();
        var finalizedBeforeCalibration = await db.ModelRuns.CountAsync(run =>
            run.Status == ModelRunStatuses.Finalized);
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
                "live-indiana-incumbent-backtest-attributes-v2",
                "2025-disposable-validation-v2",
                ValidationObjectiveFunctions.Smape,
                baseRequest,
                targets,
                candidates,
                ["total-resident-demand", "gaming-positions"],
                JsonSerializer.Serialize(new
                {
                    purpose = "Leakage-safe northern and central Indiana incumbent calibration against authoritative 2025 regulator observations.",
                    inclusion = "Training properties are Indianapolis, Michigan City, and Terre Haute markets. The independent holdout is the three-property Indiana side of the Chicago market.",
                    exclusion = "Southern and Ohio River properties are excluded because the sealed competitor field does not yet contain Kentucky facilities. Four Winds South Bend remains in the competitive field with structural attraction because public tribal GGR is unavailable.",
                    parameterDesign = "Predeclared 18-cell grid crossing beta 1.3/1.4/1.5, alpha 0.75/1.00/1.25, and two bounded regulator-attribute profiles. The profiles vary proposed comparable scale plus positions, tables, hotel rooms, gaming-floor area, food/beverage venues, and regional demand intensity. Outside-option weight remains at the common public-benchmark surface of 1e-8.",
                    zctaAssignment = "Dominant 2020 Census county by land-area overlap."
                }),
                sourceParameterSet.Id,
                "0.3.0-indiana-incumbent-attributes-calibration",
                OriginPrefilterMiles: 50));
        var evaluation = await db.ValidationEvaluations.AsNoTracking()
            .SingleAsync(item => item.Id == calibration.Evaluation.ValidationEvaluationId);
        var caseCount = await db.ValidationCases.CountAsync(item =>
            item.CaseKey.StartsWith("live-2025-attributes-v2-"));
        var finalizedRunCount = await db.ModelRuns.CountAsync(run =>
            run.Status == ModelRunStatuses.Finalized) - finalizedBeforeCalibration;
        if (evaluation.Status != ValidationEvaluationStatuses.Finalized || !evaluation.IsImmutable ||
            calibration.Evaluation.PublishedParameterSetId is null || caseCount != targets.Length ||
            finalizedRunCount != targets.Length * candidates.Length)
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
                limitation = "Public tribal GGR, market-specific tourism, and corridor-complete traffic remain unavailable and are excluded from the incumbent revenue target. Public benchmark reconciliation is a separate post-calibration test."
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
        RuleType = JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
        RuleValueJson = JsonSerializer.Serialize(new SupplementalGamingTaxPayload("commercial-casino", 0m, [])),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/integration-supplemental-tax",
        ProvenanceNotes = "Disposable integration fixture only."
    },
    DisposableDistribution(indiana.Id, "commercial-casino", GamingTaxComponents.Base, 0.80m, 0.20m,
        "https://example.invalid/integration-base-distribution"),
    DisposableDistribution(indiana.Id, "commercial-casino", GamingTaxComponents.Supplemental, 1m, 0m,
        "https://example.invalid/integration-supplemental-distribution"),
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
        RuleType = JurisdictionRuleTypes.SupplementalGamingTaxSchedule,
        RuleValueJson = JsonSerializer.Serialize(new SupplementalGamingTaxPayload("commercial-casino", 0m, [])),
        ValidationState = JurisdictionRuleValidationStates.Validated,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        SourceUrl = "https://example.invalid/ohio-synthetic-supplemental-tax",
        ProvenanceNotes = "Disposable non-Indiana portability fixture only."
    },
    DisposableDistribution(ohio.Id, "commercial-casino", GamingTaxComponents.Base, 0.85m, 0.15m,
        "https://example.invalid/ohio-synthetic-base-distribution"),
    DisposableDistribution(ohio.Id, "commercial-casino", GamingTaxComponents.Supplemental, 1m, 0m,
        "https://example.invalid/ohio-synthetic-supplemental-distribution"),
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
    new CapacityProductivityBenchmarkService(),
    new RampScheduleService(),
    new GamingTaxCalculator(new JurisdictionProfileService(db)),
    new GamingFiscalAllocationCalculator(
        new JurisdictionProfileService(db),
        new CandidateFiscalLocationResolver(db)),
    new GeneralFiscalRuleResolver(new JurisdictionProfileService(db)),
    new ProblemGamblingPrevalenceResolver(new JurisdictionProfileService(db)),
    new CannibalizationAccountingService(),
    new LocalEconomicInventoryWeightService(),
    new DisplacementModelService(),
    new EmploymentImpactService(),
    new EmploymentProductivityBenchmarkService(),
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

static JurisdictionRule DisposableDistribution(
    int jurisdictionId,
    string facilityRegime,
    string component,
    decimal stateShare,
    decimal countyShare,
    string sourceUrl) => new()
{
    JurisdictionId = jurisdictionId,
    RuleType = JurisdictionRuleTypes.GamingTaxDistribution,
    RuleValueJson = JsonSerializer.Serialize(new GamingTaxDistributionPayload(
        facilityRegime,
        component,
        [],
        MunicipalityRequired: false,
        StateShare: stateShare,
        CountyShare: countyShare,
        MunicipalityShare: 0m,
        RegionalShare: 0m)),
    ValidationState = JurisdictionRuleValidationStates.Validated,
    EffectiveFrom = new DateOnly(2025, 1, 1),
    SourceUrl = sourceUrl,
    ProvenanceNotes = "Disposable integration impact-accounting fixture only."
};

file sealed class FrozenOriginIncomeProvider(
    ProviderDataset<OriginIncomeImportRow> dataset) : IOriginIncomeProvider
{
    public string ProviderKey => "frozen-live-irs-soi-validation";

    public Task<ProviderDataset<OriginIncomeImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}

file sealed record ProviderValidationBundle(
    ProviderDataset<CasinoCompetitorImportRow> Facilities,
    ProviderDataset<CasinoGamingRevenueImportRow> Performance);

file sealed class FrozenGamingFacilityProvider(
    ProviderDataset<CasinoCompetitorImportRow> dataset,
    string providerKey,
    string geographicCoverage) : IGamingFacilityInventoryProvider
{
    public string ProviderKey => providerKey;
    public string GeographicCoverage => geographicCoverage;

    public Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}

file sealed class FrozenGamingPerformanceProvider(
    ProviderDataset<CasinoGamingRevenueImportRow> dataset,
    string providerKey,
    string geographicCoverage) : IGamingRegulatorPerformanceProvider
{
    public string ProviderKey => providerKey;
    public string GeographicCoverage => geographicCoverage;

    public Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(dataset);
}
