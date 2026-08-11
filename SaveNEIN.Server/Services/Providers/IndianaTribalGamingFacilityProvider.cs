// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IndianaTribalGamingFacilityProviderOptions
{
    public const string ConfigurationSection = "IndianaTribalGamingFacilities";

    public string ApprovedCompactUrl { get; set; } =
        "https://www.nigc.gov/wp-content/uploads/2025/10/2021.07.01-Pokagon-Band-of-Potawatomi-ORD-app.pdf";
    public string PropertyOverviewUrl { get; set; } =
        "https://fourwindscasino.com/southbend/visiting/property-overview/";
    public string HotelOpeningUrl { get; set; } =
        "https://fourwindscasino.com/press-room/the-pokagon-band-and-its-four-winds-casinos-announce-grand-opening-details-for-their-new-hotel-at-four-winds-south-bend/";
    public string AddressEvidenceUrl { get; set; } =
        "https://fourwindscasino.com/w-club/rules-and-regulations/";
}

public sealed class IndianaTribalGamingFacilityInventoryProvider(
    HttpClient http,
    IOptions<IndianaTribalGamingFacilityProviderOptions> options) : IGamingFacilityInventoryProvider
{
    private const string ReviewedCompactSha256 =
        "c8132524525a0baef4bec6873ff3126ef5d922416c3bf6271e2a08565ffcdef9";

    public string ProviderKey => "pokagon-indiana-tribal-facility-inventory";
    public string GeographicCoverage => "US-IN";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireReviewedPeriod(request);
        var configured = options.Value;
        var compactTask = GetBytesAsync(configured.ApprovedCompactUrl, cancellationToken);
        var overviewTask = GetBytesAsync(configured.PropertyOverviewUrl, cancellationToken);
        var hotelTask = GetBytesAsync(configured.HotelOpeningUrl, cancellationToken);
        var addressTask = GetBytesAsync(configured.AddressEvidenceUrl, cancellationToken);
        await Task.WhenAll(compactTask, overviewTask, hotelTask, addressTask);

        var compactBytes = await compactTask;
        var overviewBytes = await overviewTask;
        var hotelBytes = await hotelTask;
        var addressBytes = await addressTask;
        var compactChecksum = Convert.ToHexString(SHA256.HashData(compactBytes)).ToLowerInvariant();
        if (!string.Equals(compactChecksum, ReviewedCompactSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The NIGC-published Pokagon/Indiana compact checksum changed from the reviewed transform version. Expected {ReviewedCompactSha256}; received {compactChecksum}. Review the legal regime before ingestion.");
        }

        ValidatePublishedEvidence(
            Encoding.UTF8.GetString(overviewBytes),
            Encoding.UTF8.GetString(hotelBytes),
            Encoding.UTF8.GetString(addressBytes));

        var retrievedAt = DateTime.UtcNow;
        var row = CreateSouthBendRow(retrievedAt, configured);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(compactBytes);
        hasher.AppendData(overviewBytes);
        hasher.AppendData(hotelBytes);
        hasher.AppendData(addressBytes);
        hasher.AppendData(Encoding.UTF8.GetBytes(
            $"{row.StableVenueId}|{row.Latitude:R}|{row.Longitude:R}|{row.GamingPositions}|{row.HotelRoomCount}"));
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                "Pokagon Band Four Winds South Bend tribal facility inventory",
                "National Indian Gaming Commission and Pokagon Gaming Authority",
                configured.ApprovedCompactUrl,
                "federal-approved-compact-and-tribal-operator-web-evidence",
                "Four Winds South Bend tribal gaming facility, Indiana",
                "2025 inventory reverified 2026-08-11",
                retrievedAt,
                checksum,
                true,
                "NIGC public-record and Four Winds website terms apply.",
                $"Class III authority is checksum-pinned to the NIGC-published compact. Current facility scale is verified at {configured.PropertyOverviewUrl}; hotel/event scale at {configured.HotelOpeningUrl}; address at {configured.AddressEvidenceUrl}. Coordinates are the frozen 2026-08-11 ArcGIS address geocode for the operator-published address."),
            DatasetSnapshotKinds.Competitors,
            "2025",
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "pokagon-south-bend-facility-v1",
            [row],
            [
                "The operator publishes 'over 1,900' slots; 1,900 is persisted as a conservative minimum, not false point precision.",
                "No audited property-level GGR is published by these facility sources; gravity attraction must disclose structural fallback rather than impute observed revenue.",
                "Current operator-published facility features verify existence and scale but are not represented as an audited December 2025 month-end inventory."
            ]);
    }

    internal static CasinoCompetitorImportRow CreateSouthBendRow(
        DateTime retrievedAt,
        IndianaTribalGamingFacilityProviderOptions configured) =>
        new(
            StableVenueId: "USA-IN-TRIBAL-four-winds-casino-south-bend",
            Name: "Four Winds Casino South Bend",
            State: "IN",
            CountryCode: "USA",
            VenueType: "tribal-casino-resort",
            FacilityRegime: "tribal-class-iii-casino",
            RegulatoryStatus: "active-compact-class-iii",
            JurisdictionId: null,
            RegulatorName: "Pokagon Band Gaming Commission; National Indian Gaming Commission oversight",
            RegulatorLicenseId: null,
            TribalNationName: "Pokagon Band of Potawatomi Indians",
            OpenedOn: new DateOnly(2018, 1, 16),
            ClosedOn: null,
            County: "St. Joseph",
            City: "South Bend",
            Latitude: 41.645067661234,
            Longitude: -86.290402391261,
            IsActive: true,
            OperatorName: "Pokagon Gaming Authority",
            SourceUrl: configured.PropertyOverviewUrl,
            LastVerifiedAt: retrievedAt,
            HasSlots: true,
            HasTableGames: true,
            HasPoker: true,
            HasSportsbook: null,
            HasRacetrack: false,
            HasHotel: true,
            HasRestaurants: true,
            HasEntertainment: true,
            HasLoyaltyProgram: true,
            HasResortAmenities: true,
            GamingPositions: 1_939,
            SlotOrVltPositions: 1_900,
            TableGameCount: 27,
            PokerTableCount: 12,
            GamingFloorSquareFeet: 175_000,
            HotelRoomCount: 317,
            EventCapacity: 800,
            FoodBeverageVenueCount: 6,
            DevelopmentCost: null,
            DevelopmentCostDollarYear: null,
            AccessContext: "South Bend tribal destination casino",
            LimitedAccessDistanceMiles: null,
            HasInterchangeAccess: null,
            MarketOrientation: "regional-destination-border-market",
            IsBorderMarket: true,
            Notes: "NIGC-published compact verifies the Indiana Class III legal regime. Four Winds publishes over 1,900 slots, 27 table games, 12 poker tables, 175,000 square feet of gaming, six restaurants, a 317-room hotel, and an 800-seat ballroom. Slot and total-position values use 1,900 as the disclosed conservative minimum. Address: 3000 Prairie Avenue, South Bend, IN 46614; frozen address-geocode score 100.");

    internal static void ValidatePublishedEvidence(string overviewHtml, string hotelHtml, string addressHtml)
    {
        RequireText(overviewHtml, "Four Winds South Bend", "property identity");
        RequireAnyText(overviewHtml, ["over 1,900", "over 1900"], "slot inventory");
        RequireText(overviewHtml, "27 table games", "table-game inventory");
        RequireText(overviewHtml, "12 table", "poker-table inventory");
        RequireAnyText(overviewHtml, ["175,000", "175000"], "gaming-floor area");
        RequireText(overviewHtml, "six restaurants", "restaurant inventory");
        RequireText(hotelHtml, "317 rooms", "hotel-room inventory");
        RequireAnyText(hotelHtml, ["seating for 800", "seating for <strong>800"], "event capacity");
        RequireText(addressHtml, "3000 Prairie Avenue", "published street address");
        RequireText(addressHtml, "IN 46614", "published ZIP address");
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void RequireReviewedPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Pokagon Indiana facility adapter requires GeographicCoverage 'US-IN'.");
        }
        var isAnnual = request.PeriodStart == new DateOnly(2025, 1, 1) &&
                       request.PeriodEnd == new DateOnly(2025, 12, 31);
        var isDecember = request.PeriodStart == new DateOnly(2025, 12, 1) &&
                         request.PeriodEnd == new DateOnly(2025, 12, 31);
        if (!isAnnual && !isDecember)
        {
            throw new ArgumentException(
                "The reviewed Pokagon South Bend inventory provider currently supports calendar 2025 or December 2025.",
                nameof(request));
        }
    }

    private static void RequireText(string text, string expected, string evidenceName)
    {
        if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The current Four Winds source no longer verifies {evidenceName} ('{expected}').");
        }
    }

    private static void RequireAnyText(string text, IReadOnlyCollection<string> expected, string evidenceName)
    {
        if (!expected.Any(item => text.Contains(item, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"The current Four Winds source no longer verifies {evidenceName} ({string.Join(" or ", expected.Select(item => $"'{item}'"))}).");
        }
    }
}
