using System.Text;
using System.Text.Json;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class MichiganHistoricalFacilityAttributesTests
{
    private const string Source = "FireKeepers Casino Hotel May 7, 2025. New area features over 50 slot machines. " +
        "The property features 2,700 slot machines, 63 table games, and a 21-table live poker room. " +
        "The hotel offers 446 rooms, a functional multi-purpose event center, six distinctive dining destinations.";
    private static CasinoCompetitorImportRow Row => JsonSerializer.Deserialize<CasinoCompetitorImportRow>(
        "{\"StableVenueId\":\"USA-MI-TRIBAL-firekeepers-casino-hotel\",\"State\":\"MI\",\"FacilityRegime\":\"tribal-class-iii-casino\",\"Latitude\":42.29,\"Longitude\":-85.07,\"IsActive\":true}")!;

    [Fact]
    public void CapturesDatedAttributesWithoutAddingTheThemedAreaTwiceOrInventingRevenue()
    {
        var result = MichiganHistoricalFacilityAttributes.ApplyFireKeepers(Row, Encoding.UTF8.GetBytes(Source));
        Assert.Equal(2700, result.SlotOrVltPositions);
        Assert.Equal(63, result.TableGameCount);
        Assert.Equal(21, result.PokerTableCount);
        Assert.Equal(446, result.HotelRoomCount);
        Assert.Null(result.GamingPositions);
        Assert.Null(result.GamingFloorSquareFeet);
        Assert.Equal(Row.Latitude, result.Latitude);
        Assert.Contains("approximate", result.Notes);
        Assert.Contains("SHA-256", result.Notes);
    }

    [Theory]
    [InlineData("May 7, 2025", "May 7, 2026")]
    [InlineData("2,700", "2,700.5")]
    [InlineData("63 table games", "no reported tables")]
    [InlineData("446 rooms", "unknown rooms")]
    public void RejectsWrongVintageAndMissingOrUnrepresentableAttributes(string from, string to) =>
        Assert.Throws<InvalidDataException>(() => MichiganHistoricalFacilityAttributes.ApplyFireKeepers(Row, Encoding.UTF8.GetBytes(Source.Replace(from, to, StringComparison.Ordinal))));

    [Fact]
    public void CannotApplyOneOperatorsAttributesToAnotherCasino() => Assert.Throws<InvalidDataException>(() =>
        MichiganHistoricalFacilityAttributes.ApplyFireKeepers(Row with { StableVenueId = "another-venue" }, Encoding.UTF8.GetBytes(Source)));

    private const string GunLakeText = "March 6, 2025 Gun Lake Casino Resort including 2,300 state-of-the-art slot machines, over 50 table games. A 252-room hotel opens March 7.";
    private const string FourWindsText = "February 12, 2025 Four Winds New Buffalo features 40 table games along with 1,900 slot machines. " +
        "At Four Winds South Bend, players can find 27 table games, 12 live poker tables, and over 1,800 of the latest games. Jackpot payouts $158,319.";
    private static ProviderDataset<CasinoCompetitorImportRow> NearbyInventory => new(
        new("test", "test", "https://example.test", "test", "US-MI,US-IN", "2025", DateTime.UnixEpoch, new string('a', 64), true, null, null),
        DatasetSnapshotKinds.Competitors, "2025", new(2025, 1, 1), new(2025, 12, 31), new string('a', 64), "test",
        [Row with { StableVenueId = "USA-MI-TRIBAL-gun-lake-casino" },
         Row with { StableVenueId = "USA-MI-TRIBAL-four-winds-casino-new-buffalo" },
         Row with { StableVenueId = "USA-IN-TRIBAL-four-winds-casino-south-bend", State = "IN", SlotOrVltPositions = 1900, GamingFloorSquareFeet = 175000, HotelRoomCount = 317 }], []);

    [Fact]
    public void NearbyHistoricalCaptureKeepsBoundsAndDoesNotBackdateHotelExpansionOrCurrentFloorArea()
    {
        var result = MichiganHistoricalFacilityAttributes.SupplementNearbyTribalFacilitiesFromText(NearbyInventory, GunLakeText,
            Encoding.UTF8.GetBytes(GunLakeText), Encoding.UTF8.GetBytes(FourWindsText));
        var gun = result.Rows.Single(row => row.StableVenueId == "USA-MI-TRIBAL-gun-lake-casino");
        Assert.Equal(2300, gun.SlotOrVltPositions);
        Assert.Equal(50, gun.TableGameCount);
        Assert.Null(gun.HotelRoomCount);
        Assert.Contains("lower bound", gun.Notes);
        var buffalo = result.Rows.Single(row => row.StableVenueId == "USA-MI-TRIBAL-four-winds-casino-new-buffalo");
        Assert.Equal(1900, buffalo.SlotOrVltPositions);
        Assert.Equal(40, buffalo.TableGameCount);
        var southBend = result.Rows.Single(row => row.State == "IN");
        Assert.Equal(1800, southBend.SlotOrVltPositions);
        Assert.Null(southBend.GamingFloorSquareFeet);
        Assert.Equal(317, southBend.HotelRoomCount);
        Assert.Contains("jackpot payouts are not imported as GGR", southBend.Notes);
        Assert.NotEqual(NearbyInventory.ContentChecksum, result.ContentChecksum);
    }

    [Theory]
    [InlineData("March 6, 2025", "March 6, 2026")]
    [InlineData("2,300", "2,300.5")]
    [InlineData("over 50 table games", "unreported table games")]
    public void NearbyCaptureRejectsUnsupportedHistoricalValues(string from, string to) => Assert.Throws<InvalidDataException>(() =>
        MichiganHistoricalFacilityAttributes.SupplementNearbyTribalFacilitiesFromText(NearbyInventory, GunLakeText.Replace(from, to, StringComparison.Ordinal),
            Encoding.UTF8.GetBytes(GunLakeText), Encoding.UTF8.GetBytes(FourWindsText)));
}
