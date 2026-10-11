using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Tests;

public sealed class EconomicScenarioControllerTests
{
    [Fact]
    public void SavePreservesLocationAndJsonAfterRequestDocumentsAreDisposed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new EconomicScenarioController(cache);
        string token;
        using (var zones = JsonDocument.Parse("{\"count\":3,\"tiers\":[15,30,60],\"mode\":\"isochrone\"}"))
        using (var outputs = JsonDocument.Parse("{\"tier1Adults\":123,\"countyBreakdown\":{\"18003\":123}}"))
        {
            var saved = Assert.IsType<OkObjectResult>(controller.Save(Valid(zones.RootElement, outputs.RootElement)));
            using var result = JsonSerializer.SerializeToDocument(saved.Value);
            token = result.RootElement.GetProperty("token").GetString()!;
            Assert.Equal(7200, result.RootElement.GetProperty("expiresInSeconds").GetInt32());
        }
        var retrieved = Assert.IsType<EconomicScenarioSnapshot>(Assert.IsType<OkObjectResult>(controller.Get(token)).Value);
        Assert.Equal(41.0793, retrieved.MarkerLatitude);
        Assert.Equal(-85.1394, retrieved.MarkerLongitude);
        Assert.Equal("18003", retrieved.CountyFips);
        Assert.Equal(60, retrieved.ZoneConfiguration.GetProperty("tiers")[2].GetInt32());
        Assert.Equal(123, retrieved.PreviewOutputs.GetProperty("countyBreakdown").GetProperty("18003").GetInt32());
    }

    [Theory]
    [InlineData("old-version", "18", "18003", 41, -85)]
    [InlineData(EconomicScenarioSnapshot.CurrentVersion, "IN", "18003", 41, -85)]
    [InlineData(EconomicScenarioSnapshot.CurrentVersion, "18", "39001", 41, -85)]
    [InlineData(EconomicScenarioSnapshot.CurrentVersion, "18", "003", 41, -85)]
    [InlineData(EconomicScenarioSnapshot.CurrentVersion, "18", "18003", 91, -85)]
    [InlineData(EconomicScenarioSnapshot.CurrentVersion, "18", "18003", 41, -181)]
    public void SaveRejectsInvalidVersionGeographyAndCoordinates(string version, string state, string county, double latitude, double longitude)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var json = JsonDocument.Parse("{}");
        var scenario = Valid(json.RootElement, json.RootElement) with
        {
            Version = version, StateFips = state, CountyFips = county,
            MarkerLatitude = latitude, MarkerLongitude = longitude
        };
        Assert.IsType<BadRequestObjectResult>(new EconomicScenarioController(cache).Save(scenario));
    }

    [Fact]
    public void SaveRejectsMissingCoordinatesNonFiniteValuesAndMissingJson()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var json = JsonDocument.Parse("{}");
        var controller = new EconomicScenarioController(cache);
        var valid = Valid(json.RootElement, json.RootElement);
        Assert.IsType<BadRequestObjectResult>(controller.Save(valid with { MarkerLatitude = null }));
        Assert.IsType<BadRequestObjectResult>(controller.Save(valid with { MarkerLongitude = double.NaN }));
        Assert.IsType<BadRequestObjectResult>(controller.Save(valid with { ZoneConfiguration = default }));
        Assert.IsType<BadRequestObjectResult>(controller.Save(valid with { PreviewOutputs = default }));
    }

    [Fact]
    public void SaveRejectsOversizedSnapshot()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var zones = JsonDocument.Parse("{}");
        using var outputs = JsonDocument.Parse(JsonSerializer.Serialize(new { text = new string('a', EconomicScenarioSnapshot.MaximumPayloadBytes) }));
        var result = Assert.IsType<ObjectResult>(new EconomicScenarioController(cache).Save(Valid(zones.RootElement, outputs.RootElement)));
        Assert.Equal(413, result.StatusCode);
    }

    [Theory]
    [InlineData("invalid-token")]
    [InlineData("00000000000000000000000000000000")]
    public void GetRejectsInvalidOrUnavailableTokens(string token)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.IsType<NotFoundObjectResult>(new EconomicScenarioController(cache).Get(token));
    }

    private static EconomicScenarioSnapshot Valid(JsonElement zones, JsonElement outputs) =>
        new(EconomicScenarioSnapshot.CurrentVersion, "18", "18003", 41.0793, -85.1394, zones, outputs);
}
