// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class OhioLotteryVideoLotteryProviderOptions
{
    public const string ConfigurationSection = "OhioLotteryVideoLottery";

    public string PublicationUrl { get; set; } =
        "https://www.ohiolottery.com/about/about-the-ohio-lottery/financial/vlt-revenue";
    public string GeocoderUrl { get; set; } =
        "https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates";
}

public sealed class OhioLotteryVideoLotteryRevenueProvider(
    HttpClient http,
    IOptions<OhioLotteryVideoLotteryProviderOptions> options) : IGamingRegulatorPerformanceProvider
{
    public const string NetWinMetricKey = "ohio-video-lottery-net-win";

    public string ProviderKey => "ohio-lottery-video-lottery-net-win";
    public string GeographicCoverage => "US-OH";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var calendar = await FetchCalendarAsync(request, cancellationToken);
        var rows = calendar.Rows.SelectMany(ToImportRows).ToArray();
        return new ProviderDataset<CasinoGamingRevenueImportRow>(
            CreateSource(calendar),
            DatasetSnapshotKinds.ObservedPerformance,
            calendar.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            calendar.ContentChecksum,
            "ohio-lottery-vlt-fiscal-pdf-series-v1",
            rows,
            [
                "Ohio Lottery Net Win is preserved separately from Ohio Casino Control Commission Total Revenue; the two statutory regulator series are not relabeled as one jurisdiction-specific metric.",
                "Table-game revenue and sports-gaming receipts are outside the Ohio Lottery VLT series."
            ]);
    }

    internal async Task<OhioLotteryCalendarDataset> FetchCalendarAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var selection = RequireSupportedPeriod(request);
        var configured = options.Value;
        using var indexResponse = await http.GetAsync(
            configured.PublicationUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        indexResponse.EnsureSuccessStatusCode();
        var indexBytes = await indexResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var indexHtml = Encoding.UTF8.GetString(indexBytes);
        var publishedLinks = OhioLotteryVideoLotteryLinkResolver.ExtractPdfLinks(indexHtml, configured.PublicationUrl);

        var fiscalYears = selection.CalendarMonths
            .Select(month => month.Month <= 6 ? month.Year : month.Year + 1)
            .Distinct()
            .Order()
            .ToArray();
        var documents = new List<OhioLotteryFiscalDocument>();
        var allRows = new List<OhioLotteryVideoLotteryMonth>();
        foreach (var fiscalYear in fiscalYears)
        {
            var facilityLinks = OhioLotteryFacilityCatalog.Entries.ToDictionary(
                entry => entry.StableVenueId,
                entry => OhioLotteryVideoLotteryLinkResolver.ResolveFacilityReport(
                    publishedLinks,
                    entry,
                    fiscalYear),
                StringComparer.Ordinal);
            var facilityTasks = facilityLinks.Select(async pair =>
            {
                var catalog = OhioLotteryFacilityCatalog.ByStableId[pair.Key];
                var document = await FetchDocumentAsync(pair.Value, cancellationToken);
                var parsed = OhioLotteryVideoLotteryPdfParser.Parse(
                    OhioCasinoRevenuePdfParser.ExtractPageTexts(document.Bytes).Single(),
                    fiscalYear,
                    catalog);
                return new OhioLotteryParsedDocument(document, parsed);
            }).ToArray();
            var parsedFacilities = await Task.WhenAll(facilityTasks);
            documents.AddRange(parsedFacilities.Select(item => item.Document));

            var statewideUrl = OhioLotteryVideoLotteryLinkResolver.ResolveStatewideReport(
                publishedLinks,
                fiscalYear,
                facilityLinks.Values.First());
            var statewideDocument = await FetchDocumentAsync(statewideUrl, cancellationToken);
            var statewide = OhioLotteryVideoLotteryPdfParser.Parse(
                OhioCasinoRevenuePdfParser.ExtractPageTexts(statewideDocument.Bytes).Single(),
                fiscalYear,
                null);
            documents.Add(statewideDocument);
            ReconcileStatewide(fiscalYear, parsedFacilities.Select(item => item.Report).ToArray(), statewide);
            allRows.AddRange(parsedFacilities.SelectMany(item => item.Report.Rows));
        }

        var requestedMonthKeys = selection.CalendarMonths
            .Select(month => month.Year * 100 + month.Month)
            .ToHashSet();
        var selectedRows = allRows
            .Where(row => requestedMonthKeys.Contains(row.CalendarYear * 100 + row.CalendarMonth))
            .OrderBy(row => row.StableVenueId, StringComparer.Ordinal)
            .ThenBy(row => row.CalendarYear)
            .ThenBy(row => row.CalendarMonth)
            .ToArray();
        var expectedRows = OhioLotteryFacilityCatalog.Entries.Count * selection.CalendarMonths.Count;
        if (selectedRows.Length != expectedRows)
        {
            throw new InvalidDataException(
                $"The Ohio Lottery VLT fiscal reports did not contain the expected complete {selection.PeriodLabel} seven-racino calendar series.");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(indexBytes);
        foreach (var document in documents.OrderBy(document => document.Url, StringComparer.Ordinal))
        {
            hasher.AppendData(document.Bytes);
        }
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new OhioLotteryCalendarDataset(
            selection.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            DateTime.UtcNow,
            checksum,
            selectedRows,
            documents.OrderBy(document => document.Url, StringComparer.Ordinal).ToArray());
    }

    private RegisterDataSourceRequest CreateSource(OhioLotteryCalendarDataset calendar) =>
        new(
            $"Ohio Lottery video-lottery net win {calendar.PeriodLabel}",
            "Ohio Lottery Commission",
            options.Value.PublicationUrl,
            "state-lottery-pdf-series",
            "Ohio's seven licensed video-lottery racinos",
            calendar.PeriodLabel,
            calendar.RetrievedAtUtc,
            calendar.ContentChecksum,
            true,
            "Ohio public-record terms apply.",
            "Calendar periods are assembled from the exact overlapping Ohio Lottery fiscal-year facility reports and independently reconciled to statewide reports. " +
            $"Source PDFs: {string.Join(", ", calendar.Documents.Select(document => document.Url))}");

    private async Task<OhioLotteryFiscalDocument> FetchDocumentAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"The Ohio Lottery VLT report '{uri}' is empty.");
        }
        return new OhioLotteryFiscalDocument(uri.AbsoluteUri, bytes);
    }

    private static void ReconcileStatewide(
        int fiscalYear,
        IReadOnlyList<OhioLotteryParsedFiscalReport> facilities,
        OhioLotteryParsedFiscalReport statewide)
    {
        var commonKeys = facilities
            .Select(report => report.Rows.Select(row => row.FiscalMonthIndex).ToHashSet())
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });
        commonKeys.IntersectWith(statewide.Rows.Select(row => row.FiscalMonthIndex));
        if (commonKeys.Count < 6)
        {
            throw new InvalidDataException(
                $"The Ohio Lottery FY {fiscalYear} facility and statewide reports share fewer than six months and cannot support calendar reconciliation.");
        }
        foreach (var fiscalMonth in commonKeys.Order())
        {
            var stateRow = statewide.Rows.Single(row => row.FiscalMonthIndex == fiscalMonth);
            var facilityRows = facilities.Select(report => report.Rows.Single(row => row.FiscalMonthIndex == fiscalMonth)).ToArray();
            RequireEqual(stateRow.CreditsPlayed, facilityRows.Sum(row => row.CreditsPlayed), fiscalYear, fiscalMonth, "Credits Played");
            RequireEqual(stateRow.CreditsWon, facilityRows.Sum(row => row.CreditsWon), fiscalYear, fiscalMonth, "Credits Won");
            RequireEqual(stateRow.PromotionalPlayCredits, facilityRows.Sum(row => row.PromotionalPlayCredits), fiscalYear, fiscalMonth, "Promotional Play Credits");
            RequireEqual(stateRow.NetWin, facilityRows.Sum(row => row.NetWin), fiscalYear, fiscalMonth, "Net Win");
            RequireEqual(stateRow.AverageVltCount, facilityRows.Sum(row => row.AverageVltCount), fiscalYear, fiscalMonth, "Average Number of VLTs");
        }
    }

    private static void RequireEqual(decimal statewide, decimal facilities, int fiscalYear, int fiscalMonth, string metric)
    {
        if (statewide != facilities)
        {
            throw new InvalidDataException(
                $"The Ohio Lottery FY {fiscalYear} statewide {metric} does not equal the sum of facility reports for fiscal month {fiscalMonth}: {statewide} versus {facilities}.");
        }
    }

    private static IEnumerable<CasinoGamingRevenueImportRow> ToImportRows(OhioLotteryVideoLotteryMonth row)
    {
        var start = new DateOnly(row.CalendarYear, row.CalendarMonth, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var flags = new List<string>();
        if (start >= new DateOnly(2020, 3, 1) && start <= new DateOnly(2021, 6, 30))
        {
            flags.Add("covid-19-pandemic-period");
        }
        if (row.HasSourceFootnote)
        {
            flags.Add("regulator-footnote-anomaly");
        }
        var notes =
            $"Ohio Lottery FY {row.FiscalYear} facility report. Credits Played {row.CreditsPlayed.ToString(CultureInfo.InvariantCulture)} less Credits Won {row.CreditsWon.ToString(CultureInfo.InvariantCulture)} less Promotional Play Credits {row.PromotionalPlayCredits.ToString(CultureInfo.InvariantCulture)} reconciles to Net Win {row.NetWin.ToString(CultureInfo.InvariantCulture)}. Average VLTs: {row.AverageVltCount}.";
        yield return new CasinoGamingRevenueImportRow(
            row.StableVenueId,
            start,
            end,
            "monthly",
            NetWinMetricKey,
            "Ohio Lottery Net Win: credits played less credits won and less promotional play credits for video lottery terminals.",
            row.NetWin,
            null,
            null,
            flags,
            notes);
        yield return new CasinoGamingRevenueImportRow(
            row.StableVenueId,
            start,
            end,
            "monthly",
            GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
            "Cross-jurisdiction land-based comparable metric sourced from Ohio Lottery video-lottery Net Win.",
            row.NetWin,
            null,
            null,
            flags,
            $"Comparable-series transform uses Ohio Lottery Net Win without numeric adjustment. Regulator-specific metric: {NetWinMetricKey}. {notes}");
    }

    private static OhioLotteryCalendarSelection RequireSupportedPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-OH", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Ohio Lottery VLT adapter requires GeographicCoverage 'US-OH'.");
        }
        var year = request.PeriodStart.Year;
        if (year < 2014 || request.PeriodEnd.Year != year)
        {
            throw new NotSupportedException("The Ohio Lottery VLT adapter supports one calendar month or year from 2014 onward.");
        }
        if (request.PeriodStart == new DateOnly(year, 1, 1) && request.PeriodEnd == new DateOnly(year, 12, 31))
        {
            return new OhioLotteryCalendarSelection(
                year.ToString(CultureInfo.InvariantCulture),
                Enumerable.Range(1, 12).Select(month => new DateOnly(year, month, 1)).ToArray());
        }
        if (request.PeriodStart.Day == 1 && request.PeriodEnd == request.PeriodStart.AddMonths(1).AddDays(-1))
        {
            return new OhioLotteryCalendarSelection(
                request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                [request.PeriodStart]);
        }
        throw new ArgumentException(
            "An Ohio Lottery VLT request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private sealed record OhioLotteryCalendarSelection(string PeriodLabel, IReadOnlyList<DateOnly> CalendarMonths);
    private sealed record OhioLotteryParsedDocument(OhioLotteryFiscalDocument Document, OhioLotteryParsedFiscalReport Report);
}

public sealed class OhioLotteryVideoLotteryFacilityInventoryProvider(
    HttpClient http,
    OhioLotteryVideoLotteryRevenueProvider revenueProvider,
    IOptions<OhioLotteryVideoLotteryProviderOptions> options) : IGamingFacilityInventoryProvider
{
    public string ProviderKey => "ohio-lottery-video-lottery-facility-inventory";
    public string GeographicCoverage => "US-OH";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var calendar = await revenueProvider.FetchCalendarAsync(request, cancellationToken);
        var latestRows = calendar.Rows
            .GroupBy(row => row.StableVenueId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(row => row.CalendarYear).ThenByDescending(row => row.CalendarMonth).First())
            .OrderBy(row => row.StableVenueId, StringComparer.Ordinal)
            .ToArray();
        if (latestRows.Length != OhioLotteryFacilityCatalog.Entries.Count)
        {
            throw new InvalidDataException("The Ohio Lottery VLT reports did not contain one inventory row for every racino.");
        }

        var rows = new List<CasinoCompetitorImportRow>(latestRows.Length);
        var geocodes = new List<string>(latestRows.Length);
        foreach (var latest in latestRows)
        {
            var catalog = OhioLotteryFacilityCatalog.ByStableId[latest.StableVenueId];
            var geocode = await GeocodeAsync(catalog, cancellationToken);
            geocodes.Add($"{catalog.StableVenueId}|{geocode.Latitude:R}|{geocode.Longitude:R}|{geocode.MatchAddress}|{geocode.Score:R}");
            rows.Add(new CasinoCompetitorImportRow(
                StableVenueId: catalog.StableVenueId,
                Name: catalog.DisplayName,
                State: "OH",
                CountryCode: "USA",
                VenueType: "racino",
                FacilityRegime: "commercial-racino",
                RegulatoryStatus: "active",
                JurisdictionId: null,
                RegulatorName: "Ohio Lottery Commission",
                RegulatorLicenseId: null,
                TribalNationName: null,
                OpenedOn: null,
                ClosedOn: null,
                County: catalog.County,
                City: catalog.City,
                Latitude: geocode.Latitude,
                Longitude: geocode.Longitude,
                IsActive: true,
                OperatorName: null,
                SourceUrl: options.Value.PublicationUrl,
                LastVerifiedAt: calendar.RetrievedAtUtc,
                HasSlots: latest.AverageVltCount > 0,
                HasTableGames: false,
                HasPoker: false,
                HasSportsbook: null,
                HasRacetrack: true,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: latest.AverageVltCount,
                SlotOrVltPositions: latest.AverageVltCount,
                TableGameCount: 0,
                PokerTableCount: 0,
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
                Notes: $"Ohio Lottery fiscal report supplied the active facility identity, address, and average VLT count {latest.AverageVltCount} for {latest.CalendarYear}-{latest.CalendarMonth:00}. " +
                       $"Coordinates derive from the regulator-published address '{catalog.GeocodeAddress}' through the configured geocoder; match '{geocode.MatchAddress}' scored {geocode.Score.ToString("0.0", CultureInfo.InvariantCulture)}. Unreported attributes remain null."));
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(Convert.FromHexString(calendar.ContentChecksum));
        hasher.AppendData(Encoding.UTF8.GetBytes(string.Join('\n', geocodes)));
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                $"Ohio Lottery operating video-lottery racino inventory {calendar.PeriodLabel}",
                "Ohio Lottery Commission",
                options.Value.PublicationUrl,
                "state-lottery-pdf-series-with-address-geocode",
                "Ohio's seven licensed video-lottery racinos",
                calendar.PeriodLabel,
                calendar.RetrievedAtUtc,
                checksum,
                true,
                "Ohio public-record terms apply; coordinate service terms apply to derived geocodes.",
                $"The exact facility fiscal-report set establishes identities, regulator-published addresses, and average VLT counts. Coordinates are reproducibly derived through {options.Value.GeocoderUrl}."),
            DatasetSnapshotKinds.Competitors,
            calendar.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "ohio-lottery-vlt-inventory-v1",
            rows,
            ["Average VLT counts are period observations rather than licensed maximums; non-VLT amenities absent from the selected regulator sources remain unknown."]);
    }

    private async Task<OhioLotteryGeocode> GeocodeAsync(
        OhioLotteryFacilityCatalogEntry facility,
        CancellationToken cancellationToken)
    {
        var uri = $"{options.Value.GeocoderUrl}?SingleLine={Uri.EscapeDataString(facility.GeocodeAddress)}&f=json&countryCode=USA&maxLocations=1&outFields=Match_addr,Addr_type";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
        {
            throw new InvalidDataException($"The Ohio Lottery racino geocoder did not return exactly one candidate for '{facility.DisplayName}'.");
        }
        var candidate = candidates[0];
        var score = candidate.GetProperty("score").GetDouble();
        var matchAddress = candidate.GetProperty("address").GetString() ?? string.Empty;
        var location = candidate.GetProperty("location");
        var longitude = location.GetProperty("x").GetDouble();
        var latitude = location.GetProperty("y").GetDouble();
        if (score < 80 || latitude is < 38.0 or > 43.0 || longitude is < -85.0 or > -80.0)
        {
            throw new InvalidDataException(
                $"The Ohio Lottery racino geocode for '{facility.DisplayName}' failed score/bounds validation: score {score}, {latitude}, {longitude}, '{matchAddress}'.");
        }
        return new OhioLotteryGeocode(latitude, longitude, matchAddress, score);
    }

    private sealed record OhioLotteryGeocode(double Latitude, double Longitude, string MatchAddress, double Score);
}

internal sealed record OhioLotteryCalendarDataset(
    string PeriodLabel,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime RetrievedAtUtc,
    string ContentChecksum,
    IReadOnlyList<OhioLotteryVideoLotteryMonth> Rows,
    IReadOnlyList<OhioLotteryFiscalDocument> Documents);

internal sealed record OhioLotteryFiscalDocument(string Url, byte[] Bytes);

internal sealed record OhioLotteryVideoLotteryMonth(
    string StableVenueId,
    int FiscalYear,
    int FiscalMonthIndex,
    int CalendarYear,
    int CalendarMonth,
    decimal CreditsPlayed,
    decimal CreditsWon,
    decimal PromotionalPlayCredits,
    decimal NetWin,
    int AverageVltCount,
    bool HasSourceFootnote);

internal sealed record OhioLotteryParsedFiscalReport(
    int FiscalYear,
    IReadOnlyList<OhioLotteryVideoLotteryMonth> Rows);

internal static partial class OhioLotteryVideoLotteryPdfParser
{
    private static readonly string[] FiscalMonths =
        ["July", "August", "September", "October", "November", "December", "January", "February", "March", "April", "May", "June"];

    internal static OhioLotteryParsedFiscalReport Parse(
        string pageText,
        int expectedFiscalYear,
        OhioLotteryFacilityCatalogEntry? facility)
    {
        var normalized = Normalize(pageText);
        var fiscalYearMatch = FiscalYearPattern().Match(normalized);
        if (!fiscalYearMatch.Success ||
            int.Parse(fiscalYearMatch.Groups["year"].Value, CultureInfo.InvariantCulture) != expectedFiscalYear)
        {
            throw new InvalidDataException($"The Ohio Lottery VLT report does not identify fiscal year {expectedFiscalYear}.");
        }
        if (facility is not null && !facility.ReportNames.Any(name =>
                normalized.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"The Ohio Lottery FY {expectedFiscalYear} PDF did not contain the expected facility identity '{facility.DisplayName}'.");
        }

        var rows = MonthlyRowPattern().Matches(normalized)
            .Select(match => ParseMonth(match, expectedFiscalYear, facility?.StableVenueId ?? "STATEWIDE"))
            .ToArray();
        if (rows.Length == 0 ||
            !rows.Select(row => row.FiscalMonthIndex).SequenceEqual(Enumerable.Range(1, rows.Length)))
        {
            throw new InvalidDataException(
                $"The Ohio Lottery FY {expectedFiscalYear} VLT report must contain a contiguous monthly series beginning in July.");
        }
        var total = TotalPattern().Match(normalized);
        if (!total.Success)
        {
            throw new InvalidDataException($"The Ohio Lottery FY {expectedFiscalYear} VLT report omitted its TOTAL row.");
        }
        var reported = new[]
        {
            Money(total.Groups["played"].Value),
            Money(total.Groups["won"].Value),
            Money(total.Groups["promo"].Value),
            Money(total.Groups["net"].Value)
        };
        var calculated = new[]
        {
            rows.Sum(row => row.CreditsPlayed),
            rows.Sum(row => row.CreditsWon),
            rows.Sum(row => row.PromotionalPlayCredits),
            rows.Sum(row => row.NetWin)
        };
        for (var index = 0; index < reported.Length; index++)
        {
            if (reported[index] != calculated[index])
            {
                throw new InvalidDataException(
                    $"The Ohio Lottery FY {expectedFiscalYear} TOTAL row column {index + 1} does not equal the sum of reported months.");
            }
        }
        return new OhioLotteryParsedFiscalReport(expectedFiscalYear, rows);
    }

    private static OhioLotteryVideoLotteryMonth ParseMonth(Match match, int fiscalYear, string stableVenueId)
    {
        var monthName = match.Groups["month"].Value;
        var fiscalMonth = Array.FindIndex(FiscalMonths, name =>
            string.Equals(name, monthName, StringComparison.OrdinalIgnoreCase)) + 1;
        var calendarMonth = DateTime.ParseExact(monthName, "MMMM", CultureInfo.InvariantCulture).Month;
        var calendarYear = calendarMonth >= 7 ? fiscalYear - 1 : fiscalYear;
        var played = Money(match.Groups["played"].Value);
        var won = Money(match.Groups["won"].Value);
        var promo = Money(match.Groups["promo"].Value);
        var net = Money(match.Groups["net"].Value);
        if (played - won - promo != net)
        {
            throw new InvalidDataException(
                $"The Ohio Lottery FY {fiscalYear} {stableVenueId} {monthName} Credits Played less Credits Won and Promotional Play Credits does not reconcile to Net Win.");
        }
        return new OhioLotteryVideoLotteryMonth(
            stableVenueId,
            fiscalYear,
            fiscalMonth,
            calendarYear,
            calendarMonth,
            played,
            won,
            promo,
            net,
            WholeNumber(match.Groups["vlts"].Value),
            match.Groups["footnote"].Length > 0);
    }

    private static decimal Money(string raw)
    {
        var cleaned = raw.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException($"Ohio Lottery VLT value is missing, negative, or not numeric: '{raw}'.");
        }
        return value;
    }

    private static int WholeNumber(string raw)
    {
        var value = Money(raw);
        if (value != decimal.Truncate(value) || value > int.MaxValue)
        {
            throw new InvalidDataException($"Ohio Lottery VLT count is not a whole number: '{raw}'.");
        }
        return (int)value;
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), @"[ \t]+", " ").Trim();

    [GeneratedRegex(@"VLT RESULTS FOR FISCAL YEAR\s+(?<year>\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FiscalYearPattern();

    [GeneratedRegex(
        @"^(?<footnote>\*{0,3})(?<month>July|August|September|October|November|December|January|February|March|April|May|June)\s+(?<played>\$?[\d,]+)\s+(?<won>\$?[\d,]+)\s+(?<promo>\$?[\d,]+)\s+(?<net>\$?[\d,]+)\s+[\d.]+%\s+(?<vlts>[\d,]+)\s+\$?[\d,]+\s+\$?[\d,]+\s+\$?[\d,]+\s+\$?[\d,]+\s+\$?[\d,]+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MonthlyRowPattern();

    [GeneratedRegex(
        @"^TOTAL\s+(?<played>\$?[\d,]+)\s+(?<won>\$?[\d,]+)\s+(?<promo>\$?[\d,]+)\s+(?<net>\$?[\d,]+)\s+\$?[\d,]+\s+\$?[\d,]+\s+\$?[\d,]+\s+\$?[\d,]+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TotalPattern();
}

internal static partial class OhioLotteryVideoLotteryLinkResolver
{
    internal static IReadOnlyList<Uri> ExtractPdfLinks(string html, string publicationUrl)
    {
        var baseUri = new Uri(publicationUrl);
        var links = PdfLinkPattern().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value))
            .Select(href => Uri.TryCreate(baseUri, href, out var uri) ? uri : null)
            .Where(uri => uri is not null && uri.Scheme is "https" or "http")
            .Cast<Uri>()
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (links.Length == 0)
        {
            throw new InvalidDataException("The Ohio Lottery VLT publication page exposed no PDF links.");
        }
        return links;
    }

    internal static Uri ResolveFacilityReport(
        IReadOnlyList<Uri> links,
        OhioLotteryFacilityCatalogEntry facility,
        int fiscalYear)
    {
        var matches = links
            .Where(uri => facility.ReportCodes.Any(code =>
                Regex.IsMatch(uri.AbsolutePath, $@"VLT-{Regex.Escape(code)}-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .Where(uri => Regex.IsMatch(uri.AbsoluteUri, $@"(?<!\d){fiscalYear}(?!\d)", RegexOptions.CultureInvariant))
            .OrderByDescending(CoverageIndex)
            .ToArray();
        return matches.FirstOrDefault()
            ?? throw new InvalidDataException(
                $"The Ohio Lottery publication page does not expose an FY {fiscalYear} report for '{facility.DisplayName}'.");
    }

    internal static Uri ResolveStatewideReport(
        IReadOnlyList<Uri> links,
        int fiscalYear,
        Uri facilityReport)
    {
        var published = links
            .Where(uri => uri.AbsolutePath.Contains("VLT-Statewide-", StringComparison.OrdinalIgnoreCase))
            .Where(uri => Regex.IsMatch(uri.AbsoluteUri, $@"(?<!\d){fiscalYear}(?!\d)", RegexOptions.CultureInvariant))
            .OrderByDescending(CoverageIndex)
            .FirstOrDefault();
        if (published is not null)
        {
            return published;
        }
        var file = Path.GetFileName(facilityReport.AbsolutePath);
        var statewideFile = Regex.Replace(file, @"^VLT-[A-Z]+-", "VLT-Statewide-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Uri(facilityReport, statewideFile);
    }

    private static int CoverageIndex(Uri uri)
    {
        var match = Regex.Match(
            Path.GetFileNameWithoutExtension(uri.AbsolutePath),
            @"_(?<month>JUL|AUG|SEP|OCT|NOV|DEC|JAN|FEB|MAR|APR|MAY|JUN)(?:-|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return 12;
        }
        var months = new[] { "JUL", "AUG", "SEP", "OCT", "NOV", "DEC", "JAN", "FEB", "MAR", "APR", "MAY", "JUN" };
        return Array.FindIndex(months, month => string.Equals(month, match.Groups["month"].Value, StringComparison.OrdinalIgnoreCase)) + 1;
    }

    [GeneratedRegex(@"href\s*=\s*[\""'](?<href>[^\""']+\.pdf(?:\?[^\""']*)?)[\""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PdfLinkPattern();
}

internal sealed record OhioLotteryFacilityCatalogEntry(
    string StableVenueId,
    string DisplayName,
    IReadOnlyList<string> ReportNames,
    IReadOnlyList<string> ReportCodes,
    string City,
    string County,
    string GeocodeAddress);

internal static class OhioLotteryFacilityCatalog
{
    public static readonly IReadOnlyList<OhioLotteryFacilityCatalogEntry> Entries =
    [
        new("USA-OH-OLC-belterra-park-cincinnati", "Belterra Park Cincinnati", ["Belterra Park Cincinnati"], ["BPC"], "Cincinnati", "Hamilton", "6301 Kellogg Avenue, Cincinnati, OH 45230"),
        new("USA-OH-OLC-eldorado-gaming-scioto-downs", "Eldorado Gaming Scioto Downs", ["Eldorado Gaming Scioto Downs"], ["ESD"], "Columbus", "Franklin", "6000 South High Street, Columbus, OH 43207"),
        new("USA-OH-OLC-mgm-northfield-park", "MGM Northfield Park", ["MGM Northfield Park", "Northfield Park Racino"], ["MGM", "NFP"], "Northfield", "Summit", "10777 Northfield Road, Northfield, OH 44067"),
        new("USA-OH-OLC-hollywood-gaming-dayton-raceway", "Hollywood Gaming Dayton Raceway", ["Hollywood Gaming Dayton Raceway"], ["HDR"], "Dayton", "Montgomery", "777 Hollywood Boulevard, Dayton, OH 45414"),
        new("USA-OH-OLC-hollywood-mahoning-valley-race-course", "Hollywood Mahoning Valley Race Course", ["Hollywood Mahoning Valley Race Course"], ["HMV"], "Youngstown", "Mahoning", "777 Hollywood Avenue, Youngstown, OH 44515"),
        new("USA-OH-OLC-jack-thistledown-racino", "JACK Thistledown Racino", ["JACK Thistledown Racino"], ["THD"], "Cleveland", "Cuyahoga", "21501 Emery Road, Cleveland, OH 44128"),
        new("USA-OH-OLC-miami-valley-gaming", "Miami Valley Gaming", ["Miami Valley Gaming"], ["MVG"], "Lebanon", "Warren", "6000 State Route 63, Lebanon, OH 45036")
    ];

    public static readonly IReadOnlyDictionary<string, OhioLotteryFacilityCatalogEntry> ByStableId =
        Entries.ToDictionary(entry => entry.StableVenueId, StringComparer.Ordinal);
}
