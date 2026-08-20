// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/model-validation/demand-specifications")]
public sealed class DemandSpecificationValidationController(
    AppDbContext db,
    IGravityModelExecutionService gravityModel,
    IValidationMetricsService metricsService,
    IModelParameterSetService parameterSetService) : ControllerBase
{
    [HttpPost("run-pair")]
    public async Task<ActionResult<DemandSpecificationRunPairResult>> ExecutePair(
        [FromBody] GravityModelRunRequest request,
        CancellationToken cancellationToken)
    {
        var result = await new DemandSpecificationRunPairService(gravityModel)
            .ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("evaluations")]
    public async Task<ActionResult<DemandSpecificationValidationEvaluationResult>> FinalizeEvaluation(
        [FromBody] DemandSpecificationValidationEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await new DemandSpecificationValidationEvaluationService(
                db,
                metricsService,
                parameterSetService)
            .FinalizeAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetEvaluation),
            new { validationEvaluationId = result.ValidationEvaluationId },
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

        using var selected = JsonDocument.Parse(evaluation.SelectedParametersJson);
        if (!TryReadString(selected.RootElement, "evaluationKind", out var kind) ||
            !string.Equals(kind, "demand-specification-reconciliation", StringComparison.Ordinal))
        {
            return BadRequest("The requested validation evaluation is not a demand-specification reconciliation.");
        }

        return Ok(new
        {
            Evaluation = evaluation,
            Selection = JsonDocument.Parse(evaluation.SelectedParametersJson).RootElement.Clone(),
            Evidence = JsonDocument.Parse(evaluation.InclusionRulesJson).RootElement.Clone(),
            TrainingMetrics = JsonDocument.Parse(evaluation.TrainingMetricsJson).RootElement.Clone(),
            HoldoutMetrics = JsonDocument.Parse(evaluation.HoldoutMetricsJson).RootElement.Clone(),
            BenchmarkMetrics = JsonDocument.Parse(evaluation.BenchmarkMetricsJson).RootElement.Clone()
        });
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }
}
