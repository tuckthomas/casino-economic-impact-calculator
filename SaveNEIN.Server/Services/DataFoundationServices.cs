using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public static class DatasetSnapshotRoles
{
    public const string OriginDemographics = "origin-demographics";
    public const string IncomeAgi = "income-agi";
    public const string Competitors = "competitors";
    public const string ObservedPerformance = "observed-performance";
    public const string Tourism = "tourism";
    public const string Traffic = "traffic";
    public const string LocalEconomicInventory = "local-economic-inventory";
}

public sealed record RegisterDataSourceRequest(
    string Name,
    string Publisher,
    string Url,
    string SourceType,
    string GeographicCoverage,
    string VintagePeriod,
    DateTime RetrievedAtUtc,
    string ContentHash,
    bool IsAuthoritative,
    string? LicenseTermsNotes,
    string? Notes);

public sealed record BeginDatasetSnapshotRequest(
    long DataSourceId,
    string DatasetKey,
    string Period,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    long ExpectedRowCount,
    string Checksum,
    string TransformVersion);

public sealed record SealDatasetSnapshotRequest(
    Guid DatasetSnapshotId,
    string ValidationState,
    IReadOnlyCollection<string>? Warnings,
    IReadOnlyCollection<string>? Errors);

public interface IDataSnapshotService
{
    Task<DataSource> RegisterSourceAsync(
        RegisterDataSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<DatasetSnapshot> BeginSnapshotAsync(
        BeginDatasetSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<DatasetSnapshot> SealSnapshotAsync(
        SealDatasetSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<ModelRunDatasetSnapshotReference> AddRunReferenceAsync(
        Guid modelRunId,
        Guid datasetSnapshotId,
        string role,
        string referenceKey = "default",
        CancellationToken cancellationToken = default);
}

public sealed class DataSnapshotService(AppDbContext db) : IDataSnapshotService
{
    public async Task<DataSource> RegisterSourceAsync(
        RegisterDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(request.Name, nameof(request.Name));
        Require(request.Publisher, nameof(request.Publisher));
        Require(request.Url, nameof(request.Url));
        Require(request.SourceType, nameof(request.SourceType));
        Require(request.GeographicCoverage, nameof(request.GeographicCoverage));
        Require(request.VintagePeriod, nameof(request.VintagePeriod));
        Require(request.ContentHash, nameof(request.ContentHash));

        var existing = await db.DataSources.SingleOrDefaultAsync(
            source => source.Url == request.Url && source.ContentHash == request.ContentHash,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = new DataSource
        {
            Name = request.Name.Trim(),
            Publisher = request.Publisher.Trim(),
            Url = request.Url.Trim(),
            SourceType = request.SourceType.Trim(),
            GeographicCoverage = request.GeographicCoverage.Trim(),
            VintagePeriod = request.VintagePeriod.Trim(),
            RetrievedAtUtc = request.RetrievedAtUtc,
            ContentHash = request.ContentHash.Trim().ToLowerInvariant(),
            IsAuthoritative = request.IsAuthoritative,
            LicenseTermsNotes = request.LicenseTermsNotes,
            Notes = request.Notes
        };
        db.DataSources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return source;
    }

    public async Task<DatasetSnapshot> BeginSnapshotAsync(
        BeginDatasetSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(request.DatasetKey, nameof(request.DatasetKey));
        Require(request.Period, nameof(request.Period));
        Require(request.Checksum, nameof(request.Checksum));
        Require(request.TransformVersion, nameof(request.TransformVersion));
        if (request.ExpectedRowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedRowCount));
        }
        if (request.PeriodStart is { } start && request.PeriodEnd is { } end && end < start)
        {
            throw new ArgumentException("Dataset period end cannot precede its start.", nameof(request));
        }
        if (!await db.DataSources.AnyAsync(source => source.Id == request.DataSourceId, cancellationToken))
        {
            throw new KeyNotFoundException($"Data source '{request.DataSourceId}' was not found.");
        }

        var checksum = request.Checksum.Trim().ToLowerInvariant();
        if (await db.DatasetSnapshots.AnyAsync(
                snapshot => snapshot.DatasetKey == request.DatasetKey.Trim() && snapshot.Checksum == checksum,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Dataset snapshot '{request.DatasetKey}' with checksum '{checksum}' already exists.");
        }

        var snapshot = new DatasetSnapshot
        {
            DataSourceId = request.DataSourceId,
            DatasetKey = request.DatasetKey.Trim(),
            Period = request.Period.Trim(),
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            RowCount = request.ExpectedRowCount,
            Checksum = checksum,
            TransformVersion = request.TransformVersion.Trim(),
            ValidationState = DatasetValidationStates.Pending,
            IsSealed = false,
            WarningsJson = "[]",
            ErrorsJson = "[]"
        };
        db.DatasetSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<DatasetSnapshot> SealSnapshotAsync(
        SealDatasetSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ValidationState == DatasetValidationStates.Pending ||
            !ValidValidationStates.Contains(request.ValidationState))
        {
            throw new ArgumentException(
                "A sealed snapshot must be validated, warning, or rejected.",
                nameof(request));
        }
        var snapshot = await db.DatasetSnapshots
            .SingleOrDefaultAsync(item => item.Id == request.DatasetSnapshotId, cancellationToken)
            ?? throw new KeyNotFoundException($"Dataset snapshot '{request.DatasetSnapshotId}' was not found.");
        if (snapshot.IsSealed)
        {
            throw new InvalidOperationException($"Dataset snapshot '{snapshot.Id}' is already sealed.");
        }
        if (await db.ModelRunDatasetSnapshotReferences.AnyAsync(
                reference => reference.DatasetSnapshotId == snapshot.Id,
                cancellationToken))
        {
            throw new InvalidOperationException("A snapshot cannot be sealed after a model run has referenced it.");
        }

        var actualRowCount = await CountSnapshotRowsAsync(snapshot.Id, cancellationToken);
        var warnings = request.Warnings?.ToArray() ?? [];
        var errors = request.Errors?.ToArray() ?? [];
        var countMismatch = snapshot.RowCount != actualRowCount;
        if (countMismatch)
        {
            warnings = [.. warnings, $"Expected {snapshot.RowCount} rows but ingested {actualRowCount}."];
        }
        var finalState = request.ValidationState;
        if (countMismatch && finalState == DatasetValidationStates.Validated)
        {
            finalState = DatasetValidationStates.Warning;
        }
        if (errors.Length > 0 && finalState != DatasetValidationStates.Rejected)
        {
            throw new InvalidOperationException("A snapshot containing validation errors must be rejected.");
        }

        snapshot.RowCount = actualRowCount;
        snapshot.ValidationState = finalState;
        snapshot.WarningsJson = JsonSerializer.Serialize(warnings);
        snapshot.ErrorsJson = JsonSerializer.Serialize(errors);
        snapshot.IsSealed = true;
        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    private async Task<long> CountSnapshotRowsAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        var origins = await db.OriginZones.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var ageBins = await db.OriginZoneAgeBins.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var incomes = await db.OriginZoneIncomePeriods.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var competitors = await db.CasinoCompetitors.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var histories = await db.CasinoCompetitorHistory.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var revenuePeriods = await db.CasinoGamingRevenuePeriods.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var tourismObservations = await db.TourismMarketObservations.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var trafficObservations = await db.TrafficCorridorObservations.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        var localEconomicObservations = await db.LocalEconomicSectorObservations.LongCountAsync(
            row => row.DatasetSnapshotId == snapshotId,
            cancellationToken);
        return origins + ageBins + incomes + competitors + histories + revenuePeriods +
               tourismObservations + trafficObservations + localEconomicObservations;
    }

    public async Task<ModelRunDatasetSnapshotReference> AddRunReferenceAsync(
        Guid modelRunId,
        Guid datasetSnapshotId,
        string role,
        string referenceKey = "default",
        CancellationToken cancellationToken = default)
    {
        Require(role, nameof(role));
        Require(referenceKey, nameof(referenceKey));
        var modelRun = await db.ModelRuns.SingleOrDefaultAsync(run => run.Id == modelRunId, cancellationToken)
            ?? throw new KeyNotFoundException($"Model run '{modelRunId}' was not found.");
        if (!string.Equals(modelRun.Status, ModelRunStatuses.Draft, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataset references cannot be changed after a model run is finalized.");
        }
        if (!await db.DatasetSnapshots.AnyAsync(
                snapshot => snapshot.Id == datasetSnapshotId &&
                            snapshot.IsSealed &&
                            snapshot.ValidationState != DatasetValidationStates.Pending &&
                            snapshot.ValidationState != DatasetValidationStates.Rejected,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Dataset snapshot '{datasetSnapshotId}' must be sealed and usable before a run can reference it.");
        }

        var existing = await db.ModelRunDatasetSnapshotReferences.SingleOrDefaultAsync(
            reference => reference.ModelRunId == modelRunId &&
                         reference.Role == role &&
                         reference.ReferenceKey == referenceKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.DatasetSnapshotId != datasetSnapshotId)
            {
                throw new InvalidOperationException(
                    $"Run reference '{role}/{referenceKey}' already points to a different immutable snapshot.");
            }
            return existing;
        }

        var reference = new ModelRunDatasetSnapshotReference
        {
            ModelRunId = modelRunId,
            DatasetSnapshotId = datasetSnapshotId,
            Role = role.Trim(),
            ReferenceKey = referenceKey.Trim()
        };
        db.ModelRunDatasetSnapshotReferences.Add(reference);
        await db.SaveChangesAsync(cancellationToken);
        return reference;
    }

    private static readonly HashSet<string> ValidValidationStates =
    [
        DatasetValidationStates.Pending,
        DatasetValidationStates.Validated,
        DatasetValidationStates.Warning,
        DatasetValidationStates.Rejected
    ];

    private static void Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }
}

public sealed record AgeBinValue(int MinimumAge, int? MaximumAge, long Population);

public sealed record EligiblePopulationResult(
    double Population,
    int LegalGamingAge,
    bool UsedInterpolation,
    string InterpolationMethod);

public static class EligiblePopulationCalculator
{
    public static EligiblePopulationResult Calculate(
        IReadOnlyCollection<AgeBinValue> bins,
        int legalGamingAge)
    {
        if (legalGamingAge is < 0 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(legalGamingAge));
        }
        if (bins.Count == 0)
        {
            throw new InvalidOperationException("At least one raw population age bin is required.");
        }

        var ordered = bins.OrderBy(bin => bin.MinimumAge).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var bin = ordered[index];
            if (bin.MinimumAge < 0 || bin.MaximumAge is { } maximum && maximum < bin.MinimumAge || bin.Population < 0)
            {
                throw new InvalidOperationException("Population age bins contain invalid bounds or population.");
            }
            if (index > 0)
            {
                var previous = ordered[index - 1];
                if (previous.MaximumAge is null)
                {
                    throw new InvalidOperationException("An open-ended population age bin must be last.");
                }
                if (bin.MinimumAge != previous.MaximumAge.Value + 1)
                {
                    throw new InvalidOperationException("Population age bins must be contiguous and non-overlapping.");
                }
            }
        }
        if (ordered[^1].MaximumAge is not null)
        {
            throw new InvalidOperationException("Population age bins must end with an open-ended bin.");
        }

        double eligiblePopulation = 0;
        var usedInterpolation = false;
        foreach (var bin in ordered)
        {
            if (bin.MaximumAge is { } maximum && maximum < legalGamingAge)
            {
                continue;
            }
            if (bin.MinimumAge >= legalGamingAge)
            {
                eligiblePopulation += bin.Population;
                continue;
            }
            if (bin.MaximumAge is not { } boundedMaximum)
            {
                throw new InvalidOperationException(
                    "The legal gaming age cuts an open-ended age bin; a defensible interpolation cannot be derived.");
            }

            var totalAges = boundedMaximum - bin.MinimumAge + 1;
            var eligibleAges = boundedMaximum - legalGamingAge + 1;
            eligiblePopulation += bin.Population * ((double)eligibleAges / totalAges);
            usedInterpolation = true;
        }

        return new EligiblePopulationResult(
            eligiblePopulation,
            legalGamingAge,
            usedInterpolation,
            usedInterpolation ? AgeBinInterpolationMethods.UniformWithinBin : AgeBinInterpolationMethods.None);
    }
}

public interface IOriginEligiblePopulationService
{
    Task<EligiblePopulationResult> ResolveAsync(
        long originZoneId,
        Guid ageDatasetSnapshotId,
        int observationYear,
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public sealed class OriginEligiblePopulationService(
    AppDbContext db,
    IGamingAgeResolver gamingAgeResolver) : IOriginEligiblePopulationService
{
    public async Task<EligiblePopulationResult> ResolveAsync(
        long originZoneId,
        Guid ageDatasetSnapshotId,
        int observationYear,
        string jurisdictionCode,
        string facilityRegime,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        var legalAge = await gamingAgeResolver.ResolveMinimumAgeAsync(
            jurisdictionCode,
            facilityRegime,
            effectiveOn,
            cancellationToken);
        var bins = await db.OriginZoneAgeBins
            .Where(bin => bin.OriginZoneId == originZoneId &&
                          bin.DatasetSnapshotId == ageDatasetSnapshotId &&
                          bin.ObservationYear == observationYear)
            .OrderBy(bin => bin.MinimumAge)
            .Select(bin => new AgeBinValue(bin.MinimumAge, bin.MaximumAge, bin.Population))
            .ToListAsync(cancellationToken);
        return EligiblePopulationCalculator.Calculate(bins, legalAge);
    }
}
