using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class CalibrationRevenueScopeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] Origins = ["USA-ZCTA-11111", "USA-ZCTA-22222"];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static CalibrationScopeSource Source(string basis, string metric) => new(
        "https://example.org/synthetic-test-source", new string('a', 64), metric,
        new(2025, 1, 1), new(2025, 12, 31), basis, 2025);
    private static CalibrationRevenueScopeCase Resident() => new("case-a", CalibrationRevenueScope.OriginSetHash(Origins),
        Source("observed-origin-attributed-ggr", "land-based-ggr-usd"), "audit.json", new string('b', 64), 600);
    private static CalibrationRevenueScopeBundle Bundle(string scope, params CalibrationRevenueScopeCase[] rows) =>
        new("revenue-scope-v1", scope, 2025, Guid.NewGuid(), Guid.NewGuid(), rows);
    private static CalibrationRevenueScopeCase Total() => new("case-a", CalibrationRevenueScope.OriginSetHash(Origins),
        Source("independent-site-demand-coverage-audit", "nonresident-demand-inputs"), "audit.json", new string('b', 64),
        Tourism: [new("visitors", 100, 0.5, 1, 0.2, 0.5, 10)],
        Traffic: [new("corridor", 10, 100, 1, 1, 1, 0.1, 0.5, 0, 2)]);

    [Fact]
    public void ResidentObservationIsMatchedWithoutChangingRegulatorTotal()
    {
        var row = Resident();
        var scope = CalibrationRevenueScope.Reconcile(Bundle(CalibrationRevenueScope.ModeledOrigins, row), row, Origins, 1000);
        Assert.Equal(1000, scope.PropertyObservedGgr);
        Assert.Equal(600, scope.ComparableObservedGgr);
        Assert.Equal(600, scope.Predict(10, 60));
        Assert.Equal(0, scope.FixedNonresidentGgr);
    }

    [Theory]
    [InlineData("consultant-forecast", "land-based-ggr-usd")]
    [InlineData("observed-origin-attributed-ggr", "patron-count")]
    [InlineData("observed-patron-residency-share", "land-based-ggr-usd")]
    public void ForecastsAndVisitorCountsCannotBeUsedAsObservedResidentRevenue(string basis, string metric)
    {
        var row = Resident() with { Source = Source(basis, metric) };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.ModeledOrigins, row), row, Origins, 1000));
    }

    [Fact]
    public void ResidentScopeRequiresTheExactOriginMembershipAndPeriod()
    {
        var row = Resident();
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.ModeledOrigins, row), row, ["USA-ZCTA-33333"], 1000));
        var stale = row with { Source = row.Source with { PeriodStart = new(2024, 1, 1) } };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.ModeledOrigins, stale), stale, Origins, 1000));
        var differentDollars = row with { Source = row.Source with { DollarYear = 2024 } };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.ModeledOrigins, differentDollars), differentDollars, Origins, 1000));
        Assert.Equal(CalibrationRevenueScope.OriginSetHash(Origins), CalibrationRevenueScope.OriginSetHash(Origins.Reverse()));
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.OriginSetHash([Origins[0], Origins[0]]));
    }

    [Fact]
    public void ComponentRevenueCannotExceedTheOriginalPropertyObservation()
    {
        var row = Resident() with { ObservedOriginGgr = 1001 };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.ModeledOrigins, row), row, Origins, 1000));
    }

    [Fact]
    public void FullRevenueUsesExistingDeduplicationEquationsAndKeepsVisitorRevenueOutsideTheSpendingFit()
    {
        var row = Total();
        var scope = CalibrationRevenueScope.Reconcile(Bundle(CalibrationRevenueScope.TotalProperty, row), row, Origins, 1000);
        Assert.Equal(50, scope.TourismGgr);
        Assert.Equal(100, scope.TrafficGgr);
        Assert.Equal(1000, scope.ComparableObservedGgr);
        var scale = CalibrationDevelopmentSelection.FitTrainingWapeScaleWithOffsets(
            [(1000, 10, scope.FixedNonresidentGgr), (2000, 20, 2 * scope.FixedNonresidentGgr)], 1, 200);
        Assert.Equal(85, scale);
        Assert.Equal(1000, scope.Predict(10, scale));
        Assert.Equal(1850, scope.Predict(20, scale));
        Assert.Equal(100, CalibrationDevelopmentSelection.FitTrainingWapeScale([(1000, 10), (2000, 20)], 1, 200));
    }

    [Fact]
    public void MissingNonresidentEvidenceCannotBecomeAnAssumedZeroOrAResidentTargetAdjustment()
    {
        var missing = Total() with { Tourism = null, Traffic = null };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.TotalProperty, missing), missing, Origins, 1000));
        var adjusted = Total() with { ObservedOriginGgr = 600 };
        Assert.Throws<InvalidDataException>(() => CalibrationRevenueScope.Reconcile(
            Bundle(CalibrationRevenueScope.TotalProperty, adjusted), adjusted, Origins, 1000));
    }

    [Fact]
    public void ExcessVisitorPredictionRemainsAnErrorRatherThanDroppingTheCase()
    {
        var scale = CalibrationDevelopmentSelection.FitTrainingWapeScaleWithOffsets([(100, 1, 500)], 1, 200, true);
        Assert.Equal(1, scale);
        Assert.False(CalibrationDevelopmentSelection.Evaluate([
            new("t1", "training", "a", "IN", 100, 500 + scale),
            new("t2", "training", "a", "IN", 100, 100),
            new("s1", "selection", "b", "OH", 100, 100)]).Admitted);
    }

    [Fact]
    public async Task MissingScopeEvidenceStopsBeforeDatabaseRoutingAndFitting()
    {
        var directory = Directory.CreateTempSubdirectory("savenein-revenue-scope-");
        var input = Path.Combine(directory.FullName, "protocol.json");
        var output = Path.Combine(directory.FullName, "result.json");
        try
        {
            await File.WriteAllTextAsync(input, """
                {"selectionPolicyVersion":"gravity-development-selection-v1","competitorSelectionVersion":"origin-union-v2",
                 "training":["t1","t2"],"selection":["s1"],"holdout":{},
                 "developmentMarkets":{"one":["t1","t2"],"two":["s1"]}}
                """);
            await new MultimarketRevenueCalibration(null!, null!).RunAsync(input, output, evaluateFinalHoldout: false);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.Equal("not-scored-revenue-scope-unreconciled", result.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, result.RootElement.GetProperty("candidateCount").GetInt32());
            Assert.False(result.RootElement.GetProperty("accuracyMetricsValid").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("fittedDemandScale").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("selectionMetrics").ValueKind);
        }
        finally { File.Delete(input); File.Delete(output); directory.Delete(); }
    }

    [Fact]
    public async Task ScopeBundleAndAuditMustMatchFrozenBytes()
    {
        var directory = Directory.CreateTempSubdirectory("savenein-revenue-scope-evidence-");
        var path = Path.Combine(directory.FullName, "evidence.json");
        var auditPath = Path.Combine(directory.FullName, "audit.json");
        try
        {
            var audit = Encoding.UTF8.GetBytes("synthetic scope-audit fixture");
            await File.WriteAllBytesAsync(auditPath, audit);
            var row = Resident() with { ScopeAuditSha256 = Hash(audit) };
            var bundle = Bundle(CalibrationRevenueScope.ModeledOrigins, row);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, Options);
            await File.WriteAllBytesAsync(path, bytes);
            var protocol = JsonSerializer.Deserialize<MultimarketCalibrationProtocol>("{}", Options)! with
            {
                Training = [row.CaseKey], Selection = [], Holdout = [],
                RevenueComparisonScope = bundle.ComparisonScope, RevenueScopeEvidencePath = "evidence.json",
                RevenueScopeEvidenceSha256 = Hash(bytes), ObservedYear = 2025,
                OriginSnapshotId = bundle.OriginSnapshotId, PerformanceSnapshotId = bundle.PerformanceSnapshotId
            };
            var protocolPath = Path.Combine(directory.FullName, "protocol.json");
            Assert.Single((await CalibrationRevenueScope.LoadAsync(protocol, protocolPath, default)).Cases);
            await File.AppendAllTextAsync(auditPath, "changed");
            await Assert.ThrowsAsync<InvalidDataException>(() => CalibrationRevenueScope.LoadAsync(protocol, protocolPath, default));
            await File.WriteAllBytesAsync(auditPath, audit);
            await File.AppendAllTextAsync(path, " ");
            await Assert.ThrowsAsync<InvalidDataException>(() => CalibrationRevenueScope.LoadAsync(protocol, protocolPath, default));
            var escaped = bundle with { Cases = [row with { ScopeAuditPath = "../outside-audit.json" }] };
            var escapedBytes = JsonSerializer.SerializeToUtf8Bytes(escaped, Options);
            await File.WriteAllBytesAsync(path, escapedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => CalibrationRevenueScope.LoadAsync(
                protocol with { RevenueScopeEvidenceSha256 = Hash(escapedBytes) }, protocolPath, default));
        }
        finally { File.Delete(path); File.Delete(auditPath); directory.Delete(); }
    }
}
