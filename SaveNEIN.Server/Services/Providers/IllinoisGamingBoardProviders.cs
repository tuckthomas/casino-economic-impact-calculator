using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IllinoisGamingBoardProviderOptions
{
    public const string ConfigurationSection = "IllinoisGamingBoard";

    public string ReportsApplicationUrl { get; set; } =
        "https://igbapps.illinois.gov/CasinoReports_AEM.aspx";
    public string ReportsPublicationUrl { get; set; } =
        "https://igb.illinois.gov/casino-gambling/casino-reports.html";
    public string LicenseesPublicationUrl { get; set; } =
        "https://igb.illinois.gov/casino-gambling/casino-lists.html";
    public string OrganizationLicenseesJsonUrl { get; set; } =
        "https://igb.illinois.gov/content/soi/igb/en/casino-gambling/casino-lists/jcr:content/responsivegrid/container/container_293684588/container/container/contentfragmentlist.model.json";
    public string OwnerLicenseesJsonUrl { get; set; } =
        "https://igb.illinois.gov/content/soi/igb/en/casino-gambling/casino-lists/jcr:content/responsivegrid/container/container_293684588/container/container/contentfragmentlist_1067907471.model.json";
    public string GeocoderUrl { get; set; } =
        "https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates";
}

public sealed class IllinoisGamingBoardRevenueProvider(
    HttpClient http,
    IOptions<IllinoisGamingBoardProviderOptions> options) : IGamingRegulatorPerformanceProvider
{
    public string ProviderKey => "illinois-gaming-board-casino-revenue";
    public string GeographicCoverage => "US-IL";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var report = await FetchSummaryAsync(request, cancellationToken);
        var rows = report.Rows.SelectMany(row =>
        {
            var stableVenueId = IllinoisGamingFacilityIds.FromReportedName(row.Name);
            var notes = $"IGB Casino Summary label: {row.Name}. Table-game AGR: {row.TableGameAgr.ToString(CultureInfo.InvariantCulture)}; EGD AGR: {row.EgdAgr.ToString(CultureInfo.InvariantCulture)}.";
            return new[]
            {
                new CasinoGamingRevenueImportRow(
                    stableVenueId,
                    request.PeriodStart,
                    request.PeriodEnd,
                    report.PeriodGranularity,
                    "illinois-total-agr",
                    "Illinois Gaming Board Casino Summary Total AGR, equal to reported table-game AGR plus electronic-gaming-device AGR.",
                    row.TotalAgr,
                    null,
                    null,
                    [],
                    notes),
                new CasinoGamingRevenueImportRow(
                    stableVenueId,
                    request.PeriodStart,
                    request.PeriodEnd,
                    report.PeriodGranularity,
                    GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
                    "Cross-jurisdiction model metric sourced from IGB-reported Total AGR for land-based table games and electronic gaming devices.",
                    row.TotalAgr,
                    null,
                    null,
                    [],
                    $"Comparable-series transform uses IGB Total AGR without numeric adjustment. {notes}")
            };
        }).ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(report.CsvBytes)).ToLowerInvariant();
        var configured = options.Value;

        return new ProviderDataset<CasinoGamingRevenueImportRow>(
            new RegisterDataSourceRequest(
                $"Illinois Gaming Board casino revenue {report.PeriodLabel}",
                "Illinois Gaming Board",
                configured.ReportsPublicationUrl,
                "state-regulator-csv",
                "Illinois licensed casinos and organization gaming licensees",
                report.PeriodLabel,
                report.RetrievedAtUtc,
                checksum,
                true,
                "Illinois public-record terms apply.",
                $"CSV downloaded through the IGB Casino Revenue Reports application at {configured.ReportsApplicationUrl}. The comparable model metric preserves Total AGR numerically while retaining its Illinois-specific definition."),
            DatasetSnapshotKinds.ObservedPerformance,
            report.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "igb-casino-summary-csv-v1",
            rows,
            ["The comparable land-based gaming revenue field is a transparent cross-jurisdiction crosswalk; Illinois Total AGR remains separately preserved under its regulator-specific metric key."]);
    }

    internal async Task<IllinoisCasinoSummary> FetchSummaryAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var (periodLabel, periodGranularity) = RequireIllinoisPeriod(request);
        var configured = options.Value;
        using var landingResponse = await http.GetAsync(
            configured.ReportsApplicationUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        landingResponse.EnsureSuccessStatusCode();
        var landingHtml = await landingResponse.Content.ReadAsStringAsync(cancellationToken);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__VIEWSTATE"] = HiddenField(landingHtml, "__VIEWSTATE"),
            ["__VIEWSTATEGENERATOR"] = HiddenField(landingHtml, "__VIEWSTATEGENERATOR"),
            ["__EVENTVALIDATION"] = HiddenField(landingHtml, "__EVENTVALIDATION"),
            ["CasinoReportTypes"] = "Casino Summary",
            ["SearchStartMonth"] = request.PeriodStart.ToString("MMMM", CultureInfo.InvariantCulture),
            ["SearchStartYear"] = request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture),
            ["SearchEndMonth"] = request.PeriodEnd.ToString("MMMM", CultureInfo.InvariantCulture),
            ["SearchEndYear"] = request.PeriodEnd.Year.ToString(CultureInfo.InvariantCulture),
            ["ViewType"] = "ViewCSV",
            ["ButtonSearch.x"] = "1",
            ["ButtonSearch.y"] = "1"
        });
        using var csvResponse = await http.PostAsync(configured.ReportsApplicationUrl, form, cancellationToken);
        csvResponse.EnsureSuccessStatusCode();
        var csvBytes = await csvResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var rows = ParseCasinoSummaryCsv(Encoding.UTF8.GetString(csvBytes));
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The Illinois Gaming Board Casino Summary CSV contained no facility rows.");
        }
        return new IllinoisCasinoSummary(
            periodLabel,
            periodGranularity,
            DateTime.UtcNow,
            csvBytes,
            rows);
    }

    internal static IReadOnlyList<IllinoisCasinoSummaryRow> ParseCasinoSummaryCsv(string csv)
    {
        var rows = CsvRecordReader.Read(csv);
        var headerIndex = rows.FindIndex(row => row.Count >= 7 &&
            string.Equals(row[0], "Casino", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row[6], "Total AGR", StringComparison.OrdinalIgnoreCase));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("The IGB CSV did not contain the expected Casino Summary header.");
        }
        var result = new List<IllinoisCasinoSummaryRow>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }
            if (string.Equals(row[0].Trim(), "TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            if (row.Count < 7)
            {
                throw new InvalidDataException($"IGB Casino Summary row '{row[0]}' has fewer than seven columns.");
            }
            var tableAgr = Money(row[4], row[0], "Table Game AGR");
            var egdAgr = Money(row[5], row[0], "EGD AGR");
            var totalAgr = Money(row[6], row[0], "Total AGR");
            if (Math.Abs((tableAgr + egdAgr) - totalAgr) > 0.02m)
            {
                throw new InvalidDataException($"IGB Casino Summary row '{row[0]}' does not reconcile Table Game AGR plus EGD AGR to Total AGR.");
            }
            result.Add(new IllinoisCasinoSummaryRow(
                row[0].Trim(),
                WholeNumber(row[1], row[0], "Square Feet"),
                WholeNumber(row[2], row[0], "Admissions"),
                WholeNumber(row[3], row[0], "Operating Days"),
                tableAgr,
                egdAgr,
                totalAgr));
        }
        var duplicate = result.GroupBy(row => IllinoisGamingFacilityIds.FromReportedName(row.Name), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"IGB Casino Summary repeats stable facility ID '{duplicate.Key}'.");
        }
        return result;
    }

    private static (string PeriodLabel, string PeriodGranularity) RequireIllinoisPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IL", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Illinois Gaming Board revenue adapter requires GeographicCoverage 'US-IL'.");
        }
        var monthStart = new DateOnly(request.PeriodStart.Year, request.PeriodStart.Month, 1);
        if (request.PeriodStart == monthStart && request.PeriodEnd == monthStart.AddMonths(1).AddDays(-1))
        {
            return (request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture), "monthly");
        }
        if (request.PeriodStart == new DateOnly(request.PeriodStart.Year, 1, 1) &&
            request.PeriodEnd == new DateOnly(request.PeriodStart.Year, 12, 31))
        {
            return (request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture), "annual");
        }
        throw new ArgumentException(
            "An IGB revenue request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private static string HiddenField(string html, string fieldName)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]+(?:name|id)=[\"']{Regex.Escape(fieldName)}[\"'][^>]+value=[\"'](?<value>[^\"']*)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException($"The IGB report application omitted required form field '{fieldName}'.");
        }
        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    private static decimal Money(string raw, string facility, string metric)
    {
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException($"IGB {metric} for '{facility}' is missing, negative, or not numeric: '{raw}'.");
        }
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static int WholeNumber(string raw, string facility, string metric)
    {
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value < 0 || value != decimal.Truncate(value) || value > int.MaxValue)
        {
            throw new InvalidDataException($"IGB {metric} for '{facility}' is not a nonnegative integer: '{raw}'.");
        }
        return (int)value;
    }
}

public sealed class IllinoisGamingBoardFacilityInventoryProvider(
    HttpClient http,
    IllinoisGamingBoardRevenueProvider revenueProvider,
    IOptions<IllinoisGamingBoardProviderOptions> options) : IGamingFacilityInventoryProvider
{
    public string ProviderKey => "illinois-gaming-board-facility-inventory";
    public string GeographicCoverage => "US-IL";

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        var reportTask = revenueProvider.FetchSummaryAsync(request, cancellationToken);
        var organizationTask = GetBytesAsync(configured.OrganizationLicenseesJsonUrl, cancellationToken);
        var ownersTask = GetBytesAsync(configured.OwnerLicenseesJsonUrl, cancellationToken);
        await Task.WhenAll(reportTask, organizationTask, ownersTask);
        var report = await reportTask;
        var organizationBytes = await organizationTask;
        var ownerBytes = await ownersTask;
        var licenses = ParseLicensedFacilities(organizationBytes)
            .Concat(ParseLicensedFacilities(ownerBytes))
            .GroupBy(item => item.StableVenueId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidDataException($"IGB license data repeats stable facility ID '{group.Key}'."),
                StringComparer.Ordinal);

        var rows = new List<CasinoCompetitorImportRow>(report.Rows.Count);
        var canonicalGeocodes = new List<string>(report.Rows.Count);
        foreach (var summary in report.Rows.OrderBy(row => row.Name, StringComparer.Ordinal))
        {
            var stableVenueId = IllinoisGamingFacilityIds.FromReportedName(summary.Name);
            if (!licenses.TryGetValue(stableVenueId, out var license))
            {
                throw new InvalidDataException(
                    $"IGB Casino Summary facility '{summary.Name}' has no matching active licensed-facility record.");
            }
            var geocode = await GeocodeAsync(license, cancellationToken);
            canonicalGeocodes.Add($"{stableVenueId}|{geocode.Latitude:R}|{geocode.Longitude:R}|{geocode.MatchAddress}|{geocode.Score:R}");
            rows.Add(new CasinoCompetitorImportRow(
                StableVenueId: stableVenueId,
                Name: license.DisplayName,
                State: "IL",
                CountryCode: "USA",
                VenueType: license.IsOrganizationGaming ? "racino" : "commercial-casino",
                FacilityRegime: license.IsOrganizationGaming ? "commercial-racino" : "commercial-casino",
                RegulatoryStatus: "licensed",
                JurisdictionId: null,
                RegulatorName: "Illinois Gaming Board",
                RegulatorLicenseId: null,
                TribalNationName: null,
                OpenedOn: null,
                ClosedOn: null,
                County: null,
                City: license.City,
                Latitude: geocode.Latitude,
                Longitude: geocode.Longitude,
                IsActive: true,
                OperatorName: license.OperatorName,
                SourceUrl: configured.LicenseesPublicationUrl,
                LastVerifiedAt: report.RetrievedAtUtc,
                HasSlots: summary.EgdAgr > 0,
                HasTableGames: summary.TableGameAgr > 0,
                HasPoker: null,
                HasSportsbook: null,
                HasRacetrack: license.IsOrganizationGaming,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: null,
                SlotOrVltPositions: null,
                TableGameCount: null,
                PokerTableCount: null,
                GamingFloorSquareFeet: summary.GamingFloorSquareFeet,
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
                Notes: $"IGB licensed address: {license.FullAddress}. Coordinate match: {geocode.MatchAddress} (score {geocode.Score.ToString("0.0", CultureInfo.InvariantCulture)}). Gaming floor square feet and game-presence flags derive from the {report.PeriodLabel} IGB Casino Summary; unreported attributes remain null."));
        }

        var expectedIds = report.Rows.Select(row => IllinoisGamingFacilityIds.FromReportedName(row.Name)).ToHashSet(StringComparer.Ordinal);
        if (rows.Select(row => row.StableVenueId).ToHashSet(StringComparer.Ordinal).SetEquals(expectedIds) is false)
        {
            throw new InvalidDataException("The IGB licensed facility inventory did not reconcile to the Casino Summary operating universe.");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(report.CsvBytes);
        hasher.AppendData(organizationBytes);
        hasher.AppendData(ownerBytes);
        hasher.AppendData(Encoding.UTF8.GetBytes(string.Join('\n', canonicalGeocodes)));
        var checksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new ProviderDataset<CasinoCompetitorImportRow>(
            new RegisterDataSourceRequest(
                $"Illinois Gaming Board licensed operating casino inventory {report.PeriodLabel}",
                "Illinois Gaming Board",
                configured.LicenseesPublicationUrl,
                "state-regulator-json-and-csv-with-address-geocode",
                "Illinois licensed casinos and organization gaming licensees with reported gaming activity",
                report.PeriodLabel,
                report.RetrievedAtUtc,
                checksum,
                true,
                "Illinois public-record terms apply; coordinate service terms apply to derived geocodes.",
                $"Active operating properties are the exact intersection of the IGB licensed-facility JSON and Casino Summary CSV. Coordinates are reproducibly derived from regulator-published addresses through {configured.GeocoderUrl}; the matched address and score are retained per row."),
            DatasetSnapshotKinds.Competitors,
            report.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "igb-licensed-operating-facilities-v1",
            rows,
            ["Facility coordinates are derived address geocodes rather than regulator-published coordinates; amenities and gaming-position counts absent from the selected IGB sources remain null."]);
    }

    internal static IReadOnlyList<IllinoisGamingLicense> ParseLicensedFacilities(byte[] jsonBytes)
    {
        using var document = JsonDocument.Parse(jsonBytes);
        if (!document.RootElement.TryGetProperty("listItems", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The IGB licensed-facility JSON omitted listItems.");
        }
        var results = new List<IllinoisGamingLicense>();
        foreach (var item in items.EnumerateArray())
        {
            var operatorLabel = RequiredString(item, "businessName");
            var display = RequiredString(item, "licenseDisplay");
            var status = OptionalString(item, "licenseStatus");
            var explicitlyLicensed = string.Equals(status, "Licensed", StringComparison.OrdinalIgnoreCase) ||
                                     operatorLabel.EndsWith("(Licensed)", StringComparison.OrdinalIgnoreCase) &&
                                     display.Contains("License Status: Licensed", StringComparison.OrdinalIgnoreCase);
            if (!explicitlyLicensed)
            {
                continue;
            }
            var operatorName = Regex.Replace(operatorLabel, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
            var lines = Regex.Split(display, @"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(PlainText)
                .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("License Status:", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (lines.Length < 2)
            {
                throw new InvalidDataException($"IGB license record '{operatorName}' has no parseable facility address.");
            }
            var hasDba = lines[0].StartsWith("d/b/a ", StringComparison.OrdinalIgnoreCase);
            var displayName = hasDba ? lines[0][6..].Trim() : operatorName;
            var addressIndex = hasDba ? 1 : 0;
            if (lines.Length <= addressIndex + 1)
            {
                throw new InvalidDataException($"IGB license record '{operatorName}' has an incomplete facility address.");
            }
            var street = lines[addressIndex];
            var cityStatePostal = lines[addressIndex + 1];
            var cityMatch = Regex.Match(cityStatePostal, @"^(?<city>.+?),\s*IL\s+(?<postal>\d{5}(?:-\d{4})?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!cityMatch.Success)
            {
                throw new InvalidDataException($"IGB license record '{operatorName}' has an unexpected Illinois city/state/postal line: '{cityStatePostal}'.");
            }
            var licenseType = RequiredString(item, "licenseType");
            results.Add(new IllinoisGamingLicense(
                IllinoisGamingFacilityIds.FromReportedName(displayName),
                displayName,
                operatorName,
                street,
                cityMatch.Groups["city"].Value.Trim(),
                cityMatch.Groups["postal"].Value,
                licenseType.StartsWith("Organization Gaming", StringComparison.OrdinalIgnoreCase)));
        }
        return results;
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<IllinoisAddressGeocode> GeocodeAsync(
        IllinoisGamingLicense license,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var uri = $"{configured.GeocoderUrl}?SingleLine={Uri.EscapeDataString(license.FullAddress)}&f=json&countryCode=USA&maxLocations=1&outFields=Match_addr,Addr_type";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"The configured geocoder returned no match for IGB facility '{license.DisplayName}' at '{license.FullAddress}'.");
        }
        var candidate = candidates[0];
        var score = candidate.GetProperty("score").GetDouble();
        var location = candidate.GetProperty("location");
        var longitude = location.GetProperty("x").GetDouble();
        var latitude = location.GetProperty("y").GetDouble();
        var matchAddress = candidate.GetProperty("address").GetString() ?? string.Empty;
        if (score < 90 || latitude is < 36.8 or > 42.6 || longitude is < -91.7 or > -87.0)
        {
            throw new InvalidDataException(
                $"Geocoder match for IGB facility '{license.DisplayName}' failed Illinois bounds/quality validation: score={score:R}, latitude={latitude:R}, longitude={longitude:R}.");
        }
        return new IllinoisAddressGeocode(latitude, longitude, score, matchAddress);
    }

    private static string RequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"The IGB licensed-facility JSON omitted required field '{propertyName}'.");
        }
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string PlainText(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant)).Trim();
}

internal sealed record IllinoisCasinoSummary(
    string PeriodLabel,
    string PeriodGranularity,
    DateTime RetrievedAtUtc,
    byte[] CsvBytes,
    IReadOnlyList<IllinoisCasinoSummaryRow> Rows);

internal sealed record IllinoisCasinoSummaryRow(
    string Name,
    int GamingFloorSquareFeet,
    int Admissions,
    int OperatingDays,
    decimal TableGameAgr,
    decimal EgdAgr,
    decimal TotalAgr);

internal sealed record IllinoisGamingLicense(
    string StableVenueId,
    string DisplayName,
    string OperatorName,
    string Street,
    string City,
    string PostalCode,
    bool IsOrganizationGaming)
{
    public string FullAddress => $"{Street}, {City}, IL {PostalCode}";
}

internal sealed record IllinoisAddressGeocode(double Latitude, double Longitude, double Score, string MatchAddress);

internal static class IllinoisGamingFacilityIds
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["argosycasinoalton"] = "argosy-casino-alton",
            ["ballyschicago"] = "ballys-chicago",
            ["ballyschicagooperatingcompanyllc"] = "ballys-chicago",
            ["ballysquadcitiescasinohotel"] = "ballys-quad-cities",
            ["danvilledevelopmentllc"] = "golden-nugget-danville",
            ["goldennuggetdanville"] = "golden-nugget-danville",
            ["draftkingsatcasinoqueen"] = "draftkings-casino-queen",
            ["fairmountpark"] = "fairmount-park-casino-racing",
            ["fairmountparkcasinoracing"] = "fairmount-park-casino-racing",
            ["fhrillinoisllc"] = "american-place",
            ["americanplace"] = "american-place",
            ["grandvictoriacasino"] = "grand-victoria-casino",
            ["hardrockcasinorockford"] = "hard-rock-casino-rockford",
            ["harrahsjolietcasinohotel"] = "harrahs-joliet",
            ["harrahsmetropoliscasino"] = "harrahs-metropolis",
            ["hollywoodcasinoaurora"] = "hollywood-casino-aurora",
            ["hollywoodcasinojoliet"] = "hollywood-casino-joliet",
            ["paradicehotelcasino"] = "par-a-dice-hotel-casino",
            ["riverscasino"] = "rivers-casino-des-plaines",
            ["riverscasinodesplaines"] = "rivers-casino-des-plaines",
            ["walkersbluffcasinoresortllc"] = "walkers-bluff-casino-resort",
            ["windcreekilllc"] = "wind-creek-chicago-southland",
            ["windcreekchicagosouthland"] = "wind-creek-chicago-southland"
        };

    public static string FromReportedName(string name)
    {
        var key = new string(name.Normalize(NormalizationForm.FormKD)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (Aliases.TryGetValue(key, out var alias))
        {
            return $"USA-IL-IGB-{alias}";
        }
        if (key.Length == 0)
        {
            throw new InvalidDataException("An IGB facility name did not contain a stable identifier component.");
        }
        var slug = Regex.Replace(
            name.Normalize(NormalizationForm.FormKD).ToLowerInvariant(),
            @"[^a-z0-9]+",
            "-",
            RegexOptions.CultureInvariant).Trim('-');
        return $"USA-IL-IGB-{slug}";
    }
}

internal static class CsvRecordReader
{
    public static List<IReadOnlyList<string>> Read(string value)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < value.Length && value[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }
            if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                }
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(character);
            }
        }
        if (quoted)
        {
            throw new InvalidDataException("CSV input ended inside a quoted field.");
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }
}
