using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public sealed record ParameterOverride(string Key, double Value);

public sealed record ParameterResolutionRequest(
    string ModelVersion,
    long? NationalParameterSetId,
    long? JurisdictionParameterSetId,
    long? ScenarioParameterSetId,
    IReadOnlyCollection<ParameterOverride>? UserOverrides);

public sealed record ResolvedModelParameter(
    ModelParameterDefinition Definition,
    double SystemFallbackValue,
    double DefaultValue,
    double? ScenarioValue,
    double? UserOverrideValue,
    double FinalValue,
    string SourceLayer,
    bool IsOutsideRecommendedRange,
    string? WarningText);

public interface IModelParameterService
{
    Task<IReadOnlyList<ResolvedModelParameter>> ResolveAsync(
        ParameterResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ModelParameterService(AppDbContext db) : IModelParameterService
{
    public async Task<IReadOnlyList<ResolvedModelParameter>> ResolveAsync(
        ParameterResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelVersion))
        {
            throw new ArgumentException("A model version is required.", nameof(request));
        }

        var definitions = await db.ModelParameterDefinitions
            .Where(definition => definition.IsActive &&
                                 (definition.ModelVersionApplicability == request.ModelVersion ||
                                  definition.ModelVersionApplicability == "all"))
            .OrderBy(definition => definition.Key)
            .ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException($"No active parameter definitions exist for model version '{request.ModelVersion}'.");
        }

        var selectedSets = new[]
        {
            new SelectedParameterSet(request.NationalParameterSetId, "national-calibrated-set", ["national"]),
            new SelectedParameterSet(request.JurisdictionParameterSetId, "jurisdiction-market-set", ["jurisdiction", "market"]),
            new SelectedParameterSet(request.ScenarioParameterSetId, "scenario-preset", ["scenario", "benchmark", "experimental"])
        };
        var setIds = selectedSets
            .Where(selection => selection.Id.HasValue)
            .Select(selection => selection.Id!.Value)
            .Distinct()
            .ToArray();

        var parameterSets = setIds.Length == 0
            ? []
            : await db.ModelParameterSets
                .Where(set => setIds.Contains(set.Id))
                .ToListAsync(cancellationToken);
        ValidateSelectedSets(selectedSets, parameterSets, request.ModelVersion);

        var values = setIds.Length == 0
            ? []
            : await db.ModelParameterSetValues
                .Where(value => setIds.Contains(value.ParameterSetId))
                .ToListAsync(cancellationToken);

        return ModelParameterResolver.Resolve(definitions, values, request);
    }

    private static void ValidateSelectedSets(
        IEnumerable<SelectedParameterSet> selectedSets,
        IReadOnlyCollection<ModelParameterSet> parameterSets,
        string modelVersion)
    {
        var byId = parameterSets.ToDictionary(set => set.Id);
        foreach (var selection in selectedSets.Where(selection => selection.Id.HasValue))
        {
            var id = selection.Id!.Value;
            if (!byId.TryGetValue(id, out var parameterSet))
            {
                throw new KeyNotFoundException($"Parameter set '{id}' was not found.");
            }

            if (!selection.AllowedScopes.Contains(parameterSet.Scope, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Parameter set '{parameterSet.Key}' has scope '{parameterSet.Scope}' and cannot be used as the {selection.SourceLayer} layer.");
            }
            if (!ModelParameterResolver.AppliesToModel(parameterSet.ModelVersionApplicability, modelVersion))
            {
                throw new InvalidOperationException(
                    $"Parameter set '{parameterSet.Key}' applies to model version '{parameterSet.ModelVersionApplicability}', not '{modelVersion}'.");
            }
        }
    }

    private sealed record SelectedParameterSet(long? Id, string SourceLayer, string[] AllowedScopes);
}

public static class ModelParameterResolver
{
    internal static bool AppliesToModel(string applicability, string modelVersion) =>
        string.Equals(applicability, "all", StringComparison.Ordinal) ||
        string.Equals(modelVersion, "all", StringComparison.Ordinal) ||
        string.Equals(applicability, modelVersion, StringComparison.Ordinal);

    public static IReadOnlyList<ResolvedModelParameter> Resolve(
        IReadOnlyCollection<ModelParameterDefinition> definitions,
        IReadOnlyCollection<ModelParameterSetValue> parameterSetValues,
        ParameterResolutionRequest request)
    {
        var duplicateDefinition = definitions
            .GroupBy(definition => definition.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDefinition is not null)
        {
            throw new InvalidOperationException($"Duplicate parameter definition key '{duplicateDefinition.Key}'.");
        }

        var duplicateOverride = (request.UserOverrides ?? [])
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOverride is not null)
        {
            throw new InvalidOperationException($"Duplicate user override key '{duplicateOverride.Key}'.");
        }

        var definitionsByKey = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
        var overrides = (request.UserOverrides ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var unknownOverrides = overrides.Keys.Where(key => !definitionsByKey.ContainsKey(key)).OrderBy(key => key).ToArray();
        if (unknownOverrides.Length > 0)
        {
            throw new KeyNotFoundException($"Unknown parameter override(s): {string.Join(", ", unknownOverrides)}.");
        }

        var duplicateSetValue = parameterSetValues
            .GroupBy(value => (value.ParameterSetId, value.ParameterDefinitionId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSetValue is not null)
        {
            throw new InvalidOperationException(
                $"Parameter set '{duplicateSetValue.Key.ParameterSetId}' contains duplicate values for definition '{duplicateSetValue.Key.ParameterDefinitionId}'.");
        }

        var valuesBySetAndDefinition = parameterSetValues.ToDictionary(
            value => (value.ParameterSetId, value.ParameterDefinitionId),
            value => value.Value);
        var resolved = new List<ResolvedModelParameter>(definitions.Count);

        foreach (var definition in definitions.OrderBy(definition => definition.Key, StringComparer.Ordinal))
        {
            var systemFallbackValue = definition.SystemDefaultValue;
            ValidateComputationalBounds(definition, systemFallbackValue, "system fallback");

            var defaultValue = systemFallbackValue;
            var finalValue = systemFallbackValue;
            var sourceLayer = "system-fallback";
            double? scenarioValue = null;

            Apply(request.NationalParameterSetId, "national-calibrated-set", contributesToDefault: true);
            Apply(request.JurisdictionParameterSetId, "jurisdiction-market-set", contributesToDefault: true);
            Apply(request.ScenarioParameterSetId, "scenario-preset", captureScenario: true);

            double? overrideValue = null;
            if (overrides.TryGetValue(definition.Key, out var requestedOverride))
            {
                if (!definition.IsUserOverridable)
                {
                    throw new InvalidOperationException($"Parameter '{definition.Key}' cannot be overridden.");
                }

                ValidateComputationalBounds(definition, requestedOverride, "user override");
                overrideValue = requestedOverride;
                finalValue = requestedOverride;
                sourceLayer = "user-override";
            }

            var outsideRange =
                definition.RecommendedMinimum is { } recommendedMinimum && finalValue < recommendedMinimum ||
                definition.RecommendedMaximum is { } recommendedMaximum && finalValue > recommendedMaximum;
            var warnings = new List<string>();
            if (!definition.IsCalibrated)
            {
                warnings.Add($"{definition.DisplayName} does not have a completed calibration designation.");
            }
            if (outsideRange)
            {
                warnings.Add($"{definition.DisplayName} is outside its validated/recommended range.");
            }

            resolved.Add(new ResolvedModelParameter(
                definition,
                systemFallbackValue,
                defaultValue,
                scenarioValue,
                overrideValue,
                finalValue,
                sourceLayer,
                outsideRange,
                warnings.Count == 0 ? null : string.Join(" ", warnings)));

            void Apply(
                long? parameterSetId,
                string layer,
                bool contributesToDefault = false,
                bool captureScenario = false)
            {
                if (parameterSetId is not { } setId ||
                    !valuesBySetAndDefinition.TryGetValue((setId, definition.Id), out var setValue))
                {
                    return;
                }

                ValidateComputationalBounds(definition, setValue, layer);
                finalValue = setValue;
                sourceLayer = layer;
                if (contributesToDefault)
                {
                    defaultValue = setValue;
                }
                if (captureScenario)
                {
                    scenarioValue = setValue;
                }
            }
        }

        return resolved;
    }

    private static void ValidateComputationalBounds(
        ModelParameterDefinition definition,
        double value,
        string source)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(definition.Key, value, $"Parameter '{definition.Key}' from {source} must be finite.");
        }

        if (definition.ComputationalMinimum is { } minimum && value < minimum ||
            definition.ComputationalMaximum is { } maximum && value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                definition.Key,
                value,
                $"Parameter '{definition.Key}' from {source} is outside its computational safety bounds.");
        }
    }
}
