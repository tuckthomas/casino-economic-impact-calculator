// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public interface ICompetitiveUniverseService
{
    Task<IReadOnlyList<CasinoCompetitor>> SelectAsync(
        Guid competitorSnapshotId,
        IReadOnlyCollection<OriginZone> origins,
        DateOnly effectiveOn,
        double broadPrefilterMiles,
        IReadOnlyCollection<int>? explicitlyIncludedCompetitorIds = null,
        CancellationToken cancellationToken = default);
}

public sealed class CompetitiveUniverseService(AppDbContext db) : ICompetitiveUniverseService
{
    public async Task<IReadOnlyList<CasinoCompetitor>> SelectAsync(
        Guid competitorSnapshotId,
        IReadOnlyCollection<OriginZone> origins,
        DateOnly effectiveOn,
        double broadPrefilterMiles,
        IReadOnlyCollection<int>? explicitlyIncludedCompetitorIds = null,
        CancellationToken cancellationToken = default)
    {
        if (origins.Count == 0)
        {
            throw new ArgumentException("At least one origin is required to assemble a competitive universe.", nameof(origins));
        }
        if (!double.IsFinite(broadPrefilterMiles) || broadPrefilterMiles is <= 0 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(broadPrefilterMiles),
                "The broad diagnostic prefilter must be between 0 and 2,000 miles.");
        }

        var explicitIds = (explicitlyIncludedCompetitorIds ?? []).Distinct().ToHashSet();
        var candidates = await db.CasinoCompetitors
            .AsNoTracking()
            .Where(competitor => competitor.DatasetSnapshotId == competitorSnapshotId &&
                                 competitor.IsActive &&
                                 (competitor.OpenedOn == null || competitor.OpenedOn <= effectiveOn) &&
                                 (competitor.ClosedOn == null || competitor.ClosedOn > effectiveOn))
            .ToListAsync(cancellationToken);
        var missingExplicitIds = explicitIds.Except(candidates.Select(competitor => competitor.Id)).ToArray();
        if (missingExplicitIds.Length > 0)
        {
            throw new KeyNotFoundException(
                $"Competitor snapshot does not contain requested active facility IDs: {string.Join(", ", missingExplicitIds)}.");
        }

        var selected = candidates
            .Where(competitor => explicitIds.Contains(competitor.Id) ||
                                 IsCasinoFloorSubstitute(competitor) &&
                                 origins.Any(origin => IsWithinBroadPrefilter(
                                     origin.RepresentativePoint.Y,
                                     origin.RepresentativePoint.X,
                                     competitor.Latitude,
                                     competitor.Longitude,
                                     broadPrefilterMiles)))
            .OrderBy(competitor => competitor.StableVenueId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException(
                "No substitutable facilities were selected for the origin market; expand the prefilter or snapshot coverage.");
        }
        return selected;
    }

    public static bool IsCasinoFloorSubstitute(CasinoCompetitor competitor)
    {
        if (competitor.HasSlots == true || competitor.HasTableGames == true ||
            competitor.SlotOrVltPositions > 0 || competitor.TableGameCount > 0)
        {
            return true;
        }
        return competitor.VenueType.Contains("casino", StringComparison.OrdinalIgnoreCase) ||
               competitor.VenueType.Equals("racino", StringComparison.OrdinalIgnoreCase) ||
               competitor.FacilityRegime?.Contains("tribal", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsWithinBroadPrefilter(
        double originLatitude,
        double originLongitude,
        double facilityLatitude,
        double facilityLongitude,
        double maximumMiles)
    {
        const double EarthRadiusMiles = 3_958.7613;
        var latitude1 = originLatitude * Math.PI / 180;
        var latitude2 = facilityLatitude * Math.PI / 180;
        var latitudeDelta = (facilityLatitude - originLatitude) * Math.PI / 180;
        var longitudeDelta = (facilityLongitude - originLongitude) * Math.PI / 180;
        var haversine = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2) +
                        Math.Cos(latitude1) * Math.Cos(latitude2) *
                        Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        var angularDistance = 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
        return EarthRadiusMiles * angularDistance <= maximumMiles;
    }
}
