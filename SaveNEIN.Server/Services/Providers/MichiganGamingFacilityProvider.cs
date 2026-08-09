using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class MichiganGamingFacilityProviderOptions
{
    public const string ConfigurationSection = "MichiganGamingFacilities";

    public string TribalGamingReportUrl { get; set; } =
        "https://www.michigan.gov/mgcb/-/media/Project/Websites/mgcb/Annual-Reports/2025/2025-Tribal-Gaming-Report-Final.pdf";
    public string DetroitCasinosUrl { get; set; } =
        "https://www.michigan.gov/mgcb/detroit-casinos";
    public string GeocoderUrl { get; set; } =
        "https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates";
}

public sealed class MichiganGamingFacilityInventoryProvider(
    HttpClient http,
    IOptions<MichiganGamingFacilityProviderOptions> options) : IGamingFacilityInventoryProvider
{
    private const string Pinned2025TribalReportSha256 =
        "73c0359b0153dcee691efa40187ee7bbc95da2e832b08775c75082f61e2459f0";

    public string ProviderKey => "michigan-commercial-and-tribal-facility-inventory";
    public string GeographicCoverage => "US-MI";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        Require2025InventoryPeriod(request);
        var configured = options.Value;
        var tribalReportTask = GetBytesAsync(configured.TribalGamingReportUrl, cancellationToken);
        var detroitPageTask = GetBytesAsync(configured.DetroitCasinosUrl, cancellationToken);
        await Task.WhenAll(tribalReportTask, detroitPageTask);
        var tribalReportBytes = await tribalReportTask;
        var detroitPageBytes = await detroitPageTask;
        var tribalReportChecksum = Convert.ToHexString(SHA256.HashData(tribalReportBytes)).ToLowerInvariant();
        if (!string.Equals(tribalReportChecksum, Pinned2025TribalReportSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The MGCB 2025 Tribal Gaming Report checksum changed from the reviewed transform version. Expected {Pinned2025TribalReportSha256}; received {tribalReportChecksum}. Review the facility catalog before ingestion.");
        }
        var detroitHtml = Encoding.UTF8.GetString(detroitPageBytes);
        foreach (var commercial in MichiganCommercialCasinoCatalog.Entries)
        {
            if (!commercial.PageVerificationLabels.Any(label =>
                    detroitHtml.Contains(label, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"The current MGCB Detroit casino page no longer verifies commercial facility '{commercial.Name}'.");
            }
        }

        var retrievedAt = DateTime.UtcNow;
        var rows = new List<CasinoCompetitorImportRow>(
            MichiganTribalCasinoCatalog.Entries.Count + MichiganCommercialCasinoCatalog.Entries.Count);
        var canonicalGeocodes = new List<string>(rows.Capacity);
        foreach (var tribal in MichiganTribalCasinoCatalog.Entries)
        {
            var geocode = await GeocodeAsync(tribal.Name, tribal.City, tribal.GeocodeAddress, cancellationToken);
            var stableVenueId = $"USA-MI-TRIBAL-{Slug(tribal.Name)}";
            canonicalGeocodes.Add($"{stableVenueId}|{geocode.Latitude:R}|{geocode.Longitude:R}|{geocode.MatchAddress}|{geocode.Score:R}");
            rows.Add(new CasinoCompetitorImportRow(
                StableVenueId: stableVenueId,
                Name: tribal.Name,
                State: "MI",
                CountryCode: "USA",
                VenueType: "tribal-casino",
                FacilityRegime: "tribal-class-iii-casino",
                RegulatoryStatus: "active-compact-class-iii",
                JurisdictionId: null,
                RegulatorName: $"{tribal.TribalNationName} gaming authority; MGCB compact-compliance oversight",
                RegulatorLicenseId: null,
                TribalNationName: tribal.TribalNationName,
                OpenedOn: null,
                ClosedOn: null,
                County: null,
                City: tribal.City,
                Latitude: geocode.Latitude,
                Longitude: geocode.Longitude,
                IsActive: true,
                OperatorName: tribal.TribalNationName,
                SourceUrl: configured.TribalGamingReportUrl,
                LastVerifiedAt: retrievedAt,
                HasSlots: null,
                HasTableGames: null,
                HasPoker: null,
                HasSportsbook: null,
                HasRacetrack: null,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: null,
                SlotOrVltPositions: null,
                TableGameCount: null,
                PokerTableCount: null,
                GamingFloorSquareFeet: null,
                HotelRoomCount: null,
                EventCapacity: null,
                FoodBeverageVenueCount: null,
                DevelopmentCost: null,
                DevelopmentCostDollarYear: null,
                AccessContext: null,
                LimitedAccessDistanceMiles: null,
                HasInterchangeAccess: null,
                MarketOrientation: null,
                IsBorderMarket: null,
                Notes: $"Listed in the pinned MGCB 2025 Tribal Gaming Annual Report as a Class III tribal casino in {tribal.City}. MGCB performs compact-compliance oversight but the tribal nation regulates gaming. Coordinate POI match: {geocode.MatchAddress} (score {geocode.Score.ToString("0.0", CultureInfo.InvariantCulture)}). Unreported attributes remain null."));
        }
        foreach (var commercial in MichiganCommercialCasinoCatalog.Entries)
        {
            var geocode = await GeocodeAsync(commercial.Name, "Detroit", commercial.GeocodeAddress, cancellationToken);
            var stableVenueId = $"USA-MI-MGCB-{Slug(commercial.Name)}";
            canonicalGeocodes.Add($"{stableVenueId}|{geocode.Latitude:R}|{geocode.Longitude:R}|{geocode.MatchAddress}|{geocode.Score:R}");
            rows.Add(new CasinoCompetitorImportRow(
                StableVenueId: stableVenueId,
                Name: commercial.Name,
                State: "MI",
                CountryCode: "USA",
                VenueType: "commercial-casino",
                FacilityRegime: "commercial-casino",
                RegulatoryStatus: "active-licensed",
                JurisdictionId: null,
                RegulatorName: "Michigan Gaming Control Board",
                RegulatorLicenseId: null,
                TribalNationName: null,
                OpenedOn: null,
                ClosedOn: null,
                County: "Wayne",
                City: "Detroit",
                Latitude: geocode.Latitude,
                Longitude: geocode.Longitude,
                IsActive: true,
                OperatorName: null,
                SourceUrl: configured.DetroitCasinosUrl,
                LastVerifiedAt: retrievedAt,
                HasSlots: true,
                HasTableGames: true,
                HasPoker: null,
                HasSportsbook: null,
                HasRacetrack: false,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: null,
                SlotOrVltPositions: null,
                TableGameCount: null,
                PokerTableCount: null,
                GamingFloorSquareFeet: null,
                HotelRoomCount: null,
                EventCapacity: null,
                FoodBeverageVenueCount: null,
                DevelopmentCost: null,
                DevelopmentCostDollarYear: null,
                AccessContext: null,
                LimitedAccessDistanceMiles: null,
                HasInterchangeAccess: null,
                MarketOrientation: null,
                IsBorderMarket: null,
                Notes: $"Verified on the current MGCB Detroit Casinos page. Coordinate POI match: {geocode.MatchAddress} (score {geocode.Score.ToString("0.0", CultureInfo.InvariantCulture)}). Unreported attributes remain null."));
        }
        if (rows.Count != 27 || rows.Select(row => row.StableVenueId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
        {
            throw new InvalidDataException("The reviewed Michigan 2025 commercial and tribal inventory must contain exactly 27 unique facilities.");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(tribalReportBytes);
        hasher.AppendData(detroitPageBytes);
        hasher.AppendData(Encoding.UTF8.GetBytes(string.Join('\n', canonicalGeocodes)));
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                "Michigan commercial and tribal Class III casino inventory 2025",
                "Michigan Gaming Control Board",
                configured.TribalGamingReportUrl,
                "state-compact-report-and-regulator-html-with-poi-geocode",
                "Michigan commercial casinos and tribal Class III casinos",
                "2025",
                retrievedAt,
                checksum,
                true,
                "Michigan public-record terms and coordinate service terms apply.",
                $"Tribal universe is pinned to reviewed MGCB report SHA-256 {Pinned2025TribalReportSha256}; Detroit commercial properties are reverified at {configured.DetroitCasinosUrl}. Facility coordinates are reproducibly resolved through {configured.GeocoderUrl}."),
            DatasetSnapshotKinds.Competitors,
            "2025",
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "mgcb-2025-commercial-tribal-facilities-v1",
            rows,
            ["Michigan tribal facility revenue is not assumed or imputed by this inventory provider; hybrid attraction must expose structural fallback where audited observed revenue is unavailable. Coordinates are derived POI geocodes and amenities absent from reviewed sources remain null."]);
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<MichiganFacilityGeocode> GeocodeAsync(
        string facilityName,
        string city,
        string? geocodeAddress,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var query = geocodeAddress ?? $"{facilityName}, {city}, MI";
        var category = geocodeAddress is null ? "&category=Casino" : string.Empty;
        var uri = $"{configured.GeocoderUrl}?SingleLine={Uri.EscapeDataString(query)}{category}&f=json&countryCode=USA&maxLocations=1&outFields=Match_addr,Addr_type";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"The configured geocoder returned no casino POI for '{query}'.");
        }
        var candidate = candidates[0];
        var score = candidate.GetProperty("score").GetDouble();
        var location = candidate.GetProperty("location");
        var longitude = location.GetProperty("x").GetDouble();
        var latitude = location.GetProperty("y").GetDouble();
        var matchAddress = candidate.GetProperty("address").GetString() ?? string.Empty;
        if (score < 80 || latitude is < 41.6 or > 48.5 || longitude is < -90.6 or > -82.0)
        {
            throw new InvalidDataException(
                $"Casino POI match for '{query}' failed Michigan bounds/quality validation: score={score:R}, latitude={latitude:R}, longitude={longitude:R}.");
        }
        return new MichiganFacilityGeocode(latitude, longitude, score, matchAddress);
    }

    private static void Require2025InventoryPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-MI", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Michigan facility adapter requires GeographicCoverage 'US-MI'.");
        }
        var decemberStart = new DateOnly(2025, 12, 1);
        var isDecember = request.PeriodStart == decemberStart && request.PeriodEnd == new DateOnly(2025, 12, 31);
        var isAnnual = request.PeriodStart == new DateOnly(2025, 1, 1) && request.PeriodEnd == new DateOnly(2025, 12, 31);
        if (!isDecember && !isAnnual)
        {
            throw new ArgumentException(
                "The reviewed Michigan inventory provider currently requires calendar-year 2025 or December 2025, matching the pinned annual-report vintage.",
                nameof(request));
        }
    }

    private static string Slug(string value) =>
        Regex.Replace(
            value.Normalize(NormalizationForm.FormKD).ToLowerInvariant(),
            @"[^a-z0-9]+",
            "-",
            RegexOptions.CultureInvariant).Trim('-');
}

internal sealed record MichiganFacilityGeocode(double Latitude, double Longitude, double Score, string MatchAddress);

internal sealed record MichiganTribalCasinoCatalogEntry(
    string Name,
    string City,
    string TribalNationName,
    string? GeocodeAddress = null);

internal static class MichiganTribalCasinoCatalog
{
    public static readonly IReadOnlyList<MichiganTribalCasinoCatalogEntry> Entries =
    [
        new("Bay Mills Resort & Casino", "Brimley", "Bay Mills Indian Community"),
        new("Crystal Shores Casino", "Benzonia", "Grand Traverse Band of Ottawa and Chippewa Indians", "7282 Hoadley Rd, Benzonia, MI 49616"),
        new("Leelanau Sands Casino & Lodge", "Peshawbestown", "Grand Traverse Band of Ottawa and Chippewa Indians"),
        new("Turtle Creek Casino & Hotel", "Williamsburg", "Grand Traverse Band of Ottawa and Chippewa Indians"),
        new("Island Resort & Casino", "Harris", "Hannahville Indian Community", "W399 US Highway 2, Harris, MI 49845"),
        new("Ojibwa Casino Baraga", "Baraga", "Keweenaw Bay Indian Community"),
        new("Ojibwa Casino Marquette", "Marquette", "Keweenaw Bay Indian Community"),
        new("Northern Waters Casino Resort", "Watersmeet", "Lac Vieux Desert Band of Lake Superior Chippewa Indians", "Lac Vieux Desert Resort and Casino, Watersmeet, MI"),
        new("Little River Casino Resort", "Manistee", "Little River Band of Ottawa Indians"),
        new("Odawa Casino Mackinaw City", "Mackinaw City", "Little Traverse Bay Bands of Odawa Indians"),
        new("Odawa Casino Petoskey", "Petoskey", "Little Traverse Bay Bands of Odawa Indians"),
        new("Gun Lake Casino", "Wayland", "Match-E-Be-Nash-She-Wish Band of Pottawatomi Indians (Gun Lake Tribe)"),
        new("FireKeepers Casino Hotel", "Battle Creek", "Nottawaseppi Huron Band of the Potawatomi", "11177 E Michigan Ave, Battle Creek, MI 49014"),
        new("Four Winds Casino Dowagiac", "Dowagiac", "Pokagon Band of Potawatomi Indians", "58700 M-51 S, Dowagiac, MI 49047"),
        new("Four Winds Casino Hartford", "Hartford", "Pokagon Band of Potawatomi Indians", "68600 Red Arrow Hwy, Hartford, MI 49057"),
        new("Four Winds Casino New Buffalo", "New Buffalo", "Pokagon Band of Potawatomi Indians", "11111 Wilson Rd, New Buffalo, MI 49117"),
        new("Saganing Eagles Landing Casino & Hotel", "Standish", "Saginaw Chippewa Indian Tribe"),
        new("Soaring Eagle Casino & Resort", "Mount Pleasant", "Saginaw Chippewa Indian Tribe"),
        new("Soaring Eagle Slot Palace", "Mount Pleasant", "Saginaw Chippewa Indian Tribe"),
        new("Kewadin Casino Christmas", "Christmas", "Sault Ste. Marie Tribe of Chippewa Indians", "N7761 Candy Cane Ln, Christmas, MI 49862"),
        new("Kewadin Casino Hessel", "Hessel", "Sault Ste. Marie Tribe of Chippewa Indians", "3395 N 3 Mile Rd, Hessel, MI 49745"),
        new("Kewadin Casino Manistique", "Manistique", "Sault Ste. Marie Tribe of Chippewa Indians", "5630 W US Highway 2, Manistique, MI 49854"),
        new("Kewadin Casino Sault Ste. Marie", "Sault Ste. Marie", "Sault Ste. Marie Tribe of Chippewa Indians", "2186 Shunk Rd, Sault Ste. Marie, MI 49783"),
        new("Kewadin Casino St. Ignace", "St. Ignace", "Sault Ste. Marie Tribe of Chippewa Indians", "3015 Mackinac Trail, St. Ignace, MI 49781")
    ];
}

internal sealed record MichiganCommercialCasinoCatalogEntry(
    string Name,
    IReadOnlyList<string> PageVerificationLabels,
    string GeocodeAddress);

internal static class MichiganCommercialCasinoCatalog
{
    public static readonly IReadOnlyList<MichiganCommercialCasinoCatalogEntry> Entries =
    [
        new("MGM Grand Detroit", ["MGM Grand - Detroit", "MGM Grand Detroit"], "1777 Third St, Detroit, MI 48226"),
        new("MotorCity Casino", ["Motor City Casino", "MotorCity Casino"], "2901 Grand River Ave, Detroit, MI 48201"),
        new("Hollywood Casino at Greektown", ["Hollywood Casino at Greektown"], "555 E Lafayette St, Detroit, MI 48226")
    ];
}
