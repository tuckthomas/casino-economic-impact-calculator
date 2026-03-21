using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;
using SaveFW.Server.Data.Entities;
using NetTopologySuite.Geometries;

namespace SaveFW.Server.Services;

public class CompetitionScoreResult
{
    public CasinoCompetitor Competitor { get; set; } = null!;
    public double BaseWeight { get; set; }
    public double FeatureWeight { get; set; }
    public double TotalWeight { get; set; }
    public double DistanceMiles { get; set; }
    public double OverlapPressure { get; set; }
}

public class SiteCompetitionResult
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double TotalPressureScore { get; set; }
    public List<CompetitionScoreResult> CompetitorDetails { get; set; } = new();
}

public class CompetitionScoringService
{
    private readonly AppDbContext _db;

    public CompetitionScoringService(AppDbContext db)
    {
        _db = db;
    }

    public double ComputeVenueWeight(CasinoCompetitor venue)
    {
        double score = 0;
        
        // Base type values
        score += venue.VenueType switch
        {
            "full_service_casino" => 1.00,
            "racino" => 0.70,
            "sportsbook_only" => 0.35,
            "off_track_betting" => 0.10,
            "charity_gaming" => 0.05,
            _ => 0.0
        };

        // Feature adders
        if (venue.HasSlots) score += 0.15;
        if (venue.HasTableGames) score += 0.20;
        if (venue.HasPoker) score += 0.10;
        if (venue.HasSportsbook) score += 0.05;
        if (venue.HasHotel) score += 0.15;
        if (venue.HasEntertainment) score += 0.05;
        if (venue.HasRestaurants) score += 0.05;

        return score;
    }

    /// <summary>
    /// Computes the competitive pressure on a proposed site based on existing active venues.
    /// Overlap pressure decreases over distance using a gravity model proxy.
    /// </summary>
    public async Task<SiteCompetitionResult> ComputeSiteCompetitionPressureAsync(double lat, double lon, double maxDistanceMiles = 150)
    {
        var targetPoint = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326)
            .CreatePoint(new Coordinate(lon, lat));

        // Note: we fetch all and calculate precise distance in memory to avoid complex PostGIS 
        // coordinate casting issues unless it's a huge dataset, but N=100 is tiny.
        var competitors = await _db.CasinoCompetitors
            .Where(c => c.IsActive)
            .ToListAsync();
            
        var result = new SiteCompetitionResult { Latitude = lat, Longitude = lon };

        foreach (var comp in competitors)
        {
            var distanceMeters = CalculateHaversineDistance(lat, lon, comp.Latitude, comp.Longitude);
            var distanceMiles = distanceMeters / 1609.34;
            
            if (distanceMiles > maxDistanceMiles) continue;

            var totalW = ComputeVenueWeight(comp);

            // Pressure decays with distance. 
            // Using a gravity-like model where close competitors matter much more.
            // Half-distance roughly 40 miles: 1 / (1 + (distance/40)^2)
            var distanceDecay = 1.0 / (1.0 + Math.Pow(distanceMiles / 40.0, 2));
            var overlapPressure = totalW * distanceDecay;

            result.CompetitorDetails.Add(new CompetitionScoreResult
            {
                Competitor = comp,
                BaseWeight = totalW - (comp.HasSlots ? 0.15 : 0), // Simplifying display
                FeatureWeight = totalW, // Simplifying display
                TotalWeight = totalW,
                DistanceMiles = distanceMiles,
                OverlapPressure = overlapPressure
            });
            
            result.TotalPressureScore += overlapPressure;
        }

        // Sort by pressure descending for easier debugging
        result.CompetitorDetails = result.CompetitorDetails.OrderByDescending(c => c.OverlapPressure).ToList();

        return result;
    }

    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371e3; // metres
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }
}
