using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Data;

namespace SaveNEIN.Server.Tests;

public sealed class MunicipalityLookupTests
{
    [Theory]
    [InlineData(91, -85, "18003")]
    [InlineData(41, -181, "18003")]
    [InlineData(41, -85, "18x03")]
    [InlineData(41, -85, "003")]
    public async Task InvalidLocationIsRejectedBeforeDatabaseLookup(double latitude, double longitude, string county)
    {
        await using var db = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new CensusController(db, null!, cache, Options.Create(new TaxAllocationOptions()));
        Assert.IsType<BadRequestObjectResult>(await controller.GetMunicipalityAt(latitude, longitude, county));
    }

    [Fact]
    public async Task CountyOutsideConfiguredMunicipalRulesReturnsExplicitNonEligibility()
    {
        await using var db = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new CensusController(db, null!, cache, Options.Create(new TaxAllocationOptions()));
        var result = Assert.IsType<OkObjectResult>(await controller.GetMunicipalityAt(41, -85, "18003"));
        using var json = System.Text.Json.JsonSerializer.SerializeToDocument(result.Value);
        Assert.False(json.RootElement.GetProperty("isEligibleCounty").GetBoolean());
        Assert.False(json.RootElement.GetProperty("cityRevenueApplies").GetBoolean());
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
