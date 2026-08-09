namespace SaveNEIN.Server.Services;

public sealed record ProviderFetchRequest(
    string GeographicCoverage,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyDictionary<string, string>? Options = null);

public sealed record ProviderDataset<T>(
    RegisterDataSourceRequest Source,
    string DatasetKey,
    string Period,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string ContentChecksum,
    string TransformVersion,
    IReadOnlyCollection<T> Rows,
    IReadOnlyCollection<string> Warnings);

public interface ITourismObservationProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<TourismMarketObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOriginGeographyProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<OriginZoneImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgePopulationProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<OriginAgeBinImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOriginIncomeProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<OriginIncomeImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGamingFacilityInventoryProvider
{
    string ProviderKey { get; }
    string GeographicCoverage { get; }
    Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGamingFacilityHistoryProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<CasinoCompetitorHistoryImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGamingRegulatorPerformanceProvider
{
    string ProviderKey { get; }
    string GeographicCoverage { get; }
    Task<ProviderDataset<CasinoGamingRevenueImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public static class GamingRevenueMetricKeys
{
    public const string ComparableLandBasedGamingRevenue = "comparable-land-based-gaming-revenue";
}

public interface ITrafficObservationProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<TrafficCorridorObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILocalEconomicInventoryProvider
{
    string ProviderKey { get; }
    Task<ProviderDataset<LocalEconomicSectorObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProviderSnapshotIngestionService
{
    Task<Guid> IngestOriginsAsync(
        IOriginGeographyProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestAgePopulationAsync(
        IAgePopulationProvider provider,
        ProviderFetchRequest request,
        Guid originGeographySnapshotId,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestIncomeAsync(
        IOriginIncomeProvider provider,
        ProviderFetchRequest request,
        Guid originGeographySnapshotId,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestGamingFacilitiesAsync(
        IGamingFacilityInventoryProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestGamingFacilityHistoryAsync(
        IGamingFacilityHistoryProvider provider,
        ProviderFetchRequest request,
        Guid competitorSnapshotId,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestGamingPerformanceAsync(
        IGamingRegulatorPerformanceProvider provider,
        ProviderFetchRequest request,
        Guid competitorSnapshotId,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestTourismAsync(
        ITourismObservationProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestTrafficAsync(
        ITrafficObservationProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> IngestLocalEconomicInventoryAsync(
        ILocalEconomicInventoryProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderSnapshotIngestionService(
    IDataSnapshotService snapshots,
    IModelDataIngestionService ingestion) : IProviderSnapshotIngestionService
{
    private const int IngestionBatchSize = 50_000;

    public async Task<Guid> IngestOriginsAsync(
        IOriginGeographyProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.OriginGeography, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendOriginsAsync(snapshot.Id, batch, cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    internal static IEnumerable<IReadOnlyCollection<OriginAgeBinImportRow>> CompleteAgeBatches(
        IReadOnlyCollection<OriginAgeBinImportRow> rows,
        int maximumRows = IngestionBatchSize)
    {
        if (maximumRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }
        var batch = new List<OriginAgeBinImportRow>(Math.Min(rows.Count, maximumRows));
        foreach (var group in rows
                     .GroupBy(row => (row.StableOriginId, row.ObservationYear))
                     .OrderBy(group => group.Key.StableOriginId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.ObservationYear))
        {
            var completeSeries = group.OrderBy(row => row.MinimumAge).ToArray();
            if (batch.Count > 0 && batch.Count + completeSeries.Length > maximumRows)
            {
                yield return batch.ToArray();
                batch.Clear();
            }
            batch.AddRange(completeSeries);
        }
        if (batch.Count > 0)
        {
            yield return batch.ToArray();
        }
    }

    public async Task<Guid> IngestAgePopulationAsync(
        IAgePopulationProvider provider,
        ProviderFetchRequest request,
        Guid originGeographySnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.AgePopulation, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in CompleteAgeBatches(dataset.Rows))
        {
            await ingestion.AppendAgeBinsAsync(
                snapshot.Id,
                new OriginAgeBinImportRequest(originGeographySnapshotId, batch),
                cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestIncomeAsync(
        IOriginIncomeProvider provider,
        ProviderFetchRequest request,
        Guid originGeographySnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.Income, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendIncomeAsync(
                snapshot.Id,
                new OriginIncomeImportRequest(originGeographySnapshotId, batch),
                cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestGamingFacilitiesAsync(
        IGamingFacilityInventoryProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.Competitors, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendCompetitorsAsync(snapshot.Id, batch, cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestGamingFacilityHistoryAsync(
        IGamingFacilityHistoryProvider provider,
        ProviderFetchRequest request,
        Guid competitorSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.CompetitorHistory, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendCompetitorHistoryAsync(
                snapshot.Id,
                new CasinoCompetitorHistoryImportRequest(competitorSnapshotId, batch),
                cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestGamingPerformanceAsync(
        IGamingRegulatorPerformanceProvider provider,
        ProviderFetchRequest request,
        Guid competitorSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.ObservedPerformance, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendGamingRevenueAsync(
                snapshot.Id,
                new CasinoGamingRevenueImportRequest(competitorSnapshotId, batch),
                cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestTourismAsync(
        ITourismObservationProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.Tourism, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendTourismObservationsAsync(snapshot.Id, batch, cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestTrafficAsync(
        ITrafficObservationProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.Traffic, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendTrafficObservationsAsync(snapshot.Id, batch, cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    public async Task<Guid> IngestLocalEconomicInventoryAsync(
        ILocalEconomicInventoryProvider provider,
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var dataset = await provider.FetchAsync(request, cancellationToken);
        RequireDatasetKind(dataset, DatasetSnapshotKinds.LocalEconomicInventory, provider.ProviderKey);
        var snapshot = await BeginAsync(dataset, cancellationToken);
        foreach (var batch in dataset.Rows.Chunk(IngestionBatchSize))
        {
            await ingestion.AppendLocalEconomicSectorObservationsAsync(snapshot.Id, batch, cancellationToken);
        }
        await SealAsync(snapshot.Id, dataset.Warnings, cancellationToken);
        return snapshot.Id;
    }

    private async Task<Data.Entities.DatasetSnapshot> BeginAsync<T>(
        ProviderDataset<T> dataset,
        CancellationToken cancellationToken)
    {
        if (dataset.Rows.Count == 0)
        {
            throw new InvalidOperationException("A provider dataset cannot create an empty production snapshot.");
        }
        var source = await snapshots.RegisterSourceAsync(dataset.Source, cancellationToken);
        return await snapshots.BeginSnapshotAsync(new BeginDatasetSnapshotRequest(
            source.Id,
            dataset.DatasetKey,
            dataset.Period,
            dataset.PeriodStart,
            dataset.PeriodEnd,
            dataset.Rows.Count,
            dataset.ContentChecksum,
            dataset.TransformVersion), cancellationToken);
    }

    private Task<Data.Entities.DatasetSnapshot> SealAsync(
        Guid snapshotId,
        IReadOnlyCollection<string> warnings,
        CancellationToken cancellationToken) =>
        snapshots.SealSnapshotAsync(new SealDatasetSnapshotRequest(
            snapshotId,
            warnings.Count == 0 ? Data.Entities.DatasetValidationStates.Validated : Data.Entities.DatasetValidationStates.Warning,
            warnings,
            []), cancellationToken);

    private static void RequireDatasetKind<T>(
        ProviderDataset<T> dataset,
        string expectedKind,
        string providerKey)
    {
        if (!string.Equals(dataset.DatasetKey, expectedKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Provider '{providerKey}' returned dataset kind '{dataset.DatasetKey}', not '{expectedKind}'.");
        }
    }
}
