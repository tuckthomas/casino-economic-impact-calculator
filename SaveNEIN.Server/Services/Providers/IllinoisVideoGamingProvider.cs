// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SaveNEIN.Server.Services.Providers;

public sealed record IllinoisVideoGamingObservation(
    string? StableLicenseId, string Establishment, string Municipality, int ReportedVgtCount,
    decimal AmountPlayed, decimal AmountWon, decimal NetWager, decimal FundsIn, decimal FundsOut,
    decimal NetTerminalIncome, decimal NtiTax, decimal StateShare, decimal MunicipalityShare,
    decimal FundsMinusReportedNtiDifference, IReadOnlyCollection<string> AnomalyFlags, bool IsPriorPeriodAdjustment);

/// <summary>Source-backed distributed gaming observations; municipality names are not fabricated facility coordinates.</summary>
public static class IllinoisVideoGamingProvider
{
    public const string ReportsUrl = "https://igbapps.illinois.gov/VideoReports_AEM.aspx";
    public const string TransformVersion = "igb-establishment-nti-v1";

    public static ProviderDataset<IllinoisVideoGamingObservation> ParseCapture(byte[] csvBytes, int year, DateTime retrievedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(csvBytes);
        if (year is < 2012 or > 2200) throw new ArgumentOutOfRangeException(nameof(year));
        var csv = CsvRecordReader.Read(Encoding.UTF8.GetString(csvBytes));
        if (csv.Count < 5 || csv[0][0].Trim() != "All Establishment Details" ||
            csv[1][0].Trim() != $"January {year} - December {year}")
            throw new InvalidDataException("IGB establishment capture has a different report type or observation year.");
        var header = csv.FindIndex(row => row.Count >= 13 && row[0] == "Establishment" && row[1] == "License Number" &&
            row[3] == "VGT Count" && row[9] == "Net Terminal Income");
        if (header < 0) throw new InvalidDataException("IGB establishment report omits the expected revenue columns.");
        var identities = new HashSet<(string License, string Municipality, int Units)>();
        var rows = new List<IllinoisVideoGamingObservation>();
        foreach (var row in csv.Skip(header + 1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count < 13 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[2]))
                throw new InvalidDataException("IGB establishment row is incomplete.");
            var license = row[1].Trim();
            var priorAdjustment = license.Length == 0 && row[0].Trim() == "Prior Period Adjustment";
            if (!priorAdjustment && (license.Length != 9 || license.Any(character => character is < '0' or > '9')))
                throw new InvalidDataException("IGB establishment license is invalid.");
            if (!int.TryParse(row[3], NumberStyles.None, CultureInfo.InvariantCulture, out var units) || units < 0)
                throw new InvalidDataException("IGB VGT count must be a nonnegative whole number.");
            if (priorAdjustment && units != 0) throw new InvalidDataException("A prior-period accounting adjustment cannot add physical units.");
            if (!identities.Add((license, row[2].Trim(), units)))
                throw new InvalidDataException("IGB repeats the same license/municipality/unit observation.");
            var amounts = row.Skip(4).Take(9).Select(value => decimal.Parse(value.Trim(), NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture)).ToArray();
            if (Math.Abs(amounts[0] - amounts[1] - amounts[2]) > 0.02m ||
                Math.Abs(amounts[6] - amounts[7] - amounts[8]) > 0.02m)
                throw new InvalidDataException("IGB wagers or tax allocation do not reconcile.");
            var difference = amounts[3] - amounts[4] - amounts[5];
            var flags = new List<string>();
            if (Math.Abs(difference) > 0.02m) flags.Add("reported-nti-differs-from-funds-subtraction");
            if (amounts.Any(value => value < 0)) flags.Add("signed-source-adjustment");
            if (priorAdjustment) flags.Add("prior-period-adjustment-unassigned-to-license");
            rows.Add(new(priorAdjustment ? null : license, row[0].Trim(), row[2].Trim(), units,
                amounts[0], amounts[1], amounts[2], amounts[3], amounts[4], amounts[5], amounts[6], amounts[7], amounts[8], difference, flags, priorAdjustment));
        }
        if (rows.Count == 0) throw new InvalidDataException("IGB establishment report is empty.");
        var checksum = Convert.ToHexString(SHA256.HashData(csvBytes)).ToLowerInvariant();
        var period = year.ToString(CultureInfo.InvariantCulture);
        return new(new RegisterDataSourceRequest($"Illinois video gaming establishment income {year}", "Illinois Gaming Board",
                ReportsUrl, "state-regulator-csv", "Illinois video gaming establishments in the annual report", period,
                retrievedAtUtc, checksum, true, "Illinois public-record terms apply.",
                "Published Net Terminal Income is preserved separately from Amount Played and Net Wager. Funds-subtraction discrepancies and signed source adjustments are flagged, not overwritten. Exact CSV hash retained."),
            "distributed-gaming-observations", period, new(year, 1, 1), new(year, 12, 31), checksum, TransformVersion, rows,
            ["Annual VGT counts retain the source's reported units; no year-end-seat or annual-average convention is inferred.",
             "A license can have several municipality/unit observations and signed reallocations. Annual rows are not distinct physical locations; summing their VGT counts would double-count supply.",
             "Prior Period Adjustment rows have no establishment license and zero physical units. They are retained separately; their original earning periods are not inferred from the annual report window.",
             "Municipality labels are not coordinates or origin locations. Historical addresses, geocodes, closure periods and market membership require reconciliation before routing.",
             "Terminal income is a separate regulator metric. No numeric substitution for casino AGR, residential demand, or a fitted outside-option weight was made."]);
    }
}
