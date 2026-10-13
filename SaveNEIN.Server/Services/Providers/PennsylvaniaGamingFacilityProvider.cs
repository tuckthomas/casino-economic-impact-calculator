// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaveNEIN.Server.Services.Providers;

public sealed record PennsylvaniaGamingProfile(string Key, string Name, string Address, int AverageMachines, int Tables, int NonBankedTables, bool Racino);

public sealed class PennsylvaniaGamingFacilityProvider(HttpClient http)
{
    public const string TransformVersion = "pgcb-fy2025-physical-v2";
    public const string SourceUrl = "https://gamingcontrolboard.pa.gov/sites/default/files/2025-09/PGCB%20Annual%20Report%202024-2025%20FINAL.pdf";
    public async Task<ProviderDataset<CasinoCompetitorImportRow>> CaptureAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        var profiles = ParseProfiles(MissouriGamingCommissionProvider.ExtractPages(pdfBytes));
        var rows = new List<CasinoCompetitorImportRow>();
        var geocodes = new List<string>();
        foreach (var profile in profiles)
        {
            var uri = $"https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates?SingleLine={Uri.EscapeDataString(profile.Address)}&f=json&countryCode=USA&maxLocations=1";
            var geoBytes = await http.GetByteArrayAsync(uri, cancellationToken);
            using var geo = JsonDocument.Parse(geoBytes);
            var matches = geo.RootElement.GetProperty("candidates");
            if (matches.GetArrayLength() == 0 || matches[0].GetProperty("score").GetDouble() < 90)
                throw new InvalidDataException($"PGCB address did not produce a reliable geocode: {profile.Address}.");
            var match = matches[0];
            var lat = match.GetProperty("location").GetProperty("y").GetDouble();
            var lon = match.GetProperty("location").GetProperty("x").GetDouble();
            geocodes.Add($"{profile.Key}|{Hash(geoBytes)}");
            rows.Add(new($"USA-PA-PGCB-{profile.Key}", profile.Name, "PA", "USA", "commercial-casino", "commercial-casino",
                "licensed-fy2025", null, "Pennsylvania Gaming Control Board", null, null, null, null, null, null,
                lat, lon, true, null, SourceUrl, DateTime.UtcNow, true, profile.Tables > 0, null,
                null, profile.Racino, null, null, null, null, null, null, profile.AverageMachines, profile.Tables, null,
                null, null, null, null, null, null, null, null, null, null, null,
                $"PGCB FY2024-2025 property profile as of June 30, 2025. Address {profile.Address}; geocode {match.GetProperty("address").GetString()}, score {match.GetProperty("score").GetDouble():F1}. " +
                "Slot count is reported Average Number of Machines in Play, not exact year-end seats. Table count sums banked, nonbanked, fully automated and hybrid categories; source category counts are not converted to player-seat capacity. Fiscal-year revenue was not substituted for calendar-2025 observations. Unreported amenities remain null."));
        }
        var rawHash = Hash(pdfBytes);
        var hash = Hash(Encoding.UTF8.GetBytes(TransformVersion + "\n" + rawHash + "\n" + string.Join('\n', geocodes.Order(StringComparer.Ordinal))));
        return new(new RegisterDataSourceRequest("PGCB FY2025 licensed physical casino inventory", "Pennsylvania Gaming Control Board", SourceUrl,
                "state-regulator-pdf-with-address-geocode", "US-PA", "FY2024-2025", DateTime.UtcNow, hash, true,
                "Pennsylvania public-record and geocoder terms apply.", $"Exact source PDF SHA-256 {rawHash}. Geocoder response hashes:\n{string.Join('\n', geocodes)}"),
            DatasetSnapshotKinds.Competitors, "FY2024-2025", new(2024, 7, 1), new(2025, 6, 30), hash, TransformVersion, rows,
            ["Seventeen land-based casino profiles captured. Pennsylvania truck-stop VGTs are separate and remain outside this inventory.",
             "June-2025 fiscal-year capacity proxies and current address geocodes are not a complete calendar-2025 temporal/fiscal audit. Gaming floor, hotel and other missing attributes remain unknown."]);
    }

    internal static IReadOnlyList<PennsylvaniaGamingProfile> ParseProfiles(IEnumerable<string> pages)
    {
        var result = new List<PennsylvaniaGamingProfile>();
        foreach (var (key, name, address) in Catalog)
        {
            var matching = pages.Where(page => page.Split('\n').Take(3).Any(line => Normalize(line) == Normalize(name))).ToArray();
            if (matching.Length != 1) throw new InvalidDataException($"PGCB expected one property profile for {name}.");
            var page = matching[0];
            var counts = ParsePhysicalCounts(page);
            result.Add(new(key, name, address, counts.Machines, counts.Tables, counts.NonBankedTables, page.Contains("Racino", StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }

    internal static (int Machines, int Tables, int NonBankedTables) ParsePhysicalCounts(string page)
    {
        if (!page.Contains("June 30, 2025", StringComparison.Ordinal)) throw new InvalidDataException("PGCB physical inventory has a different date.");
        int Count(string pattern)
        {
            var matches = Regex.Matches(page, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (matches.Count != 1) throw new InvalidDataException("PGCB physical-count label is absent or duplicated.");
            return int.Parse(matches[0].Groups[1].Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        }
        var machines = Count(@"Average\s*#\s*of\s*Machines\s*in\s*Play\s+([0-9,]+)(?![0-9.,])");
        var nonBanked = Count(@"Non[-\s]*Banked\s*\(([0-9,]+)\)");
        var banked = Count(@"(?<![A-Za-z-])Banked\s*\(([0-9,]+)\)");
        var automated = Count(@"Fully\s+Automated\s*\(([0-9,]+)\)");
        var hybrid = Count(@"Hybrid\s*\(([0-9,]+)\)");
        if (machines <= 0) throw new InvalidDataException("An operating PGCB casino requires a positive reported machine count.");
        return (machines, checked(nonBanked + banked + automated + hybrid), nonBanked);
    }

    private static string Normalize(string value) => Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]", "");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static readonly (string Key, string Name, string Address)[] Catalog =
    [
        ("mohegan-pennsylvania", "MOHEGAN PENNSYLVANIA", "1280 PA-315, Wilkes-Barre, PA 18702"),
        ("harrahs-philadelphia", "HARRAH’S PHILADELPHIA CASINO & RACETRACK", "777 Harrahs Boulevard, Chester, PA 19013"),
        ("presque-isle-downs", "PRESQUE ISLE DOWNS AND CASINO", "8199 Perry Highway, Erie, PA 16509"),
        ("hollywood-meadows", "HOLLYWOOD CASINO AT THE MEADOWS", "210 Racetrack Road, Washington, PA 15301"),
        ("mount-airy", "MOUNT AIRY CASINO RESORT", "312 Woodland Road, Mount Pocono, PA 18344"),
        ("hollywood-penn-national", "HOLLYWOOD CASINO AT PENN NATIONAL RACE COURSE", "777 Hollywood Boulevard, Grantville, PA 17028"),
        ("wind-creek-bethlehem", "WIND CREEK BETHLEHEM CASINO", "77 Wind Creek Boulevard, Bethlehem, PA 18015"),
        ("rivers-pittsburgh", "RIVERS CASINO PITTSBURGH", "777 Casino Drive, Pittsburgh, PA 15212"),
        ("rivers-philadelphia", "RIVERS CASINO PHILADELPHIA", "1001 North Delaware Avenue, Philadelphia, PA 19125"),
        ("valley-forge", "VALLEY FORGE CASINO RESORT", "1160 First Avenue, King of Prussia, PA 19406"),
        ("nemacolin", "THE CASINO AT NEMACOLIN", "4067 National Pike, Farmington, PA 15437"),
        ("parx-bensalem", "PARX CASINO", "2999 Street Road, Bensalem, PA 19020"),
        ("live-philadelphia", "LIVE! CASINO PHILADELPHIA", "900 Packer Avenue, Philadelphia, PA 19148"),
        ("live-pittsburgh", "LIVE! CASINO PITTSBURGH", "5260 US-30, Greensburg, PA 15601"),
        ("hollywood-york", "HOLLYWOOD CASINO YORK", "2899 Whiteford Road, York, PA 17402"),
        ("hollywood-morgantown", "HOLLYWOOD CASINO MORGANTOWN", "6021 Morgantown Road, Morgantown, PA 19543"),
        ("parx-shippensburg", "PARX CASINO SHIPPENSBURG", "250 Conestoga Drive, Shippensburg, PA 17257")
    ];
}
