// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Validation;

public sealed record GeographicResidualObservation(
    string CaseKey,
    string DatasetPartition,
    string MarketCode,
    string JurisdictionCode,
    string? HoldoutGroup,
    double Observed,
    double Predicted);

public sealed record GeographicResidualPattern(
    string DatasetPartition,
    string GeographyKind,
    string GeographyCode,
    int ObservationCount,
    double ObservedRevenue,
    double PredictedRevenue,
    double Residual,
    double MeanResidual,
    double MeanAbsoluteError,
    double? MeanAbsolutePercentageError,
    double SymmetricMeanAbsolutePercentageError,
    int OverpredictionCount,
    int UnderpredictionCount,
    int ExactPredictionCount);

public interface IGeographicResidualPatternService
{
    IReadOnlyCollection<GeographicResidualPattern> Calculate(
        IReadOnlyCollection<GeographicResidualObservation> observations);
}

public sealed class GeographicResidualPatternService(
    IValidationMetricsService metricsService) : IGeographicResidualPatternService
{
    private const double ExactResidualTolerance = 1e-9;

    public IReadOnlyCollection<GeographicResidualPattern> Calculate(
        IReadOnlyCollection<GeographicResidualObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            throw new ArgumentException("At least one geographic residual observation is required.", nameof(observations));
        }

        var sample = observations.ToArray();
        if (sample.Any(observation =>
                string.IsNullOrWhiteSpace(observation.CaseKey) ||
                string.IsNullOrWhiteSpace(observation.DatasetPartition) ||
                string.IsNullOrWhiteSpace(observation.MarketCode) ||
                string.IsNullOrWhiteSpace(observation.JurisdictionCode) ||
                !double.IsFinite(observation.Observed) || observation.Observed < 0 ||
                !double.IsFinite(observation.Predicted) || observation.Predicted < 0))
        {
            throw new ArgumentException(
                "Geographic residual observations require case, partition, market, jurisdiction, and finite nonnegative revenue.",
                nameof(observations));
        }
        if (sample.Select(observation => observation.CaseKey)
            .Distinct(StringComparer.Ordinal).Count() != sample.Length)
        {
            throw new ArgumentException(
                "Geographic residual case keys must be unique within one prediction evaluation.",
                nameof(observations));
        }

        var memberships = sample.SelectMany(observation => Memberships(observation)).ToArray();
        return memberships
            .GroupBy(
                membership => new
                {
                    membership.Observation.DatasetPartition,
                    membership.GeographyKind,
                    membership.GeographyCode
                })
            .Select(group => BuildPattern(
                group.Key.DatasetPartition,
                group.Key.GeographyKind,
                group.Key.GeographyCode,
                group.Select(item => item.Observation).ToArray()))
            .OrderBy(pattern => pattern.DatasetPartition, StringComparer.Ordinal)
            .ThenBy(pattern => pattern.GeographyKind, StringComparer.Ordinal)
            .ThenBy(pattern => pattern.GeographyCode, StringComparer.Ordinal)
            .ToArray();
    }

    private GeographicResidualPattern BuildPattern(
        string partition,
        string geographyKind,
        string geographyCode,
        IReadOnlyCollection<GeographicResidualObservation> observations)
    {
        var metrics = metricsService.Calculate(observations.Select(observation =>
            new ValidationObservation(
                observation.CaseKey,
                observation.Observed,
                observation.Predicted)).ToArray());
        var errors = observations
            .Select(observation => observation.Predicted - observation.Observed)
            .ToArray();
        return new GeographicResidualPattern(
            partition,
            geographyKind,
            geographyCode,
            observations.Count,
            observations.Sum(observation => observation.Observed),
            observations.Sum(observation => observation.Predicted),
            errors.Sum(),
            metrics.Bias,
            metrics.MeanAbsoluteError,
            metrics.MeanAbsolutePercentageError,
            metrics.SymmetricMeanAbsolutePercentageError,
            errors.Count(error => error > ExactResidualTolerance),
            errors.Count(error => error < -ExactResidualTolerance),
            errors.Count(error => Math.Abs(error) <= ExactResidualTolerance));
    }

    private static IEnumerable<GeographicMembership> Memberships(
        GeographicResidualObservation observation)
    {
        yield return new GeographicMembership(
            observation,
            ValidationGeographyKinds.Market,
            observation.MarketCode.Trim());
        yield return new GeographicMembership(
            observation,
            ValidationGeographyKinds.Jurisdiction,
            observation.JurisdictionCode.Trim());
        if (!string.IsNullOrWhiteSpace(observation.HoldoutGroup))
        {
            yield return new GeographicMembership(
                observation,
                ValidationGeographyKinds.HoldoutGroup,
                observation.HoldoutGroup.Trim());
        }
    }

    private sealed record GeographicMembership(
        GeographicResidualObservation Observation,
        string GeographyKind,
        string GeographyCode);
}
