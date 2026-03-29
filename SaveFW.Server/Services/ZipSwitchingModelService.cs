using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;

namespace SaveFW.Server.Services;

public class ZipDemandInput
{
    public string ZipCode { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Adults { get; set; }
    public double IncomeIndex { get; set; } = 1.0;
    public double ParticipationRate { get; set; } = 0.28;
    public double AnnualGgrPerParticipant { get; set; } = 1200.0;
}

public class ZipSwitchingRequest
{
    public double ProposedLatitude { get; set; }
    public double ProposedLongitude { get; set; }
    public double ProposedVenueQuality { get; set; } = 1.0;
    public double DistanceBeta { get; set; } = 0.06;
    public double QualityBeta { get; set; } = 1.0;
    public double MaxDistanceMiles { get; set; } = 180.0;
    public List<ZipDemandInput> ZipDemands { get; set; } = new();
}

public class ZipVenueShareResult
{
    public string VenueKey { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public double DistanceMiles { get; set; }
    public double Utility { get; set; }
    public double Share { get; set; }
    public bool IsProposedSite { get; set; }
}

public class ZipSwitchingZipResult
{
    public string ZipCode { get; set; } = string.Empty;
    public int Adults { get; set; }
    public double ZipDemandGgr { get; set; }
    public double ProposedShare { get; set; }
    public double ProposedGgrContribution { get; set; }
    public List<ZipVenueShareResult> VenueShares { get; set; } = new();
}

public class ZipSwitchingResult
{
    public double ProposedLatitude { get; set; }
    public double ProposedLongitude { get; set; }
    public double TotalDemandGgr { get; set; }
    public double ProposedProjectedGgr { get; set; }
    public double ProposedShareOfDemand => TotalDemandGgr <= 0 ? 0 : ProposedProjectedGgr / TotalDemandGgr;
    public List<ZipSwitchingZipResult> ZipResults { get; set; } = new();
}

public class ZipSwitchingModelService
{
    private readonly AppDbContext _db;
    private readonly CompetitionScoringService _competitionScoringService;

    public ZipSwitchingModelService(AppDbContext db, CompetitionScoringService competitionScoringService)
    {
        _db = db;
        _competitionScoringService = competitionScoringService;
    }

    public async Task<ZipSwitchingResult> CalculateAsync(ZipSwitchingRequest request)
    {
        var competitors = await _db.CasinoCompetitors
            .Where(c => c.IsActive)
            .ToListAsync();

        var result = new ZipSwitchingResult
        {
            ProposedLatitude = request.ProposedLatitude,
            ProposedLongitude = request.ProposedLongitude
        };

        foreach (var zip in request.ZipDemands)
        {
            var zipDemandGgr = zip.Adults
                * Math.Max(0, zip.ParticipationRate)
                * Math.Max(0, zip.AnnualGgrPerParticipant)
                * Math.Max(0, zip.IncomeIndex);

            var venueUtilities = new List<ZipVenueShareResult>();

            var proposedDistance = CalculateHaversineDistanceMiles(zip.Latitude, zip.Longitude, request.ProposedLatitude, request.ProposedLongitude);
            if (proposedDistance <= request.MaxDistanceMiles)
            {
                var proposedUtility = (request.QualityBeta * request.ProposedVenueQuality) - (request.DistanceBeta * proposedDistance);
                venueUtilities.Add(new ZipVenueShareResult
                {
                    VenueKey = "proposed_site",
                    VenueName = "Proposed Site",
                    DistanceMiles = proposedDistance,
                    Utility = proposedUtility,
                    IsProposedSite = true
                });
            }

            foreach (var competitor in competitors)
            {
                var distance = CalculateHaversineDistanceMiles(zip.Latitude, zip.Longitude, competitor.Latitude, competitor.Longitude);
                if (distance > request.MaxDistanceMiles) continue;

                var quality = _competitionScoringService.ComputeVenueWeight(competitor);
                var utility = (request.QualityBeta * quality) - (request.DistanceBeta * distance);
                venueUtilities.Add(new ZipVenueShareResult
                {
                    VenueKey = $"competitor_{competitor.Id}",
                    VenueName = competitor.Name,
                    DistanceMiles = distance,
                    Utility = utility,
                    IsProposedSite = false
                });
            }

            if (!venueUtilities.Any())
            {
                continue;
            }

            var denominator = venueUtilities.Sum(v => Math.Exp(v.Utility));
            foreach (var venue in venueUtilities)
            {
                venue.Share = denominator <= 0 ? 0 : Math.Exp(venue.Utility) / denominator;
            }

            var proposedShare = venueUtilities.FirstOrDefault(v => v.IsProposedSite)?.Share ?? 0;
            var proposedContribution = zipDemandGgr * proposedShare;

            result.TotalDemandGgr += zipDemandGgr;
            result.ProposedProjectedGgr += proposedContribution;
            result.ZipResults.Add(new ZipSwitchingZipResult
            {
                ZipCode = zip.ZipCode,
                Adults = zip.Adults,
                ZipDemandGgr = zipDemandGgr,
                ProposedShare = proposedShare,
                ProposedGgrContribution = proposedContribution,
                VenueShares = venueUtilities.OrderByDescending(v => v.Share).ToList()
            });
        }

        result.ZipResults = result.ZipResults
            .OrderByDescending(z => z.ProposedGgrContribution)
            .ToList();

        return result;
    }

    private static double CalculateHaversineDistanceMiles(double lat1, double lon1, double lat2, double lon2)
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
        var meters = R * c;
        return meters / 1609.34;
    }
}
