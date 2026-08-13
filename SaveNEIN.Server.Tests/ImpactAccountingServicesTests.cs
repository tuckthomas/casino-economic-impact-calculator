using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class ImpactAccountingServicesTests
{
    [Fact]
    public void CannibalizationAccounting_KeepsTransfersAndIncrementalSourcesSeparate()
    {
        var result = new CannibalizationAccountingService().Calculate(new CannibalizationAccountingInput(
            100, 200, 50, 25, 30, 40, 10));

        Assert.Equal(250, result.CrossJurisdictionCapture, 8);
        Assert.Equal(375, result.TransferEffectGgr, 8);
        Assert.Equal(80, result.MarketExpansionAndImportGgr, 8);
        Assert.Equal(455, result.StabilizedGgr, 8);
    }

    [Fact]
    public void Displacement_ExcludesCasinoTransfersAndRepatriatedLeakageBeforeSectorAllocation()
    {
        var result = new DisplacementModelService().Calculate(new DisplacementInput(
            1_000,
            300,
            400,
            0.5,
            0.4,
            [
                new DisplacementSectorInput("restaurant-hospitality", 3, 1, 0.8, 0.2, 0.07, 0.05, 100),
                new DisplacementSectorInput("arts-entertainment-recreation", 1, 1, 1, 0.1, 0.07, 0.05, 100)
            ]));

        Assert.Equal(300, result.RemainingLocalResidentGamingBase, 8);
        Assert.Equal(150, result.DisplacementEligibleBase, 8);
        Assert.Equal(60, result.TotalDisplacedSales, 8);
        Assert.Equal(45, result.Sectors[0].DisplacedSales, 8);
        Assert.Equal(15, result.Sectors[1].DisplacedSales, 8);
        Assert.Equal(1, result.Sectors.Sum(sector => sector.NormalizedWeight), 8);
    }

    [Fact]
    public void Employment_ReportsGrossJobsLossesAndNetPermanentJobsSeparately()
    {
        var sectors = new[]
        {
            new SectorDisplacementResult("a", 0.5, 1, 1, 1, 1, 1, 10),
            new SectorDisplacementResult("b", 0.5, 1, 1, 1, 1, 1, 20)
        };
        var result = new EmploymentImpactService().Calculate(new EmploymentImpactInput(
            100_000_000,
            500_000_000,
            20_000_000,
            4,
            2,
            0.5,
            3,
            50_000,
            40_000,
            45_000,
            sectors));

        Assert.Equal(400, result.DirectCasinoJobs, 8);
        Assert.Equal(1_000, result.ConstructionJobYears, 8);
        Assert.Equal(200, result.IndirectAndInducedJobs, 8);
        Assert.Equal(30, result.DisplacedSectorJobs, 8);
        Assert.Equal(60, result.IncumbentCasinoJobsLost, 8);
        Assert.Equal(510, result.NetPermanentJobs, 8);
    }

    [Fact]
    public void FiscalImpact_DistinguishesGrossReceiptsFromLossesAndNetEffects()
    {
        var result = new FiscalImpactService().Calculate(new FiscalImpactInput(
            25, 5, 2, 3, 1, 24, 2, 3, 1, 4, 1, 0.5, 2, 3));

        Assert.Equal(25, result.BaseGamingTax, 8);
        Assert.Equal(5, result.SupplementalGamingTax, 8);
        Assert.Equal(30, result.GrossGamingTax, 8);
        Assert.Equal(9, result.HostLocalGrossPublicRevenue, 8);
        Assert.Equal(31, result.HostStateGrossPublicRevenue, 8);
        Assert.Equal(1.5, result.DisplacedLocalFiscalLoss, 8);
        Assert.Equal(7.5, result.NetHostLocalFiscalImpact, 8);
        Assert.Equal(29, result.NetHostStateFiscalImpact, 8);
        Assert.Equal(-3, result.OtherJurisdictionFiscalImpact, 8);
    }

    [Fact]
    public void SocialCost_PersistsNonoverlappingDomainsAndUncertaintyRange()
    {
        var result = new SocialCostService().Calculate(new SocialCostInput(
            100_000,
            0.02,
            0.1,
            0.8,
            1.2,
            [
                new SocialCostDomainInput("treatment-health", 1_000),
                new SocialCostDomainInput("crime-public-safety", 500, 2)
            ]));

        Assert.Equal(200, result.IncrementalCases, 8);
        Assert.Equal(400_000, result.AnnualCost, 8);
        Assert.Equal(320_000, result.LowAnnualCost, 8);
        Assert.Equal(480_000, result.HighAnnualCost, 8);
        Assert.Equal(2, result.Domains.Count);
    }

    [Fact]
    public void NetImpact_UsesAnExplicitBridgeAndExcludesUnclassifiedOutsideCapture()
    {
        var result = new NetImpactService().Calculate(new NetImpactInput(
            300, 150, 100, 50, 20, 30, 10, 25, 5, 8, 15));

        Assert.Equal(140, result.NetNewLocalGamingActivity, 8);
        Assert.Equal(130, result.NetHostLocalImpact, 8);
        Assert.Equal(133, result.NetHostStateImpact, 8);
        Assert.Equal(50, result.OutsideOrUnmodeledLeakageCapture, 8);
    }

    [Fact]
    public void ImpactServices_RejectInvalidSharesOrOverallocatedLocalRevenue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplacementModelService().Calculate(
            new DisplacementInput(1, 0, 0, 1.1, 0.5,
                [new DisplacementSectorInput("sector", 1, 1, 1, 1, 0, 0, 1)])));
        Assert.Throws<InvalidOperationException>(() => new FiscalImpactService().Calculate(
            new FiscalImpactInput(1, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }
}
