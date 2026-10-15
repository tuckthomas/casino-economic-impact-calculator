using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class CalibrationCompetitiveUniverseTests
{
    private static MultimarketCalibrationProtocol Protocol(string version) =>
        JsonSerializer.Deserialize<MultimarketCalibrationProtocol>(
            "{\"competitorRadiusMiles\":100,\"competitorSelectionVersion\":\"" + version + "\"}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static CasinoCompetitor Facility(int id, double longitude) => new()
    {
        Id = id, StableVenueId = "venue-" + id, Latitude = 0, Longitude = longitude,
        IsActive = true, HasSlots = true, Geom = new Point(longitude, 0) { SRID = 4326 }
    };

    [Fact]
    public void CalibrationIncludesAlternativesNearPatronsBeyondTheTargetCircle()
    {
        var target = Facility(1, 0);
        var nearbyAlternative = Facility(2, 2);
        OriginZone[] origins = [new() { RepresentativePoint = new Point(1, 0) { SRID = 4326 } }];
        CasinoCompetitor[] inventory = [target, nearbyAlternative];
        var date = new DateOnly(2025, 12, 31);

        Assert.Empty(MultimarketRevenueCalibration.SelectCasePeers(Protocol("target-radius-v1"), inventory, target, origins, date));
        var corrected = MultimarketRevenueCalibration.SelectCasePeers(Protocol("origin-union-v2"), inventory, target, origins, date);
        Assert.Equal([nearbyAlternative.Id], corrected.Select(row => row.Id));
        Assert.Equal(CompetitiveUniverseService.SelectForOrigins(inventory, origins, date, 100)
            .Where(row => row.Id != target.Id).Select(row => row.Id), corrected.Select(row => row.Id));
    }

    [Fact]
    public void CorrectedCalibrationExcludesClosedFutureAndNonSubstituteFacilities()
    {
        var target = Facility(1, 0);
        var active = Facility(2, 0.1);
        var closed = Facility(3, 0.2); closed.ClosedOn = new DateOnly(2025, 12, 31);
        var future = Facility(4, 0.3); future.OpenedOn = new DateOnly(2026, 1, 1);
        var inactive = Facility(5, 0.4); inactive.IsActive = false;
        var sportsbook = Facility(6, 0.5); sportsbook.HasSlots = false; sportsbook.VenueType = "sportsbook_only";
        OriginZone[] origins = [new() { RepresentativePoint = new Point(0, 0) { SRID = 4326 } }];
        var peers = MultimarketRevenueCalibration.SelectCasePeers(Protocol("origin-union-v2"),
            [target, active, closed, future, inactive, sportsbook], target, origins, new DateOnly(2025, 12, 31));
        Assert.Equal([active.Id], peers.Select(row => row.Id));
    }

    [Fact]
    public async Task DatabaseExecutionAndCalibrationUseTheSameOriginField()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("competitive-universe-" + Guid.NewGuid()).Options);
        var snapshot = Guid.NewGuid();
        var target = Facility(1, 0);
        var nearbyPatrons = Facility(2, 2);
        var nearTargetOnly = Facility(3, -1);
        CasinoCompetitor[] inventory = [target, nearbyPatrons, nearTargetOnly];
        foreach (var facility in inventory) facility.DatasetSnapshotId = snapshot;
        db.CasinoCompetitors.AddRange(inventory);
        await db.SaveChangesAsync();
        OriginZone[] origins = [new() { RepresentativePoint = new Point(1, 0) { SRID = 4326 } }];
        var date = new DateOnly(2025, 12, 31);
        var executed = await new CompetitiveUniverseService(db).SelectAsync(snapshot, origins, date, 100);
        var calibrated = MultimarketRevenueCalibration.SelectCasePeers(Protocol("origin-union-v2"), inventory, target, origins, date);
        Assert.Equal([nearbyPatrons.Id], calibrated.Select(row => row.Id));
        Assert.Equal(executed.Where(row => row.Id != target.Id).Select(row => row.Id), calibrated.Select(row => row.Id));
    }

    [Fact]
    public void NewEvaluationsRejectHistoricalTargetCenteredFields()
    {
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateServerCompetitiveUniverse(Protocol("target-radius-v1")));
        MultimarketRevenueCalibration.ValidateServerCompetitiveUniverse(Protocol("origin-union-v2"));
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateAttractionProtocol(Protocol("unknown")));
    }
}
