using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/model-validation")]
public sealed class ModelValidationController(
    AppDbContext db,
    IBenchmarkOutputReader benchmarkOutputReader,
    IValidationEvaluationService evaluationService,
    IIncumbentBacktestCalibrationService incumbentCalibration) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("benchmarks")]
    public async Task<IActionResult> ListBenchmarks(CancellationToken cancellationToken) =>
        Ok(await db.BenchmarkStudies.AsNoTracking()
            .OrderBy(study => study.MarketCode)
            .ThenBy(study => study.StudyDate)
            .Select(study => new
            {
                study.Id,
                study.BenchmarkKey,
                study.Title,
                study.MarketCode,
                study.GeographyType,
                study.GeographyCode,
                study.StudyDate,
                study.ConsultantOrSource,
                study.CandidateLatitude,
                study.CandidateLongitude,
                study.DevelopmentProgramJson,
                study.ReportedOutputsJson,
                study.ReportedAssumptionsJson,
                study.MethodologicalNotes,
                study.SourceUrl,
                study.SourceFileChecksum,
                study.ProvenanceNotes,
                study.ValidationState
            })
            .ToListAsync(cancellationToken));

    [HttpPost("benchmarks")]
    public async Task<IActionResult> CreateBenchmark(
        [FromBody] CreateBenchmarkStudyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BenchmarkKey) ||
            string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.MarketCode) ||
            string.IsNullOrWhiteSpace(request.GeographyType) ||
            string.IsNullOrWhiteSpace(request.GeographyCode) ||
            string.IsNullOrWhiteSpace(request.ConsultantOrSource) ||
            !Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme is not ("https" or "http"))
        {
            return BadRequest("Benchmark identity, geography, source, and an HTTP(S) source URL are required.");
        }
        if (request.CandidateLatitude is < -90 or > 90 || request.CandidateLongitude is < -180 or > 180)
        {
            return BadRequest("Candidate coordinates must be valid WGS84 coordinates.");
        }
        if (await db.BenchmarkStudies.AnyAsync(
                study => study.BenchmarkKey == request.BenchmarkKey.Trim(),
                cancellationToken))
        {
            return Conflict($"Benchmark '{request.BenchmarkKey}' already exists.");
        }

        BenchmarkStudy benchmark;
        try
        {
            benchmark = new BenchmarkStudy
            {
                BenchmarkKey = request.BenchmarkKey.Trim(),
                Title = request.Title.Trim(),
                MarketCode = request.MarketCode.Trim(),
                GeographyType = request.GeographyType.Trim(),
                GeographyCode = request.GeographyCode.Trim(),
                StudyDate = request.StudyDate,
                ConsultantOrSource = request.ConsultantOrSource.Trim(),
                CandidateLatitude = request.CandidateLatitude,
                CandidateLongitude = request.CandidateLongitude,
                DevelopmentProgramJson = CanonicalJson(request.DevelopmentProgramJson),
                ReportedOutputsJson = CanonicalJson(request.ReportedOutputsJson),
                ReportedAssumptionsJson = CanonicalJson(request.ReportedAssumptionsJson),
                MethodologicalNotes = request.MethodologicalNotes,
                SourceUrl = sourceUri.ToString(),
                SourceFileChecksum = request.SourceFileChecksum,
                ProvenanceNotes = request.ProvenanceNotes,
                ValidationState = request.ValidationState
            };
        }
        catch (JsonException exception)
        {
            return BadRequest(exception.Message);
        }
        if (benchmark.ValidationState is not (
                BenchmarkValidationStates.Registered or BenchmarkValidationStates.Extracted or BenchmarkValidationStates.Validated))
        {
            return BadRequest("Unsupported benchmark validation state.");
        }
        db.BenchmarkStudies.Add(benchmark);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListBenchmarks), new { }, benchmark);
    }

    [HttpGet("cases")]
    public async Task<IActionResult> ListCases(CancellationToken cancellationToken) =>
        Ok(await db.ValidationCases.AsNoTracking()
            .OrderBy(validationCase => validationCase.DatasetPartition)
            .ThenBy(validationCase => validationCase.MarketCode)
            .ThenBy(validationCase => validationCase.CaseKey)
            .ToListAsync(cancellationToken));

    [HttpPost("benchmarks/{benchmarkStudyId:guid}/cases")]
    public async Task<IActionResult> CreatePublicBenchmarkCase(
        Guid benchmarkStudyId,
        [FromBody] CreatePublicBenchmarkCaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CaseKey) ||
            string.IsNullOrWhiteSpace(request.ReportedMetricPath) ||
            string.IsNullOrWhiteSpace(request.ObservedMetricDefinition) ||
            request.ReferencePeriodEnd < request.ReferencePeriodStart)
        {
            return BadRequest("Case identity, reported metric path, definition, and a valid reference period are required.");
        }
        if (await db.ValidationCases.AnyAsync(
                validationCase => validationCase.CaseKey == request.CaseKey.Trim(),
                cancellationToken))
        {
            return Conflict($"Validation case '{request.CaseKey}' already exists.");
        }

        var benchmark = await db.BenchmarkStudies.AsNoTracking()
            .SingleOrDefaultAsync(study => study.Id == benchmarkStudyId, cancellationToken);
        if (benchmark is null)
        {
            return NotFound("Benchmark study was not found.");
        }
        if (benchmark.ValidationState == BenchmarkValidationStates.Registered)
        {
            return BadRequest("Benchmark outputs must be source-extracted before a comparison case can be created.");
        }

        BenchmarkOutputMetric metric;
        try
        {
            metric = benchmarkOutputReader.ReadMonetaryMetric(
                benchmark.ReportedOutputsJson,
                request.ReportedMetricPath);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(exception.Message);
        }
        if (metric.Value < 0)
        {
            return BadRequest("Revenue benchmark metrics must be nonnegative.");
        }

        var run = await db.ModelRuns.AsNoTracking()
            .SingleOrDefaultAsync(modelRun => modelRun.Id == request.ModelRunId, cancellationToken);
        if (run is null || run.Status != ModelRunStatuses.Finalized || run.JurisdictionId is null ||
            !await db.ModelRunFacilityResults.AsNoTracking().AnyAsync(
                result => result.ModelRunId == request.ModelRunId && result.IsProposedFacility,
                cancellationToken))
        {
            return BadRequest("Benchmark comparisons require a finalized jurisdiction-bound run with a proposed-facility result.");
        }
        var jurisdictionCode = await db.Jurisdictions.AsNoTracking()
            .Where(jurisdiction => jurisdiction.Id == run.JurisdictionId.Value)
            .Select(jurisdiction => jurisdiction.Code)
            .SingleAsync(cancellationToken);

        ValidationCase validationCase;
        try
        {
            validationCase = new ValidationCase
            {
                BenchmarkStudyId = benchmark.Id,
                CaseKey = request.CaseKey.Trim(),
                Name = string.IsNullOrWhiteSpace(request.Name)
                    ? $"{benchmark.Title}: {metric.Path}"
                    : request.Name.Trim(),
                MarketCode = benchmark.MarketCode,
                JurisdictionCode = jurisdictionCode,
                CaseKind = ValidationCaseKinds.PublicBenchmark,
                DatasetPartition = ValidationPartitions.Benchmark,
                HoldoutGroup = benchmark.MarketCode,
                ModelRunId = run.Id,
                ObservedRevenue = metric.Value,
                ObservedMetricKey = "public-benchmark-revenue",
                ObservedMetricDefinition = request.ObservedMetricDefinition.Trim(),
                TrainingPeriodStart = request.ReferencePeriodStart,
                TrainingPeriodEnd = request.ReferencePeriodEnd,
                ValidationPeriodStart = request.ReferencePeriodStart,
                ValidationPeriodEnd = request.ReferencePeriodEnd,
                InclusionRulesJson = JsonSerializer.Serialize(new
                {
                    benchmark.BenchmarkKey,
                    ReportedMetricPath = metric.Path,
                    metric.Currency,
                    benchmark.SourceFileChecksum,
                    Rule = "Observed value is read directly from the source-extracted benchmark registry; it is never accepted from the request body."
                }, JsonOptions),
                PredictorValuesJson = CanonicalJson(request.PredictorValuesJson),
                ExecutionRequestJson = CanonicalJson(run.ResolvedInputJson),
                Notes = request.Notes
            };
        }
        catch (JsonException exception)
        {
            return BadRequest(exception.Message);
        }

        db.ValidationCases.Add(validationCase);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListCases), new { }, validationCase);
    }

    [HttpPost("cases")]
    public async Task<IActionResult> CreateCase(
        [FromBody] CreateValidationCaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CaseKey) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.MarketCode) ||
            string.IsNullOrWhiteSpace(request.JurisdictionCode) ||
            string.IsNullOrWhiteSpace(request.ObservedMetricKey) ||
            string.IsNullOrWhiteSpace(request.ObservedMetricDefinition) ||
            request.ObservedRevenue < 0 ||
            request.TrainingPeriodEnd < request.TrainingPeriodStart ||
            request.ValidationPeriodEnd < request.ValidationPeriodStart)
        {
            return BadRequest("Validation case identity, metric definition, nonnegative revenue, and valid periods are required.");
        }
        if (request.CaseKind is not (
                ValidationCaseKinds.IncumbentBacktest or ValidationCaseKinds.PublicBenchmark or
                ValidationCaseKinds.SyntheticNational) ||
            request.DatasetPartition is not (
                ValidationPartitions.Training or ValidationPartitions.Holdout or ValidationPartitions.Benchmark))
        {
            return BadRequest("Unsupported case kind or dataset partition.");
        }
        if (request.CaseKind == ValidationCaseKinds.IncumbentBacktest && request.TargetCasinoCompetitorId is null)
        {
            return BadRequest("Incumbent back-tests must identify the held-out incumbent competitor.");
        }
        if (request.BenchmarkStudyId is { } benchmarkId &&
            !await db.BenchmarkStudies.AsNoTracking().AnyAsync(study => study.Id == benchmarkId, cancellationToken))
        {
            return BadRequest("Benchmark study was not found.");
        }
        var targetCompetitor = request.TargetCasinoCompetitorId is not { } competitorId
            ? null
            : await db.CasinoCompetitors.AsNoTracking()
                .SingleOrDefaultAsync(competitor => competitor.Id == competitorId, cancellationToken);
        if (request.TargetCasinoCompetitorId is not null && targetCompetitor is null)
        {
            return BadRequest("Target incumbent competitor was not found.");
        }
        var run = await db.ModelRuns.AsNoTracking()
            .SingleOrDefaultAsync(modelRun => modelRun.Id == request.ModelRunId, cancellationToken);
        if (run is null || run.Status != ModelRunStatuses.Finalized ||
            !await db.ModelRunFacilityResults.AsNoTracking().AnyAsync(
                result => result.ModelRunId == request.ModelRunId && result.IsProposedFacility,
                cancellationToken))
        {
            return BadRequest("Validation cases must reference a finalized run with a proposed-facility result.");
        }
        if (request.CaseKind == ValidationCaseKinds.IncumbentBacktest && targetCompetitor is not null)
        {
            if (Math.Abs(run.CandidateLatitude - targetCompetitor.Latitude) > 0.01 ||
                Math.Abs(run.CandidateLongitude - targetCompetitor.Longitude) > 0.01)
            {
                return BadRequest("An incumbent back-test candidate must be located at the held-out incumbent site.");
            }
            if (await db.ModelRunFacilityResults.AsNoTracking().AnyAsync(
                    result => result.ModelRunId == request.ModelRunId &&
                              result.CasinoCompetitorId == targetCompetitor.Id &&
                              !result.IsProposedFacility,
                    cancellationToken))
            {
                return BadRequest("The held-out incumbent is still present in the run's competitive field.");
            }
        }
        if (await db.ValidationCases.AnyAsync(
                validationCase => validationCase.CaseKey == request.CaseKey.Trim(),
                cancellationToken))
        {
            return Conflict($"Validation case '{request.CaseKey}' already exists.");
        }

        ValidationCase validationCase;
        try
        {
            validationCase = new ValidationCase
            {
                BenchmarkStudyId = request.BenchmarkStudyId,
                CaseKey = request.CaseKey.Trim(),
                Name = request.Name.Trim(),
                MarketCode = request.MarketCode.Trim(),
                JurisdictionCode = request.JurisdictionCode.Trim(),
                CaseKind = request.CaseKind,
                DatasetPartition = request.DatasetPartition,
                HoldoutGroup = request.HoldoutGroup,
                TargetCasinoCompetitorId = request.TargetCasinoCompetitorId,
                ModelRunId = request.ModelRunId,
                ObservedRevenue = request.ObservedRevenue,
                ObservedMetricKey = request.ObservedMetricKey.Trim(),
                ObservedMetricDefinition = request.ObservedMetricDefinition.Trim(),
                TrainingPeriodStart = request.TrainingPeriodStart,
                TrainingPeriodEnd = request.TrainingPeriodEnd,
                ValidationPeriodStart = request.ValidationPeriodStart,
                ValidationPeriodEnd = request.ValidationPeriodEnd,
                InclusionRulesJson = CanonicalJson(request.InclusionRulesJson),
                PredictorValuesJson = CanonicalJson(request.PredictorValuesJson),
                ExecutionRequestJson = CanonicalJson(request.ExecutionRequestJson),
                Notes = request.Notes
            };
        }
        catch (JsonException exception)
        {
            return BadRequest(exception.Message);
        }
        db.ValidationCases.Add(validationCase);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListCases), new { }, validationCase);
    }

    [HttpPost("evaluations")]
    public async Task<ActionResult<ValidationEvaluationResult>> FinalizeEvaluation(
        [FromBody] ValidationEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await evaluationService.FinalizeAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetEvaluation), new { validationEvaluationId = result.ValidationEvaluationId }, result);
    }

    [HttpPost("calibrations/incumbent-backtests")]
    public async Task<ActionResult<IncumbentBacktestCalibrationResult>> CalibrateIncumbentBacktests(
        [FromBody] IncumbentBacktestCalibrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await incumbentCalibration.CalibrateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetEvaluation),
            new { validationEvaluationId = result.Evaluation.ValidationEvaluationId },
            result);
    }

    [HttpGet("evaluations/{validationEvaluationId:guid}")]
    public async Task<IActionResult> GetEvaluation(
        Guid validationEvaluationId,
        CancellationToken cancellationToken)
    {
        var evaluation = await db.ValidationEvaluations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == validationEvaluationId, cancellationToken);
        if (evaluation is null)
        {
            return NotFound();
        }
        var results = await db.ValidationCaseResults.AsNoTracking()
            .Where(result => result.ValidationEvaluationId == validationEvaluationId)
            .Join(
                db.ValidationCases.AsNoTracking(),
                result => result.ValidationCaseId,
                validationCase => validationCase.Id,
                (result, validationCase) => new
                {
                    validationCase.CaseKey,
                    validationCase.Name,
                    validationCase.MarketCode,
                    validationCase.HoldoutGroup,
                    result.PredictionKind,
                    result.DatasetPartition,
                    result.ModelRunId,
                    result.ObservedRevenue,
                    result.PredictedRevenue,
                    result.Residual,
                    result.AbsolutePercentageError,
                    result.SymmetricAbsolutePercentageError,
                    result.DiagnosticsJson
                })
            .OrderBy(result => result.PredictionKind)
            .ThenBy(result => result.DatasetPartition)
            .ThenBy(result => result.CaseKey)
            .ToListAsync(cancellationToken);
        return Ok(new { Evaluation = evaluation, Results = results });
    }

    private static string CanonicalJson(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new JsonException("Supplied benchmark or validation metadata must be valid JSON.", exception);
        }
    }
}

public sealed record CreateBenchmarkStudyRequest(
    string BenchmarkKey,
    string Title,
    string MarketCode,
    string GeographyType,
    string GeographyCode,
    DateOnly? StudyDate,
    string ConsultantOrSource,
    double? CandidateLatitude,
    double? CandidateLongitude,
    string? DevelopmentProgramJson,
    string? ReportedOutputsJson,
    string? ReportedAssumptionsJson,
    string? MethodologicalNotes,
    string SourceUrl,
    string? SourceFileChecksum,
    string? ProvenanceNotes,
    string ValidationState = BenchmarkValidationStates.Registered);

public sealed record CreateValidationCaseRequest(
    Guid? BenchmarkStudyId,
    string CaseKey,
    string Name,
    string MarketCode,
    string JurisdictionCode,
    string CaseKind,
    string DatasetPartition,
    string? HoldoutGroup,
    int? TargetCasinoCompetitorId,
    Guid ModelRunId,
    decimal ObservedRevenue,
    string ObservedMetricKey,
    string ObservedMetricDefinition,
    DateOnly TrainingPeriodStart,
    DateOnly TrainingPeriodEnd,
    DateOnly? ValidationPeriodStart,
    DateOnly? ValidationPeriodEnd,
    string? InclusionRulesJson,
    string? PredictorValuesJson,
    string? ExecutionRequestJson,
    string? Notes);

public sealed record CreatePublicBenchmarkCaseRequest(
    string CaseKey,
    string? Name,
    Guid ModelRunId,
    string ReportedMetricPath,
    string ObservedMetricDefinition,
    DateOnly ReferencePeriodStart,
    DateOnly ReferencePeriodEnd,
    string? PredictorValuesJson,
    string? Notes);
