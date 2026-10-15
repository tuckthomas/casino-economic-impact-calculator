using System.Text.Json;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class ModelReleaseAcceptanceTests
{
    [Fact]
    public void CompleteIndependentSamplePassesAndPersistsMetrics()
    {
        var (cases, observations, evidence) = Fixture();
        var report = Evaluate(cases, observations, evidence);
        Assert.True(report.Accepted, string.Join("; ", report.Gates.Where(item => !item.Passed).Select(item => item.Key)));
        Assert.Equal(10, report.Wape);
        Assert.Equal(0, report.NormalizedSignedBias);
        Assert.Equal(12, report.DistinctHoldoutProperties);
        var json = ModelReleaseAcceptance.Persist("{\"originalRule\":true}", report, evidence);
        Assert.True(ModelReleaseAcceptance.HasAcceptedReport(json));
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("originalRule").GetBoolean());
    }

    [Fact]
    public void GoodAverageCannotHideOneLargePropertyFailure()
    {
        var (cases, observations, evidence) = Fixture();
        observations = observations.Select(item => item.CaseKey == "holdout-0" ? item with { Predicted = 150 } : item with { Predicted = item.Observed }).ToArray();
        var report = Evaluate(cases, observations, evidence);
        Assert.True(report.Mape < 20);
        Assert.True(report.Wape < 15);
        Assert.False(report.Accepted);
        Assert.Contains(report.Gates, item => item.Key == "property-maximum" && !item.Passed);
    }

    [Fact]
    public void CancelledOverallBiasCannotHideGeographicBias()
    {
        var (cases, observations, evidence) = Fixture();
        var market = cases.ToDictionary(item => item.CaseKey, item => item.MarketCode);
        observations = observations.Select(item => item with { Predicted = market[item.CaseKey] switch { "market-0" => 120, "market-1" => 80, _ => 100 } }).ToArray();
        var report = Evaluate(cases, observations, evidence);
        Assert.Equal(0, report.NormalizedSignedBias);
        Assert.False(report.Accepted);
        Assert.Contains(report.Gates, item => item.Key == "market:market-0:error" && !item.Passed);
    }

    [Fact]
    public void RepeatedPropertyYearsDoNotInflateCoverage()
    {
        var (cases, observations, evidence) = Fixture();
        foreach (var item in cases.Where(item => item.DatasetPartition == ValidationPartitions.Holdout)) item.TargetCasinoCompetitorId = 1;
        var report = Evaluate(cases, observations, evidence);
        Assert.Equal(1, report.DistinctHoldoutProperties);
        Assert.Contains(report.Gates, item => item.Key == "holdout-properties" && !item.Passed);
    }

    [Fact]
    public void SnapshotSpecificIdsForTheSameVenueDoNotInflateCoverage()
    {
        var (cases, observations, evidence) = Fixture();
        var identities = evidence.EligiblePropertyIds.ToDictionary(id => id, id => id <= 12 ? 1 : id);
        var report = ModelReleaseAcceptance.Evaluate(cases, observations, evidence, RunCreated, [], identities);
        Assert.Equal(1, report.DistinctHoldoutProperties);
        Assert.False(report.Accepted);
    }

    [Fact]
    public void MissingEvidenceCannotPublishEvenWithZeroError()
    {
        var (cases, observations, _) = Fixture();
        observations = observations.Select(item => item with { Predicted = item.Observed }).ToArray();
        var report = Evaluate(cases, observations, null);
        Assert.Equal(0, report.Wape);
        Assert.False(report.Accepted);
        Assert.Contains(report.Gates, item => item.Key == "frozen-evidence" && !item.Passed);
        Assert.Contains(report.Gates, item => item.Key == "population-coverage" && !item.Passed);
    }

    [Theory]
    [InlineData("population")]
    [InlineData("competitors")]
    [InlineData("routes")]
    [InlineData("leakage")]
    [InlineData("reproducibility")]
    [InlineData("late-freeze")]
    [InlineData("baseline")]
    public void IncompleteOrPostHocEvidenceBlocksAcceptance(string defect)
    {
        var (cases, observations, evidence) = Fixture();
        evidence = defect switch
        {
            "population" => evidence with { CoveredEligiblePopulation = 98_999 },
            "competitors" => evidence with { IncludedCompetitorCount = 68 },
            "routes" => evidence with { FailedRouteOrCaseCount = 1 },
            "leakage" => evidence with { TargetRevenueLeakageExcluded = false },
            "reproducibility" => evidence with { ReproducibilityVerified = false },
            "late-freeze" => evidence with { FrozenAtUtc = RunCreated.AddDays(1) },
            _ => evidence with { BaselinePredictions = observations.ToDictionary(item => item.CaseKey, item => item.Predicted) }
        };
        Assert.False(Evaluate(cases, observations, evidence).Accepted);
    }

    [Fact]
    public void ZeroObservedCasesRemainInWeightedErrorWithoutCountingAsPositiveHoldoutProperties()
    {
        var (cases, observations, evidence) = Fixture();
        cases.Add(new ValidationCase { CaseKey = "zero", DatasetPartition = ValidationPartitions.Holdout, TargetCasinoCompetitorId = 13, MarketCode = "market-0", JurisdictionCode = "US-IN", TrainingPeriodEnd = new DateOnly(2025, 12, 31) });
        observations = observations.Append(new("zero", 0, 120)).ToArray();
        var report = Evaluate(cases, observations, evidence);
        Assert.Equal(20, report.Wape);
        Assert.Equal(12, report.DistinctHoldoutProperties);
        Assert.False(report.Accepted);
    }

    [Fact]
    public void InspectedHoldoutOrPropertyLeakageCannotPass()
    {
        var (cases, observations, evidence) = Fixture();
        var report = ModelReleaseAcceptance.Evaluate(cases, observations, evidence, RunCreated, [1]);
        Assert.Contains(report.Gates, item => item.Key == "fresh-holdout" && !item.Passed);
        cases.First(item => item.DatasetPartition == ValidationPartitions.Training).TargetCasinoCompetitorId = 1;
        Assert.Contains(Evaluate(cases, observations, evidence).Gates, item => item.Key == "property-partition-isolation" && !item.Passed);
    }

    [Fact]
    public void LegacyOrMalformedAssessmentCannotEnableEnsemble()
    {
        Assert.False(ModelReleaseAcceptance.HasAcceptedReport("{}"));
        Assert.False(ModelReleaseAcceptance.HasAcceptedReport("invalid-json"));
        Assert.False(ModelReleaseAcceptance.HasAcceptedReport("{\"releaseAcceptance\":{\"accepted\":true,\"policyVersion\":\"gravity-economic-acceptance-v1\",\"gates\":[]}}"));
    }

    internal static ReleaseAcceptanceReport PassingReport()
    {
        var (cases, observations, evidence) = Fixture();
        return Evaluate(cases, observations, evidence);
    }

    private static readonly DateTime RunCreated = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
    private static ReleaseAcceptanceReport Evaluate(List<ValidationCase> cases, ValidationObservation[] observations, ReleaseAcceptanceEvidence? evidence) =>
        ModelReleaseAcceptance.Evaluate(cases, observations, evidence, RunCreated, []);

    private static (List<ValidationCase>, ValidationObservation[], ReleaseAcceptanceEvidence) Fixture()
    {
        var cases = Enumerable.Range(0, 12).Select(index => new ValidationCase
        {
            CaseKey = $"holdout-{index}", TargetCasinoCompetitorId = index + 1,
            DatasetPartition = ValidationPartitions.Holdout, MarketCode = $"market-{index / 4}",
            JurisdictionCode = index is < 4 or 8 or 9 ? "US-IN" : "US-OH", ObservedRevenue = 100,
            TrainingPeriodEnd = new DateOnly(2025, 12, 31)
        }).ToList();
        cases.Add(new ValidationCase { CaseKey = "training", TargetCasinoCompetitorId = 100, DatasetPartition = ValidationPartitions.Training, MarketCode = "development", JurisdictionCode = "US-IN", ObservedRevenue = 100 });
        cases.Add(new ValidationCase { CaseKey = "training-2", TargetCasinoCompetitorId = 102, DatasetPartition = ValidationPartitions.Training, MarketCode = "development", JurisdictionCode = "US-IN", ObservedRevenue = 100 });
        cases.Add(new ValidationCase { CaseKey = "selection", TargetCasinoCompetitorId = 101, DatasetPartition = DemandValidationPartitions.Selection, MarketCode = "development", JurisdictionCode = "US-IN", ObservedRevenue = 100 });
        var observations = cases.Select((item, index) => new ValidationObservation(item.CaseKey, 100, index % 2 == 0 ? 110 : 90)).ToArray();
        var evidence = new ReleaseAcceptanceEvidence(ModelReleaseAcceptance.PolicyVersion,
            RunCreated.AddDays(-2), RunCreated.AddDays(-1), cases.Select(item => item.TargetCasinoCompetitorId!.Value).ToArray(),
            observations.ToDictionary(item => item.CaseKey, _ => 130d), 100_000, 100_000, 69, 69, 0,
            true, true, true, new string('a', 64), new string('b', 64));
        return (cases, observations, evidence);
    }
}
