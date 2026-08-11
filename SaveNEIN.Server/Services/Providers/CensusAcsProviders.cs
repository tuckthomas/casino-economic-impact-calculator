using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Providers;

public sealed class CensusAcsProviderOptions
{
    public const string ConfigurationSection = "CensusAcs";

    public string BaseUrl { get; set; } = "https://api.census.gov";
    public string? ApiKey { get; set; }
    public string TableSummaryBaseUrl { get; set; } =
        "https://www2.census.gov/programs-surveys/acs/summary_file";
}

internal static class CensusAcsVariables
{
    public sealed record AgePair(int MinimumAge, int? MaximumAge, string MaleVariable, string FemaleVariable);

    public static readonly IReadOnlyList<AgePair> AgePairs =
    [
        new(0, 4, "B01001_003E", "B01001_027E"),
        new(5, 9, "B01001_004E", "B01001_028E"),
        new(10, 14, "B01001_005E", "B01001_029E"),
        new(15, 17, "B01001_006E", "B01001_030E"),
        new(18, 19, "B01001_007E", "B01001_031E"),
        new(20, 20, "B01001_008E", "B01001_032E"),
        new(21, 21, "B01001_009E", "B01001_033E"),
        new(22, 24, "B01001_010E", "B01001_034E"),
        new(25, 29, "B01001_011E", "B01001_035E"),
        new(30, 34, "B01001_012E", "B01001_036E"),
        new(35, 39, "B01001_013E", "B01001_037E"),
        new(40, 44, "B01001_014E", "B01001_038E"),
        new(45, 49, "B01001_015E", "B01001_039E"),
        new(50, 54, "B01001_016E", "B01001_040E"),
        new(55, 59, "B01001_017E", "B01001_041E"),
        new(60, 61, "B01001_018E", "B01001_042E"),
        new(62, 64, "B01001_019E", "B01001_043E"),
        new(65, 66, "B01001_020E", "B01001_044E"),
        new(67, 69, "B01001_021E", "B01001_045E"),
        new(70, 74, "B01001_022E", "B01001_046E"),
        new(75, 79, "B01001_023E", "B01001_047E"),
        new(80, 84, "B01001_024E", "B01001_048E"),
        new(85, null, "B01001_025E", "B01001_049E")
    ];

    public const string MedianHouseholdIncome = "B19013_001E";
    public const string ZctaHeader = "zip code tabulation area";
}

internal sealed record CensusAcsResponse(
    int Year,
    Uri PublicSourceUri,
    DateTime RetrievedAtUtc,
    string ContentHash,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

internal static class CensusAcsProviderSupport
{
    public static int RequireSingleYear(ProviderFetchRequest request)
    {
        if (request.PeriodStart.Year != request.PeriodEnd.Year)
        {
            throw new ArgumentException("An ACS dataset request must cover one vintage year.", nameof(request));
        }
        if (request.PeriodEnd.Year is < 2009 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "ACS 5-year vintages must be 2009 or later.");
        }
        if (!string.Equals(request.GeographicCoverage, "US-ZCTA", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "This ACS adapter emits national ZCTA rows. GeographicCoverage must be 'US-ZCTA'.");
        }
        return request.PeriodEnd.Year;
    }

    public static async Task<CensusAcsResponse> FetchAsync(
        HttpClient http,
        CensusAcsProviderOptions options,
        int year,
        IReadOnlyCollection<string> variables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "CensusAcs:ApiKey is required for Census Data API requests. " +
                "The provider uses the public table-based summary file when that format is available.");
        }

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var variableList = string.Join(',', variables);
        var publicSource = new Uri(
            $"{baseUrl}/data/{year}/acs/acs5?get={Uri.EscapeDataString(variableList)}&for={Uri.EscapeDataString("zip code tabulation area:*")}");
        var requestUri = new Uri(publicSource + $"&key={Uri.EscapeDataString(options.ApiKey)}");
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Census Data API did not return JSON. Verify the optional API key, query limit, and requested vintage.",
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() < 2)
            {
                throw new InvalidOperationException("The Census Data API response contains no data rows.");
            }

            var arrays = document.RootElement.EnumerateArray().ToArray();
            var headers = arrays[0].EnumerateArray().Select(ReadCell).ToArray();
            if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            {
                throw new InvalidOperationException("The Census Data API response contains duplicate headers.");
            }
            var rows = arrays.Skip(1)
                .Select(element => (IReadOnlyList<string>)element.EnumerateArray().Select(ReadCell).ToArray())
                .ToArray();
            if (rows.Any(row => row.Count != headers.Length))
            {
                throw new InvalidOperationException("The Census Data API response contains a malformed row.");
            }

            return new CensusAcsResponse(
                year,
                publicSource,
                retrievedAt,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                headers,
                rows);
        }
    }

    public static Dictionary<string, int> HeaderIndex(CensusAcsResponse response) =>
        response.Headers.Select((header, index) => (header, index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.Ordinal);

    public static long ReadNonnegativeLong(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        string variable,
        string geographyCode)
    {
        if (!headers.TryGetValue(variable, out var index) ||
            !long.TryParse(row[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < 0)
        {
            throw new InvalidOperationException(
                $"ACS variable '{variable}' for ZCTA '{geographyCode}' is missing or invalid.");
        }
        return value;
    }

    public static long? ReadNullableNonnegativeLong(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        string variable,
        string geographyCode)
    {
        if (!headers.TryGetValue(variable, out var index))
        {
            throw new InvalidOperationException($"ACS response is missing required variable '{variable}'.");
        }
        var raw = row[index];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException(
                $"ACS variable '{variable}' for ZCTA '{geographyCode}' is malformed.");
        }
        return value < 0 ? null : value;
    }

    public static string ReadRequired(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        string variable)
    {
        if (!headers.TryGetValue(variable, out var index) || string.IsNullOrWhiteSpace(row[index]))
        {
            throw new InvalidOperationException($"ACS response is missing required field '{variable}'.");
        }
        return row[index].Trim();
    }

    public static RegisterDataSourceRequest Source(CensusAcsResponse response, string description) => new(
        $"ACS 5-year {response.Year} {description}",
        "United States Census Bureau",
        response.PublicSourceUri.ToString(),
        "federal-api",
        "United States ZCTAs",
        response.Year.ToString(CultureInfo.InvariantCulture),
        response.RetrievedAtUtc,
        response.ContentHash,
        true,
        "Census API terms and conditions apply.",
        "ZIP Code Tabulation Areas are Census statistical geographies and are not USPS ZIP Codes.");

    public static IReadOnlyList<IReadOnlyList<string>> SelectRequestedZctas(
        CensusAcsResponse response,
        IReadOnlyDictionary<string, int> headers,
        ProviderFetchRequest request)
    {
        var filter = ZctaCodeFilter.Optional(request.Options);
        if (filter is null)
        {
            return response.Rows;
        }
        var selected = response.Rows.Where(row => filter.Contains(
            ReadRequired(row, headers, CensusAcsVariables.ZctaHeader))).ToArray();
        var selectedCodes = selected.Select(row => ReadRequired(row, headers, CensusAcsVariables.ZctaHeader))
            .ToHashSet(StringComparer.Ordinal);
        var missing = filter.Except(selectedCodes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException(
                $"The ACS response is missing requested ZCTA code(s): {string.Join(", ", missing)}.");
        }
        return selected;
    }

    public static string DatasetChecksum(
        string sourceHash,
        IReadOnlyDictionary<string, string>? requestOptions)
    {
        var codes = ZctaCodeFilter.Optional(requestOptions);
        if (codes is null)
        {
            return sourceHash;
        }
        var canonicalCodes = string.Join(',', codes.Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{sourceHash}\nzcta-codes={canonicalCodes}")))
            .ToLowerInvariant();
    }

    private static string ReadCell(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.Null => string.Empty,
        _ => throw new InvalidOperationException("The Census Data API returned a non-scalar cell.")
    };
}

internal sealed record CensusAcsTableSummaryResponse(
    int Year,
    Uri SourceUri,
    DateTime RetrievedAtUtc,
    string ContentHash,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ValuesByZcta);

internal static class CensusAcsTableSummarySupport
{
    public static async Task<CensusAcsTableSummaryResponse> FetchAsync(
        HttpClient http,
        CensusAcsProviderOptions options,
        int year,
        string tableId,
        IReadOnlyCollection<string> apiVariables,
        ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        if (year < 2022)
        {
            throw new InvalidOperationException(
                $"ACS {year} requires CensusAcs:ApiKey because the supported table-based fallback begins with 2022.");
        }
        var normalizedTable = tableId.Trim().ToLowerInvariant();
        var sourceUri = new Uri(
            $"{options.TableSummaryBaseUrl.TrimEnd('/')}/{year}/table-based-SF/data/5YRData/" +
            $"acsdt5y{year}-{normalizedTable}.dat");
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = (await reader.ReadLineAsync(cancellationToken))?.Split('|')
            ?? throw new InvalidDataException($"ACS table-based summary file '{sourceUri}' is empty.");
        var geographyIndex = RequiredColumn(header, "GEO_ID");
        if (geographyIndex != 0)
        {
            throw new InvalidDataException("ACS table-based summary file must place GEO_ID in its first column.");
        }
        var sourceColumns = apiVariables.ToDictionary(
            variable => variable,
            variable => RequiredColumn(header, ToTableColumn(variable)),
            StringComparer.Ordinal);
        var filter = ZctaCodeFilter.Optional(request.Options);
        var valuesByZcta = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("860Z200US", StringComparison.Ordinal) || line.Length < 14)
            {
                continue;
            }
            var zcta = line.Substring(9, 5);
            if (filter is not null && !filter.Contains(zcta))
            {
                continue;
            }
            var fields = line.TrimEnd('\r').Split('|');
            if (fields.Length != header.Length)
            {
                throw new InvalidDataException($"ACS table '{tableId}' contains a malformed ZCTA row '{zcta}'.");
            }
            if (!valuesByZcta.TryAdd(
                    zcta,
                    sourceColumns.ToDictionary(
                        pair => pair.Key,
                        pair => fields[pair.Value],
                        StringComparer.Ordinal)))
            {
                throw new InvalidDataException($"ACS table '{tableId}' contains duplicate ZCTA '{zcta}'.");
            }
        }
        if (filter is not null)
        {
            var missing = filter.Except(valuesByZcta.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
            {
                throw new KeyNotFoundException(
                    $"ACS table '{tableId}' is missing requested ZCTA code(s): {string.Join(", ", missing)}.");
            }
        }
        if (valuesByZcta.Count == 0)
        {
            throw new InvalidDataException($"ACS table '{tableId}' contains no selected ZCTA rows.");
        }
        return new CensusAcsTableSummaryResponse(
            year,
            sourceUri,
            retrievedAt,
            contentHash,
            valuesByZcta);
    }

    public static long ReadNonnegativeLong(
        IReadOnlyDictionary<string, string> values,
        string variable,
        string zcta)
    {
        if (!values.TryGetValue(variable, out var raw) ||
            !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < 0)
        {
            throw new InvalidDataException($"ACS table variable '{variable}' for ZCTA '{zcta}' is missing or invalid.");
        }
        return value;
    }

    public static long? ReadNullableNonnegativeLong(
        IReadOnlyDictionary<string, string> values,
        string variable,
        string zcta)
    {
        if (!values.TryGetValue(variable, out var raw))
        {
            throw new InvalidDataException($"ACS table is missing required variable '{variable}'.");
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"ACS table variable '{variable}' for ZCTA '{zcta}' is malformed.");
        }
        return value < 0 ? null : value;
    }

    public static RegisterDataSourceRequest Source(
        CensusAcsTableSummaryResponse response,
        string description) => new(
        $"ACS 5-year {response.Year} {description}",
        "United States Census Bureau",
        response.SourceUri.ToString(),
        "federal-table-based-summary-file",
        "United States ZCTAs",
        response.Year.ToString(CultureInfo.InvariantCulture),
        response.RetrievedAtUtc,
        response.ContentHash,
        true,
        "Census public-use terms apply.",
        "Official table-based ACS Summary File. ZIP Code Tabulation Areas are Census statistical geographies and are not USPS ZIP Codes.");

    private static string ToTableColumn(string apiVariable)
    {
        if (apiVariable.Length < 6 || apiVariable[^1] != 'E')
        {
            throw new ArgumentException($"Unsupported ACS estimate variable '{apiVariable}'.", nameof(apiVariable));
        }
        var separator = apiVariable.LastIndexOf('_');
        if (separator <= 0)
        {
            throw new ArgumentException($"Unsupported ACS estimate variable '{apiVariable}'.", nameof(apiVariable));
        }
        return $"{apiVariable[..separator]}_E{apiVariable[(separator + 1)..^1]}";
    }

    private static int RequiredColumn(IReadOnlyList<string> header, string name)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (string.Equals(header[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }
        throw new InvalidDataException($"ACS table-based summary file is missing column '{name}'.");
    }
}

public sealed class CensusAcsAgePopulationProvider(
    HttpClient http,
    IOptions<CensusAcsProviderOptions> options) : IAgePopulationProvider
{
    public string ProviderKey => "census-acs5-zcta-age";

    public async Task<ProviderDataset<OriginAgeBinImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var year = CensusAcsProviderSupport.RequireSingleYear(request);
        var variables = CensusAcsVariables.AgePairs
            .SelectMany(pair => new[] { pair.MaleVariable, pair.FemaleVariable })
            .Prepend("NAME")
            .ToArray();
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return await FetchFromTableSummaryAsync(request, year, variables, cancellationToken);
        }
        var response = await CensusAcsProviderSupport.FetchAsync(http, options.Value, year, variables, cancellationToken);
        var headers = CensusAcsProviderSupport.HeaderIndex(response);
        var selectedSourceRows = CensusAcsProviderSupport.SelectRequestedZctas(response, headers, request);
        var rows = new List<OriginAgeBinImportRow>(selectedSourceRows.Count * CensusAcsVariables.AgePairs.Count);

        foreach (var sourceRow in selectedSourceRows)
        {
            var zcta = CensusAcsProviderSupport.ReadRequired(sourceRow, headers, CensusAcsVariables.ZctaHeader);
            foreach (var pair in CensusAcsVariables.AgePairs)
            {
                var population = checked(
                    CensusAcsProviderSupport.ReadNonnegativeLong(sourceRow, headers, pair.MaleVariable, zcta) +
                    CensusAcsProviderSupport.ReadNonnegativeLong(sourceRow, headers, pair.FemaleVariable, zcta));
                rows.Add(new OriginAgeBinImportRow(
                    $"USA-ZCTA-{zcta}",
                    year,
                    pair.MinimumAge,
                    pair.MaximumAge,
                    population,
                    DatasetValidationStates.Validated));
            }
        }

        return new ProviderDataset<OriginAgeBinImportRow>(
            CensusAcsProviderSupport.Source(response, "ZCTA sex-by-age population"),
            DatasetSnapshotKinds.AgePopulation,
            year.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            CensusAcsProviderSupport.DatasetChecksum(response.ContentHash, request.Options),
            "census-acs5-b01001-zcta-v1",
            rows,
            []);
    }

    private async Task<ProviderDataset<OriginAgeBinImportRow>> FetchFromTableSummaryAsync(
        ProviderFetchRequest request,
        int year,
        IReadOnlyCollection<string> variables,
        CancellationToken cancellationToken)
    {
        var response = await CensusAcsTableSummarySupport.FetchAsync(
            http,
            options.Value,
            year,
            "B01001",
            variables.Where(variable => variable != "NAME").ToArray(),
            request,
            cancellationToken);
        var rows = new List<OriginAgeBinImportRow>(
            response.ValuesByZcta.Count * CensusAcsVariables.AgePairs.Count);
        foreach (var (zcta, values) in response.ValuesByZcta.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var pair in CensusAcsVariables.AgePairs)
            {
                var population = checked(
                    CensusAcsTableSummarySupport.ReadNonnegativeLong(values, pair.MaleVariable, zcta) +
                    CensusAcsTableSummarySupport.ReadNonnegativeLong(values, pair.FemaleVariable, zcta));
                rows.Add(new OriginAgeBinImportRow(
                    $"USA-ZCTA-{zcta}",
                    year,
                    pair.MinimumAge,
                    pair.MaximumAge,
                    population,
                    DatasetValidationStates.Validated));
            }
        }
        return new ProviderDataset<OriginAgeBinImportRow>(
            CensusAcsTableSummarySupport.Source(response, "ZCTA sex-by-age population"),
            DatasetSnapshotKinds.AgePopulation,
            year.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            CensusAcsProviderSupport.DatasetChecksum(response.ContentHash, request.Options),
            "census-acs5-b01001-table-summary-zcta-v1",
            rows,
            ["Census API key was not configured; ingestion used the official table-based ACS Summary File."]);
    }
}

public sealed class CensusAcsMedianIncomeProvider(
    HttpClient http,
    IOptions<CensusAcsProviderOptions> options) : IOriginIncomeProvider
{
    public string ProviderKey => "census-acs5-zcta-median-household-income";

    public async Task<ProviderDataset<OriginIncomeImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var year = CensusAcsProviderSupport.RequireSingleYear(request);
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return await FetchFromTableSummaryAsync(request, year, cancellationToken);
        }
        var response = await CensusAcsProviderSupport.FetchAsync(
            http,
            options.Value,
            year,
            ["NAME", CensusAcsVariables.MedianHouseholdIncome],
            cancellationToken);
        var headers = CensusAcsProviderSupport.HeaderIndex(response);
        var selectedSourceRows = CensusAcsProviderSupport.SelectRequestedZctas(response, headers, request);
        var unavailableZctas = new List<string>();
        var rows = new List<OriginIncomeImportRow>(selectedSourceRows.Count);
        foreach (var sourceRow in selectedSourceRows)
        {
            var zcta = CensusAcsProviderSupport.ReadRequired(sourceRow, headers, CensusAcsVariables.ZctaHeader);
            var income = CensusAcsProviderSupport.ReadNullableNonnegativeLong(
                sourceRow,
                headers,
                CensusAcsVariables.MedianHouseholdIncome,
                zcta);
            if (income is null)
            {
                unavailableZctas.Add(zcta);
                continue;
            }
            rows.Add(new OriginIncomeImportRow(
                $"USA-ZCTA-{zcta}",
                year,
                null,
                null,
                null,
                income,
                year,
                "ACS B19013 estimate; this is median household income, not IRS adjusted gross income."));
        }

        return new ProviderDataset<OriginIncomeImportRow>(
            CensusAcsProviderSupport.Source(response, "ZCTA median household income"),
            DatasetSnapshotKinds.Income,
            year.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            CensusAcsProviderSupport.DatasetChecksum(response.ContentHash, request.Options),
            "census-acs5-b19013-zcta-v1",
            rows,
            UnavailableIncomeWarnings(unavailableZctas));
    }

    private async Task<ProviderDataset<OriginIncomeImportRow>> FetchFromTableSummaryAsync(
        ProviderFetchRequest request,
        int year,
        CancellationToken cancellationToken)
    {
        var response = await CensusAcsTableSummarySupport.FetchAsync(
            http,
            options.Value,
            year,
            "B19013",
            [CensusAcsVariables.MedianHouseholdIncome],
            request,
            cancellationToken);
        var unavailableZctas = new List<string>();
        var rows = new List<OriginIncomeImportRow>(response.ValuesByZcta.Count);
        foreach (var pair in response.ValuesByZcta.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var income = CensusAcsTableSummarySupport.ReadNullableNonnegativeLong(
                pair.Value,
                CensusAcsVariables.MedianHouseholdIncome,
                pair.Key);
            if (income is null)
            {
                unavailableZctas.Add(pair.Key);
                continue;
            }
            rows.Add(new OriginIncomeImportRow(
                $"USA-ZCTA-{pair.Key}",
                year,
                null,
                null,
                null,
                income,
                year,
                "ACS B19013 estimate from the table-based Summary File; this is median household income, not IRS adjusted gross income."));
        }
        var warnings = new List<string>
        {
            "Census API key was not configured; ingestion used the official table-based ACS Summary File."
        };
        warnings.AddRange(UnavailableIncomeWarnings(unavailableZctas));
        return new ProviderDataset<OriginIncomeImportRow>(
            CensusAcsTableSummarySupport.Source(response, "ZCTA median household income"),
            DatasetSnapshotKinds.Income,
            year.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            CensusAcsProviderSupport.DatasetChecksum(response.ContentHash, request.Options),
            "census-acs5-b19013-table-summary-zcta-v1",
            rows,
            warnings);
    }

    private static IReadOnlyCollection<string> UnavailableIncomeWarnings(IReadOnlyCollection<string> zctas) =>
        zctas.Count == 0
            ? []
            :
            [
                $"ACS B19013 publishes no usable median-household-income estimate for {zctas.Count} selected ZCTA(s): " +
                $"{string.Join(", ", zctas.Order(StringComparer.Ordinal))}. Those origins are omitted from this income snapshot; " +
                "no zero or imputed value was substituted."
            ];
}
