using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;
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
    public List<string> Reasons { get; set; } = new();
}

public class RevenueHeuristicService
{
    private const double FtWayneLat = 41.0793;
    private const double FtWayneLon = -85.1394;

    private readonly CompetitionScoringService _competitionService;
    private readonly AppDbContext _db;

    public RevenueHeuristicService(CompetitionScoringService competitionService, AppDbContext db)
    {
        _competitionService = competitionService;
        _db = db;
    }

    /// <summary>
    /// Public-data-informed heuristic for relative site quality.
    /// This intentionally supports directional comparison and sensitivity testing,
    /// not a precise AGR point forecast.
    /// </summary>
    public async Task<RevenuePotentialResult> CalculateHeuristicAsync(double lat, double lon)
    {
        var reasons = new List<string>();

        var targetPoint = NetTopologySuite.NtsGeometryServices.Instance
            .CreateGeometryFactory(srid: 4326)
            .CreatePoint(new Coordinate(lon, lat));

        // 1) Competition penalty (bounded so competition cannot zero out the score).
        var compResult = await _competitionService.ComputeSiteCompetitionPressureAsync(lat, lon);
        var compPenalty = Math.Min(compResult.TotalPressureScore * 0.1, 0.4);
        if (compPenalty > 0.25)
        {
            reasons.Add("High overlapping competition pressure from existing casinos/racinos.");
        }

        // 2) Access score (Fort Wayne core as a primary market anchor).
        var distToFtWayneMiles = CalculateHaversineDistance(lat, lon, FtWayneLat, FtWayneLon) / 1609.34;
        double accessScore = 1.0;

        if (distToFtWayneMiles > 30)
        {
            accessScore -= 0.15;
            reasons.Add("Farther from the Fort Wayne primary demand center.");
        }

        if (distToFtWayneMiles > 50)
        {
            accessScore -= 0.20;
            reasons.Add("Outside the strongest Northeast Indiana corridor access band.");
        }

        // 3) Market-depth proxy from nearby block groups.
        // NOTE: This is still a heuristic proxy until ZIP/tract-level AGI layers are fully integrated.
        var maxDistDegrees = 30.0 / 69.0;
        var nearbyGroups = await _db.BlockGroups
            .Where(b => b.Geom.IsWithinDistance(targetPoint, maxDistDegrees))
            .ToListAsync();

        double weightedMarketDepth = 0;
        foreach (var bg in nearbyGroups)
        {
            var adultsProxy = bg.Population * 0.75;
            var incomeWeight = Math.Clamp((bg.MedianIncome ?? 65000.0) / 65000.0, 0.4, 1.8);
            weightedMarketDepth += adultsProxy * incomeWeight;
        }

        // 4) Normalize against benchmark depth near the I-69 / Fort Wayne corridor.
        const double benchmarkDepth = 400000;
        var depthScore = weightedMarketDepth > 0
            ? Math.Min(weightedMarketDepth / benchmarkDepth, 1.2)
            : 0;

        if (depthScore < 0.65)
        {
            reasons.Add("Weaker weighted nearby adult market depth.");
        }

        // 5) Combine into a bounded relative multiplier.
        var finalMultiplier = (accessScore * depthScore) - compPenalty;
        finalMultiplier = Math.Clamp(finalMultiplier, 0.2, 1.2);

        var result = new RevenuePotentialResult
        {
            NormalizedMultiplier = finalMultiplier,
            AccessScore = accessScore,
            CompetitionPenalty = compPenalty,
            WeightedMarketDepth = weightedMarketDepth,
            Reasons = reasons
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
