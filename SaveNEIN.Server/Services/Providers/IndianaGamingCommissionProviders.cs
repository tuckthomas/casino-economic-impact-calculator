using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IndianaGamingCommissionProviderOptions
{
    public const string ConfigurationSection = "IndianaGamingCommission";

    public string MonthlyReportBaseUrl { get; set; } = "https://www.in.gov/igc/files/reports";
    public string PublicationUrl { get; set; } = "https://www.in.gov/igc/publications/monthly-revenue/";
    public string CasinoLocationsUrl { get; set; } =
        "https://www.in.gov/igc/about-us/casino-locations-and-information/";
    public string AnnualReportBaseUrl { get; set; } = "https://www.in.gov/igc/files";
}

public sealed class IndianaGamingCommissionMonthlyRevenueProvider(
    HttpClient http,
    IOptions<IndianaGamingCommissionProviderOptions> options) : IGamingRegulatorPerformanceProvider
{
    private const string TaxSummarySheetName = "1 Tax Summary";

    public string ProviderKey => "indiana-gaming-commission-monthly-revenue";
    public string GeographicCoverage => "US-IN";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var monthStarts = RequireIndianaReportPeriod(request);
        var configured = options.Value;
        var retrievedAt = DateTime.UtcNow;
        var reports = await Task.WhenAll(monthStarts.Select(monthStart => FetchMonthAsync(
            configured,
            monthStart,
            cancellationToken)));
        var rows = reports.SelectMany(report => report.Rows).ToArray();
        if (reports.Any(report => report.Rows.Count == 0))
        {
            var empty = reports.Single(report => report.Rows.Count == 0);
            throw new InvalidDataException($"The IGC workbook '{empty.Uri}' contained no facility rows under its Win/Taxable AGR table.");
        }
        using var contentHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var report in reports.OrderBy(report => report.MonthStart))
        {
            contentHasher.AppendData(report.Bytes);
        }
        var checksum = Convert.ToHexString(contentHasher.GetHashAndReset()).ToLowerInvariant();
        var isAnnual = monthStarts.Count == 12;
        var period = isAnnual
            ? request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture)
            : request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var sourceUrl = isAnnual ? configured.PublicationUrl : reports.Single().Uri.ToString();
        var reportUrls = string.Join(", ", reports.OrderBy(report => report.MonthStart).Select(report => report.Uri));

        return new ProviderDataset<CasinoGamingRevenueImportRow>(
            new RegisterDataSourceRequest(
                isAnnual
                    ? $"Indiana Gaming Commission monthly revenue report series {request.PeriodStart.Year}"
                    : $"Indiana Gaming Commission monthly revenue report {request.PeriodStart:yyyy-MM}",
                "Indiana Gaming Commission",
                sourceUrl,
                isAnnual ? "state-regulator-xlsx-series" : "state-regulator-xlsx",
                "Indiana commercial casinos and racinos",
                period,
                retrievedAt,
                checksum,
                true,
                "Indiana public-record terms apply.",
                $"Publication index: {configured.PublicationUrl}. The adapter preserves casino win and the jurisdiction-specific taxable base as separate metrics. Source workbooks: {reportUrls}"),
            DatasetSnapshotKinds.ObservedPerformance,
            period,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "igc-monthly-revenue-xlsx-series-v2",
            rows,
            []);
    }

    private async Task<IgcMonthlyReport> FetchMonthAsync(
        IndianaGamingCommissionProviderOptions configured,
        DateOnly monthStart,
        CancellationToken cancellationToken)
    {
        var reportUri = new Uri(
            $"{configured.MonthlyReportBaseUrl.TrimEnd('/')}/{monthStart.Year}/{monthStart:yyyy-MM}-Revenue.xlsx");
        using var response = await http.GetAsync(reportUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, string>> cells;
        try
        {
            cells = OpenXmlWorksheetReader.ReadRows(bytes, TaxSummarySheetName, "Sheet1");
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"IGC workbook '{reportUri}' could not be parsed.", exception);
        }
        var headerIndex = FindMetricHeader(cells);
        var monthRequest = new ProviderFetchRequest(
            "US-IN",
            monthStart,
            monthStart.AddMonths(1).AddDays(-1));
        var rows = ParseRevenueRows(cells, headerIndex, monthRequest).ToArray();
        return new IgcMonthlyReport(monthStart, reportUri, bytes, rows);
    }

    private static IReadOnlyList<DateOnly> RequireIndianaReportPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "The Indiana Gaming Commission adapter requires GeographicCoverage 'US-IN'.");
        }
        var monthStart = new DateOnly(request.PeriodStart.Year, request.PeriodStart.Month, 1);
        if (request.PeriodStart == monthStart && request.PeriodEnd == monthStart.AddMonths(1).AddDays(-1))
        {
            return [monthStart];
        }
        var yearStart = new DateOnly(request.PeriodStart.Year, 1, 1);
        if (request.PeriodStart == yearStart && request.PeriodEnd == new DateOnly(request.PeriodStart.Year, 12, 31))
        {
            return Enumerable.Range(0, 12).Select(yearStart.AddMonths).ToArray();
        }
        throw new ArgumentException(
            "An IGC revenue request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private sealed record IgcMonthlyReport(
        DateOnly MonthStart,
        Uri Uri,
        byte[] Bytes,
        IReadOnlyCollection<CasinoGamingRevenueImportRow> Rows);

    private static int FindMetricHeader(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (CellEquals(row, "C", "Win") &&
                CellEquals(row, "D", "Free Play") &&
                CellStartsWith(row, "E", "Other") &&
                CellEquals(row, "F", "Taxable AGR"))
            {
                return index;
            }
        }
        throw new InvalidDataException(
            "The IGC workbook did not contain the expected Win/Free Play/Other/Taxable AGR header.");
    }

    private static IEnumerable<CasinoGamingRevenueImportRow> ParseRevenueRows(
        IReadOnlyList<IReadOnlyDictionary<string, string>> sourceRows,
        int headerIndex,
        ProviderFetchRequest request)
    {
        foreach (var row in sourceRows.Skip(headerIndex + 1))
        {
            var reportedName = Cell(row, "A");
            if (string.IsNullOrWhiteSpace(reportedName))
            {
                continue;
            }
            if (string.Equals(reportedName.Trim(), "TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var normalizedName = reportedName.Trim().TrimEnd('*').Trim();
            var stableVenueId = IndianaGamingFacilityIds.FromReportedName(normalizedName);
            var casinoWin = Money(row, "C", normalizedName, "Win");
            var taxableRevenue = Money(row, "F", normalizedName, "Taxable AGR");
            var flags = PandemicFlags(request.PeriodStart);

            yield return new CasinoGamingRevenueImportRow(
                stableVenueId,
                request.PeriodStart,
                request.PeriodEnd,
                "monthly",
                "casino-win",
                "IGC-reported Win: patron gaming wagers less payouts, before free-play and jurisdiction-specific taxable adjustments.",
                casinoWin,
                null,
                null,
                flags,
                $"Facility label in source workbook: {reportedName.Trim()}");
            yield return new CasinoGamingRevenueImportRow(
                stableVenueId,
                request.PeriodStart,
                request.PeriodEnd,
                "monthly",
                "taxable-gaming-revenue",
                "IGC-reported Taxable AGR after the workbook's free-play, other, and applicable racino statutory adjustments.",
                taxableRevenue,
                null,
                null,
                flags,
                $"This Indiana statutory tax base is intentionally distinct from casino win. Facility label in source workbook: {reportedName.Trim()}");
            yield return new CasinoGamingRevenueImportRow(
                stableVenueId,
                request.PeriodStart,
                request.PeriodEnd,
                "monthly",
                GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
                "Cross-jurisdiction model metric sourced from IGC-reported casino Win for land-based gaming; it does not redefine Indiana Taxable AGR.",
                casinoWin,
                null,
                null,
                flags,
                $"Comparable-series transform uses the IGC Win column without numeric adjustment. Facility label in source workbook: {reportedName.Trim()}");
        }
    }

    private static IReadOnlyCollection<string> PandemicFlags(DateOnly periodStart) =>
        periodStart >= new DateOnly(2020, 3, 1) && periodStart <= new DateOnly(2021, 6, 30)
            ? ["covid-19-pandemic-period"]
            : [];

    private static decimal Money(
        IReadOnlyDictionary<string, string> row,
        string column,
        string facility,
        string metric)
    {
        var raw = Cell(row, column);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            throw new InvalidDataException(
                $"IGC {metric} for '{facility}' is missing, negative, or not numeric: '{raw}'.");
        }
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static bool CellEquals(
        IReadOnlyDictionary<string, string> row,
        string column,
        string expected) =>
        string.Equals(Cell(row, column).Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool CellStartsWith(
        IReadOnlyDictionary<string, string> row,
        string column,
        string expected) =>
        Cell(row, column).Trim().StartsWith(expected, StringComparison.OrdinalIgnoreCase);

    private static string Cell(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : string.Empty;
}

public sealed class IndianaGamingCommissionFacilityInventoryProvider(
    HttpClient http,
    IOptions<IndianaGamingCommissionProviderOptions> options) : IGamingFacilityInventoryProvider
{
    private const string TransformVersion = "igc-facilities-units-annual-attributes-employment-v4";
    private static readonly Regex TableRowPattern = new(
        @"<tr\b[^>]*>(?<body>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TableCellPattern = new(
        @"<td\b[^>]*>(?<body>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex LinkPattern = new(
        @"<a\b[^>]*href\s*=\s*[\""'](?<href>[^\""']+)[\""']",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex BreakPattern = new(
        @"<br\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TagPattern = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant);

    public string ProviderKey => "indiana-gaming-commission-facility-inventory";
    public string GeographicCoverage => "US-IN";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var inventoryMonth = RequireIndianaInventoryPeriod(request);
        var periodLabel = request.PeriodStart.Month == 1 && request.PeriodStart.Day == 1 &&
                          request.PeriodEnd == new DateOnly(request.PeriodStart.Year, 12, 31)
            ? request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture)
            : inventoryMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var configured = options.Value;
        var reportUri = new Uri(
            $"{configured.MonthlyReportBaseUrl.TrimEnd('/')}/{inventoryMonth.Year}/{inventoryMonth:yyyy-MM}-Revenue.xlsx");
        var locationsUri = new Uri(configured.CasinoLocationsUrl);
        var annualReportUri = new Uri(
            $"{configured.AnnualReportBaseUrl.TrimEnd('/')}/FY{inventoryMonth.Year}-Annual.pdf");
        var retrievedAt = DateTime.UtcNow;
        using var reportResponse = await http.GetAsync(reportUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        reportResponse.EnsureSuccessStatusCode();
        var workbookBytes = await reportResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        using var locationsResponse = await http.GetAsync(locationsUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        locationsResponse.EnsureSuccessStatusCode();
        var locationsHtml = await locationsResponse.Content.ReadAsStringAsync(cancellationToken);
        using var annualReportResponse = await http.GetAsync(
            annualReportUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        annualReportResponse.EnsureSuccessStatusCode();
        var annualReportBytes = await annualReportResponse.Content.ReadAsByteArrayAsync(cancellationToken);

        var locations = ParseLocations(locationsHtml);
        var unitCounts = ParseGamingUnits(OpenXmlWorksheetReader.ReadRows(workbookBytes, "1 Tax Summary"));
        var facilityAttributes = IndianaGamingCommissionAnnualFacilityParser.Parse(
            annualReportBytes,
            inventoryMonth.Year);
        var expectedIds = IndianaGamingFacilityCatalog.Entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RequireExactInventory("IGC casino-locations page", expectedIds, locations.Keys);
        RequireExactInventory("IGC monthly gaming-unit table", expectedIds, unitCounts.Keys);
        RequireExactInventory("IGC annual facility profiles", expectedIds, facilityAttributes.Keys);

        var rows = IndianaGamingFacilityCatalog.Entries
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var stableId = pair.Key;
                var catalog = pair.Value;
                var location = locations[stableId];
                var units = unitCounts[stableId];
                var attributes = facilityAttributes[stableId];
                return new CasinoCompetitorImportRow(
                    StableVenueId: stableId,
                    Name: location.Name,
                    State: "IN",
                    CountryCode: "USA",
                    VenueType: catalog.IsRacino ? "racino" : "commercial-casino",
                    FacilityRegime: catalog.IsRacino ? "commercial-racino" : "commercial-casino",
                    RegulatoryStatus: "active",
                    JurisdictionId: null,
                    RegulatorName: "Indiana Gaming Commission",
                    RegulatorLicenseId: null,
                    TribalNationName: null,
                    OpenedOn: null,
                    ClosedOn: null,
                    County: catalog.County,
                    City: catalog.City,
                    Latitude: catalog.Latitude,
                    Longitude: catalog.Longitude,
                    IsActive: true,
                    OperatorName: null,
                    SourceUrl: location.PropertyUrl,
                    LastVerifiedAt: retrievedAt,
                    HasSlots: units.SlotOrVltPositions > 0,
                    HasTableGames: units.TableGameCount > 0,
                    HasPoker: null,
                    HasSportsbook: null,
                    HasRacetrack: catalog.IsRacino,
                    HasHotel: attributes.HotelRoomCount > 0,
                    HasRestaurants: attributes.RestaurantCount > 0,
                    HasEntertainment: null,
                    HasLoyaltyProgram: null,
                    HasResortAmenities: null,
                    GamingPositions: null,
                    SlotOrVltPositions: units.SlotOrVltPositions,
                    TableGameCount: units.TableGameCount,
                    PokerTableCount: null,
                    GamingFloorSquareFeet: attributes.GamingFloorSquareFeet,
                    HotelRoomCount: attributes.HotelRoomCount,
                    EventCapacity: null,
                    FoodBeverageVenueCount: attributes.RestaurantCount,
                    DevelopmentCost: null,
                    DevelopmentCostDollarYear: null,
                    AccessContext: null,
                    LimitedAccessDistanceMiles: null,
                    HasInterchangeAccess: null,
                    MarketOrientation: null,
                    IsBorderMarket: null,
                    Notes: $"IGC-published address: {location.Address}. Coordinates are a frozen geocode of that address. " +
                           $"The IGC FY{inventoryMonth.Year} Annual Report reports {attributes.GamingFloorSquareFeet:N0} gaming square feet, " +
                           $"{attributes.RestaurantCount} restaurants, and " +
                           $"{(attributes.HotelRoomCount == 0 ? "no on-property hotel" : $"{attributes.HotelRoomCount:N0} hotel rooms")}, " +
                           $"and total employment of {attributes.TotalEmployment:N0}. " +
                           "Amenities and employment definitions not reported by these sources remain null.",
                    ReportedEmployment: attributes.TotalEmployment);
            })
            .ToArray();
        var canonicalLocations = string.Join(
            '\n',
            locations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}|{pair.Value.Name}|{pair.Value.Address}|{pair.Value.PropertyUrl}"));
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(workbookBytes);
        hasher.AppendData(Encoding.UTF8.GetBytes(canonicalLocations));
        hasher.AppendData(annualReportBytes);
        var rawSourceChecksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var snapshotChecksum = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{rawSourceChecksum}|{TransformVersion}"))).ToLowerInvariant();

        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                $"Indiana Gaming Commission facility inventory and gaming units {periodLabel}",
                "Indiana Gaming Commission",
                locationsUri.ToString(),
                "state-regulator-html-xlsx-and-pdf",
                "Indiana commercial casinos and racinos",
                inventoryMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                retrievedAt,
                rawSourceChecksum,
                true,
                "Indiana public-record terms apply.",
                $"Facility locations come from the IGC locations page; table and EGD/slot counts are the {inventoryMonth:yyyy-MM} month-end inventory from {reportUri}. " +
                $"Gaming-floor area, restaurant count, explicit hotel-room count/absence, and total employment come from the facility profiles in {annualReportUri}. " +
                "Coordinate transform catalog: igc-address-geocodes-2026-08-09-v1."),
            DatasetSnapshotKinds.Competitors,
            periodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            snapshotChecksum,
            TransformVersion,
            rows,
            []);
    }

    private static DateOnly RequireIndianaInventoryPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "The Indiana Gaming Commission facility adapter requires GeographicCoverage 'US-IN'.");
        }
        var expectedStart = new DateOnly(request.PeriodStart.Year, request.PeriodStart.Month, 1);
        if (request.PeriodStart == expectedStart && request.PeriodEnd == expectedStart.AddMonths(1).AddDays(-1))
        {
            return expectedStart;
        }
        if (request.PeriodStart == new DateOnly(request.PeriodStart.Year, 1, 1) &&
            request.PeriodEnd == new DateOnly(request.PeriodStart.Year, 12, 31))
        {
            return new DateOnly(request.PeriodStart.Year, 12, 1);
        }
        throw new ArgumentException(
            "An IGC facility request must use one complete monthly report period or one complete calendar year; " +
            "annual inventory uses the December month-end gaming-unit table.",
            nameof(request));
    }

    private static IReadOnlyDictionary<string, IgcFacilityLocation> ParseLocations(string html)
    {
        var locations = new Dictionary<string, IgcFacilityLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (Match rowMatch in TableRowPattern.Matches(html))
        {
            var cells = TableCellPattern.Matches(rowMatch.Groups["body"].Value)
                .Select(match => match.Groups["body"].Value)
                .ToArray();
            if (cells.Length < 4)
            {
                continue;
            }
            var name = PlainText(cells[0]);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            var stableId = IndianaGamingFacilityIds.FromReportedName(name);
            var link = LinkPattern.Match(cells[3]);
            if (!link.Success)
            {
                throw new InvalidDataException($"IGC facility '{name}' has no property URL in the locations table.");
            }
            if (!locations.TryAdd(stableId, new IgcFacilityLocation(
                    name,
                    PlainText(cells[1]),
                    WebUtility.HtmlDecode(link.Groups["href"].Value))))
            {
                throw new InvalidDataException($"IGC locations page repeats stable facility ID '{stableId}'.");
            }
        }
        return locations;
    }

    private static IReadOnlyDictionary<string, IgcGamingUnits> ParseGamingUnits(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var header = rows
            .Select((row, index) => new { row, index })
            .Where(item =>
                CellEquals(item.row, "A", "WAGERING TAX") &&
                CellStartsWith(item.row, "B", "No. of Table Games") &&
                CellStartsWith(item.row, "D", "No. of EGD/Slots"))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (header < 0)
        {
            throw new InvalidDataException("The IGC workbook has no WAGERING TAX gaming-unit table.");
        }
        var units = new Dictionary<string, IgcGamingUnits>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(header + 1))
        {
            var name = Cell(row, "A");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (string.Equals(name.Trim(), "TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            var normalized = name.Trim().TrimEnd('*').Trim();
            units.Add(
                IndianaGamingFacilityIds.FromReportedName(normalized),
                new IgcGamingUnits(
                    WholeNumber(row, "B", normalized, "table-game count"),
                    WholeNumber(row, "D", normalized, "EGD/slot count")));
        }
        return units;
    }

    private static int WholeNumber(
        IReadOnlyDictionary<string, string> row,
        string column,
        string facility,
        string metric)
    {
        var raw = Cell(row, column);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value < 0 || value != decimal.Truncate(value) || value > int.MaxValue)
        {
            throw new InvalidDataException($"IGC {metric} for '{facility}' is not a nonnegative integer: '{raw}'.");
        }
        return (int)value;
    }

    private static void RequireExactInventory(
        string source,
        IReadOnlySet<string> expected,
        IEnumerable<string> actualValues)
    {
        var actual = actualValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected.Except(actual).Order().ToArray();
        var unexpected = actual.Except(expected).Order().ToArray();
        if (missing.Length > 0 || unexpected.Length > 0)
        {
            throw new InvalidDataException(
                $"{source} did not match the maintained IGC facility catalog. Missing: [{string.Join(", ", missing)}]; unexpected: [{string.Join(", ", unexpected)}].");
        }
    }

    private static string PlainText(string html) =>
        WhitespacePattern.Replace(
                WebUtility.HtmlDecode(
                    TagPattern.Replace(BreakPattern.Replace(html, ", "), " ")),
                " ")
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Trim();

    private static bool CellEquals(
        IReadOnlyDictionary<string, string> row,
        string column,
        string expected) =>
        string.Equals(Cell(row, column).Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool CellStartsWith(
        IReadOnlyDictionary<string, string> row,
        string column,
        string expected) =>
        Cell(row, column).Trim().StartsWith(expected, StringComparison.OrdinalIgnoreCase);

    private static string Cell(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : string.Empty;

    private sealed record IgcFacilityLocation(string Name, string Address, string PropertyUrl);
    private sealed record IgcGamingUnits(int TableGameCount, int SlotOrVltPositions);
}

internal sealed record IndianaGamingFacilityAttributes(
    int GamingFloorSquareFeet,
    int RestaurantCount,
    int HotelRoomCount,
    int TotalEmployment);

internal static partial class IndianaGamingCommissionAnnualFacilityParser
{
    private static readonly IReadOnlyDictionary<string, string> FacilityTitles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AMERISTAR CASINO"] = "USA-IN-IGC-ameristar-casino",
            ["BALLY'S EVANSVILLE"] = "USA-IN-IGC-ballys-evansville",
            ["BELTERRA CASINO"] = "USA-IN-IGC-belterra-casino",
            ["BLUE CHIP CASINO"] = "USA-IN-IGC-blue-chip-casino",
            ["CAESARS SOUTHERN INDIANA"] = "USA-IN-IGC-caesars-southern-indiana",
            ["FRENCH LICK RESORT CASINO"] = "USA-IN-IGC-french-lick-resort",
            ["HARD ROCK NORTHERN INDIANA"] = "USA-IN-IGC-hard-rock-casino-northern-indiana",
            ["HARRAH'S HOOSIER PARK CASINO"] = "USA-IN-IGC-harrahs-hoosier-park",
            ["HOLLYWOOD CASINO"] = "USA-IN-IGC-hollywood-lawrenceburg",
            ["HORSESHOE CASINO HAMMOND"] = "USA-IN-IGC-horseshoe-hammond",
            ["HORSESHOE INDIANAPOLIS"] = "USA-IN-IGC-horseshoe-indianapolis",
            ["RISING STAR CASINO"] = "USA-IN-IGC-rising-star-casino",
            ["TERRE HAUTE CASINO RESORT"] = "USA-IN-IGC-terre-haute-casino"
        };

    internal static IReadOnlyDictionary<string, IndianaGamingFacilityAttributes> Parse(
        byte[] pdfBytes,
        int expectedFiscalYear)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
        {
            throw new InvalidDataException("The IGC annual report PDF is empty.");
        }
        IReadOnlyList<string> pageTexts;
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            pageTexts = document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)).ToArray();
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException("The IGC annual report PDF could not be parsed.", exception);
        }
        return ParsePageTexts(pageTexts, expectedFiscalYear);
    }

    internal static IReadOnlyDictionary<string, IndianaGamingFacilityAttributes> ParsePageTexts(
        IReadOnlyCollection<string> pageTexts,
        int expectedFiscalYear)
    {
        ArgumentNullException.ThrowIfNull(pageTexts);
        if (expectedFiscalYear is < 2007 or > 3000)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedFiscalYear));
        }
        var results = new Dictionary<string, IndianaGamingFacilityAttributes>(StringComparer.Ordinal);
        foreach (var rawPageText in pageTexts)
        {
            var text = Normalize(rawPageText);
            var title = FacilityTitles.FirstOrDefault(pair => text.Contains(pair.Key, StringComparison.Ordinal));
            if (string.IsNullOrEmpty(title.Key))
            {
                continue;
            }
            if (!text.Contains($"ANNUAL REPORT {expectedFiscalYear}", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The IGC facility profile for '{title.Key}' is not from expected annual report {expectedFiscalYear}.");
            }
            var floorMatch = GamingFloorPattern().Match(text);
            var restaurantsMatch = RestaurantsPattern().Match(text);
            var hotelMatch = HotelPattern().Match(text);
            var employmentMatch = EmploymentPattern().Match(text);
            if (!floorMatch.Success || !restaurantsMatch.Success || !hotelMatch.Success || !employmentMatch.Success)
            {
                // Facility names also occur on statewide summary pages. Only a page carrying
                // the complete regulator-published facility attribute tuple is a profile page.
                continue;
            }
            var hotelValue = hotelMatch.Groups["value"].Value;
            var normalizedHotelValue = hotelValue.Replace(" ", string.Empty, StringComparison.Ordinal);
            var attributes = new IndianaGamingFacilityAttributes(
                ParseWholeNumber(floorMatch.Groups["value"].Value, title.Key, "gaming-floor square feet"),
                ParseWholeNumber(restaurantsMatch.Groups["value"].Value, title.Key, "restaurant count"),
                string.Equals(normalizedHotelValue, "N/A", StringComparison.Ordinal)
                    ? 0
                    : ParseWholeNumber(normalizedHotelValue, title.Key, "hotel-room count"),
                ParseWholeNumber(employmentMatch.Groups["value"].Value, title.Key, "total employment"));
            if (!results.TryAdd(title.Value, attributes))
            {
                throw new InvalidDataException($"The IGC annual report repeats facility profile '{title.Key}'.");
            }
        }
        var expected = FacilityTitles.Values.ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(results.Keys))
        {
            var missing = expected.Except(results.Keys).Order(StringComparer.Ordinal);
            var unexpected = results.Keys.Except(expected).Order(StringComparer.Ordinal);
            throw new InvalidDataException(
                $"The IGC annual report facility profiles did not reconcile. Missing: [{string.Join(", ", missing)}]; " +
                $"unexpected: [{string.Join(", ", unexpected)}].");
        }
        return results;
    }

    private static int ParseWholeNumber(string raw, string facility, string field)
    {
        var normalized = raw.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException(
                $"The IGC annual report {field} for '{facility}' is invalid: '{raw}'.");
        }
        return value;
    }

    private static string Normalize(string text) =>
        WhitespacePattern().Replace((text ?? string.Empty).Replace('\u2019', '\''), " ")
            .Trim()
            .ToUpperInvariant();

    [GeneratedRegex(@"GAMING\s*SPACE\s*:?\s*(?<value>[\d,]+)\s*SQUARE\s*FEET", RegexOptions.CultureInvariant)]
    private static partial Regex GamingFloorPattern();

    [GeneratedRegex(@"RESTAURANTS\s*:?\s*(?<value>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RestaurantsPattern();

    [GeneratedRegex(@"HOTEL\s*:?\s*(?<value>N\s*/\s*A|[\d,]+)\s*(?:ROOMS?)?", RegexOptions.CultureInvariant)]
    private static partial Regex HotelPattern();

    [GeneratedRegex(@"TOTAL\s*EMPLOYMENT\s*:?\s*(?<value>[\d,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex EmploymentPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}

internal sealed record IndianaGamingFacilityCatalogEntry(
    string City,
    string County,
    double Latitude,
    double Longitude,
    bool IsRacino);

internal static class IndianaGamingFacilityCatalog
{
    public static readonly IReadOnlyDictionary<string, IndianaGamingFacilityCatalogEntry> Entries =
        new Dictionary<string, IndianaGamingFacilityCatalogEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["USA-IN-IGC-ameristar-casino"] = new("East Chicago", "Lake", 41.6521503, -87.4356793, false),
            ["USA-IN-IGC-ballys-evansville"] = new("Evansville", "Vanderburgh", 37.972581893824, -87.57850700611, false),
            ["USA-IN-IGC-belterra-casino"] = new("Florence", "Switzerland", 38.781538185458, -84.940459046408, false),
            ["USA-IN-IGC-blue-chip-casino"] = new("Michigan City", "LaPorte", 41.718776974043, -86.891430253456, false),
            ["USA-IN-IGC-caesars-southern-indiana"] = new("Elizabeth", "Harrison", 38.1789169, -85.9046683, false),
            ["USA-IN-IGC-french-lick-resort"] = new("French Lick", "Orange", 38.550993124181, -86.619480752287, false),
            ["USA-IN-IGC-hard-rock-casino-northern-indiana"] = new("Gary", "Lake", 41.566315483341, -87.403521426675, false),
            ["USA-IN-IGC-harrahs-hoosier-park"] = new("Anderson", "Madison", 40.0679, -85.641, true),
            ["USA-IN-IGC-hollywood-lawrenceburg"] = new("Lawrenceburg", "Dearborn", 39.0973991, -84.8440636, false),
            ["USA-IN-IGC-horseshoe-hammond"] = new("Hammond", "Lake", 41.692681915464, -87.507449080342, false),
            ["USA-IN-IGC-horseshoe-indianapolis"] = new("Shelbyville", "Shelby", 39.587909998332, -85.826873511019, true),
            ["USA-IN-IGC-rising-star-casino"] = new("Rising Sun", "Ohio", 38.9533, -84.8476, false),
            ["USA-IN-IGC-terre-haute-casino"] = new("Terre Haute", "Vigo", 39.433810860613, -87.347525598996, false)
        };
}

internal static class IndianaGamingFacilityIds
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Indiana Grand"] = "horseshoe-indianapolis",
            ["Hoosier Park"] = "harrahs-hoosier-park",
            ["Harrah's Hoosier Park"] = "harrahs-hoosier-park",
            ["Bally's Evansville"] = "ballys-evansville",
            ["Tropicana Evansville"] = "ballys-evansville",
            ["Ameristar Casino East Chicago"] = "ameristar-casino",
            ["Caesars Southern"] = "caesars-southern-indiana",
            ["French Lick Resort & Casino"] = "french-lick-resort",
            ["Hollywood Casino"] = "hollywood-lawrenceburg",
            ["Harrah's Hoosier Park Casino"] = "harrahs-hoosier-park",
            ["Horseshoe Hammond Casino"] = "horseshoe-hammond",
            ["Terre Haute Casino Resort"] = "terre-haute-casino"
        };

    public static string FromReportedName(string name)
    {
        if (Aliases.TryGetValue(name, out var alias))
        {
            return $"USA-IN-IGC-{alias}";
        }
        var slug = new StringBuilder(name.Length);
        var needsDash = false;
        foreach (var character in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(character))
            {
                if (needsDash && slug.Length > 0)
                {
                    slug.Append('-');
                }
                slug.Append(char.ToLowerInvariant(character));
                needsDash = false;
            }
            else
            {
                needsDash = true;
            }
        }
        if (slug.Length == 0)
        {
            throw new InvalidDataException("An IGC facility name did not contain a stable identifier component.");
        }
        return $"USA-IN-IGC-{slug}";
    }
}

internal static class OpenXmlWorksheetReader
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(
        byte[] workbookBytes,
        params string[] sheetNames)
    {
        try
        {
            using var stream = new MemoryStream(workbookBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var workbook = ReadXml(archive, "xl/workbook.xml");
            if (sheetNames.Length == 0 || sheetNames.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("At least one nonempty XLSX sheet name is required.", nameof(sheetNames));
            }
            var selectedSheet = workbook
                .Descendants(Spreadsheet + "sheet")
                .FirstOrDefault(sheet => sheetNames.Contains(
                    (string?)sheet.Attribute("name"),
                    StringComparer.Ordinal));
            var sheetName = (string?)selectedSheet?.Attribute("name")
                ?? throw new InvalidDataException(
                    $"The XLSX workbook has none of the expected sheets: {string.Join(", ", sheetNames)}.");
            var relationshipId = selectedSheet!.Attribute(Relationships + "id")?.Value
                ?? throw new InvalidDataException($"The XLSX relationship ID for sheet '{sheetName}' is missing.");
            var workbookRelationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
            var target = workbookRelationships
                .Descendants(PackageRelationships + "Relationship")
                .SingleOrDefault(relationship => string.Equals((string?)relationship.Attribute("Id"), relationshipId, StringComparison.Ordinal))
                ?.Attribute("Target")?.Value
                ?? throw new InvalidDataException($"The XLSX relationship for sheet '{sheetName}' is missing.");
            var sheetPath = target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : $"xl/{target.TrimStart('/')}";
            var worksheet = ReadXml(archive, sheetPath.Replace("\\", "/", StringComparison.Ordinal));
            var sharedStrings = ReadSharedStrings(archive);

            return worksheet
                .Descendants(Spreadsheet + "sheetData")
                .Elements(Spreadsheet + "row")
                .Select(row => (IReadOnlyDictionary<string, string>)row
                    .Elements(Spreadsheet + "c")
                    .Select(cell => new KeyValuePair<string, string>(
                        ColumnName((string?)cell.Attribute("r")),
                        CellValue(cell, sharedStrings)))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.Xml.XmlException)
        {
            throw new InvalidDataException("The IGC response was not a readable XLSX workbook.", exception);
        }
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"The XLSX package is missing '{path}'.");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }
        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream, LoadOptions.None);
        return document
            .Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
        }
        var raw = cell.Element(Spreadsheet + "v")?.Value ?? string.Empty;
        if (!string.Equals(type, "s", StringComparison.Ordinal))
        {
            return raw;
        }
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index < 0 || index >= sharedStrings.Count)
        {
            throw new InvalidDataException($"XLSX shared-string index '{raw}' is invalid.");
        }
        return sharedStrings[index];
    }

    private static string ColumnName(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            throw new InvalidDataException("An XLSX cell has no reference.");
        }
        var column = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        if (column.Length == 0)
        {
            throw new InvalidDataException($"XLSX cell reference '{cellReference}' has no column.");
        }
        return column;
    }
}
