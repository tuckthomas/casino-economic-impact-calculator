using Microsoft.AspNetCore.Mvc;
using SaveFW.Server.Services.Valhalla;

namespace SaveFW.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ValhallaController : ControllerBase
{
    private readonly ValhallaClient _client;

    public ValhallaController(ValhallaClient client)
    {
        _client = client;
    }

    [HttpGet("isochrone")]
    public async Task<IActionResult> GetIsochrone(double lat, double lon, int minutes)
    {
        try 
        {
            var json = await _client.GetIsochroneJsonAsync(lat, lon, minutes);
            if (json == null) return NotFound();
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
