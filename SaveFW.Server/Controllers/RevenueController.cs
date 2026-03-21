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
    private readonly AppDbContext _db;

    public RevenueController(RevenueHeuristicService heuristicService, AppDbContext db)
    {
        _heuristicService = heuristicService;
        _db = db;
    }

    [HttpGet("potential")]
    public async Task<IActionResult> GetRevenuePotential(double lat, double lon)
    {
        var result = await _heuristicService.CalculateHeuristicAsync(lat, lon);
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
