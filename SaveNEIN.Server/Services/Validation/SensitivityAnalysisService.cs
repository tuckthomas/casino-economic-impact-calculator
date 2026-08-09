using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record SensitivityParameterRange(string ParameterKey, double LowValue, double HighValue);

public sealed record SensitivityAnalysisRequest(
    string AnalysisKey,
    string Version,
    string Name,
    string OutputMetric,
    GravityModelRunRequest BaseRunRequest,
    IReadOnlyCollection<SensitivityParameterRange> ParameterRanges);

public sealed record SensitivityPointResult(
    string ParameterKey,
    string Direction,
    double ParameterValue,
    Guid ModelRunId,
    decimal OutputMetricValue,
    decimal DeltaFromBaseline,
    decimal StabilizedTotalGgr,
    decimal LocalDiscretionaryDisplacement,
    decimal GrossGamingTax,
    decimal GrossSocialCost,
    double NetPermanentJobs,
    decimal NetHostLocalImpact,
    decimal NetHostStateImpact);

public sealed record SensitivityTornadoRow(
    string ParameterKey,
    double LowParameterValue,
    double BaseParameterValue,
    double HighParameterValue,
    decimal LowMetricValue,
    decimal BaseMetricValue,
    decimal HighMetricValue,
    decimal LowDelta,
    decimal HighDelta,
    decimal TotalRange);

public sealed record SensitivityAnalysisResult(
    Guid SensitivityAnalysisId,
    string AnalysisKey,
    string Version,
    string Name,
    string OutputMetric,
    Guid BaselineModelRunId,
    decimal BaselineMetricValue,
    string Status,
    IReadOnlyList<SensitivityPointResult> Points,
    IReadOnlyList<SensitivityTornadoRow> Tornado);

public interface ISensitivityAnalysisService
{
    Task<SensitivityAnalysisResult> ExecuteAsync(
        SensitivityAnalysisRequest request,
        CancellationToken cancellationToken = default);

    Task<SensitivityAnalysisResult?> GetAsync(
        Guid sensitivityAnalysisId,
        CancellationToken cancellationToken = default);
}

public sealed class SensitivityAnalysisService(
    AppDbContext db,
    IModelParameterService parameterService,
    IGravityModelExecutionService executionService) : ISensitivityAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SensitivityAnalysisResult> ExecuteAsync(
        SensitivityAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var analysisKey = request.AnalysisKey.Trim();
        var version = request.Version.Trim();
        if (await db.SensitivityAnalyses.AsNoTracking().AnyAsync(
                analysis => analysis.AnalysisKey == analysisKey && analysis.Version == version,
                cancellationToken))
        {
            throw new InvalidOperationException($"Sensitivity analysis '{analysisKey}' version '{version}' already exists.");
        }

        var resolved = await parameterService.ResolveAsync(
            new ParameterResolutionRequest(
                "gravity-v1",
                request.BaseRunRequest.NationalParameterSetId,
                request.BaseRunRequest.JurisdictionParameterSetId,
                request.BaseRunRequest.ScenarioParameterSetId,
                request.BaseRunRequest.UserOverrides),
            cancellationToken);
        var baseValues = ValidateRanges(request.ParameterRanges, resolved);

        var baseline = await executionService.ExecuteAsync(
            request.BaseRunRequest with { ScenarioName = $"{request.BaseRunRequest.ScenarioName} — sensitivity baseline" },
            cancellationToken);
        var baselineMetric = SelectMetric(baseline, request.OutputMetric);
        var analysis = new SensitivityAnalysis
        {
            AnalysisKey = analysisKey,
            Version = version,
            Name = request.Name.Trim(),
            BaselineModelRunId = baseline.ModelRunId,
            OutputMetric = request.OutputMetric,
            BaselineMetricValue = baselineMetric,
            Status = SensitivityAnalysisStatuses.Draft,
            InputJson = JsonSerializer.Serialize(request, JsonOptions)
        };
        db.SensitivityAnalyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            foreach (var range in request.ParameterRanges.OrderBy(item => item.ParameterKey, StringComparer.Ordinal))
            {
                await ExecutePointAsync(analysis, request.BaseRunRequest, range.ParameterKey, "low", range.LowValue, baselineMetric, cancellationToken);
                await ExecutePointAsync(analysis, request.BaseRunRequest, range.ParameterKey, "high", range.HighValue, baselineMetric, cancellationToken);
            }

            analysis.Status = SensitivityAnalysisStatuses.Finalized;
            analysis.FinalizedAtUtc = DateTime.UtcNow;
            analysis.IsImmutable = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            analysis.Status = SensitivityAnalysisStatuses.Failed;
            analysis.ErrorSummary = exception.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return (await GetAsync(analysis.Id, cancellationToken))! with
        {
            Tornado = BuildTornado(
                baselineMetric,
                baseValues,
                await db.SensitivityAnalysisPoints.AsNoTracking()
                    .Where(point => point.SensitivityAnalysisId == analysis.Id)
                    .ToListAsync(cancellationToken))
        };
    }

    public async Task<SensitivityAnalysisResult?> GetAsync(
        Guid sensitivityAnalysisId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await db.SensitivityAnalyses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sensitivityAnalysisId, cancellationToken);
        if (analysis is null)
        {
            return null;
        }
        var entities = await db.SensitivityAnalysisPoints.AsNoTracking()
            .Where(point => point.SensitivityAnalysisId == sensitivityAnalysisId)
            .OrderBy(point => point.ParameterKey)
            .ThenBy(point => point.Direction)
            .ToListAsync(cancellationToken);
        var baseValues = await db.ModelRunParameterValues.AsNoTracking()
            .Where(value => value.ModelRunId == analysis.BaselineModelRunId)
            .Join(
                db.ModelParameterDefinitions.AsNoTracking(),
                value => value.ParameterDefinitionId,
                definition => definition.Id,
                (value, definition) => new { definition.Key, value.FinalValue })
            .ToDictionaryAsync(item => item.Key, item => item.FinalValue, cancellationToken);
        var points = entities.Select(ToResult).ToArray();
        return new SensitivityAnalysisResult(
            analysis.Id,
            analysis.AnalysisKey,
            analysis.Version,
            analysis.Name,
            analysis.OutputMetric,
            analysis.BaselineModelRunId,
            analysis.BaselineMetricValue,
            analysis.Status,
            points,
            BuildTornado(analysis.BaselineMetricValue, baseValues, entities));
    }

    private async Task ExecutePointAsync(
        SensitivityAnalysis analysis,
        GravityModelRunRequest baseRequest,
        string parameterKey,
        string direction,
        double value,
        decimal baselineMetric,
        CancellationToken cancellationToken)
    {
        var request = SensitivityRunFactory.WithOverride(baseRequest, parameterKey, value, direction);
        var result = await executionService.ExecuteAsync(request, cancellationToken);
        var metricValue = SelectMetric(result, analysis.OutputMetric);
        db.SensitivityAnalysisPoints.Add(new SensitivityAnalysisPoint
        {
            SensitivityAnalysisId = analysis.Id,
            ParameterKey = parameterKey,
            Direction = direction,
            ParameterValue = value,
            ModelRunId = result.ModelRunId,
            OutputMetricValue = metricValue,
            DeltaFromBaseline = metricValue - baselineMetric,
            StabilizedTotalGgr = result.StabilizedTotalGgr,
            LocalDiscretionaryDisplacement = result.LocalDiscretionaryDisplacement,
            GrossGamingTax = result.GrossGamingTax,
            GrossSocialCost = result.GrossSocialCost,
            NetPermanentJobs = result.NetPermanentJobs,
            NetHostLocalImpact = result.NetHostLocalImpact,
            NetHostStateImpact = result.NetHostStateImpact
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static decimal SelectMetric(GravityModelRunResult result, string metric) => metric switch
    {
        SensitivityOutputMetrics.StabilizedTotalGgr => result.StabilizedTotalGgr,
        SensitivityOutputMetrics.LocalDiscretionaryDisplacement => result.LocalDiscretionaryDisplacement,
        SensitivityOutputMetrics.GrossGamingTax => result.GrossGamingTax,
        SensitivityOutputMetrics.GrossSocialCost => result.GrossSocialCost,
        SensitivityOutputMetrics.NetPermanentJobs => Math.Round(Convert.ToDecimal(result.NetPermanentJobs), 4),
        SensitivityOutputMetrics.NetHostLocalImpact => result.NetHostLocalImpact,
        SensitivityOutputMetrics.NetHostStateImpact => result.NetHostStateImpact,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported sensitivity output metric.")
    };

    private static Dictionary<string, double> ValidateRanges(
        IReadOnlyCollection<SensitivityParameterRange> ranges,
        IReadOnlyCollection<ResolvedModelParameter> resolved)
    {
        var resolvedByKey = resolved.ToDictionary(item => item.Definition.Key, StringComparer.Ordinal);
        var baseValues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var range in ranges)
        {
            var key = range.ParameterKey.Trim();
            if (!resolvedByKey.TryGetValue(key, out var parameter) || !parameter.Definition.IsUserOverridable)
            {
                throw new ArgumentException($"Sensitivity parameter '{key}' is not an active user-overridable gravity-v1 parameter.", nameof(ranges));
            }
            if (parameter.Definition.ComputationalMinimum is { } minimum && range.LowValue < minimum ||
                parameter.Definition.ComputationalMaximum is { } maximum && range.HighValue > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(ranges), $"Sensitivity range for '{key}' crosses its computational safety bound.");
            }
            if (!(range.LowValue <= parameter.FinalValue && parameter.FinalValue <= range.HighValue))
            {
                throw new ArgumentException(
                    $"Sensitivity range for '{key}' must include the resolved baseline value {parameter.FinalValue:G17}.",
                    nameof(ranges));
            }
            baseValues.Add(key, parameter.FinalValue);
        }
        return baseValues;
    }

    private static void ValidateRequest(SensitivityAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AnalysisKey) || string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Sensitivity analysis key, version, and name are required.", nameof(request));
        }
        if (!SensitivityOutputMetrics.IsSupported(request.OutputMetric))
        {
            throw new ArgumentException($"Unsupported sensitivity output metric '{request.OutputMetric}'.", nameof(request));
        }
        if (request.ParameterRanges.Count is < 1 or > 12)
        {
            throw new ArgumentException("Select between 1 and 12 one-at-a-time sensitivity parameters.", nameof(request));
        }
        var normalizedKeys = request.ParameterRanges.Select(range => range.ParameterKey.Trim()).ToArray();
        if (normalizedKeys.Any(string.IsNullOrWhiteSpace) || normalizedKeys.Distinct(StringComparer.Ordinal).Count() != normalizedKeys.Length ||
            request.ParameterRanges.Any(range => !double.IsFinite(range.LowValue) || !double.IsFinite(range.HighValue) || range.LowValue >= range.HighValue))
        {
            throw new ArgumentException("Sensitivity parameter keys must be unique and every finite low value must be below its high value.", nameof(request));
        }
    }

    private static SensitivityPointResult ToResult(SensitivityAnalysisPoint point) => new(
        point.ParameterKey,
        point.Direction,
        point.ParameterValue,
        point.ModelRunId,
        point.OutputMetricValue,
        point.DeltaFromBaseline,
        point.StabilizedTotalGgr,
        point.LocalDiscretionaryDisplacement,
        point.GrossGamingTax,
        point.GrossSocialCost,
        point.NetPermanentJobs,
        point.NetHostLocalImpact,
        point.NetHostStateImpact);

    private static IReadOnlyList<SensitivityTornadoRow> BuildTornado(
        decimal baselineMetric,
        IReadOnlyDictionary<string, double> baseValues,
        IReadOnlyCollection<SensitivityAnalysisPoint> points) =>
        points.GroupBy(point => point.ParameterKey, StringComparer.Ordinal)
            .Where(group => group.Count() == 2 && baseValues.ContainsKey(group.Key))
            .Select(group =>
            {
                var low = group.Single(point => point.Direction == "low");
                var high = group.Single(point => point.Direction == "high");
                return new SensitivityTornadoRow(
                    group.Key,
                    low.ParameterValue,
                    baseValues[group.Key],
                    high.ParameterValue,
                    low.OutputMetricValue,
                    baselineMetric,
                    high.OutputMetricValue,
                    low.DeltaFromBaseline,
                    high.DeltaFromBaseline,
                    Math.Abs(high.OutputMetricValue - low.OutputMetricValue));
            })
            .OrderByDescending(row => row.TotalRange)
            .ThenBy(row => row.ParameterKey, StringComparer.Ordinal)
            .ToArray();
}

public static class SensitivityRunFactory
{
    public static GravityModelRunRequest WithOverride(
        GravityModelRunRequest baseRequest,
        string parameterKey,
        double value,
        string direction)
    {
        var key = parameterKey.Trim();
        var overrides = (baseRequest.UserOverrides ?? [])
            .Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal))
            .Append(new ParameterOverride(key, value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        return baseRequest with
        {
            ScenarioName = $"{baseRequest.ScenarioName} — {key} {direction} ({value:G17})",
            UserOverrides = overrides
        };
    }
}
