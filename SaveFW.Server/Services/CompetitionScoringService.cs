using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;
using SaveFW.Server.Data.Entities;

namespace SaveFW.Server.Services;

public class CompetitionScoreResult
{
    public CasinoCompetitor Competitor { get; set; } = null!;
    public double BaseWeight { get; set; }
    public double FeatureWeight { get; set; }
    public double TotalWeight { get; set; }
    public double DistanceMiles { get; set; }
    public double MarketCenterDistanceMiles { get; set; }
    public double MarketOverlapFactor { get; set; }
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
    private const double FtWayneLat = 41.0793;
    private const double FtWayneLon = -85.1394;

    private readonly AppDbContext _db;

    public CompetitionScoringService(AppDbContext db)
    {
        _db = db;
    }

    public (double BaseWeight, double FeatureWeight, double TotalWeight) ComputeVenueWeightBreakdown(CasinoCompetitor venue)
    {
        var baseWeight = venue.VenueType switch
        {
            "full_service_casino" => 1.00,
            "racino" => 0.70,
            "sportsbook_only" => 0.35,
            "off_track_betting" => 0.10,
            "charity_gaming" => 0.05,
            _ => 0.0
        };

        var featureWeight = 0.0;
        if (venue.HasSlots) featureWeight += 0.15;
        if (venue.HasTableGames) featureWeight += 0.20;
        if (venue.HasPoker) featureWeight += 0.10;
        if (venue.HasSportsbook) featureWeight += 0.05;
        if (venue.HasHotel) featureWeight += 0.15;
        if (venue.HasEntertainment) featureWeight += 0.05;
        if (venue.HasRestaurants) featureWeight += 0.05;

        return (baseWeight, featureWeight, baseWeight + featureWeight);
    }

    public double ComputeVenueWeight(CasinoCompetitor venue)
        => ComputeVenueWeightBreakdown(venue).TotalWeight;

    /// <summary>
    /// Computes competitive pressure using venue weights, distance decay,
    /// and overlap with the Fort Wayne primary demand center.
    /// </summary>
    public async Task<SiteCompetitionResult> ComputeSiteCompetitionPressureAsync(double lat, double lon, double maxDistanceMiles = 150)
    {
        var competitors = await _db.CasinoCompetitors
            .Where(c => c.IsActive)
            .ToListAsync();

        var result = new SiteCompetitionResult { Latitude = lat, Longitude = lon };

        foreach (var comp in competitors)
        {
            var distanceMeters = CalculateHaversineDistance(lat, lon, comp.Latitude, comp.Longitude);
            var distanceMiles = distanceMeters / 1609.34;
            if (distanceMiles > maxDistanceMiles) continue;

            var marketCenterDistanceMiles = CalculateHaversineDistance(FtWayneLat, FtWayneLon, comp.Latitude, comp.Longitude) / 1609.34;
            var marketOverlapFactor = 1.0 / (1.0 + Math.Pow(marketCenterDistanceMiles / 55.0, 2));

            var weightBreakdown = ComputeVenueWeightBreakdown(comp);

            var distanceDecay = 1.0 / (1.0 + Math.Pow(distanceMiles / 40.0, 2));
            var overlapAdjustment = 0.6 + (0.4 * marketOverlapFactor);
            var overlapPressure = weightBreakdown.TotalWeight * distanceDecay * overlapAdjustment;

            result.CompetitorDetails.Add(new CompetitionScoreResult
            {
                Competitor = comp,
                BaseWeight = weightBreakdown.BaseWeight,
                FeatureWeight = weightBreakdown.FeatureWeight,
                TotalWeight = weightBreakdown.TotalWeight,
                DistanceMiles = distanceMiles,
                MarketCenterDistanceMiles = marketCenterDistanceMiles,
                MarketOverlapFactor = marketOverlapFactor,
                OverlapPressure = overlapPressure
            });

            result.TotalPressureScore += overlapPressure;
        }

        result.CompetitorDetails = result.CompetitorDetails
            .OrderByDescending(c => c.OverlapPressure)
            .ToList();

        return result;
    }

    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371e3;
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
