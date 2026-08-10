// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Valhalla;

namespace SaveNEIN.Server.Services.Gravity;

public sealed record TravelMatrixOrigin(
    long OriginZoneId,
    string StableOriginId,
    double Latitude,
    double Longitude);

public sealed record TravelMatrixFacility(
    string FacilityKey,
    string FacilityKind,
    int? CasinoCompetitorId,
    Guid? ModelRunId,
    double Latitude,
    double Longitude);

public sealed record TravelMatrixResolution(
    string RoutingGraphHash,
    string ValhallaVersion,
    long? TilesetLastModified,
    string CostingProfile,
    IReadOnlyList<OriginFacilityTravel> Routes);

public interface ITravelMatrixService
{
    Task<TravelMatrixResolution> ResolveAsync(
        IReadOnlyCollection<TravelMatrixOrigin> origins,
        IReadOnlyCollection<TravelMatrixFacility> facilities,
        string costingProfile = "auto",
        CancellationToken cancellationToken = default);
}

public sealed class TravelMatrixService(
    AppDbContext db,
    ValhallaClient valhallaClient) : ITravelMatrixService
{
    // The deployed Valhalla contract allows at most 20 total matrix locations.
    // Ten-by-ten batches stay within that limit for every request shape.
    private const int SourceBatchSize = 10;
    private const int TargetBatchSize = 10;

    public async Task<TravelMatrixResolution> ResolveAsync(
        IReadOnlyCollection<TravelMatrixOrigin> origins,
        IReadOnlyCollection<TravelMatrixFacility> facilities,
        string costingProfile = "auto",
        CancellationToken cancellationToken = default)
    {
        Validate(origins, facilities, costingProfile);
        var orderedOrigins = origins.OrderBy(origin => origin.OriginZoneId).ToArray();
        var orderedFacilities = facilities
            .OrderBy(facility => facility.FacilityKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var graph = await valhallaClient.GetRoutingGraphIdentityAsync(cancellationToken);
        var originIds = orderedOrigins.Select(origin => origin.OriginZoneId).ToArray();
        var facilityKeys = orderedFacilities.Select(facility => facility.FacilityKey).ToArray();
        var cached = await db.OriginFacilityTravel
            .Where(route => originIds.Contains(route.OriginZoneId) &&
                            facilityKeys.Contains(route.FacilityKey) &&
                            route.RoutingGraphHash == graph.GraphHash &&
                            route.CostingProfile == costingProfile)
            .ToListAsync(cancellationToken);
        var routeByKey = cached.ToDictionary(
            route => (route.OriginZoneId, route.FacilityKey),
            OriginFacilityTravelKeyComparer.Instance);

        foreach (var originBatch in orderedOrigins.Chunk(SourceBatchSize))
        {
            foreach (var facilityBatch in orderedFacilities.Chunk(TargetBatchSize))
            {
                var missingPairs = originBatch
                    .SelectMany(origin => facilityBatch.Select(facility => (origin, facility)))
                    .Where(pair => !routeByKey.ContainsKey((pair.origin.OriginZoneId, pair.facility.FacilityKey)))
                    .ToArray();
                if (missingPairs.Length == 0)
                {
                    continue;
                }

                var matrix = await valhallaClient.GetDriveTimeMatrixAsync(
                    originBatch
                        .Select(origin => new ValhallaMatrixLocation(origin.Latitude, origin.Longitude))
                        .ToArray(),
                    facilityBatch
                        .Select(facility => new ValhallaMatrixLocation(facility.Latitude, facility.Longitude))
                        .ToArray(),
                    costingProfile,
                    cancellationToken);
                foreach (var cell in matrix.Cells)
                {
                    var origin = originBatch[cell.SourceIndex];
                    var facility = facilityBatch[cell.TargetIndex];
                    var key = (origin.OriginZoneId, facility.FacilityKey);
                    if (routeByKey.ContainsKey(key))
                    {
                        continue;
                    }

                    var route = new OriginFacilityTravel
                    {
                        OriginZoneId = origin.OriginZoneId,
                        CasinoCompetitorId = facility.CasinoCompetitorId,
                        ModelRunId = facility.ModelRunId,
                        FacilityKey = facility.FacilityKey,
                        FacilityKind = facility.FacilityKind,
                        RoutingGraphHash = graph.GraphHash,
                        CostingProfile = costingProfile,
                        TravelTimeMinutes = cell.TravelTimeMinutes,
                        RoutedDistanceMeters = cell.RoutedDistanceMeters,
                        RouteFound = cell.RouteFound,
                        RouteFailureReason = cell.RouteFound ? null : "Valhalla returned the origin-facility pair as unreachable.",
                        CalculatedAtUtc = DateTime.UtcNow
                    };
                    db.OriginFacilityTravel.Add(route);
                    routeByKey.Add(key, route);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var resolvedRoutes = orderedOrigins
            .SelectMany(origin => orderedFacilities.Select(facility =>
                routeByKey[(origin.OriginZoneId, facility.FacilityKey)]))
            .ToArray();
        return new TravelMatrixResolution(
            graph.GraphHash,
            graph.ValhallaVersion,
            graph.TilesetLastModified,
            costingProfile,
            resolvedRoutes);
    }

    private static void Validate(
        IReadOnlyCollection<TravelMatrixOrigin> origins,
        IReadOnlyCollection<TravelMatrixFacility> facilities,
        string costingProfile)
    {
        if (origins.Count == 0)
        {
            throw new ArgumentException("At least one relevant origin is required.", nameof(origins));
        }
        if (facilities.Count == 0)
        {
            throw new ArgumentException("At least one relevant facility is required.", nameof(facilities));
        }
        if (string.IsNullOrWhiteSpace(costingProfile))
        {
            throw new ArgumentException("A costing profile is required.", nameof(costingProfile));
        }
        if (origins.Select(origin => origin.OriginZoneId).Distinct().Count() != origins.Count)
        {
            throw new ArgumentException("Origin-zone IDs must be unique.", nameof(origins));
        }
        if (facilities.Select(facility => facility.FacilityKey)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != facilities.Count)
        {
            throw new ArgumentException("Facility keys must be unique.", nameof(facilities));
        }

        foreach (var origin in origins)
        {
            if (origin.OriginZoneId <= 0 || string.IsNullOrWhiteSpace(origin.StableOriginId))
            {
                throw new ArgumentException("Every origin requires a persisted ID and stable key.", nameof(origins));
            }
            ValidateCoordinates(origin.Latitude, origin.Longitude, nameof(origins));
        }
        foreach (var facility in facilities)
        {
            if (string.IsNullOrWhiteSpace(facility.FacilityKey) ||
                facility.FacilityKind is not (FacilityKinds.Incumbent or FacilityKinds.Scenario))
            {
                throw new ArgumentException("Every facility requires a stable key and supported kind.", nameof(facilities));
            }
            if (facility.FacilityKind == FacilityKinds.Incumbent && facility.CasinoCompetitorId is null)
            {
                throw new ArgumentException("An incumbent route must identify its persisted competitor.", nameof(facilities));
            }
            if (facility.FacilityKind == FacilityKinds.Scenario && facility.ModelRunId is null)
            {
                throw new ArgumentException("A scenario route must identify its model run.", nameof(facilities));
            }
            ValidateCoordinates(facility.Latitude, facility.Longitude, nameof(facilities));
        }
    }

    private static void ValidateCoordinates(double latitude, double longitude, string parameterName)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Coordinates must be valid WGS84 coordinates.");
        }
    }

    private sealed class OriginFacilityTravelKeyComparer : IEqualityComparer<(long OriginZoneId, string FacilityKey)>
    {
        public static OriginFacilityTravelKeyComparer Instance { get; } = new();

        public bool Equals(
            (long OriginZoneId, string FacilityKey) x,
            (long OriginZoneId, string FacilityKey) y) =>
            x.OriginZoneId == y.OriginZoneId &&
            string.Equals(x.FacilityKey, y.FacilityKey, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((long OriginZoneId, string FacilityKey) obj) =>
            HashCode.Combine(obj.OriginZoneId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FacilityKey));
    }
}
