using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SiteScoresController : ControllerBase
{
    private readonly AppDbContext _db;

    public SiteScoresController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<SiteScore>>> Get(int countyId, int minutes = 15)
    {
        // Return precomputed scores for a specific county and drive-time radius
        var scores = await _db.SiteScores
            .Where(s => s.CountyId == countyId && s.Minutes == minutes)
            .OrderByDescending(s => s.Score)
            .ToListAsync();

        return scores;
    }
}
