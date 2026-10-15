using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public class PennsylvaniaGamingFacilityProviderTests
{
    private const string Profile = "RIVERS CASINO PITTSBURGH\nAs of June 30, 2025\n1,575 Total Employment\n507 Table Games Employees\n" +
        "Average # of Machines in Play 1,988\nNon-Banked (30) $5,286,866\nBanked (92) $66,276,407\nFully Automated (0) $0\nHybrid (11) $2,356,409";

    [Fact]
    public void UsesPhysicalCountsWithoutMistakingEmploymentOrRevenueForUnits()
    {
        var counts = PennsylvaniaGamingFacilityProvider.ParsePhysicalCounts(Profile);
        Assert.Equal(1988, counts.Machines);
        Assert.Equal(133, counts.Tables);
        Assert.Equal(30, counts.NonBankedTables);
    }

    [Fact]
    public void RejectsAProfileForAnotherYear() => Assert.Throws<InvalidDataException>(() =>
        PennsylvaniaGamingFacilityProvider.ParsePhysicalCounts(Profile.Replace("2025", "2026", StringComparison.Ordinal)));

    [Fact]
    public void DoesNotSubstituteEmployeeCountsWhenMachinesAreMissing() => Assert.Throws<InvalidDataException>(() =>
        PennsylvaniaGamingFacilityProvider.ParsePhysicalCounts(Profile.Replace("Average # of Machines in Play 1,988", "", StringComparison.Ordinal)));

    [Fact]
    public void DoesNotSilentlyTruncateFractionalReportedMachines() => Assert.Throws<InvalidDataException>(() =>
        PennsylvaniaGamingFacilityProvider.ParsePhysicalCounts(Profile.Replace("Play 1,988", "Play 1,988.5", StringComparison.Ordinal)));
}
