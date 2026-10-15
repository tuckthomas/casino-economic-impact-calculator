using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class MichiganCommercialGamingRulesTests
{
    [Fact]
    public async Task SeedIsIdempotentAndAgeScopeDoesNotCoverTribalGamingOrEarlierYears()
    {
        await using var db = CreateDb();
        db.Jurisdictions.Add(new Jurisdiction { Code = "US", Kind = "federal", Name = "United States" });
        await db.SaveChangesAsync();
        await MichiganCommercialGamingRulesSeeder.SeedAsync(db);
        await MichiganCommercialGamingRulesSeeder.SeedAsync(db);
        Assert.Equal(5, await db.JurisdictionRules.CountAsync());
        var resolver = new GamingAgeResolver(new JurisdictionProfileService(db));
        Assert.Equal(21, await resolver.ResolveMinimumAgeAsync("US-MI", "commercial-casino", new(2025, 6, 30)));
        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() => resolver.ResolveMinimumAgeAsync("US-MI", "tribal-casino", new(2025, 6, 30)));
        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() => resolver.ResolveMinimumAgeAsync("US-MI", "commercial-casino", new(2024, 12, 31)));
    }

    [Fact]
    public async Task ReportedDetroitLevyReconcilesStateAndCityWithoutDoubleCounting()
    {
        await using var db = CreateDb();
        db.Jurisdictions.Add(new Jurisdiction { Code = "US", Kind = "federal", Name = "United States" });
        await db.SaveChangesAsync();
        await MichiganCommercialGamingRulesSeeder.SeedAsync(db);
        var profiles = new JurisdictionProfileService(db);
        var date = new DateOnly(2025, 6, 30);
        var tax = await new GamingTaxCalculator(profiles).CalculateAsync(new GamingTaxRequest("US-MI", "commercial-casino", date, 0, 100_000_000m));
        Assert.Equal(19_000_000m, tax.GamingTax);
        var result = await new GamingFiscalAllocationCalculator(profiles, new LocationResolver("2622000"))
            .CalculateAsync(new GamingFiscalAllocationRequest("US-MI", "commercial-casino", date, 100_000_000m, tax.GamingTax, 42.33, -83.05));
        Assert.Equal(0, result.SupplementalGamingTax);
        Assert.Equal(0, result.OtherGamingRevenueCharges);
        Assert.Equal(10_900_000m, result.HostMunicipalityShare);
        Assert.Equal(8_100_000m, result.HostStateShare);
        Assert.Equal(result.GrossGamingTax, result.HostMunicipalityShare + result.HostStateShare);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2600000")]
    public async Task DetroitDistributionCannotLeakToAnotherMunicipalityInWayneCounty(string? municipality)
    {
        await using var db = CreateDb();
        db.Jurisdictions.Add(new Jurisdiction { Code = "US", Kind = "federal", Name = "United States" });
        await db.SaveChangesAsync();
        await MichiganCommercialGamingRulesSeeder.SeedAsync(db);
        await Assert.ThrowsAsync<UnsupportedJurisdictionException>(() => new GamingFiscalAllocationCalculator(
            new JurisdictionProfileService(db), new LocationResolver(municipality)).CalculateAsync(
                new GamingFiscalAllocationRequest("US-MI", "commercial-casino", new(2025, 6, 30), 100_000_000m, 19_000_000m, 42.33, -83.05)));
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"michigan-commercial-{Guid.NewGuid():N}").Options);

    private sealed class LocationResolver(string? municipality) : ICandidateFiscalLocationResolver
    {
        public Task<CandidateFiscalLocation> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CandidateFiscalLocation("26", "26163", "Wayne", municipality, municipality == "2622000" ? "Detroit" : null));
    }
}
