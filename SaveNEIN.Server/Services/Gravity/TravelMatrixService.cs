// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    // A single source plus ten targets also lets the service exclude pair-specific
    // broad-distance misses without one far cross-product cell rejecting the batch.
    private const int TargetBatchSize = 10;
    internal const double MaximumMatrixPrefilterMiles = 200;

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
        var facilityIdentities = orderedFacilities.ToDictionary(
            facility => facility.FacilityKey,
            facility => CandidateIdentity(facility.Latitude, facility.Longitude),
            StringComparer.OrdinalIgnoreCase);
        var cachedRows = await db.OriginFacilityTravel
            .Where(route => originIds.Contains(route.OriginZoneId) &&
                            facilityKeys.Contains(route.FacilityKey) &&
                            route.RoutingGraphHash == graph.GraphHash &&
                            route.CostingProfile == costingProfile)
            .ToListAsync(cancellationToken);
        var cached = cachedRows
            .Where(route => facilityIdentities.TryGetValue(route.FacilityKey, out var identity) &&
                            CoordinatesMatch(route, identity))
            .GroupBy(
                route => (route.OriginZoneId, route.FacilityKey),
                OriginFacilityTravelKeyComparer.Instance)
            .Select(group => group.OrderByDescending(route => route.CalculatedAtUtc).First())
            .ToArray();
        var routeByKey = cached.ToDictionary(
            route => (route.OriginZoneId, route.FacilityKey),
            OriginFacilityTravelKeyComparer.Instance);
        var scenarioFacilities = orderedFacilities
            .Where(facility => facility.FacilityKind == FacilityKinds.Scenario)
            .ToArray();
        var candidateIdentities = scenarioFacilities.ToDictionary(
            facility => facility.FacilityKey,
            facility => facilityIdentities[facility.FacilityKey],
            StringComparer.OrdinalIgnoreCase);
        var candidateHashes = candidateIdentities.Values.Select(identity => identity.Hash).Distinct().ToArray();
        var cachedCandidateRoutes = candidateHashes.Length == 0
            ? []
            : await db.CandidateLocationTravelCache
                .Where(route => originIds.Contains(route.OriginZoneId) &&
                                candidateHashes.Contains(route.CandidateCoordinateHash) &&
                                route.RoutingGraphHash == graph.GraphHash &&
                                route.CostingProfile == costingProfile)
                .ToListAsync(cancellationToken);
        var candidateRouteByKey = cachedCandidateRoutes.ToDictionary(
            route => (route.OriginZoneId, route.CandidateCoordinateHash));
        var materializedCandidateRoute = false;
        foreach (var origin in orderedOrigins)
        {
            foreach (var facility in scenarioFacilities)
            {
                var routeKey = (origin.OriginZoneId, facility.FacilityKey);
                if (routeByKey.ContainsKey(routeKey))
                {
                    continue;
                }

                var identity = candidateIdentities[facility.FacilityKey];
                if (!candidateRouteByKey.TryGetValue((origin.OriginZoneId, identity.Hash), out var cachedCandidate) ||
                    !CoordinatesMatch(cachedCandidate, identity))
                {
                    continue;
                }

                var route = new OriginFacilityTravel
                {
                    OriginZoneId = origin.OriginZoneId,
                    ModelRunId = facility.ModelRunId,
                    FacilityKey = facility.FacilityKey,
                    FacilityKind = facility.FacilityKind,
                    FacilityCoordinateHash = identity.Hash,
                    FacilityLatitude = identity.Latitude,
                    FacilityLongitude = identity.Longitude,
                    RoutingGraphHash = cachedCandidate.RoutingGraphHash,
                    CostingProfile = cachedCandidate.CostingProfile,
                    TravelTimeMinutes = cachedCandidate.TravelTimeMinutes,
                    RoutedDistanceMeters = cachedCandidate.RoutedDistanceMeters,
                    RouteFound = cachedCandidate.RouteFound,
                    RouteFailureReason = cachedCandidate.RouteFailureReason,
                    CalculatedAtUtc = cachedCandidate.CalculatedAtUtc
                };
                db.OriginFacilityTravel.Add(route);
                routeByKey.Add(routeKey, route);
                materializedCandidateRoute = true;
            }
        }
        if (materializedCandidateRoute)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var origin in orderedOrigins)
        {
            var missingFacilities = orderedFacilities
                .Where(facility => !routeByKey.ContainsKey((origin.OriginZoneId, facility.FacilityKey)))
                .ToArray();
            var excludedFacilities = missingFacilities
                .Where(facility => !CompetitiveUniverseService.IsWithinBroadPrefilter(
                    origin.Latitude,
                    origin.Longitude,
                    facility.Latitude,
                    facility.Longitude,
                    MaximumMatrixPrefilterMiles))
                .ToArray();
            foreach (var facility in excludedFacilities)
            {
                var route = CreateRoute(
                    origin,
                    facility,
                    graph,
                    costingProfile,
                    null,
                    null,
                    false,
                    $"Origin-facility pair exceeded the broad {MaximumMatrixPrefilterMiles:0}-mile Valhalla matrix eligibility prefilter; no routed travel time was assumed.");
                db.OriginFacilityTravel.Add(route);
                routeByKey.Add((origin.OriginZoneId, facility.FacilityKey), route);
                AddCandidateCacheIfNeeded(origin, facility, route);
            }
            if (excludedFacilities.Length > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var eligibleFacilities = missingFacilities
                .Except(excludedFacilities)
                .ToArray();
            foreach (var facilityBatch in eligibleFacilities.Chunk(TargetBatchSize))
            {
                await ResolveEligibleBatchAsync(origin, facilityBatch);
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

        async Task ResolveEligibleBatchAsync(
            TravelMatrixOrigin origin,
            TravelMatrixFacility[] facilityBatch)
        {
            try
            {
                var matrix = await valhallaClient.GetDriveTimeMatrixAsync(
                    [new ValhallaMatrixLocation(origin.Latitude, origin.Longitude)],
                    facilityBatch
                        .Select(facility => new ValhallaMatrixLocation(facility.Latitude, facility.Longitude))
                        .ToArray(),
                    costingProfile,
                    cancellationToken);
                foreach (var cell in matrix.Cells)
                {
                    if (cell.SourceIndex != 0)
                    {
                        throw new InvalidOperationException("Valhalla returned an unexpected source index for a single-origin matrix request.");
                    }
                    var facility = facilityBatch[cell.TargetIndex];
                    var key = (origin.OriginZoneId, facility.FacilityKey);
                    if (routeByKey.ContainsKey(key))
                    {
                        continue;
                    }

                    var route = CreateRoute(
                        origin,
                        facility,
                        graph,
                        costingProfile,
                        cell.TravelTimeMinutes,
                        cell.RoutedDistanceMeters,
                        cell.RouteFound,
                        cell.RouteFound ? null : "Valhalla returned the origin-facility pair as unreachable.");
                    db.OriginFacilityTravel.Add(route);
                    routeByKey.Add(key, route);
                    AddCandidateCacheIfNeeded(origin, facility, route);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode == HttpStatusCode.BadRequest &&
                IsPairSpecificMatrixFailure(exception.Message))
            {
                if (facilityBatch.Length > 1)
                {
                    var midpoint = facilityBatch.Length / 2;
                    await ResolveEligibleBatchAsync(origin, facilityBatch[..midpoint]);
                    await ResolveEligibleBatchAsync(origin, facilityBatch[midpoint..]);
                    return;
                }

                var facility = facilityBatch[0];
                var route = CreateRoute(
                    origin,
                    facility,
                    graph,
                    costingProfile,
                    null,
                    null,
                    false,
                    "Valhalla rejected the exact origin-facility pair as unroutable (HTTP 400); no travel time was assumed.");
                db.OriginFacilityTravel.Add(route);
                routeByKey.Add((origin.OriginZoneId, facility.FacilityKey), route);
                AddCandidateCacheIfNeeded(origin, facility, route);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        void AddCandidateCacheIfNeeded(
            TravelMatrixOrigin origin,
            TravelMatrixFacility facility,
            OriginFacilityTravel route)
        {
            if (facility.FacilityKind != FacilityKinds.Scenario)
            {
                return;
            }

            var identity = candidateIdentities[facility.FacilityKey];
            var candidateKey = (origin.OriginZoneId, identity.Hash);
            if (candidateRouteByKey.ContainsKey(candidateKey))
            {
                return;
            }

            var candidateCache = new CandidateLocationTravelCache
            {
                OriginZoneId = origin.OriginZoneId,
                CandidateCoordinateHash = identity.Hash,
                CandidateLatitude = identity.Latitude,
                CandidateLongitude = identity.Longitude,
                RoutingGraphHash = graph.GraphHash,
                ValhallaVersion = graph.ValhallaVersion,
                TilesetLastModified = graph.TilesetLastModified,
                CostingProfile = costingProfile,
                TravelTimeMinutes = route.TravelTimeMinutes,
                RoutedDistanceMeters = route.RoutedDistanceMeters,
                RouteFound = route.RouteFound,
                RouteFailureReason = route.RouteFailureReason,
                CalculatedAtUtc = route.CalculatedAtUtc
            };
            db.CandidateLocationTravelCache.Add(candidateCache);
            candidateRouteByKey.Add(candidateKey, candidateCache);
        }
    }

    private static OriginFacilityTravel CreateRoute(
        TravelMatrixOrigin origin,
        TravelMatrixFacility facility,
        ValhallaRoutingGraphIdentity graph,
        string costingProfile,
        double? travelTimeMinutes,
        double? routedDistanceMeters,
        bool routeFound,
        string? failureReason)
    {
        var identity = CandidateIdentity(facility.Latitude, facility.Longitude);
        return new OriginFacilityTravel
        {
            OriginZoneId = origin.OriginZoneId,
            CasinoCompetitorId = facility.CasinoCompetitorId,
            ModelRunId = facility.ModelRunId,
            FacilityKey = facility.FacilityKey,
            FacilityKind = facility.FacilityKind,
            FacilityCoordinateHash = identity.Hash,
            FacilityLatitude = identity.Latitude,
            FacilityLongitude = identity.Longitude,
            RoutingGraphHash = graph.GraphHash,
            CostingProfile = costingProfile,
            TravelTimeMinutes = travelTimeMinutes,
            RoutedDistanceMeters = routedDistanceMeters,
            RouteFound = routeFound,
            RouteFailureReason = failureReason,
            CalculatedAtUtc = DateTime.UtcNow
        };
    }

    private static bool IsPairSpecificMatrixFailure(string message) =>
        message.Contains("\"error_code\":442", StringComparison.Ordinal) ||
        message.Contains("\"error_code\":154", StringComparison.Ordinal);

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

    internal static string CandidateCoordinateHash(double latitude, double longitude) =>
        CandidateIdentity(latitude, longitude).Hash;

    private static CandidateLocationIdentity CandidateIdentity(double latitude, double longitude)
    {
        var value = latitude.ToString("R", CultureInfo.InvariantCulture) + "," +
                    longitude.ToString("R", CultureInfo.InvariantCulture);
        return new CandidateLocationIdentity(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(),
            latitude,
            longitude);
    }

    private static bool CoordinatesMatch(
        CandidateLocationTravelCache cached,
        CandidateLocationIdentity requested) =>
        BitConverter.DoubleToInt64Bits(cached.CandidateLatitude) == BitConverter.DoubleToInt64Bits(requested.Latitude) &&
        BitConverter.DoubleToInt64Bits(cached.CandidateLongitude) == BitConverter.DoubleToInt64Bits(requested.Longitude);

    private static bool CoordinatesMatch(
        OriginFacilityTravel cached,
        CandidateLocationIdentity requested) =>
        (string.Equals(cached.FacilityCoordinateHash, requested.Hash, StringComparison.Ordinal) ||
         cached.FacilityCoordinateHash.StartsWith("legacy-", StringComparison.Ordinal)) &&
        BitConverter.DoubleToInt64Bits(cached.FacilityLatitude) == BitConverter.DoubleToInt64Bits(requested.Latitude) &&
        BitConverter.DoubleToInt64Bits(cached.FacilityLongitude) == BitConverter.DoubleToInt64Bits(requested.Longitude);

    private sealed record CandidateLocationIdentity(string Hash, double Latitude, double Longitude);

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
