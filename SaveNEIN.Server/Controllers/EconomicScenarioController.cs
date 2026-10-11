using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/economic-scenarios")]
public sealed class EconomicScenarioController(IMemoryCache cache) : ControllerBase
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    [HttpPost]
    [RequestSizeLimit(EconomicScenarioSnapshot.MaximumPayloadBytes)]
    public IActionResult Save([FromBody] EconomicScenarioSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return BadRequest("An economic-impact scenario is required.");
        }

        try { snapshot.Validate(); }
        catch (InvalidDataException exception) { return BadRequest(exception.Message); }

        var payloadSize = JsonSerializer.SerializeToUtf8Bytes(snapshot).Length;
        if (payloadSize > EconomicScenarioSnapshot.MaximumPayloadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "Economic-impact scenario exceeds the size limit.");

        // Cache owns its JSON values even if the request's backing documents are disposed.
        snapshot = snapshot with
        {
            ZoneConfiguration = snapshot.ZoneConfiguration.Clone(),
            PreviewOutputs = snapshot.PreviewOutputs.Clone()
        };

        var token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(token), snapshot, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = Math.Max(1, payloadSize)
        });

        return Ok(new { token, expiresInSeconds = (int)Lifetime.TotalSeconds });
    }

    [HttpGet("{token}")]
    public IActionResult Get(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _))
            return NotFound("This economic-impact scenario has expired or does not exist.");

        return cache.TryGetValue(CacheKey(token), out EconomicScenarioSnapshot? snapshot)
            ? Ok(snapshot)
            : NotFound("This economic-impact scenario has expired or does not exist.");
    }

    private static string CacheKey(string token) => $"economic-impact-scenario:{token}";
}
