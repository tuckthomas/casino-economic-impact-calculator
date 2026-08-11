using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IrsSoiProviderOptions
{
    public const string ConfigurationSection = "IrsSoi";

    public string WorkbookBaseUrl { get; set; } = "https://www.irs.gov/pub/irs-soi";
    public string PublicationUrl { get; set; } =
        "https://www.irs.gov/statistics/soi-tax-stats-individual-income-tax-statistics-2022-zip-code-data-soi";
    public string CensusZctaGazetteerUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2022_Gazetteer/2022_Gaz_zcta_national.zip";
}

/// <summary>
/// Imports IRS SOI USPS ZIP aggregates only when the reported five-digit ZIP has an exact
/// code match in the Census ZCTA gazetteer. The adapter deliberately does not assert that
/// USPS ZIP Codes and Census ZCTAs are the same geography.
/// </summary>
public sealed class IrsSoiExactCodeZctaIncomeProvider(
    HttpClient http,
    IOptions<IrsSoiProviderOptions> options) : IOriginIncomeProvider
{
    private const int SupportedTaxYear = 2022;
    private const string TransformVersion = "irs-soi-zip-to-census-zcta-exact-code-2022-v3";

    public string ProviderKey => "irs-soi-zcta-income-exact-code";

    public async Task<ProviderDataset<OriginIncomeImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var states = RequireSupportedRequest(request);
        var providerOptions = options.Value;
        var gazetteerUri = new Uri(providerOptions.CensusZctaGazetteerUrl);
        var retrievedAt = DateTime.UtcNow;

        var workbookTasks = states.Select(async state =>
        {
            var uri = new Uri(
                $"{providerOptions.WorkbookBaseUrl.TrimEnd('/')}/22zp{state.Ordinal:00}{state.Abbreviation.ToLowerInvariant()}.xlsx");
            return new IrsWorkbook(state, uri, await ReadBytesAsync(uri, cancellationToken));
        }).ToArray();
        var gazetteerTask = ReadBytesAsync(gazetteerUri, cancellationToken);
        var workbooks = await Task.WhenAll(workbookTasks);
        var gazetteerBytes = await gazetteerTask;

        var zctas = ReadZctaCodes(gazetteerBytes);
        var requestedCodes = ZctaCodeFilter.Optional(request.Options);
        var sourceRows = workbooks.SelectMany(workbook => ReadStateTotals(workbook.Bytes)
            .Select(row => new IrsStateZipTotal(workbook.State, row))).ToArray();
        var marketRows = requestedCodes is null
            ? sourceRows
            : sourceRows.Where(row => requestedCodes.Contains(row.ZipCode)).ToArray();
        var matched = marketRows.Where(row => zctas.Contains(row.ZipCode)).ToArray();
        var unmatched = marketRows.Where(row => !zctas.Contains(row.ZipCode)).ToArray();
        var missingRequestedCodes = requestedCodes?
            .Except(matched.Select(row => row.ZipCode), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
        var duplicateZip = matched.GroupBy(row => row.ZipCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateZip is not null)
        {
            throw new InvalidDataException(
                $"The selected IRS SOI state workbooks contain more than one ZCTA-matched total row for ZIP '{duplicateZip.Key}'. " +
                "Resolve the state-workbook overlap explicitly rather than double-counting the ZIP.");
        }
        if (matched.Length == 0)
        {
            throw new InvalidDataException(
                $"No IRS SOI ZIP total rows for {string.Join(", ", states.Select(state => state.Name))} " +
                "exactly matched the 2022 Census ZCTA gazetteer.");
        }

        var reconciliationNote =
            "IRS SOI reports USPS ZIP Codes; Census ZCTAs are statistical geographies. " +
            "This row was retained only because its five-digit IRS ZIP code exactly matched a 2022 Census ZCTA code. " +
            "Exact code equality is a conservative concordance rule, not a claim that ZIP and ZCTA geography are identical.";
        var rows = matched.Select(row => new OriginIncomeImportRow(
            $"USA-ZCTA-{row.ZipCode}",
            SupportedTaxYear,
            row.ReturnCount,
            row.AdjustedGrossIncomeThousands * 1_000m,
            null,
            null,
            SupportedTaxYear,
            reconciliationNote)).ToArray();

        var matchedReturns = matched.Sum(row => row.ReturnCount);
        var unmatchedReturns = unmatched.Sum(row => row.ReturnCount);
        var matchedAgi = matched.Sum(row => row.AdjustedGrossIncomeThousands) * 1_000m;
        var unmatchedAgi = unmatched.Sum(row => row.AdjustedGrossIncomeThousands) * 1_000m;
        var warnings = states.Select(state =>
        {
            var stateRows = marketRows.Where(row => row.State == state).ToArray();
            var stateMatched = stateRows.Count(row => zctas.Contains(row.ZipCode));
            return requestedCodes is null
                ? $"{state.Name}: retained {stateMatched:N0} of {stateRows.Length:N0} IRS ZIP total rows by exact-code ZCTA concordance; " +
                  $"{stateRows.Length - stateMatched:N0} unmatched USPS ZIP rows were excluded."
                : $"{state.Name}: retained {stateMatched:N0} IRS ZIP total rows in the explicit ZCTA market universe by exact-code concordance; " +
                  $"{stateRows.Length - stateMatched:N0} requested-code rows that do not exist as Census ZCTAs were excluded.";
        }).Append(
            $"Exact-code ZIP-to-ZCTA reconciliation retained {matched.Length:N0} of {marketRows.Length:N0} in-scope IRS ZIP total rows " +
            $"for {string.Join(", ", states.Select(state => state.Name))}; {unmatched.Length:N0} unmatched USPS ZIP rows were excluded. " +
            "ZIP and ZCTA are not treated as identical geographies.")
        .Append(
            FormattableString.Invariant(
                $"Retained rows represent {matchedReturns:N0} returns and ${matchedAgi:N0} of AGI; excluded rows represent {unmatchedReturns:N0} returns and ${unmatchedAgi:N0} of AGI. Review these excluded totals before using the snapshot for calibration."))
        .Concat(requestedCodes is null
            ? []
            :
            [
                $"The explicit market universe requested {requestedCodes.Count:N0} ZCTAs; {matched.Length:N0} had exact-code IRS ZIP total rows and " +
                $"{missingRequestedCodes.Length:N0} had no retained IRS total: {string.Join(", ", missingRequestedCodes)}. " +
                "No missing AGI was replaced with zero or an imputed value."
            ])
        .ToArray();
        var sourceHash = ContentHash(workbooks, gazetteerBytes);
        var marketDescriptor = requestedCodes is null
            ? "all-state-workbook-zips"
            : $"zcta-codes={string.Join(',', requestedCodes.Order(StringComparer.Ordinal))}";
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{sourceHash}\n{TransformVersion}\n{marketDescriptor}")))
            .ToLowerInvariant();
        var stateLabel = string.Join(", ", states.Select(state => state.Name));
        var workbookList = string.Join(", ", workbooks.OrderBy(workbook => workbook.State.Abbreviation).Select(workbook => workbook.Uri));
        var sourceUrl = workbooks.Length == 1 ? workbooks[0].Uri.ToString() : providerOptions.PublicationUrl;

        return new ProviderDataset<OriginIncomeImportRow>(
            new RegisterDataSourceRequest(
                $"IRS SOI {SupportedTaxYear} ZIP income reconciled to Census 2022 ZCTA codes ({stateLabel})",
                "Internal Revenue Service and United States Census Bureau",
                sourceUrl,
                workbooks.Length == 1
                    ? "federal-xlsx-plus-census-exact-code-concordance"
                    : "federal-xlsx-series-plus-census-exact-code-concordance",
                workbooks.Length == 1
                    ? request.GeographicCoverage.ToUpperInvariant()
                    : $"US-STATES:{string.Join(',', states.Select(state => state.Abbreviation))}",
                SupportedTaxYear.ToString(CultureInfo.InvariantCulture),
                retrievedAt,
                sourceHash,
                true,
                "IRS and Census terms and conditions apply.",
                $"IRS publication: {providerOptions.PublicationUrl}; source workbooks: {workbookList}; Census ZCTA gazetteer: {gazetteerUri}. " +
                "Unmatched USPS ZIPs are excluded, never silently converted to ZCTAs."),
            DatasetSnapshotKinds.Income,
            SupportedTaxYear.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            TransformVersion,
            rows,
            warnings);
    }

    private async Task<byte[]> ReadBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static IReadOnlyList<IrsState> RequireSupportedRequest(ProviderFetchRequest request)
    {
        if (request.PeriodStart != new DateOnly(SupportedTaxYear, 1, 1) ||
            request.PeriodEnd != new DateOnly(SupportedTaxYear, 12, 31))
        {
            throw new NotSupportedException(
                $"This versioned IRS SOI adapter supports the complete {SupportedTaxYear} tax year only.");
        }
        var coverage = request.GeographicCoverage.Trim().ToUpperInvariant();
        if (string.Equals(coverage, "US-STATES", StringComparison.Ordinal))
        {
            if (request.Options is null || !request.Options.TryGetValue("state-codes", out var raw))
            {
                throw new ArgumentException(
                    "GeographicCoverage 'US-STATES' requires a comma-separated 'state-codes' option.");
            }
            var abbreviations = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (abbreviations.Length == 0)
            {
                throw new ArgumentException("At least one 'state-codes' value is required.");
            }
            var selected = abbreviations.Select(abbreviation => States.SingleOrDefault(state => state.Abbreviation == abbreviation)
                ?? throw new NotSupportedException($"IRS SOI state code '{abbreviation}' is not supported.")).ToArray();
            return selected;
        }
        if (!coverage.StartsWith("US-", StringComparison.Ordinal) || coverage.Length != 5)
        {
            throw new NotSupportedException(
                "IRS SOI state workbooks require GeographicCoverage 'US-XX', or 'US-STATES' with state-codes.");
        }
        var abbreviation = coverage[3..];
        return
        [
            States.SingleOrDefault(state => state.Abbreviation == abbreviation)
                ?? throw new NotSupportedException($"IRS SOI state workbook coverage '{coverage}' is not supported.")
        ];
    }

    private static IReadOnlyList<IrsZipTotal> ReadStateTotals(byte[] workbookBytes)
    {
        var totals = new List<IrsZipTotal>();
        foreach (var row in OpenXmlWorksheetReader.ReadRows(workbookBytes, "Sheet1"))
        {
            if (!string.IsNullOrWhiteSpace(Cell(row, "B")) ||
                !TryZip(Cell(row, "A"), out var zipCode) ||
                zipCode == "00000")
            {
                continue;
            }
            if (!long.TryParse(Cell(row, "C"), NumberStyles.Number, CultureInfo.InvariantCulture, out var returnCount) ||
                returnCount < 0)
            {
                throw new InvalidDataException($"IRS SOI return count for ZIP '{zipCode}' is missing or invalid.");
            }
            if (!decimal.TryParse(
                    Cell(row, "S"),
                    NumberStyles.Number | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture,
                    out var agiThousands) || agiThousands < 0)
            {
                throw new InvalidDataException($"IRS SOI adjusted gross income for ZIP '{zipCode}' is missing or invalid.");
            }
            totals.Add(new IrsZipTotal(zipCode, returnCount, agiThousands));
        }
        if (totals.Count == 0)
        {
            throw new InvalidDataException("The IRS SOI workbook contains no readable ZIP total rows.");
        }
        return totals;
    }

    private static HashSet<string> ReadZctaCodes(byte[] gazetteerBytes)
    {
        try
        {
            using var stream = new MemoryStream(gazetteerBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.Entries.SingleOrDefault(item =>
                item.FullName.EndsWith("_Gaz_zcta_national.txt", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The Census archive has no national ZCTA gazetteer text file.");
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var result = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var firstTab = line.IndexOf('\t');
                var code = (firstTab < 0 ? line : line[..firstTab]).Trim().TrimStart('\uFEFF');
                if (code.Length == 5 && code.All(char.IsAsciiDigit))
                {
                    result.Add(code);
                }
            }
            if (result.Count == 0)
            {
                throw new InvalidDataException("The Census ZCTA gazetteer contains no readable five-digit codes.");
            }
            return result;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The Census ZCTA gazetteer archive is unreadable.", exception);
        }
    }

    private static bool TryZip(string value, out string zipCode)
    {
        zipCode = string.Empty;
        var normalized = value.Trim();
        if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) ||
            numeric is < 0 or > 99_999)
        {
            return false;
        }
        zipCode = numeric.ToString("D5", CultureInfo.InvariantCulture);
        return true;
    }

    private static string Cell(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : string.Empty;

    private static string ContentHash(
        IReadOnlyCollection<IrsWorkbook> workbooks,
        byte[] gazetteerBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var workbook in workbooks.OrderBy(workbook => workbook.State.Abbreviation, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(workbook.State.Abbreviation));
            hash.AppendData(workbook.Bytes);
        }
        hash.AppendData(gazetteerBytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record IrsZipTotal(string ZipCode, long ReturnCount, decimal AdjustedGrossIncomeThousands);
    private sealed record IrsStateZipTotal(IrsState State, IrsZipTotal Total)
    {
        public string ZipCode => Total.ZipCode;
        public long ReturnCount => Total.ReturnCount;
        public decimal AdjustedGrossIncomeThousands => Total.AdjustedGrossIncomeThousands;
    }
    private sealed record IrsWorkbook(IrsState State, Uri Uri, byte[] Bytes);
    private sealed record IrsState(string Abbreviation, string Name, int Ordinal);

    private static readonly IrsState[] States =
    [
        new("AL", "Alabama", 1), new("AK", "Alaska", 2), new("AZ", "Arizona", 3),
        new("AR", "Arkansas", 4), new("CA", "California", 5), new("CO", "Colorado", 6),
        new("CT", "Connecticut", 7), new("DE", "Delaware", 8), new("DC", "District of Columbia", 9),
        new("FL", "Florida", 10), new("GA", "Georgia", 11), new("HI", "Hawaii", 12),
        new("ID", "Idaho", 13), new("IL", "Illinois", 14), new("IN", "Indiana", 15),
        new("IA", "Iowa", 16), new("KS", "Kansas", 17), new("KY", "Kentucky", 18),
        new("LA", "Louisiana", 19), new("ME", "Maine", 20), new("MD", "Maryland", 21),
        new("MA", "Massachusetts", 22), new("MI", "Michigan", 23), new("MN", "Minnesota", 24),
        new("MS", "Mississippi", 25), new("MO", "Missouri", 26), new("MT", "Montana", 27),
        new("NE", "Nebraska", 28), new("NV", "Nevada", 29), new("NH", "New Hampshire", 30),
        new("NJ", "New Jersey", 31), new("NM", "New Mexico", 32), new("NY", "New York", 33),
        new("NC", "North Carolina", 34), new("ND", "North Dakota", 35), new("OH", "Ohio", 36),
        new("OK", "Oklahoma", 37), new("OR", "Oregon", 38), new("PA", "Pennsylvania", 39),
        new("RI", "Rhode Island", 40), new("SC", "South Carolina", 41), new("SD", "South Dakota", 42),
        new("TN", "Tennessee", 43), new("TX", "Texas", 44), new("UT", "Utah", 45),
        new("VT", "Vermont", 46), new("VA", "Virginia", 47), new("WA", "Washington", 48),
        new("WV", "West Virginia", 49), new("WI", "Wisconsin", 50), new("WY", "Wyoming", 51)
    ];
}
