// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/development-programs")]
public sealed class DevelopmentProgramsController(
    AppDbContext db,
    IDevelopmentProgramService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken)
    {
        var program = await service.CreateAsync(definition, cancellationToken);
        return CreatedAtAction(nameof(Get), new { developmentProgramId = program.Id }, program);
    }

    [HttpPost("{sourceProgramId:guid}/versions")]
    public async Task<IActionResult> CreateVersion(
        Guid sourceProgramId,
        [FromBody] DevelopmentProgramDefinition definition,
        CancellationToken cancellationToken)
    {
        var program = await service.CreateVersionAsync(sourceProgramId, definition, cancellationToken);
        return CreatedAtAction(nameof(Get), new { developmentProgramId = program.Id }, program);
    }

    [HttpGet("{developmentProgramId:guid}")]
    public async Task<IActionResult> Get(Guid developmentProgramId, CancellationToken cancellationToken)
    {
        var program = await db.DevelopmentPrograms
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == developmentProgramId, cancellationToken);
        return program is null ? NotFound() : Ok(program);
    }
}
