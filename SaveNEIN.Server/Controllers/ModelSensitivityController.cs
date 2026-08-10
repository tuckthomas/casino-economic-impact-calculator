// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/model-sensitivity")]
public sealed class ModelSensitivityController(
    AppDbContext db,
    ISensitivityAnalysisService sensitivityAnalysisService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int take = 20, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100)
        {
            return BadRequest("take must be between 1 and 100.");
        }
        return Ok(await db.SensitivityAnalyses.AsNoTracking()
            .OrderByDescending(analysis => analysis.CreatedAtUtc)
            .Take(take)
            .Select(analysis => new
            {
                analysis.Id,
                analysis.AnalysisKey,
                analysis.Version,
                analysis.Name,
                analysis.BaselineModelRunId,
                analysis.OutputMetric,
                analysis.BaselineMetricValue,
                analysis.Status,
                analysis.CreatedAtUtc,
                analysis.FinalizedAtUtc,
                analysis.ErrorSummary
            })
            .ToListAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<SensitivityAnalysisResult>> Execute(
        [FromBody] SensitivityAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sensitivityAnalysisService.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { sensitivityAnalysisId = result.SensitivityAnalysisId }, result);
    }

    [HttpGet("{sensitivityAnalysisId:guid}")]
    public async Task<ActionResult<SensitivityAnalysisResult>> Get(
        Guid sensitivityAnalysisId,
        CancellationToken cancellationToken)
    {
        var result = await sensitivityAnalysisService.GetAsync(sensitivityAnalysisId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
