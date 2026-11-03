using Microsoft.AspNetCore.Mvc;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/local-economy")]
public sealed class LocalEconomyController(LocalEconomyProfileService profiles) : ControllerBase
{
    [HttpGet("{countyFips}")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(string countyFips, CancellationToken cancellationToken)
    {
        if (countyFips.Length != 5 || countyFips.Any(character => character is < '0' or > '9'))
            return BadRequest("A five-digit county FIPS is required.");
        var result = await profiles.GetAsync(countyFips, cancellationToken);
        return result is null ? NotFound(new { error = "No public workforce snapshot is available for this county." }) : Ok(result);
    }
}
