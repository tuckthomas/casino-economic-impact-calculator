// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Providers;

public sealed class CensusCountyBusinessPatternsProviderOptions
{
    public const string ConfigurationSection = "CensusCountyBusinessPatterns";

    public string DatasetBaseUrl { get; set; } =
        "https://www2.census.gov/programs-surveys/cbp/datasets";
    public string PublicationUrl { get; set; } =
        "https://www.census.gov/data/datasets/2023/econ/cbp/2023-cbp.html";
}

/// <summary>
/// Imports official Census County Business Patterns state or county CSV data without
/// requiring a Census API key. The adapter intentionally preserves payroll and employment
/// as inventory measures; CBP does not publish receipts in these annual files.
/// </summary>
public sealed class CensusCountyBusinessPatternsProvider(
    HttpClient http,
    IOptions<CensusCountyBusinessPatternsProviderOptions> options) : ILocalEconomicInventoryProvider
{
    private const int SupportedYear = 2023;
    private const string TransformVersion = "census-cbp-2023-local-economic-inventory-v1";
    private const string Coverage = "US-CBP";

    private static readonly IReadOnlyList<SectorDefinition> SectorDefinitions =
    [
        new(DisplacementSectorKeys.RestaurantHospitality, ["72----"], ["72"], true),
        new(DisplacementSectorKeys.Retail, ["44----"], ["44-45"], true),
        new(DisplacementSectorKeys.ArtsEntertainmentRecreation, ["71----"], ["71"], true),
        new(LocalEconomicSectorKeys.AllIndustries, ["------"], ["00"], false),
        new(LocalEconomicSectorKeys.CasinoGambling, ["713290", "721120"], ["713290", "721120"], false)
    ];

    public string ProviderKey => "census-county-business-patterns";

    public async Task<ProviderDataset<LocalEconomicSectorObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selection = RequireSelection(request);
        var providerOptions = options.Value;
        var suffix = selection.SourceGeography == "state" ? "st" : "co";
        var datasetUri = new Uri(
            $"{providerOptions.DatasetBaseUrl.TrimEnd('/')}/{SupportedYear}/cbp{SupportedYear % 100:00}{suffix}.zip");
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(datasetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var archiveBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var sourceHash = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        var sourceRows = ReadSelectedRows(archiveBytes, selection);
        var warnings = new List<string>
        {
            "County Business Patterns covers establishments with paid employees and reports employment during the week of March 12 plus annual payroll; it does not include nonemployers.",
            "The annual CBP state/county file does not publish receipts or sales. This snapshot therefore supports employment-, payroll-, and establishment-based local inventory weighting but does not invent a sales measure."
        };

        var rows = new List<LocalEconomicSectorObservationImportRow>();
        foreach (var definition in SectorDefinitions)
        {
            var matches = sourceRows.Where(row => definition.SourceCodes.Contains(row.NaicsCode)).ToArray();
            if (matches.Length == 0)
            {
                if (definition.Required)
                {
                    throw new InvalidDataException(
                        $"The {SupportedYear} CBP {selection.SourceGeography} file has no '{definition.SectorKey}' " +
                        $"industry row for source FIPS '{selection.SourceFips}'.");
                }
                warnings.Add(
                    $"Optional CBP sector '{definition.SectorKey}' was unavailable for {selection.SourceGeography} FIPS " +
                    $"'{selection.SourceFips}' and was not synthesized.");
                continue;
            }

            var valid = matches.Where(row => row.Employment is not null || row.AnnualPayrollThousands is not null || row.Establishments is not null).ToArray();
            if (valid.Length == 0)
            {
                if (definition.Required)
                {
                    throw new InvalidDataException(
                        $"The {SupportedYear} CBP '{definition.SectorKey}' row for source FIPS " +
                        $"'{selection.SourceFips}' contains no usable inventory measure.");
                }
                warnings.Add(
                    $"Optional CBP sector '{definition.SectorKey}' contained no usable published measure for " +
                    $"{selection.SourceGeography} FIPS '{selection.SourceFips}' and was omitted.");
                continue;
            }

            var noiseFlags = valid
                .SelectMany(row => new[] { row.EmploymentNoiseFlag, row.PayrollNoiseFlag })
                .Where(flag => !string.IsNullOrWhiteSpace(flag))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            rows.Add(new LocalEconomicSectorObservationImportRow(
                $"USA-CBP-{SupportedYear}-{selection.SourceGeography}-{selection.SourceFips}-{definition.SectorKey}",
                selection.ImpactScopeKind,
                selection.ImpactScopeCode,
                definition.SectorKey,
                definition.OutputNaicsCodes,
                new DateOnly(SupportedYear, 1, 1),
                new DateOnly(SupportedYear, 12, 31),
                SumNullable(valid.Select(row => row.Establishments)),
                SumNullable(valid.Select(row => row.Employment)),
                SumNullable(valid.Select(row => row.AnnualPayrollThousands)) is { } payrollThousands
                    ? payrollThousands * 1_000m
                    : null,
                null,
                "U.S. Census Bureau County Business Patterns: paid-employee establishments, employment during the week of March 12, and annual payroll in current dollars.",
                $"Source geography: {selection.SourceGeography} FIPS {selection.SourceFips}. " +
                $"Source NAICS rows: {string.Join(", ", valid.Select(row => row.NaicsCode))}. " +
                $"Published employment/payroll noise flags: {(noiseFlags.Length == 0 ? "none" : string.Join(", ", noiseFlags))}."));
        }

        var checksumMaterial = string.Join('\n',
            sourceHash,
            TransformVersion,
            selection.SourceGeography,
            selection.SourceFips,
            selection.ImpactScopeKind,
            selection.ImpactScopeCode);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checksumMaterial)))
            .ToLowerInvariant();
        var sourceName = selection.SourceGeography == "state" ? "State File" : "County File";
        return new ProviderDataset<LocalEconomicSectorObservationImportRow>(
            new RegisterDataSourceRequest(
                $"Census County Business Patterns {SupportedYear} {sourceName}",
                "United States Census Bureau",
                datasetUri.ToString(),
                "federal-csv",
                selection.SourceGeography == "state" ? "United States states" : "United States counties",
                SupportedYear.ToString(CultureInfo.InvariantCulture),
                retrievedAt,
                sourceHash,
                true,
                "Census Bureau data-use and disclosure-avoidance terms apply.",
                $"Publication page: {providerOptions.PublicationUrl}. CBP annual payroll is published in thousands of dollars and is converted to dollars by this versioned transform."),
            DatasetSnapshotKinds.LocalEconomicInventory,
            SupportedYear.ToString(CultureInfo.InvariantCulture),
            new DateOnly(SupportedYear, 1, 1),
            new DateOnly(SupportedYear, 12, 31),
            checksum,
            TransformVersion,
            rows,
            warnings);
    }

    private static Selection RequireSelection(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, Coverage, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"GeographicCoverage must be '{Coverage}'.");
        }
        if (request.PeriodStart.Year != SupportedYear || request.PeriodEnd.Year != SupportedYear)
        {
            throw new NotSupportedException(
                $"This versioned adapter supports the official {SupportedYear} CBP file only.");
        }

        var sourceGeography = RequireOption(request, "source-geography").ToLowerInvariant();
        if (sourceGeography is not ("state" or "county"))
        {
            throw new ArgumentException("Option 'source-geography' must be 'state' or 'county'.", nameof(request));
        }
        var sourceFips = RequireOption(request, "source-fips");
        var expectedLength = sourceGeography == "state" ? 2 : 5;
        if (sourceFips.Length != expectedLength || sourceFips.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                $"Option 'source-fips' must contain exactly {expectedLength} digits for {sourceGeography} data.",
                nameof(request));
        }
        var impactScopeKind = RequireOption(request, "impact-scope-kind").ToLowerInvariant();
        var requiredScopeKind = sourceGeography == "state"
            ? ImpactScopeKinds.HostState
            : ImpactScopeKinds.HostCounty;
        if (!string.Equals(impactScopeKind, requiredScopeKind, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"CBP {sourceGeography} data must use impact scope '{requiredScopeKind}'; " +
                $"it cannot be relabeled as '{impactScopeKind}'.",
                nameof(request));
        }
        return new Selection(
            sourceGeography,
            sourceFips,
            impactScopeKind,
            RequireOption(request, "impact-scope-code").ToUpperInvariant());
    }

    private static string RequireOption(ProviderFetchRequest request, string key)
    {
        if (request.Options is null || !request.Options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Provider option '{key}' is required.", nameof(request));
        }
        return value.Trim();
    }

    private static IReadOnlyList<SourceRow> ReadSelectedRows(byte[] archiveBytes, Selection selection)
    {
        using var archiveStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.SingleOrDefault(item => item.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("The CBP archive does not contain its expected CSV text file.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("The CBP file is empty.");
        var headers = ParseCsvLine(headerLine)
            .Select((header, index) => (Header: header.ToLowerInvariant(), Index: index))
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.Ordinal);
        foreach (var required in new[] { "fipstate", "naics", "emp", "emp_nf", "ap", "ap_nf", "est" })
        {
            if (!headers.ContainsKey(required))
            {
                throw new InvalidDataException($"The CBP file is missing required column '{required}'.");
            }
        }
        if (selection.SourceGeography == "county" && !headers.ContainsKey("fipscty") ||
            selection.SourceGeography == "state" && !headers.ContainsKey("lfo"))
        {
            throw new InvalidDataException($"The CBP {selection.SourceGeography} file has an unexpected layout.");
        }

        var selectedNaics = SectorDefinitions.SelectMany(definition => definition.SourceCodes).ToHashSet(StringComparer.Ordinal);
        var rows = new List<SourceRow>();
        while (reader.ReadLine() is { } line)
        {
            var cells = ParseCsvLine(line);
            if (cells.Count != headers.Count || cells[headers["fipstate"]] != selection.SourceFips[..2])
            {
                continue;
            }
            if (selection.SourceGeography == "county" && cells[headers["fipscty"]] != selection.SourceFips[2..] ||
                selection.SourceGeography == "state" && cells[headers["lfo"]] != "-")
            {
                continue;
            }
            var naics = cells[headers["naics"]];
            if (!selectedNaics.Contains(naics))
            {
                continue;
            }
            rows.Add(new SourceRow(
                naics,
                ParseNullableLong(cells[headers["est"]], "est", selection, naics),
                ParseNullableLong(cells[headers["emp"]], "emp", selection, naics),
                ParseNullableDecimal(cells[headers["ap"]], "ap", selection, naics),
                cells[headers["emp_nf"]],
                cells[headers["ap_nf"]]));
        }
        return rows;
    }

    private static long? ParseNullableLong(string raw, string field, Selection selection, string naics)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "N")
        {
            return null;
        }
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException(
                $"CBP field '{field}' is invalid for {selection.SourceFips}/{naics}: '{raw}'.");
        }
        return value;
    }

    private static decimal? ParseNullableDecimal(string raw, string field, Selection selection, string naics)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "N")
        {
            return null;
        }
        if (!decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException(
                $"CBP field '{field}' is invalid for {selection.SourceFips}/{naics}: '{raw}'.");
        }
        return value;
    }

    private static long? SumNullable(IEnumerable<long?> values)
    {
        var materialized = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Sum();
    }

    private static decimal? SumNullable(IEnumerable<decimal?> values)
    {
        var materialized = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Sum();
    }

    internal static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        if (quoted)
        {
            throw new InvalidDataException("The CBP CSV row contains an unterminated quoted field.");
        }
        cells.Add(current.ToString());
        return cells;
    }

    private sealed record Selection(
        string SourceGeography,
        string SourceFips,
        string ImpactScopeKind,
        string ImpactScopeCode);

    private sealed record SourceRow(
        string NaicsCode,
        long? Establishments,
        long? Employment,
        decimal? AnnualPayrollThousands,
        string EmploymentNoiseFlag,
        string PayrollNoiseFlag);

    private sealed record SectorDefinition(
        string SectorKey,
        IReadOnlyCollection<string> SourceCodes,
        IReadOnlyCollection<string> OutputNaicsCodes,
        bool Required);
}
