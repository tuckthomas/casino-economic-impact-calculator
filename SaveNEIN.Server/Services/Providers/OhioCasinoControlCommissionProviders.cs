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
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SaveNEIN.Server.Services.Providers;

public sealed class OhioCasinoControlCommissionProviderOptions
{
    public const string ConfigurationSection = "OhioCasinoControlCommission";

    public string AssetBaseUrl { get; set; } =
        "https://dam.assets.ohio.gov/image/upload/casinocontrol.ohio.gov/revenue-reports";
    public string PublicationUrl { get; set; } =
        "https://casinocontrol.ohio.gov/about/revenue-reports";
    public string GeocoderUrl { get; set; } =
        "https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates";
}

public sealed class OhioCasinoControlCommissionRevenueProvider(
    HttpClient http,
    IOptions<OhioCasinoControlCommissionProviderOptions> options) : IGamingRegulatorPerformanceProvider
{
    public const string GrossCasinoRevenueMetricKey = "ohio-gross-casino-revenue";

    public string ProviderKey => "ohio-casino-control-commission-casino-revenue";
    public string GeographicCoverage => "US-OH";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selection = RequireSupportedPeriod(request);
        var report = await FetchReportAsync(selection.Year, selection.ReportMonth, cancellationToken);
        var selectedRows = report.Rows
            .Where(row => selection.SelectedMonth is null || row.Month == selection.SelectedMonth)
            .OrderBy(row => row.StableVenueId, StringComparer.Ordinal)
            .ThenBy(row => row.Month)
            .ToArray();
        var expectedRows = OhioCasinoFacilityCatalog.Entries.Count * (selection.SelectedMonth is null ? 12 : 1);
        if (selectedRows.Length != expectedRows)
        {
            throw new InvalidDataException(
                $"The OCCC report did not contain the expected complete {selection.PeriodLabel} four-casino series.");
        }

        var imports = selectedRows.SelectMany(RevenueRows).ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(report.PdfBytes)).ToLowerInvariant();
        return new ProviderDataset<CasinoGamingRevenueImportRow>(
            new RegisterDataSourceRequest(
                $"Ohio Casino Control Commission casino gross revenue {selection.PeriodLabel}",
                "Ohio Casino Control Commission",
                report.ReportUrl,
                "state-regulator-pdf",
                "Ohio's four constitutionally authorized commercial casinos",
                selection.PeriodLabel,
                report.RetrievedAtUtc,
                checksum,
                true,
                "Ohio public-record terms apply.",
                $"Official cumulative monthly report located from deterministic OCCC asset paths documented at {options.Value.PublicationUrl}. " +
                "The comparable model metric preserves OCCC Total Revenue without numeric modification; Ohio Lottery-regulated video-lottery racinos and sports gaming are excluded."),
            DatasetSnapshotKinds.ObservedPerformance,
            selection.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "occc-casino-revenue-pdf-v1",
            imports,
            [
                "Ohio Lottery-regulated video-lottery racino revenue is not included in this OCCC casino report and requires its own provider.",
                "Sports-gaming receipts are excluded from the land-based comparable gaming-revenue metric."
            ]);
    }

    internal async Task<OhioCasinoReport> FetchReportAsync(
        int year,
        int reportMonth,
        CancellationToken cancellationToken)
    {
        var candidates = CandidateReportUris(options.Value.AssetBaseUrl, year, reportMonth);
        foreach (var candidate in candidates)
        {
            using var response = await http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var pages = OhioCasinoRevenuePdfParser.ExtractPageTexts(bytes);
            var rows = OhioCasinoRevenuePdfParser.ParsePageTexts(pages, year, reportMonth);
            return new OhioCasinoReport(candidate.AbsoluteUri, DateTime.UtcNow, bytes, rows);
        }
        throw new HttpRequestException(
            $"The OCCC casino revenue report for {year}-{reportMonth:00} was not found at any supported official asset path: {string.Join(", ", candidates)}.",
            null,
            HttpStatusCode.NotFound);
    }

    internal static IReadOnlyList<Uri> CandidateReportUris(string assetBaseUrl, int year, int month)
    {
        if (year < 2013 || month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "OCCC report candidates require a year from 2013 onward and month 1 through 12.");
        }
        var file = $"{year}_Ohio_Casino_Monthly_Revenue_Report{month:00}.pdf";
        var root = assetBaseUrl.TrimEnd('/');
        return
        [
            new Uri($"{root}/{year}/Casino/{file}"),
            new Uri($"{root}/{year}/{file}")
        ];
    }

    private static IEnumerable<CasinoGamingRevenueImportRow> RevenueRows(OhioCasinoMonthlyRevenue row)
    {
        var start = new DateOnly(row.Year, row.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var flags = start >= new DateOnly(2020, 3, 1) && start <= new DateOnly(2021, 6, 30)
            ? new[] { "covid-19-pandemic-period" }
            : [];
        var notes =
            $"OCCC facility page reconciled Total Revenue {row.TotalRevenue.ToString(CultureInfo.InvariantCulture)} to Table Revenue {row.TableRevenue.ToString(CultureInfo.InvariantCulture)} plus Slot Revenue {row.SlotRevenue.ToString(CultureInfo.InvariantCulture)}. " +
            $"Month-end inventory: {row.TableCount} tables and {row.SlotCount} slots.";
        yield return new CasinoGamingRevenueImportRow(
            row.StableVenueId,
            start,
            end,
            "monthly",
            GrossCasinoRevenueMetricKey,
            "Ohio Casino Control Commission Total Revenue: gross casino revenue as defined at Ohio Revised Code section 5753.01, equal in this report to table revenue plus slot revenue.",
            row.TotalRevenue,
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
            "Cross-jurisdiction land-based comparable metric sourced from OCCC Total Revenue for table games and slot machines.",
            row.TotalRevenue,
            null,
            null,
            flags,
            $"Comparable-series transform uses OCCC Total Revenue without numeric adjustment. Regulator-specific metric: {GrossCasinoRevenueMetricKey}. {notes}");
    }

    private static OhioRevenuePeriodSelection RequireSupportedPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-OH", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Ohio casino revenue adapter requires GeographicCoverage 'US-OH'.");
        }
        var year = request.PeriodStart.Year;
        if (year < 2013 || request.PeriodEnd.Year != year)
        {
            throw new NotSupportedException("The Ohio casino revenue adapter supports one calendar month or year from 2013 onward.");
        }
        if (request.PeriodStart == new DateOnly(year, 1, 1) &&
            request.PeriodEnd == new DateOnly(year, 12, 31))
        {
            return new OhioRevenuePeriodSelection(year, 12, null, year.ToString(CultureInfo.InvariantCulture));
        }
        if (request.PeriodStart.Day == 1 &&
            request.PeriodEnd == request.PeriodStart.AddMonths(1).AddDays(-1))
        {
            return new OhioRevenuePeriodSelection(
                year,
                request.PeriodStart.Month,
                request.PeriodStart.Month,
                request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
        throw new ArgumentException(
            "An Ohio casino revenue request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private sealed record OhioRevenuePeriodSelection(
        int Year,
        int ReportMonth,
        int? SelectedMonth,
        string PeriodLabel);
}

public sealed class OhioCasinoControlCommissionFacilityInventoryProvider(
    HttpClient http,
    OhioCasinoControlCommissionRevenueProvider revenueProvider,
    IOptions<OhioCasinoControlCommissionProviderOptions> options) : IGamingFacilityInventoryProvider
{
    public string ProviderKey => "ohio-casino-control-commission-facility-inventory";
    public string GeographicCoverage => "US-OH";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var (year, reportMonth, periodLabel) = RequireSupportedPeriod(request);
        var report = await revenueProvider.FetchReportAsync(year, reportMonth, cancellationToken);
        var monthRows = report.Rows.Where(row => row.Month == reportMonth).ToArray();
        if (monthRows.Length != OhioCasinoFacilityCatalog.Entries.Count)
        {
            throw new InvalidDataException("The OCCC report did not contain one current inventory row for every casino.");
        }

        var rows = new List<CasinoCompetitorImportRow>(monthRows.Length);
        var geocodeEvidence = new List<string>(monthRows.Length);
        foreach (var performance in monthRows.OrderBy(row => row.StableVenueId, StringComparer.Ordinal))
        {
            var catalog = OhioCasinoFacilityCatalog.ByStableId[performance.StableVenueId];
            var geocode = await GeocodeAsync(catalog, cancellationToken);
            geocodeEvidence.Add($"{catalog.StableVenueId}|{geocode.Latitude:R}|{geocode.Longitude:R}|{geocode.MatchAddress}|{geocode.Score:R}");
            rows.Add(new CasinoCompetitorImportRow(
                StableVenueId: catalog.StableVenueId,
                Name: catalog.DisplayName,
                State: "OH",
                CountryCode: "USA",
                VenueType: "commercial-casino",
                FacilityRegime: "commercial-casino",
                RegulatoryStatus: "active",
                JurisdictionId: null,
                RegulatorName: "Ohio Casino Control Commission",
                RegulatorLicenseId: null,
                TribalNationName: null,
                OpenedOn: catalog.OpenedOn,
                ClosedOn: null,
                County: catalog.County,
                City: catalog.City,
                Latitude: geocode.Latitude,
                Longitude: geocode.Longitude,
                IsActive: true,
                OperatorName: null,
                SourceUrl: report.ReportUrl,
                LastVerifiedAt: report.RetrievedAtUtc,
                HasSlots: performance.SlotCount > 0,
                HasTableGames: performance.TableCount > 0,
                HasPoker: null,
                HasSportsbook: null,
                HasRacetrack: false,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: performance.SlotCount + performance.TableCount,
                SlotOrVltPositions: performance.SlotCount,
                TableGameCount: performance.TableCount,
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
                Notes: $"OCCC {periodLabel} report page supplied the active facility identity and month-end {performance.TableCount} tables/{performance.SlotCount} slots. " +
                       $"Coordinates derive from the cataloged venue address '{catalog.GeocodeAddress}' through the configured geocoder; match '{geocode.MatchAddress}' scored {geocode.Score.ToString("0.0", CultureInfo.InvariantCulture)}. Unreported attributes remain null."));
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(report.PdfBytes);
        hasher.AppendData(Encoding.UTF8.GetBytes(string.Join('\n', geocodeEvidence)));
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                $"Ohio Casino Control Commission operating casino inventory {periodLabel}",
                "Ohio Casino Control Commission",
                report.ReportUrl,
                "state-regulator-pdf-with-address-geocode",
                "Ohio's four constitutionally authorized commercial casinos",
                periodLabel,
                report.RetrievedAtUtc,
                checksum,
                true,
                "Ohio public-record terms apply; coordinate service terms apply to derived geocodes.",
                $"The exact OCCC facility-page set establishes the operating commercial-casino universe and month-end table/slot counts. Coordinates are reproducibly derived through {options.Value.GeocoderUrl}."),
            DatasetSnapshotKinds.Competitors,
            periodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "occc-operating-casino-inventory-v1",
            rows,
            ["Ohio Lottery-regulated video-lottery racinos are outside this OCCC source and require their own facility/performance provider."]);
    }

    private async Task<OhioCasinoGeocode> GeocodeAsync(
        OhioCasinoFacilityCatalogEntry facility,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var uri = $"{configured.GeocoderUrl}?SingleLine={Uri.EscapeDataString(facility.GeocodeAddress)}&f=json&countryCode=USA&maxLocations=1&outFields=Match_addr,Addr_type";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() != 1)
        {
            throw new InvalidDataException($"The Ohio casino geocoder did not return exactly one candidate for '{facility.DisplayName}'.");
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
                $"The Ohio casino geocode for '{facility.DisplayName}' failed score/bounds validation: score {score}, {latitude}, {longitude}, '{matchAddress}'.");
        }
        return new OhioCasinoGeocode(latitude, longitude, matchAddress, score);
    }

    private static (int Year, int ReportMonth, string PeriodLabel) RequireSupportedPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-OH", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Ohio casino inventory adapter requires GeographicCoverage 'US-OH'.");
        }
        var year = request.PeriodStart.Year;
        if (year < 2013 || request.PeriodEnd.Year != year)
        {
            throw new NotSupportedException("The Ohio casino inventory adapter supports one calendar month or year from 2013 onward.");
        }
        if (request.PeriodStart == new DateOnly(year, 1, 1) && request.PeriodEnd == new DateOnly(year, 12, 31))
        {
            return (year, 12, year.ToString(CultureInfo.InvariantCulture));
        }
        if (request.PeriodStart.Day == 1 && request.PeriodEnd == request.PeriodStart.AddMonths(1).AddDays(-1))
        {
            return (year, request.PeriodStart.Month, request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
        throw new ArgumentException(
            "An Ohio casino inventory request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private sealed record OhioCasinoGeocode(double Latitude, double Longitude, string MatchAddress, double Score);
}

internal sealed record OhioCasinoReport(
    string ReportUrl,
    DateTime RetrievedAtUtc,
    byte[] PdfBytes,
    IReadOnlyList<OhioCasinoMonthlyRevenue> Rows);

internal sealed record OhioCasinoMonthlyRevenue(
    string StableVenueId,
    int Year,
    int Month,
    decimal TotalRevenue,
    decimal TablePromotional,
    decimal SlotPromotional,
    int TableCount,
    decimal TableDrop,
    decimal TableRevenue,
    int SlotCount,
    decimal SlotCoinIn,
    decimal SlotRevenue);

internal static partial class OhioCasinoRevenuePdfParser
{
    private static readonly IReadOnlyDictionary<string, int> MonthNumbers =
        Enumerable.Range(1, 12).ToDictionary(
            month => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month),
            month => month,
            StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> ExtractPageTexts(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
        {
            throw new InvalidDataException("The OCCC revenue PDF is empty.");
        }
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            return document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)).ToArray();
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException("The OCCC revenue PDF could not be parsed.", exception);
        }
    }

    internal static IReadOnlyList<OhioCasinoMonthlyRevenue> ParsePageTexts(
        IReadOnlyList<string> pageTexts,
        int expectedYear,
        int expectedReportMonth)
    {
        if (pageTexts.Count < OhioCasinoFacilityCatalog.Entries.Count + 1)
        {
            throw new InvalidDataException("The OCCC PDF omitted the statewide page or one or more casino pages.");
        }
        OhioCasinoParsedPage? statewide = null;
        var facilities = new Dictionary<string, OhioCasinoParsedPage>(StringComparer.Ordinal);
        foreach (var pageText in pageTexts)
        {
            var normalized = NormalizeText(pageText);
            var title = TitlePattern().Match(normalized);
            if (!title.Success)
            {
                continue;
            }
            var year = int.Parse(title.Groups["year"].Value, CultureInfo.InvariantCulture);
            if (year != expectedYear)
            {
                throw new InvalidDataException($"The OCCC PDF title year {year} does not match requested year {expectedYear}.");
            }
            var reportName = title.Groups["name"].Value.Trim();
            var parsed = ParseRevenuePage(normalized, reportName, year, expectedReportMonth);
            if (string.Equals(reportName, "STATEWIDE", StringComparison.OrdinalIgnoreCase))
            {
                statewide = parsed;
                continue;
            }
            var catalog = OhioCasinoFacilityCatalog.FromReportName(reportName);
            if (!facilities.TryAdd(catalog.StableVenueId, parsed))
            {
                throw new InvalidDataException($"The OCCC PDF repeats casino page '{reportName}'.");
            }
        }
        if (statewide is null)
        {
            throw new InvalidDataException("The OCCC PDF omitted its statewide casino revenue page.");
        }
        var expectedIds = OhioCasinoFacilityCatalog.Entries.Select(entry => entry.StableVenueId).ToHashSet(StringComparer.Ordinal);
        if (!expectedIds.SetEquals(facilities.Keys))
        {
            throw new InvalidDataException(
                $"The OCCC PDF facility pages changed. Expected [{string.Join(", ", expectedIds.Order())}], received [{string.Join(", ", facilities.Keys.Order())}].");
        }

        foreach (var month in Enumerable.Range(1, expectedReportMonth))
        {
            var stateRow = statewide.Months.Single(row => row.Month == month);
            var facilityRows = facilities.Values.Select(page => page.Months.Single(row => row.Month == month)).ToArray();
            RequireEqual(stateRow.TotalRevenue, facilityRows.Sum(row => row.TotalRevenue), month, "Total Revenue");
            RequireEqual(stateRow.TablePromotional, facilityRows.Sum(row => row.TablePromotional), month, "Table Promotional");
            RequireEqual(stateRow.SlotPromotional, facilityRows.Sum(row => row.SlotPromotional), month, "Slot Promotional");
            RequireEqual(stateRow.TableCount, facilityRows.Sum(row => row.TableCount), month, "# of Tables");
            RequireEqual(stateRow.TableDrop, facilityRows.Sum(row => row.TableDrop), month, "Table Drop");
            RequireEqual(stateRow.TableRevenue, facilityRows.Sum(row => row.TableRevenue), month, "Table Revenue");
            RequireEqual(stateRow.SlotCount, facilityRows.Sum(row => row.SlotCount), month, "# of Slots");
            RequireEqual(stateRow.SlotCoinIn, facilityRows.Sum(row => row.SlotCoinIn), month, "Slot Coin In");
            RequireEqual(stateRow.SlotRevenue, facilityRows.Sum(row => row.SlotRevenue), month, "Slot Revenue");
        }

        var rows = new List<OhioCasinoMonthlyRevenue>(expectedReportMonth * facilities.Count);
        foreach (var (stableVenueId, page) in facilities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.AddRange(page.Months.Select(row => new OhioCasinoMonthlyRevenue(
                stableVenueId,
                expectedYear,
                row.Month,
                row.TotalRevenue,
                row.TablePromotional,
                row.SlotPromotional,
                row.TableCount,
                row.TableDrop,
                row.TableRevenue,
                row.SlotCount,
                row.SlotCoinIn,
                row.SlotRevenue)));
        }
        return rows;
    }

    private static OhioCasinoParsedPage ParseRevenuePage(
        string normalized,
        string reportName,
        int year,
        int expectedReportMonth)
    {
        var rows = MonthlyRowPattern().Matches(normalized)
            .Select(match => ParseMonthlyRow(match, reportName))
            .ToArray();
        var expectedMonths = Enumerable.Range(1, expectedReportMonth).ToArray();
        if (!rows.Select(row => row.Month).SequenceEqual(expectedMonths))
        {
            throw new InvalidDataException(
                $"The OCCC {year} {reportName} page must contain months January through {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(expectedReportMonth)} exactly once and in order.");
        }
        var totalMatch = AnnualTotalPattern().Match(normalized);
        if (!totalMatch.Success)
        {
            throw new InvalidDataException($"The OCCC {year} {reportName} page omitted its annual/cumulative Total row.");
        }
        var reported = new[]
        {
            Money(totalMatch.Groups["total"].Value, reportName, "Total Revenue"),
            Money(totalMatch.Groups["tablePromo"].Value, reportName, "Table Promotional"),
            Money(totalMatch.Groups["slotPromo"].Value, reportName, "Slot Promotional"),
            Money(totalMatch.Groups["tableDrop"].Value, reportName, "Table Drop"),
            Money(totalMatch.Groups["tableRevenue"].Value, reportName, "Table Revenue"),
            Money(totalMatch.Groups["slotCoinIn"].Value, reportName, "Slot Coin In"),
            Money(totalMatch.Groups["slotRevenue"].Value, reportName, "Slot Revenue")
        };
        var calculated = new[]
        {
            rows.Sum(row => row.TotalRevenue),
            rows.Sum(row => row.TablePromotional),
            rows.Sum(row => row.SlotPromotional),
            rows.Sum(row => row.TableDrop),
            rows.Sum(row => row.TableRevenue),
            rows.Sum(row => row.SlotCoinIn),
            rows.Sum(row => row.SlotRevenue)
        };
        for (var index = 0; index < reported.Length; index++)
        {
            if (reported[index] != calculated[index])
            {
                throw new InvalidDataException(
                    $"The OCCC {year} {reportName} Total row column {index + 1} does not equal the sum of monthly values.");
            }
        }
        return new OhioCasinoParsedPage(reportName, rows);
    }

    private static OhioCasinoPageMonth ParseMonthlyRow(Match match, string reportName)
    {
        var monthName = match.Groups["month"].Value;
        var total = Money(match.Groups["total"].Value, reportName, $"{monthName} Total Revenue");
        var tableRevenue = Money(match.Groups["tableRevenue"].Value, reportName, $"{monthName} Table Revenue");
        var slotRevenue = Money(match.Groups["slotRevenue"].Value, reportName, $"{monthName} Slot Revenue");
        if (tableRevenue + slotRevenue != total)
        {
            throw new InvalidDataException(
                $"The OCCC {reportName} {monthName} Table Revenue plus Slot Revenue does not reconcile to Total Revenue.");
        }
        return new OhioCasinoPageMonth(
            MonthNumbers[monthName],
            total,
            Money(match.Groups["tablePromo"].Value, reportName, $"{monthName} Table Promotional"),
            Money(match.Groups["slotPromo"].Value, reportName, $"{monthName} Slot Promotional"),
            WholeNumber(match.Groups["tables"].Value, reportName, $"{monthName} # of Tables"),
            Money(match.Groups["tableDrop"].Value, reportName, $"{monthName} Table Drop"),
            tableRevenue,
            WholeNumber(match.Groups["slots"].Value, reportName, $"{monthName} # of Slots"),
            Money(match.Groups["slotCoinIn"].Value, reportName, $"{monthName} Slot Coin In"),
            slotRevenue);
    }

    private static decimal Money(string raw, string reportName, string metric)
    {
        if (!decimal.TryParse(raw, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException($"OCCC {reportName} {metric} is missing, negative, or not numeric: '{raw}'.");
        }
        return value;
    }

    private static int WholeNumber(string raw, string reportName, string metric)
    {
        var value = Money(raw, reportName, metric);
        if (value != decimal.Truncate(value) || value > int.MaxValue)
        {
            throw new InvalidDataException($"OCCC {reportName} {metric} is not a whole number: '{raw}'.");
        }
        return (int)value;
    }

    private static void RequireEqual(decimal statewide, decimal facilities, int month, string metric)
    {
        if (statewide != facilities)
        {
            throw new InvalidDataException(
                $"The OCCC statewide {metric} does not equal the sum of facility pages for month {month}: {statewide} versus {facilities}.");
        }
    }

    private static string NormalizeText(string text) =>
        Regex.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), @"[ \t]+", " ").Trim();

    [GeneratedRegex(@"^(?<year>\d{4})\s+(?<name>.+?)\s+CASINO REVENUE\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(
        @"^(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<total>[\d,]+)\s+(?<tablePromo>[\d,]+)\s+(?<slotPromo>[\d,]+)\s+(?<tables>[\d,]+)\s+(?<tableDrop>[\d,]+)\s+(?<tableRevenue>[\d,]+)\s+[\d.]+%\s+(?<slots>[\d,]+)\s+(?<slotCoinIn>[\d,]+)\s+(?<slotRevenue>[\d,]+)\s+[\d.]+%\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MonthlyRowPattern();

    [GeneratedRegex(
        @"^Total\s+(?<total>[\d,]+)\s+(?<tablePromo>[\d,]+)\s+(?<slotPromo>[\d,]+)\s+(?<tableDrop>[\d,]+)\s+(?<tableRevenue>[\d,]+)\s+(?<slotCoinIn>[\d,]+)\s+(?<slotRevenue>[\d,]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AnnualTotalPattern();

    private sealed record OhioCasinoParsedPage(string ReportName, IReadOnlyList<OhioCasinoPageMonth> Months);

    private sealed record OhioCasinoPageMonth(
        int Month,
        decimal TotalRevenue,
        decimal TablePromotional,
        decimal SlotPromotional,
        int TableCount,
        decimal TableDrop,
        decimal TableRevenue,
        int SlotCount,
        decimal SlotCoinIn,
        decimal SlotRevenue);
}

internal sealed record OhioCasinoFacilityCatalogEntry(
    string ReportName,
    string StableVenueId,
    string DisplayName,
    string City,
    string County,
    string GeocodeAddress,
    DateOnly OpenedOn);

internal static class OhioCasinoFacilityCatalog
{
    public static readonly IReadOnlyList<OhioCasinoFacilityCatalogEntry> Entries =
    [
        new("JACK CLEVELAND", "USA-OH-OCCC-jack-cleveland-casino", "JACK Cleveland Casino", "Cleveland", "Cuyahoga", "100 Public Square, Cleveland, OH 44113", new DateOnly(2012, 5, 14)),
        new("HOLLYWOOD COLUMBUS", "USA-OH-OCCC-hollywood-casino-columbus", "Hollywood Casino Columbus", "Columbus", "Franklin", "200 Georgesville Rd, Columbus, OH 43228", new DateOnly(2012, 10, 8)),
        new("HARD ROCK CINCINNATI", "USA-OH-OCCC-hard-rock-casino-cincinnati", "Hard Rock Casino Cincinnati", "Cincinnati", "Hamilton", "1000 Broadway, Cincinnati, OH 45202", new DateOnly(2013, 3, 4)),
        new("HOLLYWOOD TOLEDO", "USA-OH-OCCC-hollywood-casino-toledo", "Hollywood Casino Toledo", "Toledo", "Lucas", "1968 Miami St, Toledo, OH 43605", new DateOnly(2012, 5, 29))
    ];

    public static readonly IReadOnlyDictionary<string, OhioCasinoFacilityCatalogEntry> ByStableId =
        Entries.ToDictionary(entry => entry.StableVenueId, StringComparer.Ordinal);

    public static OhioCasinoFacilityCatalogEntry FromReportName(string reportName) =>
        Entries.SingleOrDefault(entry => string.Equals(entry.ReportName, reportName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException(
            $"The OCCC PDF contains an unknown casino page '{reportName}'. Review the facility identity crosswalk before ingestion.");
}
