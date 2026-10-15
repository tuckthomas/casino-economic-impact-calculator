using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class MissouriGamingReportTests
{
    private const string Page = """
        MISSOURI GAMING COMMISSION
        MONTH ENDED: DECEMBER 2025
        BOAT: ST. JOSEPH
        TOTAL TABLE GAMES: 5 354,271.00 80,755.00 22.795%
        TOTAL HYBRID: 6 308,244.44 19,183.29 93.777%
        TOTAL SLOTS: 446 37,153,904.94 3,991,766.31 89.256%
        TOTAL AGR FOR MONTH: 4,091,704.60
        """;

    [Fact]
    public void ReconcilesAllThreeRevenueComponentsWithoutTreatingWagersAsRevenue()
    {
        var row = Assert.Single(MissouriGamingCommissionProvider.ParseMonthlyPages([Page], new(2025, 12, 1))).Value;
        Assert.Equal(5, row.Tables);
        Assert.Equal(446, row.Slots);
        Assert.Equal(6, row.HybridUnits);
        Assert.Equal(4_091_704.60m, row.Agr);
    }

    [Fact]
    public void RejectsWrongMonthMissingTotalsDuplicatedPropertyAndNonreconcilingAgr()
    {
        Assert.Throws<InvalidDataException>(() => MissouriGamingCommissionProvider.ParseMonthlyPages([Page], new(2025, 11, 1)));
        Assert.Throws<InvalidDataException>(() => MissouriGamingCommissionProvider.ParseMonthlyPages([Page, Page], new(2025, 12, 1)));
        Assert.Throws<InvalidDataException>(() => MissouriGamingCommissionProvider.ParseMonthlyPages([Page.Replace("TOTAL SLOTS:", "MISSING:")], new(2025, 12, 1)));
        Assert.Throws<InvalidDataException>(() => MissouriGamingCommissionProvider.ParseMonthlyPages([Page.Replace("4,091,704.60", "4,091,705.60")], new(2025, 12, 1)));
    }

    [Fact]
    public void HandlesLegitimateNegativeComponentAndNoHybridSection()
    {
        var page = """
            MONTH ENDED: JANUARY 2025
            BOAT: HOLLYWOOD
            TOTAL TABLE GAMES: 2 200.00 (5.00) 0.00%
            TOTAL SLOTS: 100 5,000.00 105.00 90.00%
            TOTAL AGR FOR MONTH: 100.00
            """;
        var row = Assert.Single(MissouriGamingCommissionProvider.ParseMonthlyPages([page], new(2025, 1, 1))).Value;
        Assert.Equal(100m, row.Agr);
        Assert.Equal(0, row.HybridUnits);
    }

    [Fact]
    public void HandlesPrintedTotalsImmediatelyAboveTheirLabels()
    {
        var page = """
            MONTH ENDED: JANUARY 2025
            BOAT: HORSESHOE ST. LOUIS
            TOTAL TABLE GAMES: 30 200.00 1,457,745.50 10.00%
            868 89,372,629.09 9,200,834.60 89.705%
            TOTAL SLOTS:
            10,658,580.10
            TOTAL AGR FOR MONTH:
            """;
        var row = Assert.Single(MissouriGamingCommissionProvider.ParseMonthlyPages([page], new(2025, 1, 1))).Value;
        Assert.Equal(868, row.Slots);
        Assert.Equal(10_658_580.10m, row.Agr);
    }

    [Fact]
    public void FlagsNonadditiveHybridSubtotalAndPreservesPublishedAgr()
    {
        var page = Page.Replace("4,091,704.60", "4,072,521.31", StringComparison.Ordinal);
        var row = Assert.Single(MissouriGamingCommissionProvider.ParseMonthlyPages([page], new(2025, 12, 1))).Value;
        Assert.True(row.HybridNonAdditive);
        Assert.Equal(4_072_521.31m, row.Agr);
    }

    [Fact]
    public void ReassemblesContinuationPageBeforeScoringAProperty()
    {
        var split = Page.IndexOf("TOTAL SLOTS:", StringComparison.Ordinal);
        var row = Assert.Single(MissouriGamingCommissionProvider.ParseMonthlyPages([Page[..split], Page[split..]], new(2025, 12, 1))).Value;
        Assert.Equal(446, row.Slots);
        Assert.Equal(4_091_704.60m, row.Agr);
    }
}
