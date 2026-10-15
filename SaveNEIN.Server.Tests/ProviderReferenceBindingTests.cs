using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class ProviderReferenceBindingTests
{
    [Fact]
    public async Task SameRevenueSourceCanBindToRefreshedInventoryWithoutChangingOldRowsOrSourceHash()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        foreach (var id in new[] { first, second })
            db.DatasetSnapshots.Add(new DatasetSnapshot { Id = id, DatasetKey = DatasetSnapshotKinds.Competitors,
                Checksum = id.ToString("N"), TransformVersion = "fixture", Period = "2025", RowCount = 1,
                IsSealed = true, ValidationState = DatasetValidationStates.Validated });
        db.CasinoCompetitors.AddRange(new CasinoCompetitor { Id = 1, StableVenueId = "venue", DatasetSnapshotId = first, Geom = new Point(-85, 41) },
            new CasinoCompetitor { Id = 2, StableVenueId = "venue", DatasetSnapshotId = second, Geom = new Point(-85, 41) });
        await db.SaveChangesAsync();
        var service = new ProviderSnapshotIngestionService(new DataSnapshotService(db), new ModelDataIngestionService(db));
        var provider = new FrozenRevenue();
        var request = new ProviderFetchRequest("US-IL", new(2025, 1, 1), new(2025, 12, 31));
        var firstRevenue = await service.IngestGamingPerformanceAsync(provider, request, first);
        var secondRevenue = await service.IngestGamingPerformanceAsync(provider, request, second);
        var oldRow = db.CasinoGamingRevenuePeriods.Single(row => row.DatasetSnapshotId == firstRevenue);
        var newRow = db.CasinoGamingRevenuePeriods.Single(row => row.DatasetSnapshotId == secondRevenue);
        Assert.Equal(1, oldRow.CasinoCompetitorId);
        Assert.Equal(2, newRow.CasinoCompetitorId);
        Assert.Equal(oldRow.ReportedAmount, newRow.ReportedAmount);
        Assert.Equal(new string('a', 64), Assert.Single(db.DataSources).ContentHash);
        Assert.NotEqual(db.DatasetSnapshots.Single(row => row.Id == firstRevenue).Checksum,
            db.DatasetSnapshots.Single(row => row.Id == secondRevenue).Checksum);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestGamingPerformanceAsync(provider, request, first));
    }

    private sealed class FrozenRevenue : IGamingRegulatorPerformanceProvider
    {
        public string ProviderKey => "fixture";
        public string GeographicCoverage => "US-IL";
        public Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(ProviderFetchRequest request, CancellationToken cancellationToken = default)
        {
            var hash = new string('a', 64);
            return Task.FromResult(new ProviderDataset<CasinoGamingRevenueImportRow>(
                new RegisterDataSourceRequest("fixture", "fixture", "https://example.test/report", "csv", "US-IL", "2025", DateTime.UtcNow, hash, true, null, null),
                DatasetSnapshotKinds.ObservedPerformance, "2025", request.PeriodStart, request.PeriodEnd, hash, "fixture-v1",
                [new CasinoGamingRevenueImportRow("venue", request.PeriodStart, request.PeriodEnd, "annual",
                    GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue, "fixture AGR", 100, null, null, [], null)], []));
        }
    }
}
