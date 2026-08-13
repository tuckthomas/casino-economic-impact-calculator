// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public static class DatasetSnapshotKinds
{
    public const string OriginGeography = "origin-geography";
    public const string AgePopulation = "age-population";
    public const string Income = "income";
    public const string Competitors = "competitors";
    public const string CompetitorHistory = "competitor-history";
    public const string ObservedPerformance = "observed-performance";
    public const string Tourism = "tourism";
    public const string Traffic = "traffic";
    public const string LocalEconomicInventory = "local-economic-inventory";
}

public sealed record TourismMarketObservationImportRow(
    string StableObservationId,
    string MarketKey,
    string GeographyType,
    string GeographyCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string SourceMetricKind,
    decimal SourceQuantity,
    decimal NormalizedVisitorPersonTrips,
    string NormalizationMethod,
    string? Notes);

public sealed record TrafficCorridorObservationImportRow(
    string StableObservationId,
    string RouteDesignation,
    string JurisdictionCode,
    double Latitude,
    double Longitude,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    double AnnualAverageDailyTraffic,
    int ObservationDays,
    string CountMethod,
    string? DirectionDefinition,
    string? Notes);

public sealed record LocalEconomicSectorObservationImportRow(
    string StableObservationId,
    string GeographyType,
    string GeographyCode,
    string SectorKey,
    IReadOnlyCollection<string> NaicsCodes,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long? Establishments,
    long? Employment,
    decimal? AnnualPayroll,
    decimal? AnnualReceiptsOrSales,
    string SourceMetricDefinition,
    string? Notes);

public sealed record OriginZoneImportRow(
    string StableOriginId,
    string OriginType,
    string GeographyCode,
    string CountryCode,
    string? StateOrTerritoryCode,
    string? CountyEquivalentCode,
    string? MetropolitanStatisticalAreaCode,
    string? CombinedStatisticalAreaCode,
    double RepresentativeLatitude,
    double RepresentativeLongitude,
    string AreaWkt);

public sealed record OriginAgeBinImportRow(
    string StableOriginId,
    int ObservationYear,
    int MinimumAge,
    int? MaximumAge,
    long Population,
    string ControlValidationState);

public sealed record OriginAgeBinImportRequest(
    Guid OriginGeographySnapshotId,
    IReadOnlyCollection<OriginAgeBinImportRow> Rows);

public sealed record OriginIncomeImportRow(
    string StableOriginId,
    int TaxYear,
    long? ReturnCount,
    decimal? AdjustedGrossIncome,
    decimal? InflationAdjustedAdjustedGrossIncome,
    decimal? MedianHouseholdIncome,
    int? DollarYear,
    string? Notes);

public sealed record OriginIncomeImportRequest(
    Guid OriginGeographySnapshotId,
    IReadOnlyCollection<OriginIncomeImportRow> Rows);

public sealed record CasinoCompetitorImportRow(
    string StableVenueId,
    string Name,
    string State,
    string CountryCode,
    string VenueType,
    string? FacilityRegime,
    string? RegulatoryStatus,
    int? JurisdictionId,
    string? RegulatorName,
    string? RegulatorLicenseId,
    string? TribalNationName,
    DateOnly? OpenedOn,
    DateOnly? ClosedOn,
    string? County,
    string? City,
    double Latitude,
    double Longitude,
    bool IsActive,
    string? OperatorName,
    string? SourceUrl,
    DateTime? LastVerifiedAt,
    bool? HasSlots,
    bool? HasTableGames,
    bool? HasPoker,
    bool? HasSportsbook,
    bool? HasRacetrack,
    bool? HasHotel,
    bool? HasRestaurants,
    bool? HasEntertainment,
    bool? HasLoyaltyProgram,
    bool? HasResortAmenities,
    int? GamingPositions,
    int? SlotOrVltPositions,
    int? TableGameCount,
    int? PokerTableCount,
    int? GamingFloorSquareFeet,
    int? HotelRoomCount,
    int? EventCapacity,
    int? FoodBeverageVenueCount,
    decimal? DevelopmentCost,
    int? DevelopmentCostDollarYear,
    string? AccessContext,
    double? LimitedAccessDistanceMiles,
    bool? HasInterchangeAccess,
    string? MarketOrientation,
    bool? IsBorderMarket,
    string? Notes);

public sealed record CasinoCompetitorHistoryImportRow(
    string StableVenueId,
    string EventType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? OperatorName,
    string? Notes);

public sealed record CasinoCompetitorHistoryImportRequest(
    Guid CompetitorSnapshotId,
    IReadOnlyCollection<CasinoCompetitorHistoryImportRow> Rows);

public sealed record CasinoGamingRevenueImportRow(
    string StableVenueId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PeriodGranularity,
    string ReportedMetricKey,
    string ReportedMetricDefinition,
    decimal ReportedAmount,
    decimal? InflationAdjustedAmount,
    int? InflationAdjustmentDollarYear,
    IReadOnlyCollection<string>? AnomalyFlags,
    string? Notes,
    double? ReportedUnitCount = null);

public sealed record CasinoGamingRevenueImportRequest(
    Guid CompetitorSnapshotId,
    IReadOnlyCollection<CasinoGamingRevenueImportRow> Rows);

public sealed record SnapshotRowCounts(
    long OriginZones,
    long AgeBins,
    long IncomePeriods,
    long Competitors,
    long CompetitorHistory,
    long GamingRevenuePeriods,
    long TourismObservations,
    long TrafficObservations,
    long LocalEconomicSectorObservations)
{
    public long Total => OriginZones + AgeBins + IncomePeriods + Competitors + CompetitorHistory +
                         GamingRevenuePeriods + TourismObservations + TrafficObservations +
                         LocalEconomicSectorObservations;
}

public interface IModelDataIngestionService
{
    Task<int> AppendOriginsAsync(Guid snapshotId, IReadOnlyCollection<OriginZoneImportRow> rows, CancellationToken cancellationToken = default);
    Task<int> AppendAgeBinsAsync(Guid snapshotId, OriginAgeBinImportRequest request, CancellationToken cancellationToken = default);
    Task<int> AppendIncomeAsync(Guid snapshotId, OriginIncomeImportRequest request, CancellationToken cancellationToken = default);
    Task<int> AppendCompetitorsAsync(Guid snapshotId, IReadOnlyCollection<CasinoCompetitorImportRow> rows, CancellationToken cancellationToken = default);
    Task<int> AppendCompetitorHistoryAsync(Guid snapshotId, CasinoCompetitorHistoryImportRequest request, CancellationToken cancellationToken = default);
    Task<int> AppendGamingRevenueAsync(Guid snapshotId, CasinoGamingRevenueImportRequest request, CancellationToken cancellationToken = default);
    Task<int> AppendTourismObservationsAsync(Guid snapshotId, IReadOnlyCollection<TourismMarketObservationImportRow> rows, CancellationToken cancellationToken = default);
    Task<int> AppendTrafficObservationsAsync(Guid snapshotId, IReadOnlyCollection<TrafficCorridorObservationImportRow> rows, CancellationToken cancellationToken = default);
    Task<int> AppendLocalEconomicSectorObservationsAsync(Guid snapshotId, IReadOnlyCollection<LocalEconomicSectorObservationImportRow> rows, CancellationToken cancellationToken = default);
    Task<SnapshotRowCounts> GetRowCountsAsync(Guid snapshotId, CancellationToken cancellationToken = default);
}

public sealed class ModelDataIngestionService(AppDbContext db) : IModelDataIngestionService
{
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    public async Task<int> AppendOriginsAsync(
        Guid snapshotId,
        IReadOnlyCollection<OriginZoneImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.OriginGeography, cancellationToken);
        RequireBatch(rows);
        RequireDistinct(rows.Select(row => row.StableOriginId), "stable origin IDs");

        var requestedIds = rows.Select(row => Required(row.StableOriginId, nameof(row.StableOriginId))).ToArray();
        if (await db.OriginZones.AnyAsync(
                origin => origin.DatasetSnapshotId == snapshotId && requestedIds.Contains(origin.StableOriginId),
                cancellationToken))
        {
            throw new InvalidOperationException("The batch contains a stable origin ID already loaded into this snapshot.");
        }

        var entities = rows.Select(row =>
        {
            ValidateCoordinate(row.RepresentativeLatitude, row.RepresentativeLongitude);
            var area = ParseArea(row.AreaWkt);
            var point = Wgs84.CreatePoint(new Coordinate(row.RepresentativeLongitude, row.RepresentativeLatitude));
            if (!area.Covers(point))
            {
                throw new InvalidOperationException(
                    $"Representative point for origin '{row.StableOriginId}' is outside its area geometry.");
            }
            var countryCode = Required(row.CountryCode, nameof(row.CountryCode)).ToUpperInvariant();
            if (countryCode.Length != 3)
            {
                throw new ArgumentException("Country codes must be ISO alpha-3 values.", nameof(rows));
            }
            return new OriginZone
            {
                DatasetSnapshotId = snapshotId,
                StableOriginId = Required(row.StableOriginId, nameof(row.StableOriginId)),
                OriginType = Required(row.OriginType, nameof(row.OriginType)).ToLowerInvariant(),
                GeographyCode = Required(row.GeographyCode, nameof(row.GeographyCode)),
                CountryCode = countryCode,
                StateOrTerritoryCode = TrimOrNull(row.StateOrTerritoryCode),
                CountyEquivalentCode = TrimOrNull(row.CountyEquivalentCode),
                MetropolitanStatisticalAreaCode = TrimOrNull(row.MetropolitanStatisticalAreaCode),
                CombinedStatisticalAreaCode = TrimOrNull(row.CombinedStatisticalAreaCode),
                RepresentativePoint = point,
                AreaGeometry = area
            };
        }).ToArray();

        db.OriginZones.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendAgeBinsAsync(
        Guid snapshotId,
        OriginAgeBinImportRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.AgePopulation, cancellationToken);
        await RequireReadableSnapshotAsync(request.OriginGeographySnapshotId, DatasetSnapshotKinds.OriginGeography, cancellationToken);
        RequireBatch(request.Rows);
        var origins = await ResolveOriginsAsync(request.OriginGeographySnapshotId, request.Rows.Select(row => row.StableOriginId), cancellationToken);

        foreach (var group in request.Rows.GroupBy(row => (row.StableOriginId, row.ObservationYear)))
        {
            if (group.Key.ObservationYear is < 1900 or > 2200)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Observation years must be between 1900 and 2200.");
            }
            var bins = group.Select(row => new AgeBinValue(row.MinimumAge, row.MaximumAge, row.Population)).ToArray();
            if (bins.Min(bin => bin.MinimumAge) != 0)
            {
                throw new InvalidOperationException($"Age bins for '{group.Key.StableOriginId}' must begin at age zero.");
            }
            _ = EligiblePopulationCalculator.Calculate(bins, 21);
        }
        RequireDistinct(
            request.Rows.Select(row => $"{row.StableOriginId}\u001f{row.ObservationYear}\u001f{row.MinimumAge}"),
            "origin/year/minimum-age keys");

        var entities = request.Rows.Select(row => new OriginZoneAgeBin
        {
            OriginZoneId = origins[Required(row.StableOriginId, nameof(row.StableOriginId))].Id,
            DatasetSnapshotId = snapshotId,
            ObservationYear = row.ObservationYear,
            MinimumAge = row.MinimumAge,
            MaximumAge = row.MaximumAge,
            Population = row.Population,
            InterpolationMethod = AgeBinInterpolationMethods.None,
            ControlValidationState = ValidateControlState(row.ControlValidationState)
        }).ToArray();
        db.OriginZoneAgeBins.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendIncomeAsync(
        Guid snapshotId,
        OriginIncomeImportRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.Income, cancellationToken);
        await RequireReadableSnapshotAsync(request.OriginGeographySnapshotId, DatasetSnapshotKinds.OriginGeography, cancellationToken);
        RequireBatch(request.Rows);
        RequireDistinct(
            request.Rows.Select(row => $"{row.StableOriginId}\u001f{row.TaxYear}"),
            "origin/tax-year keys");
        var origins = await ResolveOriginsAsync(request.OriginGeographySnapshotId, request.Rows.Select(row => row.StableOriginId), cancellationToken);

        var entities = request.Rows.Select(row =>
        {
            if (row.TaxYear is < 1900 or > 2200 || row.DollarYear is < 1900 or > 2200)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Income years must be between 1900 and 2200.");
            }
            if (row.ReturnCount is < 0 || row.AdjustedGrossIncome is < 0 ||
                row.InflationAdjustedAdjustedGrossIncome is < 0 || row.MedianHouseholdIncome is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Income measures cannot be negative.");
            }
            if (row.AdjustedGrossIncome is null && row.InflationAdjustedAdjustedGrossIncome is null &&
                row.MedianHouseholdIncome is null)
            {
                throw new InvalidOperationException(
                    $"Income row '{row.StableOriginId}/{row.TaxYear}' contains no income measure.");
            }
            return new OriginZoneIncomePeriod
            {
                OriginZoneId = origins[Required(row.StableOriginId, nameof(row.StableOriginId))].Id,
                DatasetSnapshotId = snapshotId,
                TaxYear = row.TaxYear,
                ReturnCount = row.ReturnCount,
                AdjustedGrossIncome = row.AdjustedGrossIncome,
                InflationAdjustedAdjustedGrossIncome = row.InflationAdjustedAdjustedGrossIncome,
                MedianHouseholdIncome = row.MedianHouseholdIncome,
                DollarYear = row.DollarYear,
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.OriginZoneIncomePeriods.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendCompetitorsAsync(
        Guid snapshotId,
        IReadOnlyCollection<CasinoCompetitorImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.Competitors, cancellationToken);
        RequireBatch(rows);
        RequireDistinct(rows.Select(row => row.StableVenueId), "stable venue IDs");
        var stableIds = rows.Select(row => Required(row.StableVenueId, nameof(row.StableVenueId))).ToArray();
        if (await db.CasinoCompetitors.AnyAsync(
                competitor => competitor.DatasetSnapshotId == snapshotId && stableIds.Contains(competitor.StableVenueId),
                cancellationToken))
        {
            throw new InvalidOperationException("The batch contains a stable venue ID already loaded into this snapshot.");
        }

        var entities = rows.Select(row =>
        {
            ValidateCoordinate(row.Latitude, row.Longitude);
            ValidateNonnegativeFacilityFacts(row);
            if (row.ClosedOn is { } closed && row.OpenedOn is { } opened && closed < opened)
            {
                throw new InvalidOperationException($"Venue '{row.StableVenueId}' closes before it opens.");
            }
            var countryCode = Required(row.CountryCode, nameof(row.CountryCode)).ToUpperInvariant();
            if (countryCode.Length != 3)
            {
                throw new ArgumentException("Country codes must be ISO alpha-3 values.", nameof(rows));
            }
            return new CasinoCompetitor
            {
                DatasetSnapshotId = snapshotId,
                StableVenueId = Required(row.StableVenueId, nameof(row.StableVenueId)),
                Name = Required(row.Name, nameof(row.Name)),
                State = Required(row.State, nameof(row.State)).ToUpperInvariant(),
                CountryCode = countryCode,
                VenueType = Required(row.VenueType, nameof(row.VenueType)).ToLowerInvariant(),
                FacilityRegime = TrimOrNull(row.FacilityRegime),
                RegulatoryStatus = TrimOrNull(row.RegulatoryStatus),
                JurisdictionId = row.JurisdictionId,
                RegulatorName = TrimOrNull(row.RegulatorName),
                RegulatorLicenseId = TrimOrNull(row.RegulatorLicenseId),
                TribalNationName = TrimOrNull(row.TribalNationName),
                OpenedOn = row.OpenedOn,
                ClosedOn = row.ClosedOn,
                County = TrimOrNull(row.County),
                City = TrimOrNull(row.City),
                Latitude = row.Latitude,
                Longitude = row.Longitude,
                IsActive = row.IsActive,
                OperatorName = TrimOrNull(row.OperatorName),
                SourceUrl = TrimOrNull(row.SourceUrl),
                LastVerifiedAt = row.LastVerifiedAt,
                HasSlots = row.HasSlots,
                HasTableGames = row.HasTableGames,
                HasPoker = row.HasPoker,
                HasSportsbook = row.HasSportsbook,
                HasRacetrack = row.HasRacetrack,
                HasHotel = row.HasHotel,
                HasRestaurants = row.HasRestaurants,
                HasEntertainment = row.HasEntertainment,
                HasLoyaltyProgram = row.HasLoyaltyProgram,
                HasResortAmenities = row.HasResortAmenities,
                GamingPositions = row.GamingPositions,
                SlotOrVltPositions = row.SlotOrVltPositions,
                TableGameCount = row.TableGameCount,
                PokerTableCount = row.PokerTableCount,
                GamingFloorSquareFeet = row.GamingFloorSquareFeet,
                HotelRoomCount = row.HotelRoomCount,
                EventCapacity = row.EventCapacity,
                FoodBeverageVenueCount = row.FoodBeverageVenueCount,
                DevelopmentCost = row.DevelopmentCost,
                DevelopmentCostDollarYear = row.DevelopmentCostDollarYear,
                AccessContext = TrimOrNull(row.AccessContext),
                LimitedAccessDistanceMiles = row.LimitedAccessDistanceMiles,
                HasInterchangeAccess = row.HasInterchangeAccess,
                MarketOrientation = TrimOrNull(row.MarketOrientation),
                IsBorderMarket = row.IsBorderMarket,
                Notes = TrimOrNull(row.Notes),
                Geom = Wgs84.CreatePoint(new Coordinate(row.Longitude, row.Latitude))
            };
        }).ToArray();
        db.CasinoCompetitors.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendCompetitorHistoryAsync(
        Guid snapshotId,
        CasinoCompetitorHistoryImportRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.CompetitorHistory, cancellationToken);
        await RequireReadableSnapshotAsync(request.CompetitorSnapshotId, DatasetSnapshotKinds.Competitors, cancellationToken);
        RequireBatch(request.Rows);
        var competitors = await ResolveCompetitorsAsync(request.CompetitorSnapshotId, request.Rows.Select(row => row.StableVenueId), cancellationToken);
        var entities = request.Rows.Select(row =>
        {
            if (row.EffectiveTo is { } end && end < row.EffectiveFrom)
            {
                throw new InvalidOperationException($"History period for '{row.StableVenueId}' ends before it begins.");
            }
            return new CasinoCompetitorHistory
            {
                CasinoCompetitorId = competitors[Required(row.StableVenueId, nameof(row.StableVenueId))].Id,
                DatasetSnapshotId = snapshotId,
                EventType = Required(row.EventType, nameof(row.EventType)).ToLowerInvariant(),
                EffectiveFrom = row.EffectiveFrom,
                EffectiveTo = row.EffectiveTo,
                OperatorName = TrimOrNull(row.OperatorName),
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.CasinoCompetitorHistory.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendGamingRevenueAsync(
        Guid snapshotId,
        CasinoGamingRevenueImportRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.ObservedPerformance, cancellationToken);
        await RequireReadableSnapshotAsync(request.CompetitorSnapshotId, DatasetSnapshotKinds.Competitors, cancellationToken);
        RequireBatch(request.Rows);
        RequireDistinct(
            request.Rows.Select(row => $"{row.StableVenueId}\u001f{row.PeriodStart:O}\u001f{row.PeriodEnd:O}\u001f{row.ReportedMetricKey}"),
            "venue/period/metric keys");
        var competitors = await ResolveCompetitorsAsync(request.CompetitorSnapshotId, request.Rows.Select(row => row.StableVenueId), cancellationToken);
        var entities = request.Rows.Select(row =>
        {
            if (row.PeriodEnd < row.PeriodStart)
            {
                throw new InvalidOperationException($"Revenue period for '{row.StableVenueId}' ends before it begins.");
            }
            if (row.ReportedAmount < 0 || row.InflationAdjustedAmount is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Reported gaming revenue cannot be negative.");
            }
            if (row.ReportedUnitCount is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "A reported gaming unit count must be positive when supplied.");
            }
            return new CasinoGamingRevenuePeriod
            {
                CasinoCompetitorId = competitors[Required(row.StableVenueId, nameof(row.StableVenueId))].Id,
                DatasetSnapshotId = snapshotId,
                PeriodStart = row.PeriodStart,
                PeriodEnd = row.PeriodEnd,
                PeriodGranularity = Required(row.PeriodGranularity, nameof(row.PeriodGranularity)).ToLowerInvariant(),
                ReportedMetricKey = Required(row.ReportedMetricKey, nameof(row.ReportedMetricKey)).ToLowerInvariant(),
                ReportedMetricDefinition = Required(row.ReportedMetricDefinition, nameof(row.ReportedMetricDefinition)),
                ReportedAmount = row.ReportedAmount,
                InflationAdjustedAmount = row.InflationAdjustedAmount,
                InflationAdjustmentDollarYear = row.InflationAdjustmentDollarYear,
                ReportedUnitCount = row.ReportedUnitCount,
                AnomalyFlagsJson = JsonSerializer.Serialize(row.AnomalyFlags ?? []),
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.CasinoGamingRevenuePeriods.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendTourismObservationsAsync(
        Guid snapshotId,
        IReadOnlyCollection<TourismMarketObservationImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.Tourism, cancellationToken);
        RequireBatch(rows);
        RequireDistinct(rows.Select(row => row.StableObservationId), "stable tourism observation IDs");
        var stableIds = rows.Select(row => Required(row.StableObservationId, nameof(row.StableObservationId))).ToArray();
        if (await db.TourismMarketObservations.AnyAsync(
                observation => observation.DatasetSnapshotId == snapshotId &&
                               stableIds.Contains(observation.StableObservationId),
                cancellationToken))
        {
            throw new InvalidOperationException("The batch contains a tourism observation already loaded into this snapshot.");
        }

        var entities = rows.Select(row =>
        {
            if (row.PeriodEnd < row.PeriodStart)
            {
                throw new InvalidOperationException($"Tourism observation '{row.StableObservationId}' ends before it begins.");
            }
            if (row.SourceQuantity < 0 || row.NormalizedVisitorPersonTrips < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Tourism quantities cannot be negative.");
            }
            return new TourismMarketObservation
            {
                DatasetSnapshotId = snapshotId,
                StableObservationId = Required(row.StableObservationId, nameof(row.StableObservationId)),
                MarketKey = Required(row.MarketKey, nameof(row.MarketKey)),
                GeographyType = Required(row.GeographyType, nameof(row.GeographyType)).ToLowerInvariant(),
                GeographyCode = Required(row.GeographyCode, nameof(row.GeographyCode)),
                PeriodStart = row.PeriodStart,
                PeriodEnd = row.PeriodEnd,
                SourceMetricKind = Required(row.SourceMetricKind, nameof(row.SourceMetricKind)).ToLowerInvariant(),
                SourceQuantity = row.SourceQuantity,
                NormalizedVisitorPersonTrips = row.NormalizedVisitorPersonTrips,
                NormalizationMethod = Required(row.NormalizationMethod, nameof(row.NormalizationMethod)),
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.TourismMarketObservations.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendTrafficObservationsAsync(
        Guid snapshotId,
        IReadOnlyCollection<TrafficCorridorObservationImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.Traffic, cancellationToken);
        RequireBatch(rows);
        RequireDistinct(rows.Select(row => row.StableObservationId), "stable traffic observation IDs");
        var stableIds = rows.Select(row => Required(row.StableObservationId, nameof(row.StableObservationId))).ToArray();
        if (await db.TrafficCorridorObservations.AnyAsync(
                observation => observation.DatasetSnapshotId == snapshotId &&
                               stableIds.Contains(observation.StableObservationId),
                cancellationToken))
        {
            throw new InvalidOperationException("The batch contains a traffic observation already loaded into this snapshot.");
        }

        var entities = rows.Select(row =>
        {
            ValidateCoordinate(row.Latitude, row.Longitude);
            if (row.PeriodEnd < row.PeriodStart)
            {
                throw new InvalidOperationException($"Traffic observation '{row.StableObservationId}' ends before it begins.");
            }
            if (!double.IsFinite(row.AnnualAverageDailyTraffic) || row.AnnualAverageDailyTraffic < 0 ||
                row.ObservationDays is < 1 or > 366)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Traffic counts must be nonnegative and observation days must be between 1 and 366.");
            }
            return new TrafficCorridorObservation
            {
                DatasetSnapshotId = snapshotId,
                StableObservationId = Required(row.StableObservationId, nameof(row.StableObservationId)),
                RouteDesignation = Required(row.RouteDesignation, nameof(row.RouteDesignation)),
                JurisdictionCode = Required(row.JurisdictionCode, nameof(row.JurisdictionCode)).ToUpperInvariant(),
                CountLocation = Wgs84.CreatePoint(new Coordinate(row.Longitude, row.Latitude)),
                PeriodStart = row.PeriodStart,
                PeriodEnd = row.PeriodEnd,
                AnnualAverageDailyTraffic = row.AnnualAverageDailyTraffic,
                ObservationDays = row.ObservationDays,
                CountMethod = Required(row.CountMethod, nameof(row.CountMethod)),
                DirectionDefinition = TrimOrNull(row.DirectionDefinition),
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.TrafficCorridorObservations.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<int> AppendLocalEconomicSectorObservationsAsync(
        Guid snapshotId,
        IReadOnlyCollection<LocalEconomicSectorObservationImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        await RequireOpenSnapshotAsync(snapshotId, DatasetSnapshotKinds.LocalEconomicInventory, cancellationToken);
        RequireBatch(rows);
        RequireDistinct(rows.Select(row => row.StableObservationId), "stable local-economic observation IDs");
        var stableIds = rows.Select(row => Required(row.StableObservationId, nameof(row.StableObservationId))).ToArray();
        if (await db.LocalEconomicSectorObservations.AnyAsync(
                observation => observation.DatasetSnapshotId == snapshotId &&
                               stableIds.Contains(observation.StableObservationId),
                cancellationToken))
        {
            throw new InvalidOperationException("The batch contains a local-economic observation already loaded into this snapshot.");
        }

        var entities = rows.Select(row =>
        {
            if (row.PeriodEnd < row.PeriodStart)
            {
                throw new InvalidOperationException($"Local-economic observation '{row.StableObservationId}' ends before it begins.");
            }
            if (row.Establishments is < 0 || row.Employment is < 0 || row.AnnualPayroll is < 0 ||
                row.AnnualReceiptsOrSales is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Local-economic quantities cannot be negative.");
            }
            if (row.Establishments is null && row.Employment is null && row.AnnualPayroll is null &&
                row.AnnualReceiptsOrSales is null)
            {
                throw new InvalidOperationException(
                    $"Local-economic observation '{row.StableObservationId}' contains no inventory measure.");
            }
            var naicsCodes = row.NaicsCodes
                .Select(code => Required(code, nameof(row.NaicsCodes)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (naicsCodes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Local-economic observation '{row.StableObservationId}' must identify at least one NAICS code.");
            }

            return new LocalEconomicSectorObservation
            {
                DatasetSnapshotId = snapshotId,
                StableObservationId = Required(row.StableObservationId, nameof(row.StableObservationId)),
                GeographyType = Required(row.GeographyType, nameof(row.GeographyType)).ToLowerInvariant(),
                GeographyCode = Required(row.GeographyCode, nameof(row.GeographyCode)).ToUpperInvariant(),
                SectorKey = Required(row.SectorKey, nameof(row.SectorKey)).ToLowerInvariant(),
                NaicsCodesJson = JsonSerializer.Serialize(naicsCodes),
                PeriodStart = row.PeriodStart,
                PeriodEnd = row.PeriodEnd,
                Establishments = row.Establishments,
                Employment = row.Employment,
                AnnualPayroll = row.AnnualPayroll,
                AnnualReceiptsOrSales = row.AnnualReceiptsOrSales,
                SourceMetricDefinition = Required(row.SourceMetricDefinition, nameof(row.SourceMetricDefinition)),
                Notes = TrimOrNull(row.Notes)
            };
        }).ToArray();
        db.LocalEconomicSectorObservations.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        return entities.Length;
    }

    public async Task<SnapshotRowCounts> GetRowCountsAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        if (!await db.DatasetSnapshots.AnyAsync(snapshot => snapshot.Id == snapshotId, cancellationToken))
        {
            throw new KeyNotFoundException($"Dataset snapshot '{snapshotId}' was not found.");
        }
        return new SnapshotRowCounts(
            await db.OriginZones.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.OriginZoneAgeBins.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.OriginZoneIncomePeriods.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.CasinoCompetitors.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.CasinoCompetitorHistory.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.CasinoGamingRevenuePeriods.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.TourismMarketObservations.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.TrafficCorridorObservations.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken),
            await db.LocalEconomicSectorObservations.LongCountAsync(row => row.DatasetSnapshotId == snapshotId, cancellationToken));
    }

    private async Task<DatasetSnapshot> RequireOpenSnapshotAsync(
        Guid snapshotId,
        string expectedKind,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.DatasetSnapshots.SingleOrDefaultAsync(item => item.Id == snapshotId, cancellationToken)
            ?? throw new KeyNotFoundException($"Dataset snapshot '{snapshotId}' was not found.");
        if (snapshot.IsSealed)
        {
            throw new InvalidOperationException($"Dataset snapshot '{snapshotId}' is sealed and cannot accept rows.");
        }
        if (!string.Equals(snapshot.DatasetKey, expectedKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot '{snapshotId}' is '{snapshot.DatasetKey}', not the required '{expectedKind}' dataset kind.");
        }
        return snapshot;
    }

    private async Task RequireReadableSnapshotAsync(
        Guid snapshotId,
        string expectedKind,
        CancellationToken cancellationToken)
    {
        if (!await db.DatasetSnapshots.AnyAsync(
                snapshot => snapshot.Id == snapshotId && snapshot.DatasetKey == expectedKind && snapshot.IsSealed &&
                            snapshot.ValidationState != DatasetValidationStates.Pending &&
                            snapshot.ValidationState != DatasetValidationStates.Rejected,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Referenced '{expectedKind}' snapshot '{snapshotId}' must be sealed and usable.");
        }
    }

    private async Task<Dictionary<string, OriginZone>> ResolveOriginsAsync(
        Guid originSnapshotId,
        IEnumerable<string> stableOriginIds,
        CancellationToken cancellationToken)
    {
        var ids = stableOriginIds.Select(id => Required(id, nameof(stableOriginIds))).Distinct(StringComparer.Ordinal).ToArray();
        var origins = await db.OriginZones
            .Where(origin => origin.DatasetSnapshotId == originSnapshotId && ids.Contains(origin.StableOriginId))
            .ToDictionaryAsync(origin => origin.StableOriginId, StringComparer.Ordinal, cancellationToken);
        var missing = ids.Where(id => !origins.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException($"Origin snapshot is missing stable IDs: {string.Join(", ", missing)}.");
        }
        return origins;
    }

    private async Task<Dictionary<string, CasinoCompetitor>> ResolveCompetitorsAsync(
        Guid competitorSnapshotId,
        IEnumerable<string> stableVenueIds,
        CancellationToken cancellationToken)
    {
        var ids = stableVenueIds.Select(id => Required(id, nameof(stableVenueIds))).Distinct(StringComparer.Ordinal).ToArray();
        var competitors = await db.CasinoCompetitors
            .Where(competitor => competitor.DatasetSnapshotId == competitorSnapshotId && ids.Contains(competitor.StableVenueId))
            .ToDictionaryAsync(competitor => competitor.StableVenueId, StringComparer.Ordinal, cancellationToken);
        var missing = ids.Where(id => !competitors.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException($"Competitor snapshot is missing stable IDs: {string.Join(", ", missing)}.");
        }
        return competitors;
    }

    private static Geometry ParseArea(string wkt)
    {
        var area = new WKTReader(NtsGeometryServices.Instance).Read(Required(wkt, nameof(wkt)));
        if (area is not Polygon and not MultiPolygon || area.IsEmpty || !area.IsValid)
        {
            throw new InvalidOperationException("Origin area WKT must be a valid, non-empty Polygon or MultiPolygon.");
        }
        area.SRID = 4326;
        return area;
    }

    private static void ValidateCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Coordinates must be finite WGS84 latitude/longitude values.");
        }
    }

    private static void ValidateNonnegativeFacilityFacts(CasinoCompetitorImportRow row)
    {
        var counts = new int?[]
        {
            row.GamingPositions, row.SlotOrVltPositions, row.TableGameCount, row.PokerTableCount,
            row.GamingFloorSquareFeet, row.HotelRoomCount, row.EventCapacity, row.FoodBeverageVenueCount
        };
        if (counts.Any(value => value is < 0) || row.DevelopmentCost is < 0 || row.LimitedAccessDistanceMiles is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Venue capacities, cost, and access distance cannot be negative.");
        }
    }

    private static string ValidateControlState(string value)
    {
        var state = Required(value, nameof(value)).ToLowerInvariant();
        if (state is not (DatasetValidationStates.Validated or DatasetValidationStates.Warning))
        {
            throw new InvalidOperationException("Age-bin control state must be validated or warning.");
        }
        return state;
    }

    private static void RequireBatch<T>(IReadOnlyCollection<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new ArgumentException("At least one row is required.", nameof(rows));
        }
        if (rows.Count > 50_000)
        {
            throw new ArgumentException("A single ingestion batch cannot exceed 50,000 rows.", nameof(rows));
        }
    }

    private static void RequireDistinct(IEnumerable<string> values, string description)
    {
        var normalized = values.Select(value => Required(value, description)).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new InvalidOperationException($"The ingestion batch contains duplicate {description}.");
        }
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
        return value.Trim();
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
