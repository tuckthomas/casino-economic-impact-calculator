// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record CalibrationScopeSource(string Url, string ContentSha256, string Metric,
    DateOnly PeriodStart, DateOnly PeriodEnd, string Basis, int DollarYear);

public sealed record CalibrationRevenueScopeCase(string CaseKey, string OriginSetSha256,
    CalibrationScopeSource Source, string ScopeAuditPath, string ScopeAuditSha256,
    double? ObservedOriginGgr = null, TourismDemandInput[]? Tourism = null,
    TrafficInterceptInput[]? Traffic = null);

public sealed record CalibrationRevenueScopeBundle(string Version, string ComparisonScope, int ObservedYear,
    Guid OriginSnapshotId, Guid PerformanceSnapshotId, CalibrationRevenueScopeCase[] Cases);

public sealed record ReconciledCalibrationRevenue(double PropertyObservedGgr, double ComparableObservedGgr,
    double TourismGgr, double TrafficGgr, string ComparisonScope, string OriginSetSha256)
{
    public double FixedNonresidentGgr => TourismGgr + TrafficGgr;
    public double Predict(double originUnitGgr, double fittedExpenditure) =>
        originUnitGgr * fittedExpenditure + FixedNonresidentGgr;
}

/// <summary>
/// Match observation and prediction domains before any spending fit. Source/audit
/// hashes bind evidence; they are not a substitute for independent source review.
/// </summary>
public static class CalibrationRevenueScope
{
    public const string ModeledOrigins = "modeled-origin-ggr";
    public const string TotalProperty = "total-property-ggr";
    public const string LegacyUnreconciled = "legacy-unreconciled-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string OriginSetHash(IEnumerable<string> keys)
    {
        var values = keys.Order(StringComparer.Ordinal).ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Resident-origin membership must be nonempty and unique.");
        return Hash(Encoding.UTF8.GetBytes(string.Join('\n', values)));
    }

    public static bool HasEvidence(MultimarketCalibrationProtocol protocol) =>
        protocol.RevenueComparisonScope is ModeledOrigins or TotalProperty &&
        !string.IsNullOrWhiteSpace(protocol.RevenueScopeEvidencePath) && IsHash(protocol.RevenueScopeEvidenceSha256);

    public static async Task<CalibrationRevenueScopeBundle> LoadAsync(MultimarketCalibrationProtocol protocol,
        string protocolPath, CancellationToken cancellationToken)
    {
        if (!HasEvidence(protocol)) throw new InvalidDataException("A matched revenue scope and frozen evidence bundle are required.");
        var evidenceRoot = Path.GetDirectoryName(Path.GetFullPath(protocolPath))!;
        var path = EvidencePath(evidenceRoot, evidenceRoot, protocol.RevenueScopeEvidencePath!);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (Hash(bytes) != protocol.RevenueScopeEvidenceSha256) throw new InvalidDataException("Revenue scope evidence bytes changed.");
        var bundle = JsonSerializer.Deserialize<CalibrationRevenueScopeBundle>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Revenue scope evidence is missing.");
        var keys = protocol.Training.Concat(protocol.Selection).Concat(protocol.Holdout.Values.SelectMany(value => value)).Order(StringComparer.Ordinal).ToArray();
        if (bundle.Version != "revenue-scope-v1" || bundle.ComparisonScope != protocol.RevenueComparisonScope ||
            bundle.ObservedYear != protocol.ObservedYear || bundle.OriginSnapshotId != protocol.OriginSnapshotId ||
            bundle.PerformanceSnapshotId != protocol.PerformanceSnapshotId || bundle.Cases is null || bundle.Cases.Any(row => row is null) ||
            !bundle.Cases.Select(row => row.CaseKey).Order(StringComparer.Ordinal).SequenceEqual(keys) ||
            bundle.Cases.Select(row => row.CaseKey).Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new InvalidDataException("Revenue scope evidence does not match the frozen cases, period and snapshots.");
        foreach (var row in bundle.Cases)
        {
            ValidateSource(row, bundle.ObservedYear);
            if (string.IsNullOrWhiteSpace(row.ScopeAuditPath) || !IsHash(row.ScopeAuditSha256))
                throw new InvalidDataException($"Case {row.CaseKey} lacks its independently reviewable scope audit.");
            var auditPath = EvidencePath(evidenceRoot, Path.GetDirectoryName(path)!, row.ScopeAuditPath);
            if (Hash(await File.ReadAllBytesAsync(auditPath, cancellationToken)) != row.ScopeAuditSha256)
                throw new InvalidDataException($"Case {row.CaseKey} scope audit bytes changed.");
        }
        return bundle;
    }

    public static ReconciledCalibrationRevenue Reconcile(CalibrationRevenueScopeBundle bundle,
        CalibrationRevenueScopeCase row, IEnumerable<string> originKeys, double propertyObservedGgr)
    {
        ValidateSource(row, bundle.ObservedYear);
        if (!double.IsFinite(propertyObservedGgr) || propertyObservedGgr <= 0)
            throw new InvalidDataException("Property observations must be positive finite gaming revenue.");
        var originHash = OriginSetHash(originKeys);
        if (originHash != row.OriginSetSha256)
            throw new InvalidDataException($"Case {row.CaseKey} evidence covers a different resident-origin universe.");
        if (bundle.ComparisonScope == ModeledOrigins)
        {
            if (row.Source.Basis != "observed-origin-attributed-ggr" || row.Source.Metric != "land-based-ggr-usd" ||
                row.ObservedOriginGgr is not { } amount || !double.IsFinite(amount) || amount <= 0 || amount > propertyObservedGgr ||
                row.Tourism?.Length > 0 || row.Traffic?.Length > 0)
                throw new InvalidDataException("Resident validation requires observed origin-attributed GGR, not patron counts, forecasts, inferred shares or added visitor demand.");
            return new(propertyObservedGgr, amount, 0, 0, bundle.ComparisonScope, originHash);
        }
        if (bundle.ComparisonScope != TotalProperty || row.ObservedOriginGgr is not null ||
            row.Source.Metric != "nonresident-demand-inputs" || row.Source.Basis != "independent-site-demand-coverage-audit" ||
            (row.Tourism?.Length ?? 0) + (row.Traffic?.Length ?? 0) == 0)
            throw new InvalidDataException("Total-property validation requires independently sourced site-demand inputs covering the omitted demand; missing inputs are not zero or a fitted revenue residual.");
        var tourism = new TourismDemandService();
        var traffic = new TrafficInterceptService();
        var tourismInputs = row.Tourism ?? [];
        var trafficInputs = row.Traffic ?? [];
        if (tourismInputs.Select(input => input.InputKey).Distinct(StringComparer.Ordinal).Count() != tourismInputs.Length ||
            trafficInputs.Select(input => input.CorridorKey).Distinct(StringComparer.Ordinal).Count() != trafficInputs.Length)
            throw new InvalidDataException("Nonresident input identities must be unique within a case.");
        var tourismGgr = tourismInputs.Sum(input => tourism.Calculate(input).TourismGgr);
        var trafficGgr = trafficInputs.Sum(input => traffic.Calculate(input).TrafficGgr);
        if (!double.IsFinite(tourismGgr + trafficGgr)) throw new InvalidDataException("Nonresident demand overflowed.");
        return new(propertyObservedGgr, propertyObservedGgr, tourismGgr, trafficGgr, bundle.ComparisonScope, originHash);
    }

    private static void ValidateSource(CalibrationRevenueScopeCase row, int year)
    {
        if (string.IsNullOrWhiteSpace(row.CaseKey) || !IsHash(row.OriginSetSha256) || row.Source is null ||
            !Uri.TryCreate(row.Source.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            !IsHash(row.Source.ContentSha256) || row.Source.PeriodStart != new DateOnly(year, 1, 1) ||
            row.Source.PeriodEnd != new DateOnly(year, 12, 31) || row.Source.DollarYear != year)
            throw new InvalidDataException("Revenue scope evidence needs a source, matching observation/dollar year, source checksum and resident-origin membership.");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string EvidencePath(string root, string relativeTo, string value)
    {
        var path = Path.GetFullPath(value, relativeTo);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(value) || !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Scope evidence and audits must stay within the protocol's evidence directory.");
        return path;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
