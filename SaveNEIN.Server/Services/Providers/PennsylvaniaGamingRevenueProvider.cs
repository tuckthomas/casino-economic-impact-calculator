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

/// <summary>Preserves PGCB calendar-year retail slots and tables, excluding online gaming, sports and truck-stop VGTs.</summary>
public sealed class PennsylvaniaGamingRevenueProvider(HttpClient http) : IGamingRegulatorPerformanceProvider
{
    public const string SourceUrl = "https://gamingcontrolboard.pa.gov/news-and-transparency/press-release/pa-gaming-control-board-reports-record-gaming-revenue-2025";
    public const string TransformVersion = "pgcb-calendar2025-retail-annual-v2";
    public string ProviderKey => "pgcb-calendar-year-retail-revenue";
    public string GeographicCoverage => "US-PA";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var bytes = await http.GetByteArrayAsync(SourceUrl, cancellationToken);
        return Capture(bytes);
    }

    public static ProviderDataset<CasinoGamingRevenueImportRow> Capture(byte[] bytes)
    {
        var observations = ParseAnnualRevenue(Encoding.UTF8.GetString(bytes));
        var rawHash = Hash(bytes);
        var hash = Hash(Encoding.UTF8.GetBytes(TransformVersion + "\n" + rawHash));
        return new(new RegisterDataSourceRequest("PGCB calendar-2025 retail gaming performance", "Pennsylvania Gaming Control Board",
                SourceUrl, "state-regulator-annual-retail-revenue", "US-PA", "2025", DateTime.UtcNow, hash, true,
                "Pennsylvania public-record terms apply.", $"Exact source HTML SHA-256 {rawHash}. Both 17-property retail tables reconcile to published statewide controls within whole-dollar rounding."),
            DatasetSnapshotKinds.ObservedPerformance, "2025", new(2025, 1, 1), new(2025, 12, 31), hash, TransformVersion,
            observations, ["Annual values are published in whole dollars. Slot and table observations are preserved without correction; summing rounded property observations can differ slightly from published statewide totals.",
                "This capture supplies observed retail revenue only. Temporal physical capacity, truck-stop VGT competition, input coverage and fiscal/causal acceptance require separate evidence."]);
    }

    internal static void ValidateRequest(ProviderFetchRequest request)
    {
        if (request.GeographicCoverage != "US-PA" || request.PeriodStart != new DateOnly(2025, 1, 1) || request.PeriodEnd != new DateOnly(2025, 12, 31))
            throw new ArgumentException("PGCB annual retail capture requires explicitly supported calendar-2025 Pennsylvania observations.");
    }

    internal static IReadOnlyList<CasinoGamingRevenueImportRow> ParseAnnualRevenue(string html)
    {
        if (html.Length > 2_000_000) throw new InvalidDataException("PGCB annual report exceeds the bounded parser input.");
        var tables = Regex.Matches(html, @"<table\b[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2)).Select(table => Regex.Matches(table.Groups[1].Value, @"<tr\b[^>]*>(.*?)</tr>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).Select(row =>
                    Regex.Matches(row.Groups[1].Value, @"<t[dh]\b[^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase,
                        TimeSpan.FromSeconds(2)).Select(cell => Text(cell.Groups[1].Value)).ToArray()).Where(row => row.Length > 0).ToArray()).ToArray();
        var slots = ReadTable(tables, "Retail Slot Machine Revenue by Operator", "Slot Machine Revenue 2025");
        var games = ReadTable(tables, "Retail Table Games Revenue by Operator", "2025 Table Games Revenue");
        var rows = new List<CasinoGamingRevenueImportRow>();
        foreach (var item in PennsylvaniaGamingFacilityProvider.Catalog.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var venue = "USA-PA-PGCB-" + item.Key;
            var total = slots[item.Key] + games[item.Key];
            foreach (var (metric, value) in new[] { ("pennsylvania-retail-slot-revenue", slots[item.Key]),
                         ("pennsylvania-retail-table-revenue", games[item.Key]), ("pennsylvania-retail-land-based-revenue", total),
                         (GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue, total) })
                rows.Add(new(venue, new(2025, 1, 1), new(2025, 12, 31), "annual", metric,
                    metric == "pennsylvania-retail-slot-revenue" ? "PGCB calendar-2025 published retail slot-machine gross revenue; excludes tables, iGaming, sports wagering, truck-stop VGTs and fantasy contests." :
                    metric == "pennsylvania-retail-table-revenue" ? "PGCB calendar-2025 published retail table-game gross revenue; excludes slots, iGaming, sports wagering, truck-stop VGTs and fantasy contests." :
                    "PGCB calendar-2025 retail casino gross revenue: published retail slots plus retail table games; excludes iGaming, sports wagering, truck-stop VGTs and fantasy contests.",
                    value, null, null, [], $"Source {SourceUrl}; source publishes whole-dollar annual observations; no tax, inflation or promotional adjustment applied."));
        }
        return rows;
    }

    private static Dictionary<string, decimal> ReadTable(string[][][] tables, string operatorHeader, string yearHeader)
    {
        var matching = tables.Where(table => table.Length > 0 && table[0].Length == 4 && table[0][0] == operatorHeader && table[0][1] == yearHeader).ToArray();
        if (matching.Length != 1) throw new InvalidDataException("PGCB retail annual table is missing, duplicated or for a different year.");
        var names = PennsylvaniaGamingFacilityProvider.Catalog.ToDictionary(item => Name(item.Name), item => item.Key, StringComparer.Ordinal);
        names[Name("Harrah's Philadelphia Casino")] = "harrahs-philadelphia";
        names[Name("Hollywood Casino at Penn National")] = "hollywood-penn-national";
        var observations = new Dictionary<string, decimal>(StringComparer.Ordinal);
        decimal? control = null;
        foreach (var row in matching[0].Skip(1))
        {
            if (row.Length != 4) throw new InvalidDataException("PGCB retail annual row has an unexpected shape.");
            if (!Regex.IsMatch(row[1], @"^\$[0-9]+(?:,[0-9]{3})*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
                !decimal.TryParse(row[1].TrimStart('$').Replace(",", "", StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("PGCB retail revenue must be a nonnegative published whole-dollar amount.");
            if (row[0] == "Statewide Total")
            {
                if (control is not null) throw new InvalidDataException("PGCB statewide retail control is duplicated.");
                control = value;
            }
            else if (!names.TryGetValue(Name(row[0]), out var key) || !observations.TryAdd(key, value))
                throw new InvalidDataException("PGCB retail operator is unknown or duplicated.");
        }
        if (observations.Count != PennsylvaniaGamingFacilityProvider.Catalog.Length || control is null ||
            Math.Abs(observations.Values.Sum() - control.Value) > (observations.Count + 1) * 0.5m)
            throw new InvalidDataException("PGCB retail property universe or statewide annual total does not reconcile within whole-dollar rounding.");
        return observations;
    }

    private static string Name(string value) => Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]", "", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)).Replace("CASINO", "", StringComparison.Ordinal);
    private static string Text(string value) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1))), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
