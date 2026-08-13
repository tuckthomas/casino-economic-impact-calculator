// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Gravity;

public static class OriginSummaryDimensions
{
    public const string Origin = "origin";
    public const string Zcta = "zcta";
    public const string County = "county";
    public const string State = "state";
    public const string MetropolitanArea = "msa";
    public const string CombinedStatisticalArea = "csa";
    public const string Country = "country";
    public const string HostRegion = "host-region";
    public const string Jurisdiction = "jurisdiction";
    public const string StateRelation = "state-relation";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Origin,
        Zcta,
        County,
        State,
        MetropolitanArea,
        CombinedStatisticalArea,
        Country,
        HostRegion,
        Jurisdiction,
        StateRelation
    };
}

public sealed record OriginSummarySourceRow(
    long OriginZoneId,
    string StableOriginId,
    string OriginType,
    string GeographyCode,
    string CountryCode,
    string? StateOrTerritoryCode,
    string? CountyEquivalentCode,
    string? MetropolitanStatisticalAreaCode,
    string? CombinedStatisticalAreaCode,
    decimal ResidentDemand,
    decimal InducedResidentDemand,
    decimal ProposedResidentGgr,
    decimal ProposedInducedResidentGgr,
    decimal TotalProposedResidentGgr,
    decimal HostJurisdictionCapture,
    decimal ExternalJurisdictionCapture,
    decimal TribalOrOtherJurisdictionCapture,
    decimal OutsideOptionCapture);

public sealed record OriginSummaryContext(
    string? HostCountryCode,
    string? HostStateCode,
    string? HostCountyCode,
    string? HostMetropolitanStatisticalAreaCode,
    string? HostCombinedStatisticalAreaCode,
    IReadOnlySet<long> InJurisdictionOriginZoneIds);

public sealed record OriginSummaryOptions(
    string Dimension,
    int TopN,
    decimal MinimumShare);

public sealed record OriginSummaryRow(
    string Key,
    string Label,
    bool IsResidual,
    int OriginCount,
    decimal ResidentDemand,
    decimal InducedResidentDemand,
    decimal ProposedResidentGgr,
    decimal ProposedInducedResidentGgr,
    decimal TotalProposedResidentGgr,
    decimal HostJurisdictionCapture,
    decimal ExternalJurisdictionCapture,
    decimal TribalOrOtherJurisdictionCapture,
    decimal OutsideOptionCapture,
    decimal ShareOfProposedResidentGgr);

public sealed record OriginSummaryResult(
    string Dimension,
    int UnderlyingOriginCount,
    int FullGroupCount,
    int DisplayedGroupCount,
    int TopN,
    decimal MinimumShare,
    decimal TotalProposedResidentGgr,
    bool ReconcilesToUnderlyingOrigins,
    IReadOnlyList<OriginSummaryRow> Rows,
    IReadOnlyList<string> Warnings);

public interface IOriginSummaryService
{
    OriginSummaryResult Summarize(
        IReadOnlyCollection<OriginSummarySourceRow> origins,
        OriginSummaryContext context,
        OriginSummaryOptions options);
}

public sealed class OriginSummaryService : IOriginSummaryService
{
    public OriginSummaryResult Summarize(
        IReadOnlyCollection<OriginSummarySourceRow> origins,
        OriginSummaryContext context,
        OriginSummaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(origins);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        if (!OriginSummaryDimensions.Supported.Contains(options.Dimension))
        {
            throw new ArgumentException(
                $"Unsupported origin-summary dimension '{options.Dimension}'. Supported dimensions: {string.Join(", ", OriginSummaryDimensions.Supported.Order())}.",
                nameof(options));
        }
        if (options.TopN is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "TopN must be between 1 and 100.");
        }
        if (options.MinimumShare is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumShare must be between 0 and 1.");
        }
        if (string.Equals(options.Dimension, OriginSummaryDimensions.Zcta, StringComparison.OrdinalIgnoreCase) &&
            origins.Any(origin => !IsZctaOriginType(origin.OriginType)))
        {
            throw new InvalidOperationException(
                "A ZIP/ZCTA summary cannot be synthesized from a non-ZCTA computational run without a versioned crosswalk.");
        }

        var warnings = ValidateContext(options.Dimension, context);
        var totalProposedResidentGgr = origins.Sum(origin => origin.TotalProposedResidentGgr);
        var grouped = origins
            .GroupBy(origin => ResolveGroup(origin, context, options.Dimension))
            .Select(group => Aggregate(group.Key.Key, group.Key.Label, false, group, totalProposedResidentGgr))
            .OrderByDescending(row => row.TotalProposedResidentGgr)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
        var selected = grouped
            .Where(row => totalProposedResidentGgr <= 0 ||
                          row.TotalProposedResidentGgr / totalProposedResidentGgr >= options.MinimumShare)
            .Take(options.TopN)
            .ToList();
        var selectedKeys = selected.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        var residualGroups = grouped.Where(row => !selectedKeys.Contains(row.Key)).ToArray();
        if (residualGroups.Length > 0)
        {
            selected.Add(AggregateResidual(residualGroups, totalProposedResidentGgr));
        }

        var reconciles = Reconciles(origins, selected);
        if (!reconciles)
        {
            throw new InvalidOperationException("Origin summary failed exact reconciliation to underlying stored origin results.");
        }
        return new OriginSummaryResult(
            options.Dimension.ToLowerInvariant(),
            origins.Count,
            grouped.Length,
            selected.Count,
            options.TopN,
            options.MinimumShare,
            totalProposedResidentGgr,
            true,
            selected,
            warnings);
    }

    private static IReadOnlyList<string> ValidateContext(string dimension, OriginSummaryContext context)
    {
        var warnings = new List<string>();
        if (dimension is OriginSummaryDimensions.HostRegion or OriginSummaryDimensions.StateRelation)
        {
            if (string.IsNullOrWhiteSpace(context.HostCountryCode) || string.IsNullOrWhiteSpace(context.HostStateCode))
            {
                warnings.Add("The candidate's host country/state could not be resolved from the run's computational origins; unresolved origins are disclosed as such.");
            }
        }
        if (dimension == OriginSummaryDimensions.HostRegion && string.IsNullOrWhiteSpace(context.HostCountyCode))
        {
            warnings.Add("The candidate's host county/parish could not be resolved from the run's computational origins.");
        }
        if (dimension == OriginSummaryDimensions.Jurisdiction && context.InJurisdictionOriginZoneIds.Count == 0)
        {
            warnings.Add("The run has no persisted in-jurisdiction origin set; all origins are disclosed as out of jurisdiction.");
        }
        return warnings;
    }

    private static (string Key, string Label) ResolveGroup(
        OriginSummarySourceRow origin,
        OriginSummaryContext context,
        string dimension) => dimension.ToLowerInvariant() switch
    {
        OriginSummaryDimensions.Origin => ($"origin:{origin.StableOriginId}", origin.StableOriginId),
        OriginSummaryDimensions.Zcta => ($"zcta:{origin.GeographyCode}", origin.GeographyCode),
        OriginSummaryDimensions.County => CodeGroup(
            "county",
            JoinCodes(origin.StateOrTerritoryCode, origin.CountyEquivalentCode),
            "Unassigned county/parish"),
        OriginSummaryDimensions.State => CodeGroup("state", origin.StateOrTerritoryCode, "Unassigned state/territory"),
        OriginSummaryDimensions.MetropolitanArea => CodeGroup(
            "msa",
            origin.MetropolitanStatisticalAreaCode,
            "Not assigned to an MSA"),
        OriginSummaryDimensions.CombinedStatisticalArea => CodeGroup(
            "csa",
            origin.CombinedStatisticalAreaCode,
            "Not assigned to a CSA"),
        OriginSummaryDimensions.Country => CodeGroup("country", origin.CountryCode, "Unassigned country"),
        OriginSummaryDimensions.HostRegion => ResolveHostRegion(origin, context),
        OriginSummaryDimensions.Jurisdiction => context.InJurisdictionOriginZoneIds.Contains(origin.OriginZoneId)
            ? ("jurisdiction:in", "In jurisdiction")
            : ("jurisdiction:out", "Out of jurisdiction"),
        OriginSummaryDimensions.StateRelation => ResolveStateRelation(origin, context),
        _ => throw new ArgumentException($"Unsupported origin-summary dimension '{dimension}'.", nameof(dimension))
    };

    private static (string Key, string Label) ResolveHostRegion(
        OriginSummarySourceRow origin,
        OriginSummaryContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HostCountryCode) || string.IsNullOrWhiteSpace(context.HostStateCode))
        {
            return ("host-region:unresolved", "Host relationship unresolved");
        }
        if (!EqualsCode(origin.CountryCode, context.HostCountryCode))
        {
            return ("host-region:international", "International origins");
        }
        if (EqualsCode(origin.StateOrTerritoryCode, context.HostStateCode) &&
            EqualsCode(origin.CountyEquivalentCode, context.HostCountyCode))
        {
            return ("host-region:host-county", "Host county/parish");
        }
        if (EqualsCode(origin.MetropolitanStatisticalAreaCode, context.HostMetropolitanStatisticalAreaCode))
        {
            return ("host-region:rest-host-msa", "Rest of host MSA");
        }
        if (EqualsCode(origin.CombinedStatisticalAreaCode, context.HostCombinedStatisticalAreaCode))
        {
            return ("host-region:rest-host-csa", "Rest of host CSA");
        }
        if (EqualsCode(origin.StateOrTerritoryCode, context.HostStateCode))
        {
            return ("host-region:rest-host-state", "Rest of host state/territory");
        }
        return ("host-region:out-of-state", "Out of state/territory");
    }

    private static (string Key, string Label) ResolveStateRelation(
        OriginSummarySourceRow origin,
        OriginSummaryContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HostCountryCode) || string.IsNullOrWhiteSpace(context.HostStateCode))
        {
            return ("state-relation:unresolved", "Host relationship unresolved");
        }
        if (!EqualsCode(origin.CountryCode, context.HostCountryCode))
        {
            return ("state-relation:international", "International origins");
        }
        return EqualsCode(origin.StateOrTerritoryCode, context.HostStateCode)
            ? ("state-relation:in-state", "In state/territory")
            : ("state-relation:out-of-state", "Out of state/territory");
    }

    private static (string Key, string Label) CodeGroup(string prefix, string? code, string missingLabel) =>
        string.IsNullOrWhiteSpace(code)
            ? ($"{prefix}:unassigned", missingLabel)
            : ($"{prefix}:{code.Trim().ToUpperInvariant()}", code.Trim().ToUpperInvariant());

    private static string? JoinCodes(string? first, string? second) =>
        string.IsNullOrWhiteSpace(second)
            ? null
            : string.IsNullOrWhiteSpace(first)
                ? second.Trim()
                : $"{first.Trim()}-{second.Trim()}";

    private static bool EqualsCode(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsZctaOriginType(string originType) =>
        string.Equals(originType, "zcta", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(originType, "zip-compatible", StringComparison.OrdinalIgnoreCase);

    private static OriginSummaryRow Aggregate(
        string key,
        string label,
        bool isResidual,
        IEnumerable<OriginSummarySourceRow> origins,
        decimal totalProposedResidentGgr)
    {
        var materialized = origins.ToArray();
        var proposed = materialized.Sum(origin => origin.TotalProposedResidentGgr);
        return new OriginSummaryRow(
            key,
            label,
            isResidual,
            materialized.Length,
            materialized.Sum(origin => origin.ResidentDemand),
            materialized.Sum(origin => origin.InducedResidentDemand),
            materialized.Sum(origin => origin.ProposedResidentGgr),
            materialized.Sum(origin => origin.ProposedInducedResidentGgr),
            proposed,
            materialized.Sum(origin => origin.HostJurisdictionCapture),
            materialized.Sum(origin => origin.ExternalJurisdictionCapture),
            materialized.Sum(origin => origin.TribalOrOtherJurisdictionCapture),
            materialized.Sum(origin => origin.OutsideOptionCapture),
            totalProposedResidentGgr == 0 ? 0 : proposed / totalProposedResidentGgr);
    }

    private static OriginSummaryRow AggregateResidual(
        IReadOnlyCollection<OriginSummaryRow> rows,
        decimal totalProposedResidentGgr)
    {
        var proposed = rows.Sum(row => row.TotalProposedResidentGgr);
        return new OriginSummaryRow(
            "other-origins",
            "Other origins",
            true,
            rows.Sum(row => row.OriginCount),
            rows.Sum(row => row.ResidentDemand),
            rows.Sum(row => row.InducedResidentDemand),
            rows.Sum(row => row.ProposedResidentGgr),
            rows.Sum(row => row.ProposedInducedResidentGgr),
            proposed,
            rows.Sum(row => row.HostJurisdictionCapture),
            rows.Sum(row => row.ExternalJurisdictionCapture),
            rows.Sum(row => row.TribalOrOtherJurisdictionCapture),
            rows.Sum(row => row.OutsideOptionCapture),
            totalProposedResidentGgr == 0 ? 0 : proposed / totalProposedResidentGgr);
    }

    private static bool Reconciles(
        IReadOnlyCollection<OriginSummarySourceRow> origins,
        IReadOnlyCollection<OriginSummaryRow> rows) =>
        origins.Sum(origin => origin.ResidentDemand) == rows.Sum(row => row.ResidentDemand) &&
        origins.Sum(origin => origin.InducedResidentDemand) == rows.Sum(row => row.InducedResidentDemand) &&
        origins.Sum(origin => origin.ProposedResidentGgr) == rows.Sum(row => row.ProposedResidentGgr) &&
        origins.Sum(origin => origin.ProposedInducedResidentGgr) == rows.Sum(row => row.ProposedInducedResidentGgr) &&
        origins.Sum(origin => origin.TotalProposedResidentGgr) == rows.Sum(row => row.TotalProposedResidentGgr) &&
        origins.Sum(origin => origin.HostJurisdictionCapture) == rows.Sum(row => row.HostJurisdictionCapture) &&
        origins.Sum(origin => origin.ExternalJurisdictionCapture) == rows.Sum(row => row.ExternalJurisdictionCapture) &&
        origins.Sum(origin => origin.TribalOrOtherJurisdictionCapture) == rows.Sum(row => row.TribalOrOtherJurisdictionCapture) &&
        origins.Sum(origin => origin.OutsideOptionCapture) == rows.Sum(row => row.OutsideOptionCapture);
}
