// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SaveNEIN.Server.Services.Providers;

/// <summary>Historical operator observations supplement inventory without substituting current capacity for backtest inputs.</summary>
public static class MichiganHistoricalFacilityAttributes
{
    public const string FireKeepersSourceUrl = "https://firekeeperscasino.com/2025/05/07/firekeepers-casino-hotel-unveils-new-thematic-slot-area-featuring-top-tier-games/";
    public const string FireKeepersVenueId = "USA-MI-TRIBAL-firekeepers-casino-hotel";
    public const string TransformVersion = "firekeepers-operator-may2025-physical-v1";
    public const string GunLakeSourceUrl = "https://gunlakecasino.com/wp-content/uploads/2025/03/PR-GLC-Resort-is-Here-3.6.25.pdf";
    public const string FourWindsSourceUrl = "https://fourwindscasino.com/press-room/several-life-changing-jackpots-won-at-four-winds-casinos-to-kickoff-2025/";

    public static ProviderDataset<CasinoCompetitorImportRow> SupplementNearbyTribalFacilities(
        ProviderDataset<CasinoCompetitorImportRow> inventory, byte[] gunLakePdf, byte[] fourWindsHtml)
    {
        var gunLakeText = string.Join(' ', MissouriGamingCommissionProvider.ExtractPages(gunLakePdf));
        return SupplementNearbyTribalFacilitiesFromText(inventory, gunLakeText, gunLakePdf, fourWindsHtml);
    }

    internal static ProviderDataset<CasinoCompetitorImportRow> SupplementNearbyTribalFacilitiesFromText(
        ProviderDataset<CasinoCompetitorImportRow> inventory, string gunLakeText, byte[] gunLakeBytes, byte[] fourWindsBytes)
    {
        var keys = new[] { "USA-MI-TRIBAL-gun-lake-casino", "USA-MI-TRIBAL-four-winds-casino-new-buffalo", "USA-IN-TRIBAL-four-winds-casino-south-bend" };
        if (inventory.DatasetKey != DatasetSnapshotKinds.Competitors || inventory.PeriodStart != new DateOnly(2025, 1, 1) ||
            inventory.PeriodEnd != new DateOnly(2025, 12, 31) || keys.Any(key => inventory.Rows.Count(row => row.StableVenueId == key) != 1) ||
            inventory.Rows.Where(row => keys.Contains(row.StableVenueId, StringComparer.Ordinal)).Any(row =>
                row.FacilityRegime != "tribal-class-iii-casino" || !row.StableVenueId.StartsWith("USA-" + row.State + "-TRIBAL-", StringComparison.Ordinal)))
            throw new InvalidDataException("Historical tribal supplements require the matching calendar-2025 inventory identities.");
        var gunText = Regex.Replace(gunLakeText, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var windsText = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Encoding.UTF8.GetString(fourWindsBytes), "<[^>]+>", " ",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!gunText.Contains("March 6, 2025", StringComparison.Ordinal) || !windsText.Contains("February 12, 2025", StringComparison.Ordinal))
            throw new InvalidDataException("Historical Gun Lake/Four Winds observations have a different date.");
        var gunCounts = Regex.Matches(gunText, @"including ([0-9,]+) state-of-the-art slot machines, over ([0-9,]+) table games",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var buffalo = Regex.Matches(windsText, @"Four Winds New Buffalo features ([0-9,]+) table games along with ([0-9,]+) slot machines",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var southBend = Regex.Matches(windsText, @"At Four Winds South Bend, players can find ([0-9,]+) table games, ([0-9,]+) live poker tables, and over ([0-9,]+) of the latest games",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (gunCounts.Count != 1 || buffalo.Count != 1 || southBend.Count != 1)
            throw new InvalidDataException("Historical tribal gaming counts are missing or ambiguous.");
        int Count(string value)
        {
            var count = int.Parse(value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
            return count > 0 ? count : throw new InvalidDataException("Historical gaming counts must be positive.");
        }
        var gunHash = Convert.ToHexString(SHA256.HashData(gunLakeBytes)).ToLowerInvariant();
        var windsHash = Convert.ToHexString(SHA256.HashData(fourWindsBytes)).ToLowerInvariant();
        var rows = inventory.Rows.Select(row => row.StableVenueId switch
        {
            "USA-MI-TRIBAL-gun-lake-casino" => row with
            {
                SlotOrVltPositions = Count(gunCounts[0].Groups[1].Value), TableGameCount = Count(gunCounts[0].Groups[2].Value), GamingPositions = null,
                HasSlots = true, HasTableGames = true,
                Notes = row.Notes + $" Historical operator gaming observation dated 2025-03-06, {GunLakeSourceUrl}, SHA-256 {gunHash}. " +
                    "Over-50 table games is retained as a conservative lower bound, not an audited exact count. Hotel/amenity expansion opened during 2025 and is not converted into a full-year capacity assumption. No tribal GGR is inferred."
            },
            "USA-MI-TRIBAL-four-winds-casino-new-buffalo" => row with
            {
                TableGameCount = Count(buffalo[0].Groups[1].Value), SlotOrVltPositions = Count(buffalo[0].Groups[2].Value), GamingPositions = null,
                HasSlots = true, HasTableGames = true,
                Notes = row.Notes + $" Historical operator gaming observation dated 2025-02-12, {FourWindsSourceUrl}, SHA-256 {windsHash}. " +
                    "Slot/table inventories are not player-seat counts. Jackpot payouts are not casino revenue and are not imported as GGR."
            },
            "USA-IN-TRIBAL-four-winds-casino-south-bend" => row with
            {
                TableGameCount = Count(southBend[0].Groups[1].Value), PokerTableCount = Count(southBend[0].Groups[2].Value),
                SlotOrVltPositions = Count(southBend[0].Groups[3].Value), GamingPositions = null, GamingFloorSquareFeet = null,
                Notes = row.Notes + $" Historical operator gaming observation dated 2025-02-12, {FourWindsSourceUrl}, SHA-256 {windsHash}. " +
                    "Over-1,800 games is a conservative slot-inventory lower bound. The later current-page 1,900-slot and 175,000-square-foot figures are not used as historical 2025 gaming-floor evidence. " +
                    "The separately sourced 2023 hotel remains in inventory. Gaming-floor area remains unknown; jackpot payouts are not imported as GGR."
            },
            _ => row
        }).ToArray();
        const string transform = "nearby-tribal-operator-2025-v2";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transform + "\n" + inventory.ContentChecksum + "\n" + gunHash + "\n" + windsHash))).ToLowerInvariant();
        return inventory with
        {
            Rows = rows, ContentChecksum = hash, TransformVersion = transform,
            Source = inventory.Source with { ContentHash = hash, RetrievedAtUtc = DateTime.UtcNow,
                Notes = inventory.Source.Notes + $"\nNearby tribal source supplement: base {inventory.ContentChecksum}; Gun Lake {GunLakeSourceUrl} raw {gunHash}; Four Winds {FourWindsSourceUrl} raw {windsHash}." },
            Warnings = inventory.Warnings.Append("Gun Lake and Four Winds gaming counts are dated operator observations with explicit lower bounds. Missing annual tribal GGR and gaming-floor/amenity histories remain unresolved; jackpot payouts are never treated as revenue.").ToArray()
        };
    }

    public static ProviderDataset<CasinoCompetitorImportRow> SupplementFireKeepers(
        ProviderDataset<CasinoCompetitorImportRow> inventory, byte[] sourceBytes)
    {
        if (inventory.DatasetKey != DatasetSnapshotKinds.Competitors || inventory.PeriodStart != new DateOnly(2025, 1, 1) ||
            inventory.PeriodEnd != new DateOnly(2025, 12, 31) || inventory.Rows.Count(row => row.StableVenueId == FireKeepersVenueId) != 1)
            throw new InvalidDataException("Historical attributes require one matching facility in a calendar-2025 inventory.");
        var rawHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TransformVersion + "\n" + inventory.ContentChecksum + "\n" + rawHash))).ToLowerInvariant();
        return inventory with
        {
            ContentChecksum = hash, TransformVersion = TransformVersion,
            Rows = inventory.Rows.Select(row => row.StableVenueId == FireKeepersVenueId ? ApplyFireKeepers(row, sourceBytes) : row).ToArray(),
            Source = inventory.Source with { ContentHash = hash, RetrievedAtUtc = DateTime.UtcNow,
                Notes = inventory.Source.Notes + $"\nHistorical FireKeepers supplement: base inventory {inventory.ContentChecksum}; source {FireKeepersSourceUrl}; raw SHA-256 {rawHash}." },
            Warnings = inventory.Warnings.Append("FireKeepers physical scale uses an approximate operator observation dated May 7, 2025. No audited annual capacity or tribal GGR is claimed; other missing tribal attributes remain unresolved.").ToArray()
        };
    }

    internal static CasinoCompetitorImportRow ApplyFireKeepers(CasinoCompetitorImportRow row, byte[] sourceBytes)
    {
        if (row.StableVenueId != FireKeepersVenueId || row.State != "MI" || row.FacilityRegime != "tribal-class-iii-casino")
            throw new InvalidDataException("FireKeepers historical attributes require the matching Michigan tribal facility identity.");
        var html = Encoding.UTF8.GetString(sourceBytes);
        if (html.Length > 2_000_000) throw new InvalidDataException("Historical operator source exceeds bounded parser input.");
        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1))), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!text.Contains("May 7, 2025", StringComparison.Ordinal) || !text.Contains("FireKeepers Casino Hotel", StringComparison.Ordinal))
            throw new InvalidDataException("Historical FireKeepers source has a different date or property.");
        var counts = Regex.Matches(text, @"The property features ([0-9,]+) slot machines, ([0-9,]+) table games, and a ([0-9,]+)-table live poker room\.",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var rooms = Regex.Matches(text, @"offers ([0-9,]+) rooms, a functional multi-purpose event center, six distinctive dining destinations",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (counts.Count != 1 || rooms.Count != 1) throw new InvalidDataException("Historical FireKeepers physical attributes are missing or ambiguous.");
        int Count(string value) => int.Parse(value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        var slots = Count(counts[0].Groups[1].Value);
        var tables = Count(counts[0].Groups[2].Value);
        var poker = Count(counts[0].Groups[3].Value);
        var hotel = Count(rooms[0].Groups[1].Value);
        if (slots <= 0 || tables <= 0 || poker <= 0 || hotel <= 0) throw new InvalidDataException("Historical FireKeepers attributes must be positive.");
        var hash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        return row with
        {
            SlotOrVltPositions = slots, TableGameCount = tables, PokerTableCount = poker, GamingPositions = null,
            HotelRoomCount = hotel, FoodBeverageVenueCount = 6,
            HasSlots = true, HasTableGames = true, HasPoker = true, HasHotel = true, HasRestaurants = true, HasEntertainment = true,
            Notes = row.Notes + $" Historical operator observation dated 2025-05-07 from {FireKeepersSourceUrl}, SHA-256 {hash}. " +
                "Slot count is an operator-reported approximate property inventory, not audited year-end seats. Table-game and poker inventories are separate; no table-seat capacity is fabricated. " +
                "The announced themed area is not added again to the stated property total. No gaming floor, annual GGR, annual temporal audit or nonresident-demand observation is inferred."
        };
    }
}
