// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class MichiganGamingControlBoardRevenueProvider(
    HttpClient http,
    IOptions<MichiganGamingFacilityProviderOptions> options) : IGamingRegulatorPerformanceProvider
{
    public const string DetroitAdjustedRevenueMetricKey = "michigan-detroit-adjusted-revenue";

    public string ProviderKey => "michigan-gaming-control-board-detroit-casino-revenue";
    public string GeographicCoverage => "US-MI";

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selection = RequireSupportedPeriod(request);
        var configured = options.Value;
        using var indexResponse = await http.GetAsync(
            configured.RevenueDownloadsPageUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync(cancellationToken);
        var workbookUrl = ResolveWorkbookUrl(indexHtml, configured.RevenueDownloadsPageUrl, selection.Year);

        using var workbookResponse = await http.GetAsync(
            workbookUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        workbookResponse.EnsureSuccessStatusCode();
        var workbookBytes = await workbookResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var workbookRows = MichiganDetroitRevenueWorkbookParser.Parse(workbookBytes, selection.Year);
        var selectedRows = workbookRows
            .Where(row => selection.Month is null || row.Month == selection.Month)
            .ToArray();
        if (selectedRows.Length != (selection.Month is null ? 12 : 1))
        {
            throw new InvalidDataException(
                $"The MGCB workbook did not contain the expected complete {selection.PeriodLabel} revenue series.");
        }

        var imports = selectedRows.SelectMany(row => RevenueRows(row)).ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant();
        return new ProviderDataset<CasinoGamingRevenueImportRow>(
            new RegisterDataSourceRequest(
                $"Michigan Gaming Control Board Detroit casino adjusted revenue {selection.PeriodLabel}",
                "Michigan Gaming Control Board",
                workbookUrl,
                "state-regulator-xls",
                "Michigan's three Detroit commercial casinos",
                selection.PeriodLabel,
                DateTime.UtcNow,
                checksum,
                true,
                "Michigan public-record terms apply.",
                $"Workbook link discovered from the MGCB revenue-download index at {configured.RevenueDownloadsPageUrl}. " +
                "The model comparable metric preserves regulator-reported land-based adjusted revenue for slots and table games without numeric modification; retail sports betting is excluded."),
            DatasetSnapshotKinds.ObservedPerformance,
            selection.PeriodLabel,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            "mgcb-detroit-adjusted-revenue-xls-v1",
            imports,
            [
                "Michigan tribal casino revenue is not included because the reviewed MGCB public workbook covers only the three Detroit commercial casinos.",
                "Retail sports-betting QAGR is excluded from the land-based comparable gaming-revenue metric and must not be silently added."
            ]);
    }

    internal static string ResolveWorkbookUrl(string html, string indexUrl, int year)
    {
        var match = Regex.Match(
            html,
            $"href=[\"'](?<url>[^\"']*Detroit_Casino_Revenue-{year}-XLS\\.xls[^\"']*)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException($"The MGCB revenue index does not expose the {year} Detroit casino Excel workbook.");
        }
        var decoded = WebUtility.HtmlDecode(match.Groups["url"].Value);
        if (!Uri.TryCreate(new Uri(indexUrl), decoded, out var resolved) ||
            resolved.Scheme is not ("https" or "http"))
        {
            throw new InvalidDataException($"The MGCB {year} workbook link is invalid: '{decoded}'.");
        }
        return resolved.AbsoluteUri;
    }

    private static IEnumerable<CasinoGamingRevenueImportRow> RevenueRows(MichiganDetroitMonthlyRevenue row)
    {
        foreach (var facility in new[]
                 {
                     ("USA-MI-MGCB-mgm-grand-detroit", "MGM Grand Detroit", row.MgmGrandDetroit),
                     ("USA-MI-MGCB-motorcity-casino", "MotorCity Casino", row.MotorCityCasino),
                     ("USA-MI-MGCB-hollywood-casino-at-greektown", "Hollywood Casino at Greektown", row.HollywoodGreektown)
                 })
        {
            var start = new DateOnly(row.Year, row.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var definition = "Michigan Gaming Control Board Total Adjusted Revenue for Detroit casino slots and table games; retail sports betting is excluded.";
            yield return new CasinoGamingRevenueImportRow(
                facility.Item1,
                start,
                end,
                "monthly",
                DetroitAdjustedRevenueMetricKey,
                definition,
                facility.Item3,
                null,
                null,
                [],
                $"MGCB workbook facility column '{facility.Item2}'. Monthly all-casino total reconciled to {row.AllDetroitCasinos.ToString(CultureInfo.InvariantCulture)}.");
            yield return new CasinoGamingRevenueImportRow(
                facility.Item1,
                start,
                end,
                "monthly",
                GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
                "Cross-jurisdiction land-based comparable metric sourced from MGCB Total Adjusted Revenue for slots and table games.",
                facility.Item3,
                null,
                null,
                [],
                $"Comparable-series transform uses MGCB Total Adjusted Revenue without numeric adjustment and excludes retail sports betting. Regulator-specific metric: {DetroitAdjustedRevenueMetricKey}.");
        }
    }

    private static MichiganRevenuePeriodSelection RequireSupportedPeriod(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-MI", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The Michigan revenue adapter requires GeographicCoverage 'US-MI'.");
        }
        if (request.PeriodStart.Year < 2019 || request.PeriodEnd.Year != request.PeriodStart.Year)
        {
            throw new NotSupportedException("The Michigan revenue adapter currently supports one calendar month or year from 2019 onward.");
        }
        var year = request.PeriodStart.Year;
        if (request.PeriodStart == new DateOnly(year, 1, 1) &&
            request.PeriodEnd == new DateOnly(year, 12, 31))
        {
            return new MichiganRevenuePeriodSelection(year, null, year.ToString(CultureInfo.InvariantCulture));
        }
        if (request.PeriodStart.Day == 1 &&
            request.PeriodEnd == request.PeriodStart.AddMonths(1).AddDays(-1))
        {
            return new MichiganRevenuePeriodSelection(
                year,
                request.PeriodStart.Month,
                request.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
        throw new ArgumentException(
            "A Michigan revenue request must span exactly one complete calendar month or one complete calendar year.",
            nameof(request));
    }

    private sealed record MichiganRevenuePeriodSelection(int Year, int? Month, string PeriodLabel);
}

internal sealed record MichiganDetroitMonthlyRevenue(
    int Year,
    int Month,
    decimal MgmGrandDetroit,
    decimal MotorCityCasino,
    decimal HollywoodGreektown,
    decimal AllDetroitCasinos);

internal static class MichiganDetroitRevenueWorkbookParser
{
    internal static IReadOnlyList<MichiganDetroitMonthlyRevenue> Parse(byte[] workbookBytes, int year)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        if (workbookBytes.Length == 0)
        {
            throw new InvalidDataException("The MGCB revenue workbook is empty.");
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(workbookBytes, writable: false);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            if (!string.Equals(reader.Name, $"Combined - {year}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var values = new object?[reader.FieldCount];
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    values[column] = reader.GetValue(column);
                }
                rows.Add(values);
            }
            return ParseCells(rows, year);
        } while (reader.NextResult());
        throw new InvalidDataException($"The MGCB workbook does not contain worksheet 'Combined - {year}'.");
    }

    internal static IReadOnlyList<MichiganDetroitMonthlyRevenue> ParseCells(
        IReadOnlyList<object?[]> rows,
        int year)
    {
        var months = new List<MichiganDetroitMonthlyRevenue>(12);
        decimal[]? reportedTotals = null;
        var headersValidated = false;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var label = Text(row, 0);
            if (string.Equals(label, "Month", StringComparison.OrdinalIgnoreCase))
            {
                if (rowIndex == 0)
                {
                    throw new InvalidDataException("The MGCB revenue worksheet omitted its casino-name header row.");
                }
                RequireHeader(rows[rowIndex - 1], 1, "MGM GRAND DETROIT");
                RequireHeader(rows[rowIndex - 1], 3, "MOTORCITY CASINO");
                RequireHeader(rows[rowIndex - 1], 5, "GREEKTOWN CASINO");
                RequireHeader(rows[rowIndex - 1], 7, "All Detroit Casinos");
                RequireHeader(row, 1, "Total Adjusted Revenue");
                RequireHeader(row, 3, "Total Adjusted Revenue");
                RequireHeader(row, 5, "Total Adjusted Revenue");
                RequireHeader(row, 7, "Total Adjusted Gross Receipts");
                headersValidated = true;
                continue;
            }
            if (DateTime.TryParseExact(label, "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            {
                var values = new[] { Money(row, 1, label), Money(row, 3, label), Money(row, 5, label), Money(row, 7, label) };
                RequireReconciliation(values, label);
                months.Add(new MichiganDetroitMonthlyRevenue(year, month.Month, values[0], values[1], values[2], values[3]));
            }
            else if (string.Equals(label, "Total", StringComparison.OrdinalIgnoreCase))
            {
                reportedTotals = [Money(row, 1, label), Money(row, 3, label), Money(row, 5, label), Money(row, 7, label)];
                RequireReconciliation(reportedTotals, label);
            }
        }
        if (!headersValidated || months.Count != 12 || months.Select(row => row.Month).Distinct().Count() != 12 || reportedTotals is null)
        {
            throw new InvalidDataException($"The MGCB {year} worksheet must preserve the reviewed headers, 12 unique monthly rows, and one total row.");
        }
        var calculatedTotals = new[]
        {
            months.Sum(row => row.MgmGrandDetroit),
            months.Sum(row => row.MotorCityCasino),
            months.Sum(row => row.HollywoodGreektown),
            months.Sum(row => row.AllDetroitCasinos)
        };
        for (var index = 0; index < calculatedTotals.Length; index++)
        {
            if (calculatedTotals[index] != reportedTotals[index])
            {
                throw new InvalidDataException(
                    $"MGCB {year} annual total column {index + 1} does not equal the sum of monthly adjusted revenue.");
            }
        }
        return months.OrderBy(row => row.Month).ToArray();
    }

    private static string Text(object?[] row, int index) =>
        index < row.Length ? Convert.ToString(row[index], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;

    private static void RequireHeader(object?[] row, int index, string expected)
    {
        var actual = Text(row, index);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The MGCB workbook header changed at column {index + 1}; expected '{expected}', received '{actual}'. Review the transform before ingestion.");
        }
    }

    private static decimal Money(object?[] row, int index, string period)
    {
        if (index >= row.Length || row[index] is null)
        {
            throw new InvalidDataException($"MGCB adjusted revenue is missing for {period}, column {index + 1}.");
        }
        try
        {
            var value = decimal.Round(Convert.ToDecimal(row[index], CultureInfo.InvariantCulture), 2, MidpointRounding.AwayFromZero);
            return value >= 0 ? value : throw new InvalidDataException($"MGCB adjusted revenue is negative for {period}, column {index + 1}.");
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException($"MGCB adjusted revenue is not numeric for {period}, column {index + 1}.", exception);
        }
    }

    private static void RequireReconciliation(IReadOnlyList<decimal> values, string period)
    {
        if (values[0] + values[1] + values[2] != values[3])
        {
            throw new InvalidDataException($"MGCB Detroit casino revenue does not reconcile to the all-casino total for {period}.");
        }
    }
}
