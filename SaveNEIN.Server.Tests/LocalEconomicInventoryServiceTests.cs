using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class LocalEconomicInventoryServiceTests
{
    private static readonly IReadOnlyDictionary<string, double> Priors =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DisplacementSectorKeys.RestaurantHospitality] = 0.4,
            [DisplacementSectorKeys.Retail] = 0.4,
            [DisplacementSectorKeys.ArtsEntertainmentRecreation] = 0.2
        };

    [Fact]
    public void Resolve_UsesProviderReceiptsToProduceObservedSectorComposition()
    {
        var service = new LocalEconomicInventoryWeightService();

        var result = service.Resolve(
            [
                Observation(DisplacementSectorKeys.RestaurantHospitality, receipts: 60),
                Observation(DisplacementSectorKeys.Retail, receipts: 30),
                Observation(DisplacementSectorKeys.ArtsEntertainmentRecreation, receipts: 10)
            ],
            "host-state",
            "US-IN",
            Priors,
            ConfiguredModifiers(),
            true);

        Assert.Equal("provider-snapshot:annual-receipts-or-sales:host-state:US-IN", result.WeightBasis);
        var weighted = Priors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value * result.Modifiers[pair.Key],
            StringComparer.Ordinal);
        var total = weighted.Values.Sum();
        Assert.Equal(0.6, weighted[DisplacementSectorKeys.RestaurantHospitality] / total, 10);
        Assert.Equal(0.3, weighted[DisplacementSectorKeys.Retail] / total, 10);
        Assert.Equal(0.1, weighted[DisplacementSectorKeys.ArtsEntertainmentRecreation] / total, 10);
    }

    [Fact]
    public void Resolve_FallsBackToCompleteEmploymentMeasureInsteadOfMixingUnits()
    {
        var service = new LocalEconomicInventoryWeightService();

        var result = service.Resolve(
            [
                Observation(DisplacementSectorKeys.RestaurantHospitality, receipts: 60, employment: 5),
                Observation(DisplacementSectorKeys.Retail, employment: 3),
                Observation(DisplacementSectorKeys.ArtsEntertainmentRecreation, receipts: 10, employment: 2)
            ],
            "host-state",
            "US-IN",
            Priors,
            ConfiguredModifiers(),
            true);

        Assert.Contains("provider-snapshot:employment", result.WeightBasis, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsSelectedSnapshotMissingARequiredSector()
    {
        var service = new LocalEconomicInventoryWeightService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Resolve(
            [
                Observation(DisplacementSectorKeys.RestaurantHospitality, receipts: 60),
                Observation(DisplacementSectorKeys.Retail, receipts: 30)
            ],
            "host-state",
            "US-IN",
            Priors,
            ConfiguredModifiers(),
            true));

        Assert.Contains(DisplacementSectorKeys.ArtsEntertainmentRecreation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LeavesVersionedParameterModifiersInControlWhenNoSnapshotSelected()
    {
        var service = new LocalEconomicInventoryWeightService();
        var configured = ConfiguredModifiers();

        var result = service.Resolve([], "host-state", "US-IN", Priors, configured, false);

        Assert.Same(configured, result.Modifiers);
        Assert.Equal("parameterized-prior-modifiers", result.WeightBasis);
    }

    [Fact]
    public void ResolveLaborAssumptions_UsesGeographyMatchedCbpPayrollPerEmployee()
    {
        var result = new LocalEconomicInventoryWeightService().ResolveLaborAssumptions(
            [
                Observation(LocalEconomicSectorKeys.CasinoGambling, employment: 100, payroll: 5_000_000),
                Observation(LocalEconomicSectorKeys.AllIndustries, employment: 1_000, payroll: 40_000_000)
            ],
            "host-state",
            "US-IN",
            1,
            2,
            3,
            true);

        Assert.Equal(50_000, result.DirectAverageAnnualWage);
        Assert.Equal(40_000, result.IndirectAverageAnnualWage);
        Assert.Equal(50_000, result.IncumbentAverageAnnualWage);
        Assert.Empty(result.Warnings);
        Assert.Contains("annual-payroll-per-employee:host-state:US-IN", result.AssumptionBasis, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveLaborAssumptions_DoesNotPretendStateCasinoWageIsCountySpecificWhenMissing()
    {
        var result = new LocalEconomicInventoryWeightService().ResolveLaborAssumptions(
            [Observation(
                LocalEconomicSectorKeys.AllIndustries,
                employment: 1_000,
                payroll: 40_000_000,
                geographyType: "host-county",
                geographyCode: "18003")],
            "host-county",
            "18003",
            45_000,
            35_000,
            46_000,
            true);

        Assert.Equal(45_000, result.DirectAverageAnnualWage);
        Assert.Equal(40_000, result.IndirectAverageAnnualWage);
        Assert.Equal(46_000, result.IncumbentAverageAnnualWage);
        Assert.Single(result.Warnings);
        Assert.Contains("remain at their versioned parameter values", result.Warnings.Single(), StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, double> ConfiguredModifiers() =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DisplacementSectorKeys.RestaurantHospitality] = 1,
            [DisplacementSectorKeys.Retail] = 1,
            [DisplacementSectorKeys.ArtsEntertainmentRecreation] = 1
        };

    private static LocalEconomicSectorObservation Observation(
        string sector,
        decimal? receipts = null,
        long? employment = null,
        decimal? payroll = null,
        string geographyType = "host-state",
        string geographyCode = "US-IN") => new()
    {
        StableObservationId = $"{sector}-fixture",
        GeographyType = geographyType,
        GeographyCode = geographyCode,
        SectorKey = sector,
        NaicsCodesJson = "[\"00\"]",
        PeriodStart = new DateOnly(2025, 1, 1),
        PeriodEnd = new DateOnly(2025, 12, 31),
        AnnualReceiptsOrSales = receipts,
        Employment = employment,
        AnnualPayroll = payroll,
        SourceMetricDefinition = "Fixture"
    };
}
