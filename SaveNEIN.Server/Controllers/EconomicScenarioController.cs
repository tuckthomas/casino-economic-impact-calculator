using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/economic-scenarios")]
public sealed class EconomicScenarioController(IMemoryCache cache) : ControllerBase
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    [HttpPost]
    public IActionResult Save([FromBody] EconomicScenarioSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Version != "economic-impact-client-v1")
        {
            return BadRequest("Unsupported or missing economic-impact scenario version.");
        }

        var token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(token), snapshot, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = Math.Max(1, JsonSerializer.SerializeToUtf8Bytes(snapshot).Length)
        });

        return Ok(new { token, expiresInSeconds = (int)Lifetime.TotalSeconds });
    }

    [HttpGet("{token}")]
    public IActionResult Get(string token)
    {
        return cache.TryGetValue(CacheKey(token), out EconomicScenarioSnapshot? snapshot)
            ? Ok(snapshot)
            : NotFound("This economic-impact scenario has expired or does not exist.");
    }

    private static string CacheKey(string token) => $"economic-impact-scenario:{token}";
}

public sealed record EconomicScenarioSnapshot(
    string Version,
    string? StateFips,
    string? CountyFips,
    double? MarkerLatitude,
    double? MarkerLongitude,
    JsonElement ZoneConfiguration,
    JsonElement PreviewOutputs);
