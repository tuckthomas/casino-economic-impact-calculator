// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record DemandSpecificationRunPairResult(
    GravityModelRunResult AgiShare,
    GravityModelRunResult EligibleAdultPerCapita);

public interface IDemandSpecificationRunPairService
{
    Task<DemandSpecificationRunPairResult> ExecuteAsync(
        GravityModelRunRequest baseRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes the two alternative resident-demand specifications through the same
/// authoritative gravity pipeline. Every scenario input other than the demand
/// specification and audit-only scenario label is held constant.
/// </summary>
public sealed class DemandSpecificationRunPairService(
    IGravityModelExecutionService gravityModel) : IDemandSpecificationRunPairService
{
    public async Task<DemandSpecificationRunPairResult> ExecuteAsync(
        GravityModelRunRequest baseRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseRequest);

        var baseName = string.IsNullOrWhiteSpace(baseRequest.ScenarioName)
            ? "Demand specification validation"
            : baseRequest.ScenarioName.Trim();
        var agiRequest = baseRequest with
        {
            ScenarioName = $"{baseName} [AGI-share validation]",
            DemandSpecification = GravityDemandSpecifications.AgiShare
        };
        var perCapitaRequest = baseRequest with
        {
            ScenarioName = $"{baseName} [eligible-adult validation]",
            DemandSpecification = GravityDemandSpecifications.EligibleAdultPerCapita
        };

        // Run sequentially so shared route caches may be reused without allowing
        // concurrent writes to obscure the immutable evidence for either run.
        var agi = await gravityModel.ExecuteAsync(agiRequest, cancellationToken);
        var perCapita = await gravityModel.ExecuteAsync(perCapitaRequest, cancellationToken);
        return new DemandSpecificationRunPairResult(agi, perCapita);
    }
}
