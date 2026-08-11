// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;

namespace SaveNEIN.Server.Services.Providers;

public sealed class CompositeGamingFacilityInventoryProvider(
    IEnumerable<IGamingFacilityInventoryProvider> providers) : IGamingFacilityInventoryProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IGamingFacilityInventoryProvider>> providersByCoverage =
        BuildProviderMap(providers);

    public string ProviderKey => "composite-regulated-gaming-facility-inventory";
    public string GeographicCoverage => string.Join(",", providersByCoverage.Keys.Order(StringComparer.Ordinal));

    public async Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = SelectProviders(request.GeographicCoverage, providersByCoverage);
        var datasets = await Task.WhenAll(selected.Select(item => item.Provider.FetchAsync(
            request with { GeographicCoverage = item.Coverage },
            cancellationToken)));
        var rows = datasets.SelectMany(dataset => dataset.Rows).ToArray();
        var duplicate = rows.GroupBy(row => row.StableVenueId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Composite facility providers repeat stable venue ID '{duplicate.Key}'.");
        }
        return Compose(
            request,
            datasets,
            rows,
            DatasetSnapshotKinds.Competitors,
            "composite-regulated-gaming-facilities-v1");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IGamingFacilityInventoryProvider>> BuildProviderMap(
        IEnumerable<IGamingFacilityInventoryProvider> providers)
    {
        var result = new Dictionary<string, List<IGamingFacilityInventoryProvider>>(StringComparer.OrdinalIgnoreCase);
        var providerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.GeographicCoverage))
            {
                throw new InvalidOperationException(
                    $"Gaming facility providers must have a non-empty geographic coverage; received '{provider.GeographicCoverage}'.");
            }
            if (!providerKeys.Add(provider.ProviderKey))
            {
                throw new InvalidOperationException($"Gaming facility provider key '{provider.ProviderKey}' is registered more than once.");
            }
            var coverage = provider.GeographicCoverage.Trim();
            if (!result.TryGetValue(coverage, out var coverageProviders))
            {
                coverageProviders = [];
                result.Add(coverage, coverageProviders);
            }
            coverageProviders.Add(provider);
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException("At least one gaming facility provider is required.");
        }
        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IGamingFacilityInventoryProvider>)pair.Value
                .OrderBy(provider => provider.ProviderKey, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<(string Coverage, IGamingFacilityInventoryProvider Provider)> SelectProviders(
        string requestedCoverage,
        IReadOnlyDictionary<string, IReadOnlyList<IGamingFacilityInventoryProvider>> available)
    {
        var requested = CompositeProviderSupport.ParseCoverage(requestedCoverage);
        return requested.SelectMany(coverage =>
            available.TryGetValue(coverage, out var coverageProviders)
                ? coverageProviders.Select(provider => (coverage, provider))
                : throw new NotSupportedException($"No gaming facility provider is registered for '{coverage}'."))
            .ToArray();
    }

    private ProviderDataset<T> Compose<T>(
        ProviderFetchRequest request,
        IReadOnlyCollection<ProviderDataset<T>> datasets,
        IReadOnlyCollection<T> rows,
        string datasetKey,
        string transformVersion) =>
        CompositeProviderSupport.Compose(
            ProviderKey,
            request,
            datasets,
            rows,
            datasetKey,
            transformVersion);
}

public sealed class CompositeGamingRegulatorPerformanceProvider(
    IEnumerable<IGamingRegulatorPerformanceProvider> providers) : IGamingRegulatorPerformanceProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IGamingRegulatorPerformanceProvider>> providersByCoverage =
        BuildProviderMap(providers);

    public string ProviderKey => "composite-regulated-gaming-performance";
    public string GeographicCoverage => string.Join(",", providersByCoverage.Keys.Order(StringComparer.Ordinal));

    public async Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = SelectProviders(request.GeographicCoverage, providersByCoverage);
        var datasets = await Task.WhenAll(selected.Select(item => item.Provider.FetchAsync(
            request with { GeographicCoverage = item.Coverage },
            cancellationToken)));
        var rows = datasets.SelectMany(dataset => dataset.Rows).ToArray();
        var duplicate = rows.GroupBy(
                row => (row.StableVenueId, row.PeriodStart, row.PeriodEnd, row.ReportedMetricKey),
                new PerformanceKeyComparer())
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Composite performance providers repeat '{duplicate.Key.ReportedMetricKey}' for '{duplicate.Key.StableVenueId}' during {duplicate.Key.PeriodStart:yyyy-MM-dd} through {duplicate.Key.PeriodEnd:yyyy-MM-dd}.");
        }
        return CompositeProviderSupport.Compose(
            ProviderKey,
            request,
            datasets,
            rows,
            DatasetSnapshotKinds.ObservedPerformance,
            "composite-regulated-gaming-performance-v1");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IGamingRegulatorPerformanceProvider>> BuildProviderMap(
        IEnumerable<IGamingRegulatorPerformanceProvider> providers)
    {
        var result = new Dictionary<string, List<IGamingRegulatorPerformanceProvider>>(StringComparer.OrdinalIgnoreCase);
        var providerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.GeographicCoverage))
            {
                throw new InvalidOperationException(
                    $"Gaming performance providers must have a non-empty geographic coverage; received '{provider.GeographicCoverage}'.");
            }
            if (!providerKeys.Add(provider.ProviderKey))
            {
                throw new InvalidOperationException($"Gaming performance provider key '{provider.ProviderKey}' is registered more than once.");
            }
            var coverage = provider.GeographicCoverage.Trim();
            if (!result.TryGetValue(coverage, out var coverageProviders))
            {
                coverageProviders = [];
                result.Add(coverage, coverageProviders);
            }
            coverageProviders.Add(provider);
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException("At least one gaming performance provider is required.");
        }
        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IGamingRegulatorPerformanceProvider>)pair.Value
                .OrderBy(provider => provider.ProviderKey, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<(string Coverage, IGamingRegulatorPerformanceProvider Provider)> SelectProviders(
        string requestedCoverage,
        IReadOnlyDictionary<string, IReadOnlyList<IGamingRegulatorPerformanceProvider>> available)
    {
        var requested = CompositeProviderSupport.ParseCoverage(requestedCoverage);
        return requested.SelectMany(coverage =>
            available.TryGetValue(coverage, out var coverageProviders)
                ? coverageProviders.Select(provider => (coverage, provider))
                : throw new NotSupportedException($"No gaming performance provider is registered for '{coverage}'."))
            .ToArray();
    }

    private sealed class PerformanceKeyComparer : IEqualityComparer<(string StableVenueId, DateOnly PeriodStart, DateOnly PeriodEnd, string ReportedMetricKey)>
    {
        public bool Equals(
            (string StableVenueId, DateOnly PeriodStart, DateOnly PeriodEnd, string ReportedMetricKey) x,
            (string StableVenueId, DateOnly PeriodStart, DateOnly PeriodEnd, string ReportedMetricKey) y) =>
            x.PeriodStart == y.PeriodStart &&
            x.PeriodEnd == y.PeriodEnd &&
            string.Equals(x.StableVenueId, y.StableVenueId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ReportedMetricKey, y.ReportedMetricKey, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string StableVenueId, DateOnly PeriodStart, DateOnly PeriodEnd, string ReportedMetricKey) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.StableVenueId),
                value.PeriodStart,
                value.PeriodEnd,
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ReportedMetricKey));
    }
}

internal static class CompositeProviderSupport
{
    internal static IReadOnlyList<string> ParseCoverage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("At least one geographic coverage code is required.", nameof(value));
        }
        var result = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (result.Length == 0)
        {
            throw new ArgumentException("At least one geographic coverage code is required.", nameof(value));
        }
        return result;
    }

    internal static ProviderDataset<T> Compose<T>(
        string providerKey,
        ProviderFetchRequest request,
        IReadOnlyCollection<ProviderDataset<T>> datasets,
        IReadOnlyCollection<T> rows,
        string datasetKey,
        string transformVersion)
    {
        if (datasets.Count == 0 || rows.Count == 0)
        {
            throw new InvalidOperationException("A composite provider cannot emit an empty dataset.");
        }
        if (datasets.Any(dataset => !string.Equals(dataset.DatasetKey, datasetKey, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"A component provider returned a dataset kind other than '{datasetKey}'.");
        }
        var components = datasets
            .OrderBy(dataset => dataset.Source.GeographicCoverage, StringComparer.Ordinal)
            .ThenBy(dataset => dataset.Source.Publisher, StringComparer.Ordinal)
            .ThenBy(dataset => dataset.Source.Url, StringComparer.Ordinal)
            .Select(dataset => $"{dataset.Source.Publisher}|{dataset.Source.Url}|{dataset.ContentChecksum}")
            .ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', components))))
            .ToLowerInvariant();
        var coverage = string.Join(",", datasets
            .Select(dataset => dataset.Source.GeographicCoverage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal));
        var period = request.PeriodStart == new DateOnly(request.PeriodStart.Year, 1, 1) &&
                     request.PeriodEnd == new DateOnly(request.PeriodStart.Year, 12, 31)
            ? request.PeriodStart.Year.ToString()
            : $"{request.PeriodStart:yyyy-MM-dd}_{request.PeriodEnd:yyyy-MM-dd}";
        var warnings = datasets
            .SelectMany(dataset => dataset.Warnings.Select(warning => $"{dataset.Source.Publisher}: {warning}"))
            .ToArray();
        var notes = string.Join("; ", datasets.Select(dataset =>
            $"{dataset.Source.Publisher} [{dataset.Source.Url}] checksum={dataset.ContentChecksum}"));

        return new ProviderDataset<T>(
            new RegisterDataSourceRequest(
                $"Composite authoritative gaming provider manifest {period}",
                "Multiple jurisdiction gaming regulators",
                $"urn:savenein:{providerKey}:{checksum}",
                "authoritative-regulator-provider-manifest",
                coverage,
                period,
                DateTime.UtcNow,
                checksum,
                datasets.All(dataset => dataset.Source.IsAuthoritative),
                "Each component regulator's public-record terms apply.",
                notes),
            datasetKey,
            period,
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            transformVersion,
            rows,
            warnings);
    }
}
