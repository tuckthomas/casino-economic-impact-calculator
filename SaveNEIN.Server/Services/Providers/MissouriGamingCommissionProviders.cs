// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SaveNEIN.Server.Services.Providers;

public sealed record MissouriGamingCapture(ProviderDataset<CasinoCompetitorImportRow> Facilities,
    ProviderDataset<CasinoGamingRevenueImportRow> Performance, IReadOnlyDictionary<string, byte[]> SourcePdfs);

/// <summary>Calendar-2025 regulator capture for independent revenue backtests.
/// Does not supply Missouri fiscal rules or publish calibrated defaults.</summary>
public sealed class MissouriGamingCommissionProvider(HttpClient http)
{
    private const string AnnualUrl = "https://www.mgc.dps.mo.gov/annual_reports/AR_2025.pdf";
    private const string BaseUrl = "https://www.mgc.dps.mo.gov/Casino_Gaming/rb_financials";
    private const string Geocoder = "https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates";

    public async Task<MissouriGamingCapture> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var annual = await http.GetByteArrayAsync(AnnualUrl, cancellationToken);
        sources.Add("mgc-annual-fy2025.pdf", annual);
        var profiles = ExtractPages(annual);
        var observations = new List<CasinoGamingRevenueImportRow>();
        Dictionary<string, MissouriMonthlyTotals>? december = null;
        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(2025, month, 1);
            var release = start.AddMonths(1);
            var fiscalYear = (release.Month >= 7 ? release.Year + 1 : release.Year) % 100;
            var url = $"{BaseUrl}/FY{fiscalYear}_FinReport/{release:MM}_{release.ToString("MMM", CultureInfo.InvariantCulture)}/detail{start:MMyy}.pdf";
            var bytes = await http.GetByteArrayAsync(url, cancellationToken);
            sources.Add($"mgc-detail-{start:yyyy-MM}.pdf", bytes);
            var totals = ParseMonthlyPages(ExtractPages(bytes), start);
            if (!totals.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Catalog.Select(item => item.Boat)))
                throw new InvalidDataException($"MGC {start:yyyy-MM} facility universe differs from the verified 2025 catalog.");
            if (month == 12) december = totals;
            foreach (var item in Catalog)
            {
                var total = totals[item.Boat];
                foreach (var metric in new[] { "missouri-total-agr", GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue })
                    observations.Add(new(item.Key, start, release.AddDays(-1), "monthly", metric,
                        "MGC published land-based Total AGR, preserved without numeric adjustment. Hybrid presentation is reconciled per source month rather than added twice.",
                        total.Agr, null, null, total.HybridNonAdditive ? ["hybrid-subtotal-nonadditive"] : [],
                        $"Source {url}; slots {total.Slots}; tables {total.Tables}; hybrid units {total.HybridUnits}. Hybrid subtotal {(total.HybridNonAdditive ? "is nonadditive to published slot/table totals" : "is additive when present")}. Figures subject to regulator adjustment.",
                        total.Slots + total.HybridUnits));
            }
            Console.WriteLine($"Captured MGC {start:yyyy-MM}: {totals.Count} complete property observations.");
        }
        var facilities = new List<CasinoCompetitorImportRow>();
        var geocodes = new List<string>();
        foreach (var item in Catalog)
        {
            var profile = profiles[item.AnnualPage - 1];
            if (!Normalize(profile[..Math.Min(200, profile.Length)]).Contains(Normalize(item.ProfileName), StringComparison.Ordinal))
                throw new InvalidDataException($"MGC annual profile page no longer identifies {item.ProfileName}.");
            var uri = $"{Geocoder}?SingleLine={Uri.EscapeDataString(item.Address)}&f=json&countryCode=USA&maxLocations=1&outFields=Match_addr";
            using var geocode = JsonDocument.Parse(await http.GetByteArrayAsync(uri, cancellationToken));
            var candidates = geocode.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0 || candidates[0].GetProperty("score").GetDouble() < 90)
                throw new InvalidDataException($"MGC published address has no adequate geocode: {item.Address}.");
            var match = candidates[0];
            var latitude = match.GetProperty("location").GetProperty("y").GetDouble();
            var longitude = match.GetProperty("location").GetProperty("x").GetDouble();
            var matchAddress = match.GetProperty("address").GetString();
            geocodes.Add($"{item.Key}|{latitude:R}|{longitude:R}|{matchAddress}");
            var total = december![item.Boat];
            int? ProfileCount(string pattern) { var m = Regex.Match(profile, pattern, RegexOptions.IgnoreCase); return m.Success ? int.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : null; }
            var hotel = ProfileCount(@"([\d,]+)[-\u2010-\u2015]Room Hotel");
            facilities.Add(new CasinoCompetitorImportRow(item.Key, item.Name, "MO", "USA", "commercial-casino", "commercial-casino", "licensed", null,
                "Missouri Gaming Commission", null, null, null, null, null, item.City, latitude, longitude, true, null, AnnualUrl, DateTime.UtcNow,
                total.Slots > 0, total.Tables > 0, null, null, null, hotel.HasValue ? true : null, null, null, null, null,
                null, total.Slots + total.HybridUnits, total.Tables, null,
                ProfileCount(@"Gaming Space:\s*([\d,]+)\s+Square Feet"), hotel, null, ProfileCount(@"\((\d+)\)\s*Specialty Restaurants?"),
                null, null, null, null, null, null, null,
                $"FY2025 MGC annual profile page {item.AnnualPage}: {item.Address}. Geocode {matchAddress}, score {match.GetProperty("score").GetDouble():F1}. December 2025 reported units: {total.Slots} EGD, {total.Tables} regular tables, {total.HybridUnits} hybrid electronic units. Electronic position proxy includes hybrid units; legal seat capacity remains unknown. Floor/hotel/restaurant observations use the FY2025 profile, not a newer marketing page. Fiscal and causal-impact validation are outside this capture."));
        }
        var manifest = string.Join('\n', sources.OrderBy(item => item.Key).Select(item => $"{item.Key}|{Hash(item.Value)}"));
        var performanceHash = Hash(Encoding.UTF8.GetBytes(manifest));
        var facilityHash = Hash(Encoding.UTF8.GetBytes(manifest + "\n" + string.Join('\n', geocodes)));
        RegisterDataSourceRequest Source(string hash, string kind) => new($"MGC calendar-2025 {kind}", "Missouri Gaming Commission", AnnualUrl,
            "state-regulator-pdf-with-address-geocode", "US-MO", "2025", DateTime.UtcNow, hash, true, "Missouri public-record terms and geocoder terms apply.",
            $"Monthly report base {BaseUrl}; annual profiles {AnnualUrl}. Source checksum manifest:\n{manifest}. Exact report month, 13-property universe and AGR component reconciliation are verified.");
        return new MissouriGamingCapture(
            new(Source(facilityHash, "facility inventory"), DatasetSnapshotKinds.Competitors, "2025", new(2025, 1, 1), new(2025, 12, 31), facilityHash,
                "mgc-calendar2025-physical-v1", facilities, ["Coordinates are derived address geocodes. December units and FY2025 profile amenities have different observation dates; legal seat counts and unreported attributes remain unknown.", "Missouri fiscal, admission-tax, nonresident demand and causal-impact validation remain unsupported by this revenue-only capture."]),
            new(Source(performanceHash, "land-based gaming performance"), DatasetSnapshotKinds.ObservedPerformance, "2025", new(2025, 1, 1), new(2025, 12, 31), performanceHash,
                "mgc-calendar2025-comparable-agr-v1", observations, ["Regulator figures are subject to adjustment; comparable revenue preserves published Total AGR. Hybrid subtotals are nonadditive in some source months; those records retain an explicit anomaly flag for definition review."]), sources);
    }

    public static string[] ExtractPages(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        // Reading-order extractors can separate table labels from numeric
        // columns. Reassemble words by their physical row before parsing totals.
        return document.GetPages().Select(page =>
        {
            var words = page.GetWords().OrderByDescending(word => word.BoundingBox.Centroid.Y).ToArray();
            var lines = new List<string>();
            var row = new List<UglyToad.PdfPig.Content.Word>();
            double? rowY = null;
            foreach (var word in words)
            {
                if (rowY.HasValue && Math.Abs(word.BoundingBox.Centroid.Y - rowY.Value) > 2.5)
                {
                    lines.Add(string.Join(' ', row.OrderBy(item => item.BoundingBox.Left).Select(item => item.Text)));
                    row.Clear(); rowY = null;
                }
                rowY ??= word.BoundingBox.Centroid.Y;
                row.Add(word);
            }
            if (row.Count > 0) lines.Add(string.Join(' ', row.OrderBy(item => item.BoundingBox.Left).Select(item => item.Text)));
            return string.Join('\n', lines);
        }).ToArray();
    }

    internal static Dictionary<string, MissouriMonthlyTotals> ParseMonthlyPages(IEnumerable<string> pages, DateOnly month)
    {
        var result = new Dictionary<string, MissouriMonthlyTotals>(StringComparer.Ordinal);
        const string number = @"\(?-?[\d,]+\.\d{2}\)?";
        var propertyPages = new List<string>();
        string? current = null;
        foreach (var part in pages)
        {
            var nextBoat = Regex.Match(part, @"BOAT:[ \t]*([^\r\n]+)");
            if (nextBoat.Success)
            {
                if (current is not null && !current.Contains("TOTAL AGR FOR MONTH:", StringComparison.Ordinal) &&
                    Regex.Match(current, @"BOAT:[ \t]*([^\r\n]+)").Groups[1].Value == nextBoat.Groups[1].Value)
                    current += "\n" + part;
                else { if (current is not null) propertyPages.Add(current); current = part; }
            }
            else if (current is not null && !current.Contains("TOTAL AGR FOR MONTH:", StringComparison.Ordinal)) current += "\n" + part;
        }
        if (current is not null) propertyPages.Add(current);
        foreach (var page in propertyPages)
        {
            var boat = Regex.Match(page, @"BOAT:[ \t]*([^\r\n]+)");
            if (!boat.Success) continue; // The final glossary is not a facility.
            var flat = Regex.Replace(page, @"\s+", " ");
            if (!flat.Contains($"MONTH ENDED: {month.ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"MGC page has a different observation month: {flat[..Math.Min(170, flat.Length)]}.");
            (int Units, decimal Agr) Total(string label, bool optional = false)
            {
                var match = Regex.Match(flat, Regex.Escape(label) + @"\s*(?<units>[\d,]+)\s+(?<wagers>" + number + @")\s+(?<agr>" + number + ")");
                if (!match.Success)
                    match = Regex.Match(flat, @"(?<units>[\d,]+)\s+(?<wagers>" + number + @")\s+(?<agr>" + number +
                        @")(?:\s+[-\d,.]+%)?\s*" + Regex.Escape(label));
                if (!match.Success)
                {
                    if (optional && !page.Contains(label, StringComparison.Ordinal)) return (0, 0);
                    throw new InvalidDataException($"MGC total row cannot be read safely: {boat.Groups[1].Value}, {label}.");
                }
                return (int.Parse(match.Groups["units"].Value.Replace(",", ""), CultureInfo.InvariantCulture), Money(match.Groups["agr"].Value));
            }
            var tables = Total("TOTAL TABLE GAMES:");
            var slots = Total("TOTAL SLOTS:");
            var hybrid = Total("TOTAL HYBRID:", optional: true);
            var total = Regex.Match(flat, @"TOTAL AGR FOR MONTH:\s*(" + number + ")");
            if (!total.Success) total = Regex.Match(flat, "(" + number + @")\s*TOTAL AGR FOR MONTH:");
            if (!total.Success) throw new InvalidDataException("MGC monthly AGR total is missing.");
            var agr = Money(total.Groups[1].Value);
            var additive = Math.Abs(agr - tables.Agr - slots.Agr - hybrid.Agr) <= 0.03m;
            var nonadditiveHybrid = hybrid.Agr != 0 && Math.Abs(agr - tables.Agr - slots.Agr) <= 0.03m;
            if (agr < 0 || (!additive && !nonadditiveHybrid))
                throw new InvalidDataException($"MGC components do not reconcile to monthly AGR for {boat.Groups[1].Value}.");
            if (!result.TryAdd(boat.Groups[1].Value.Trim(), new(tables.Units, slots.Units, hybrid.Units, agr, nonadditiveHybrid)))
                throw new InvalidDataException("MGC report repeats a property.");
        }
        if (result.Count == 0) throw new InvalidDataException("MGC report has no property observations.");
        return result;
    }

    private static decimal Money(string value) => decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    internal sealed record MissouriMonthlyTotals(int Tables, int Slots, int HybridUnits, decimal Agr, bool HybridNonAdditive = false);
    private sealed record Casino(string Boat, string Key, string Name, string ProfileName, string Address, string City, int AnnualPage);
    private static readonly Casino[] Catalog =
    [
        new("ARGOSY RIVERSIDE", "USA-MO-MGC-argosy-riverside", "Argosy Riverside Casino", "Argosy Riverside Casino", "777 NW Argosy Parkway, Riverside, MO 64150", "Riverside", 18),
        new("CENTURY CARUTHERSVILLE", "USA-MO-MGC-century-caruthersville", "Century Casino Caruthersville", "Century Casino Caruthersville", "777 E 3rd Street, Caruthersville, MO 63830", "Caruthersville", 21),
        new("HOLLYWOOD", "USA-MO-MGC-hollywood-st-louis", "Hollywood Casino St. Louis", "Hollywood Casino", "777 Casino Center Drive, Maryland Heights, MO 63043", "Maryland Heights", 23),
        new("HARRAHS KANSAS CITY", "USA-MO-MGC-harrahs-kansas-city", "Harrah's Kansas City", "Harrah's Kansas City", "One Riverboat Drive, North Kansas City, MO 64116", "North Kansas City", 22),
        new("BALLY'S KC", "USA-MO-MGC-ballys-kansas-city", "Bally's Kansas City Casino", "Bally's Kansas City Casino", "1800 E Front Street, Kansas City, MO 64120", "Kansas City", 19),
        new("AMERISTAR KC", "USA-MO-MGC-ameristar-kansas-city", "Ameristar Casino Kansas City", "Ameristar Casino Kansas City", "3200 N Ameristar Drive, Kansas City, MO 64161", "Kansas City", 16),
        new("MARK TWAIN", "USA-MO-MGC-mark-twain", "Mark Twain Casino", "Mark Twain Casino", "104 Pierce Street, LaGrange, MO 63448", "LaGrange", 26),
        new("ST. CHARLES", "USA-MO-MGC-ameristar-st-charles", "Ameristar Casino St. Charles", "Ameristar Casino St. Charles", "1 Ameristar Blvd, St Charles, MO 63301", "St. Charles", 17),
        new("RIVER CITY", "USA-MO-MGC-river-city", "River City Casino", "River City Casino", "777 River City Casino Blvd, St Louis, MO 63125", "St. Louis", 27),
        new("HORSESHOE ST. LOUIS", "USA-MO-MGC-horseshoe-st-louis", "Horseshoe St. Louis", "Horseshoe St. Louis", "999 N Second Street, St Louis, MO 63102", "St. Louis", 24),
        new("ISLE OF CAPRI - BOONVILLE", "USA-MO-MGC-isle-of-capri-boonville", "Isle of Capri - Boonville", "Isle of Capri - Boonville", "100 Isle of Capri Boulevard, Boonville, MO 65233", "Boonville", 25),
        new("ST. JOSEPH", "USA-MO-MGC-st-jo-frontier", "St. Jo Frontier Casino", "St. Jo Frontier Casino", "777 Winners Circle, St Joseph, MO 64505", "St. Joseph", 28),
        new("CENTURY CAPE GIRARDEAU", "USA-MO-MGC-century-cape-girardeau", "Century Casino Cape Girardeau", "Century Casino Cape Girardeau", "777 North Main Street, Cape Girardeau, MO 63701", "Cape Girardeau", 20)
    ];
}
