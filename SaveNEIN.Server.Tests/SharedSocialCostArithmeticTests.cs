using System.Text.Json;
using SaveNEIN.Client.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Server.Tests;

public sealed class SharedSocialCostArithmeticTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4.5)]
    [InlineData(0.775)]
    [InlineData(200)]
    public void ServerAndWebAssemblyUseIdenticalDomainCosts(double cases)
    {
        SocialCostDomainValue[] domains = [new("treatment-health", 1328), new("crime-public-safety", 1625, 2)];
        var browser = EconomicModelInterop.CalculateSocialCosts(new(cases, domains, 0.8, 1.2));
        var server = new SocialCostService().Calculate(new(cases, 1, 1, 0.8, 1.2,
            domains.Select(domain => new SocialCostDomainInput(domain.DomainKey, domain.PerCaseCost, domain.Scale)).ToArray()));

        Assert.Equal(cases, server.IncrementalCases);
        Assert.Equal(server.AnnualCost, browser.AnnualCost);
        Assert.Equal(server.LowAnnualCost, browser.LowAnnualCost);
        Assert.Equal(server.HighAnnualCost, browser.HighAnnualCost);
        Assert.Equal(server.Domains.Count, browser.Domains.Count);
        foreach (var (serverDomain, browserDomain) in server.Domains.Zip(browser.Domains))
        {
            Assert.Equal(serverDomain.DomainKey, browserDomain.DomainKey);
            Assert.Equal(serverDomain.PerCaseCost, browserDomain.PerCaseCost);
            Assert.Equal(serverDomain.AnnualCost, browserDomain.AnnualCost);
            Assert.Equal(serverDomain.LowAnnualCost, browserDomain.LowAnnualCost);
            Assert.Equal(serverDomain.HighAnnualCost, browserDomain.HighAnnualCost);
        }
    }

    [Fact]
    public void FractionalCasesAreRetainedThroughCostingAndSerializedInterop()
    {
        const string json = """
            {"incrementalCases":4.5,"domains":[{"domainKey":"social","perCaseCost":13203,"scale":1}],
             "lowCaseMultiplier":0.8,"highCaseMultiplier":1.2}
            """;
        var request = JsonSerializer.Deserialize<SocialCostCalculationRequest>(json)!;
        var result = EconomicModelInterop.CalculateSocialCosts(request);
        Assert.Equal(59413.5, result.AnnualCost);
        Assert.Equal(47530.8, result.LowAnnualCost, 8);
        Assert.Equal(71296.2, result.HighAnnualCost, 8);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(59413.5, serialized.RootElement.GetProperty("annualCost").GetDouble());
        Assert.Equal("social", serialized.RootElement.GetProperty("domains")[0].GetProperty("domainKey").GetString());
    }

    [Fact]
    public void ExplicitZeroCostsAndScalesRemainZero()
    {
        var result = SocialCostArithmetic.Calculate(new(200,
            [new("zero-cost", 0), new("zero-scale", 1000, 0)], 0, 0));
        Assert.Equal(0, result.AnnualCost);
        Assert.Equal(0, result.LowAnnualCost);
        Assert.Equal(0, result.HighAnnualCost);
    }

    [Fact]
    public void InvalidDomainsAndUncertaintyAreRejected()
    {
        Assert.Throws<ArgumentException>(() => SocialCostArithmetic.Calculate(new(1, [new("same", 1), new("same", 2)])));
        Assert.Throws<ArgumentException>(() => SocialCostArithmetic.Calculate(new(1, [new(" ", 1)])));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialCostArithmetic.Calculate(new(1, [new("negative", -1)])));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialCostArithmetic.Calculate(new(1, [new("scale", 1, double.NaN)])));
        Assert.Throws<InvalidOperationException>(() => SocialCostArithmetic.Calculate(new(1, [new("domain", 1)], 2, 1)));
    }

    [Fact]
    public void OverflowIsRejectedInCasesDomainCostAndTotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialCostArithmetic.Calculate(new(double.PositiveInfinity, [])));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialCostArithmetic.Calculate(new(2, [new("domain", double.MaxValue)])));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialCostArithmetic.Calculate(new(1,
            [new("one", double.MaxValue), new("two", double.MaxValue)])));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SocialCostService().Calculate(new(double.MaxValue, 1, 2, 1, 1, [])));
    }
}
