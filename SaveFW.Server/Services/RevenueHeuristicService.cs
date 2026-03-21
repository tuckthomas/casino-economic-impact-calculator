using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;
using SaveFW.Server.Data.Entities;
using NetTopologySuite.Geometries;

namespace SaveFW.Server.Services;

public class RevenuePotentialResult
{
    public double NormalizedMultiplier { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string RecommendationMessage { get; set; } = string.Empty;
    public double AccessScore { get; set; }
    public double CompetitionPenalty { get; set; }
    public double WeightedMarketDepth { get; set; }
}

public class RevenueHeuristicService
{
    private readonly CompetitionScoringService _competitionService;
    private readonly AppDbContext _db;

    public RevenueHeuristicService(CompetitionScoringService competitionService, AppDbContext db)
    {
        _competitionService = competitionService;
        _db = db;
    }

    public async Task<RevenuePotentialResult> CalculateHeuristicAsync(double lat, double lon)
    {
        var targetPoint = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326)
            .CreatePoint(new Coordinate(lon, lat));

        // 1. Competition Penalty
        var compResult = await _competitionService.ComputeSiteCompetitionPressureAsync(lat, lon);
        var compPenalty = Math.Min(compResult.TotalPressureScore * 0.1, 0.4); // max 40% penalty

        // 2. Access / Location Score (Proximity to Fort Wayne Core and major corridors)
        var ftWayneCenter = new Coordinate(-85.1394, 41.0793);
        var distToFtWayneMiles = CalculateHaversineDistance(lat, lon, 41.0793, -85.1394) / 1609.34;

        double accessScore = 1.0;
        if (distToFtWayneMiles > 30) accessScore -= 0.15;
        if (distToFtWayneMiles > 50) accessScore -= 0.20;

        // 3. Market Depth Proxy (Use population within ~30 miles)
        var maxDistDegrees = 30.0 / 69.0;
        var nearbyGroups = await _db.BlockGroups
            .Where(b => b.Geom.IsWithinDistance(targetPoint, maxDistDegrees))
            .ToListAsync();
            
        double weightedMarketDepth = 0;
        foreach (var bg in nearbyGroups)
        {
            var p = bg.Population * 0.75;
            var inc = (bg.MedianIncome ?? 65000.0) / 65000.0;
            weightedMarketDepth += (p * inc);
        }

        // 4. Normalize against a Benchmark point (roughly 400k weighted depth near Ft Wayne)
        double benchmarkDepth = 400000;
        double depthScore = weightedMarketDepth > 0 ? Math.Min(weightedMarketDepth / benchmarkDepth, 1.2) : 0; 

        // 5. Combine Multiplier
        double finalMultiplier = (accessScore * depthScore) - compPenalty;
        finalMultiplier = Math.Clamp(finalMultiplier, 0.2, 1.2);

        var result = new RevenuePotentialResult
        {
            NormalizedMultiplier = finalMultiplier,
            AccessScore = accessScore,
            CompetitionPenalty = compPenalty,
            WeightedMarketDepth = weightedMarketDepth
        };

        if (finalMultiplier >= 0.85)
        {
            result.Classification = "High revenue potential";
            result.RecommendationMessage = "Location appears to be inside the strongest Northeast Indiana casino demand corridor.";
        }
        else if (finalMultiplier >= 0.60)
        {
            result.Classification = "Moderate revenue potential";
            result.RecommendationMessage = "This location is viable but shows weaker demand metrics or higher competition. Mild revenue sensitivity testing is recommended.";
        }
        else
        {
            result.Classification = "Lower revenue potential";
            result.RecommendationMessage = "This location is outside the strongest demand corridor or faces significant competition. Revenue assumptions used for corridor sites may overstate expected AGR here. Test lower AGR scenarios before interpreting net impact results.";
        }

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
