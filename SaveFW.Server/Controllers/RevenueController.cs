using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveFW.Server.Data;
using SaveFW.Server.Services;

namespace SaveFW.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RevenueController : ControllerBase
{
    private readonly RevenueHeuristicService _heuristicService;
    private readonly ZipSwitchingModelService _zipSwitchingModelService;
    private readonly AppDbContext _db;

    public RevenueController(
        RevenueHeuristicService heuristicService,
        ZipSwitchingModelService zipSwitchingModelService,
        AppDbContext db)
    {
        _heuristicService = heuristicService;
        _zipSwitchingModelService = zipSwitchingModelService;
        _db = db;
    }

    [HttpGet("potential")]
    public async Task<IActionResult> GetRevenuePotential(double lat, double lon)
    {
        var result = await _heuristicService.CalculateHeuristicAsync(lat, lon);
        return Ok(result);
    }


    [HttpGet("benchmark-scenarios")]
    public async Task<IActionResult> GetBenchmarkScenarios()
    {
        var scenarios = new[]
        {
            new { Name = "I-69 / SR-8 Benchmark", Lat = 41.2300, Lon = -85.1300 },
            new { Name = "Allen-adjacent Corridor", Lat = 41.1800, Lon = -85.0400 },
            new { Name = "Steuben-like North", Lat = 41.6400, Lon = -85.0000 }
        };

        var results = new List<object>();
        foreach (var s in scenarios)
        {
            var score = await _heuristicService.CalculateHeuristicAsync(s.Lat, s.Lon);
            results.Add(new
            {
                s.Name,
                s.Lat,
                s.Lon,
                score.NormalizedMultiplier,
                score.Classification,
                score.RecommendationMessage,
                score.Reasons,
                score.AccessScore,
                score.CompetitionPenalty,
                score.WeightedMarketDepth
            });
        }

        return Ok(results);
    }

    [HttpPost("zip-switching")]
    public async Task<IActionResult> CalculateZipSwitching([FromBody] ZipSwitchingRequest request)
    {
        if (request.ZipDemands == null || request.ZipDemands.Count == 0)
        {
            return BadRequest("ZipDemands is required and must include at least one ZIP input.");
        }

        var result = await _zipSwitchingModelService.CalculateAsync(request);
        return Ok(result);
    }

    [HttpGet("competitors")]
    public async Task<IActionResult> GetCompetitors()
    {
        var competitors = await _db.CasinoCompetitors
            .Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.VenueType,
                c.OperatorName,
                c.Latitude,
                c.Longitude
            })
            .ToListAsync();
            
        return Ok(competitors);
    }
}
