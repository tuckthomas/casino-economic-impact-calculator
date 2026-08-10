// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public interface IModelRunService
{
    Task<ModelRun> FinalizeAsync(
        Guid modelRunId,
        ParameterResolutionRequest parameterRequest,
        CancellationToken cancellationToken = default);
}

public sealed class ModelRunService(
    AppDbContext db,
    IModelParameterService parameterService) : IModelRunService
{
    public async Task<ModelRun> FinalizeAsync(
        Guid modelRunId,
        ParameterResolutionRequest parameterRequest,
        CancellationToken cancellationToken = default)
    {
        // The snapshot and the immutable-set transition must observe one database
        // version so a concurrent calibration edit cannot leak into a finalized run.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var modelRun = await db.ModelRuns
            .SingleOrDefaultAsync(run => run.Id == modelRunId, cancellationToken)
            ?? throw new KeyNotFoundException($"Model run '{modelRunId}' was not found.");

        if (!string.Equals(modelRun.Status, ModelRunStatuses.Draft, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Only draft model runs can be finalized. Run '{modelRunId}' is '{modelRun.Status}'.");
        }
        if (!string.Equals(modelRun.ModelVersion, parameterRequest.ModelVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model run version '{modelRun.ModelVersion}' does not match parameter request version '{parameterRequest.ModelVersion}'.");
        }

        var existingSnapshot = await db.ModelRunParameterValues
            .AnyAsync(value => value.ModelRunId == modelRunId, cancellationToken);
        if (existingSnapshot)
        {
            throw new InvalidOperationException($"Model run '{modelRunId}' already contains a parameter snapshot.");
        }

        var datasetReferences = await db.ModelRunDatasetSnapshotReferences
            .Where(reference => reference.ModelRunId == modelRunId)
            .Join(
                db.DatasetSnapshots,
                reference => reference.DatasetSnapshotId,
                snapshot => snapshot.Id,
                (reference, snapshot) => new { Reference = reference, Snapshot = snapshot })
            .ToListAsync(cancellationToken);
        var requiredDatasetRoles = new[]
        {
            DatasetSnapshotRoles.OriginDemographics,
            DatasetSnapshotRoles.IncomeAgi,
            DatasetSnapshotRoles.Competitors,
            DatasetSnapshotRoles.ObservedPerformance
        };
        var missingDatasetRoles = requiredDatasetRoles
            .Where(role => datasetReferences.All(item => !string.Equals(item.Reference.Role, role, StringComparison.Ordinal)))
            .ToArray();
        if (missingDatasetRoles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Model run '{modelRunId}' is missing required immutable dataset snapshot role(s): {string.Join(", ", missingDatasetRoles)}.");
        }
        var unusableSnapshots = datasetReferences
            .Where(item => !item.Snapshot.IsSealed ||
                           item.Snapshot.ValidationState is DatasetValidationStates.Pending or DatasetValidationStates.Rejected)
            .Select(item => item.Snapshot.Id)
            .ToArray();
        if (unusableSnapshots.Length > 0)
        {
            throw new InvalidOperationException(
                $"Model run '{modelRunId}' references unsealed, pending, or rejected dataset snapshot(s): {string.Join(", ", unusableSnapshots)}.");
        }

        var resolved = await parameterService.ResolveAsync(parameterRequest, cancellationToken);
        db.ModelRunParameterValues.AddRange(resolved.Select(parameter => new ModelRunParameterValue
        {
            ModelRunId = modelRunId,
            ParameterDefinitionId = parameter.Definition.Id,
            SystemFallbackValue = parameter.SystemFallbackValue,
            DefaultValue = parameter.DefaultValue,
            ScenarioValue = parameter.ScenarioValue,
            UserOverrideValue = parameter.UserOverrideValue,
            FinalValue = parameter.FinalValue,
            SourceLayer = parameter.SourceLayer,
            IsOutsideRecommendedRange = parameter.IsOutsideRecommendedRange,
            WarningText = parameter.WarningText
        }));

        var selectedSets = new[]
        {
            (parameterRequest.NationalParameterSetId, "national-calibrated-set"),
            (parameterRequest.JurisdictionParameterSetId, "jurisdiction-market-set"),
            (parameterRequest.ScenarioParameterSetId, "scenario-preset")
        }.Where(selection => selection.Item1.HasValue)
         .Select(selection => (Id: selection.Item1!.Value, Layer: selection.Item2))
         .Distinct()
         .ToArray();

        if (selectedSets.Length > 0)
        {
            var selectedIds = selectedSets.Select(selection => selection.Id).Distinct().ToArray();
            var parameterSets = await db.ModelParameterSets
                .Where(set => selectedIds.Contains(set.Id))
                .ToListAsync(cancellationToken);
            if (parameterSets.Count != selectedIds.Length)
            {
                throw new InvalidOperationException("One or more selected parameter sets disappeared before finalization.");
            }

            foreach (var parameterSet in parameterSets)
            {
                parameterSet.IsImmutable = true;
            }
            db.ModelRunParameterSetReferences.AddRange(selectedSets.Select(selection => new ModelRunParameterSetReference
            {
                ModelRunId = modelRunId,
                ParameterSetId = selection.Id,
                SourceLayer = selection.Layer
            }));
        }

        // Persist child snapshots and lock the selected sets while the run is
        // still draft. Database triggers then make all of these rows immutable
        // once the second save transitions the run to finalized. The enclosing
        // transaction keeps both saves atomic.
        await db.SaveChangesAsync(cancellationToken);

        modelRun.Status = ModelRunStatuses.Finalized;
        modelRun.FinalizedAtUtc = DateTime.UtcNow;
        modelRun.BaseParameterSetId = parameterRequest.JurisdictionParameterSetId ?? parameterRequest.NationalParameterSetId;
        var resolvedInputs = JsonNode.Parse(modelRun.ResolvedInputJson) as JsonObject ?? new JsonObject();
        resolvedInputs["resolvedParameters"] = JsonSerializer.SerializeToNode(
            resolved.ToDictionary(parameter => parameter.Definition.Key, parameter => parameter.FinalValue, StringComparer.Ordinal));
        resolvedInputs["parameterSourceLayers"] = JsonSerializer.SerializeToNode(
            resolved.ToDictionary(parameter => parameter.Definition.Key, parameter => parameter.SourceLayer, StringComparer.Ordinal));
        var snapshotManifest = datasetReferences
            .GroupBy(item => item.Reference.Role, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    item => item.Reference.ReferenceKey,
                    item => item.Reference.DatasetSnapshotId,
                    StringComparer.Ordinal),
                StringComparer.Ordinal);
        resolvedInputs["datasetSnapshots"] = JsonSerializer.SerializeToNode(snapshotManifest);
        modelRun.ResolvedInputJson = resolvedInputs.ToJsonString();
        modelRun.DataSnapshotReferencesJson = JsonSerializer.Serialize(snapshotManifest);
        modelRun.WarningSummary = string.Join(
            " ",
            resolved.Where(parameter => parameter.WarningText is not null)
                .Select(parameter => parameter.WarningText)
                .Distinct(StringComparer.Ordinal));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return modelRun;
    }
}

public interface IModelParameterSetService
{
    Task<ModelParameterSet> CreateVersionAsync(
        long sourceParameterSetId,
        string newVersion,
        string? calibrationNotes = null,
        CancellationToken cancellationToken = default);

    Task<ModelParameterSetValue> SetValueAsync(
        long parameterSetId,
        string parameterKey,
        double value,
        string? provenanceNotes = null,
        CancellationToken cancellationToken = default);
}

public sealed class ModelParameterSetService(AppDbContext db) : IModelParameterSetService
{
    public async Task<ModelParameterSet> CreateVersionAsync(
        long sourceParameterSetId,
        string newVersion,
        string? calibrationNotes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newVersion))
        {
            throw new ArgumentException("A non-empty parameter-set version is required.", nameof(newVersion));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var source = await db.ModelParameterSets
            .SingleOrDefaultAsync(set => set.Id == sourceParameterSetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Parameter set '{sourceParameterSetId}' was not found.");
        var normalizedVersion = newVersion.Trim();
        if (await db.ModelParameterSets.AnyAsync(
                set => set.Key == source.Key && set.Version == normalizedVersion,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Parameter set '{source.Key}' already has version '{normalizedVersion}'.");
        }

        var clone = new ModelParameterSet
        {
            Key = source.Key,
            Name = source.Name,
            Scope = source.Scope,
            JurisdictionId = source.JurisdictionId,
            MarketCode = source.MarketCode,
            ScenarioKind = source.ScenarioKind,
            Version = normalizedVersion,
            ModelVersionApplicability = source.ModelVersionApplicability,
            IsImmutable = false,
            CalibrationNotes = calibrationNotes ?? $"Cloned from {source.Key} version {source.Version}."
        };
        db.ModelParameterSets.Add(clone);
        await db.SaveChangesAsync(cancellationToken);

        var sourceValues = await db.ModelParameterSetValues
            .Where(value => value.ParameterSetId == sourceParameterSetId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        db.ModelParameterSetValues.AddRange(sourceValues.Select(value => new ModelParameterSetValue
        {
            ParameterSetId = clone.Id,
            ParameterDefinitionId = value.ParameterDefinitionId,
            Value = value.Value,
            ProvenanceNotes = value.ProvenanceNotes
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return clone;
    }

    public async Task<ModelParameterSetValue> SetValueAsync(
        long parameterSetId,
        string parameterKey,
        double value,
        string? provenanceNotes = null,
        CancellationToken cancellationToken = default)
    {
        var parameterSet = await db.ModelParameterSets
            .SingleOrDefaultAsync(set => set.Id == parameterSetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Parameter set '{parameterSetId}' was not found.");
        if (parameterSet.IsImmutable || await db.ModelRunParameterSetReferences
                .AnyAsync(reference => reference.ParameterSetId == parameterSetId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Parameter set '{parameterSet.Key}' version '{parameterSet.Version}' is immutable. Create a new version instead.");
        }

        var definition = await db.ModelParameterDefinitions
            .SingleOrDefaultAsync(item => item.Key == parameterKey, cancellationToken)
            ?? throw new KeyNotFoundException($"Parameter definition '{parameterKey}' was not found.");
        if (!ModelParameterResolver.AppliesToModel(
                definition.ModelVersionApplicability,
                parameterSet.ModelVersionApplicability))
        {
            throw new InvalidOperationException(
                $"Parameter '{parameterKey}' applies to '{definition.ModelVersionApplicability}', not parameter set model '{parameterSet.ModelVersionApplicability}'.");
        }
        var selectedSetId = parameterSet.Scope switch
        {
            "national" => (National: (long?)parameterSetId, Jurisdiction: (long?)null, Scenario: (long?)null),
            "jurisdiction" or "market" => (National: (long?)null, Jurisdiction: (long?)parameterSetId, Scenario: (long?)null),
            "scenario" or "benchmark" or "experimental" => (National: (long?)null, Jurisdiction: (long?)null, Scenario: (long?)parameterSetId),
            _ => throw new InvalidOperationException($"Unsupported parameter-set scope '{parameterSet.Scope}'.")
        };
        ModelParameterResolver.Resolve(
            [definition],
            [new ModelParameterSetValue
            {
                ParameterSetId = parameterSetId,
                ParameterDefinitionId = definition.Id,
                Value = value
            }],
            new ParameterResolutionRequest(
                parameterSet.ModelVersionApplicability,
                selectedSetId.National,
                selectedSetId.Jurisdiction,
                selectedSetId.Scenario,
                null));

        var existing = await db.ModelParameterSetValues.SingleOrDefaultAsync(
            item => item.ParameterSetId == parameterSetId && item.ParameterDefinitionId == definition.Id,
            cancellationToken);
        if (existing is null)
        {
            existing = new ModelParameterSetValue
            {
                ParameterSetId = parameterSetId,
                ParameterDefinitionId = definition.Id
            };
            db.ModelParameterSetValues.Add(existing);
        }

        existing.Value = value;
        existing.ProvenanceNotes = provenanceNotes;
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
