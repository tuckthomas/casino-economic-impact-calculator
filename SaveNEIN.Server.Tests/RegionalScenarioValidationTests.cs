using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class RegionalScenarioValidationTests
{
    private static RegionalValidationProtocol Protocol() => new("test", "Configured regional suite", "US-IN", "18", new('a', 64),
        new[] { "origin-geography", "age-population", "income", "competitors", "observed-performance" }
            .ToDictionary(key => key, _ => new RegionalSnapshotReference(Guid.NewGuid(), new('b', 64))),
        new() { ["gravity.beta"] = 1.5 }, new() { ["gravity.beta"] = 1.5 },
        new("assumed", "1", "Common assumed program", 2000, 50, 0, false, 200, 60000, 5, 0, 0, null, null, null, 3, "Assumed"),
        [new("regional-case", "18033", 41.3666, -85.0815, "Analyst intersection proxy", 2030, "study", new('c', 64), null, "annual.ggr")],
        2022, 2022, 2025, new(2026, 10, 13), 75, 300, GravityDemandSpecifications.EligibleAdultPerCapita);

    [Fact]
    public void DoesNotInventAResidentForecastWhenTheStudyOnlyReportsTotal()
    {
        var protocol = Protocol();
        RegionalScenarioValidationService.ValidateProtocol(protocol);
        Assert.Null(protocol.Scenarios[0].ResidentForecastPath);
    }

    [Fact]
    public void RegionalValidationUsesConfiguredJurisdictionInsteadOfHardCodedIndiana()
    {
        var protocol = Protocol();
        RegionalScenarioValidationService.ValidateProtocol(protocol with
        {
            JurisdictionCode = "US-OH", StateFips = "39",
            Scenarios = [protocol.Scenarios[0] with { CountyFips = "39061" }]
        });
    }

    [Fact]
    public void FixedRegionalReferencePreservesAnOriginsBudgetAcrossCandidateMarkets()
    {
        var service = new OriginDemandService();
        var first = GravityModelExecutionService.ResolveReferenceIncome(80_000, [40_000, 60_000, 80_000]);
        var second = GravityModelExecutionService.ResolveReferenceIncome(80_000, [80_000, 100_000, 120_000]);
        var a = service.CalculatePerCapita(new("shared-origin", 100, 370, 80_000, first, 1, 0.5, 1.5));
        var b = service.CalculatePerCapita(new("shared-origin", 100, 370, 80_000, second, 1, 0.5, 1.5));
        Assert.Equal(37_000, a.Demand);
        Assert.Equal(a.Demand, b.Demand);
        Assert.Equal(60_000, GravityModelExecutionService.ResolveReferenceIncome(0, [40_000, 60_000, 80_000]));
        Assert.Throws<InvalidOperationException>(() => GravityModelExecutionService.ResolveReferenceIncome(double.NaN, [80_000]));
        Assert.Throws<InvalidOperationException>(() => GravityModelExecutionService.ResolveReferenceIncome(-1, [80_000]));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("snapshot")]
    [InlineData("duplicate-snapshot")]
    [InlineData("county")]
    [InlineData("location")]
    [InlineData("population-year")]
    [InlineData("duplicate-case")]
    [InlineData("parameters")]
    [InlineData("radius")]
    public void RejectsUnfrozenOrInconsistentRegionalInputs(string defect)
    {
        var protocol = Protocol();
        protocol = defect switch
        {
            "scope" => protocol with { Scope = "" },
            "snapshot" => protocol with { Snapshots = [] },
            "duplicate-snapshot" => protocol with { Snapshots = protocol.Snapshots.ToDictionary(pair => pair.Key, _ => protocol.Snapshots["income"]) },
            "county" => protocol with { Scenarios = [protocol.Scenarios[0] with { CountyFips = "39001" }] },
            "location" => protocol with { Scenarios = [protocol.Scenarios[0] with { Latitude = double.NaN }] },
            "population-year" => protocol with { Scenarios = [protocol.Scenarios[0] with { PopulationScenarioYear = 2021 }] },
            "duplicate-case" => protocol with { Scenarios = [protocol.Scenarios[0], protocol.Scenarios[0]] },
            "parameters" => protocol with { ExpectedParameters = new() { ["gravity.beta"] = double.PositiveInfinity } },
            _ => protocol with { OriginRadiusMiles = 0 }
        };
        Assert.Throws<InvalidDataException>(() => RegionalScenarioValidationService.ValidateProtocol(protocol));
    }

    [Fact]
    public async Task MissingFrozenSnapshotsPreventEveryScenarioExecution()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var executor = new NeverExecute();
        var protocolPath = Path.GetTempFileName();
        var outputPath = protocolPath + ".result";
        try
        {
            await File.WriteAllTextAsync(protocolPath, JsonSerializer.Serialize(Protocol(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await Assert.ThrowsAsync<InvalidDataException>(() => new RegionalScenarioValidationService(db, executor).RunAsync(protocolPath, outputPath));
            Assert.Equal(0, executor.Calls);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(protocolPath);
            File.Delete(outputPath);
        }
    }

    private sealed class NeverExecute : IGravityModelExecutionService
    {
        public int Calls { get; private set; }
        public Task<GravityModelRunResult> ExecuteAsync(GravityModelRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("No regional run may execute with missing frozen inputs.");
        }
    }
}
