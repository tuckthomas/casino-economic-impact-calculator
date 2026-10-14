// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Validation;

public sealed record DemographicCaptureEnvelope(double Latitude, double Longitude, double RadiusMiles);
public sealed record DemographicCaptureLocation(string Key, double Latitude, double Longitude);
public sealed record DemographicCaptureScope(DemographicCaptureLocation[] Scenarios, double OriginRadiusMiles,
    string? JurisdictionCode = null, string? FacilityRegime = null, DateOnly FiscalDate = default, int? EligibleAge = null)
{
    public void Validate()
    {
        if (Scenarios is not { Length: > 0 } || Scenarios.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != Scenarios.Length ||
            Scenarios.Any(item => string.IsNullOrWhiteSpace(item.Key) || !double.IsFinite(item.Latitude) || !double.IsFinite(item.Longitude) ||
                item.Latitude is < -90 or > 90 || item.Longitude is < -180 or > 180) ||
            !double.IsFinite(OriginRadiusMiles) || OriginRadiusMiles is <= 0 or > 500)
            throw new InvalidDataException("Demographic capture requires distinct, valid locations and a finite declared study radius.");
        if (EligibleAge is { } age ? age is < 18 or > 25 :
            string.IsNullOrWhiteSpace(JurisdictionCode) || string.IsNullOrWhiteSpace(FacilityRegime) || FiscalDate == default)
            throw new InvalidDataException("Demographic capture requires an explicit age threshold or an effective jurisdiction/regime/date.");
    }

    public IReadOnlyList<DemographicCaptureEnvelope> ProviderEnvelopes()
    {
        Validate();
        var latitude = Scenarios.Average(item => item.Latitude);
        var longitude = Scenarios.Average(item => item.Longitude);
        var broadRadius = OriginRadiusMiles + 100 * Scenarios.Max(item => Math.Abs(item.Latitude - latitude) + Math.Abs(item.Longitude - longitude));
        if (broadRadius <= 500) return [new(latitude, longitude, broadRadius)];
        var remaining = Scenarios.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
        var envelopes = new List<DemographicCaptureEnvelope>();
        while (remaining.Count > 0)
        {
            var anchor = remaining[0];
            envelopes.Add(new(anchor.Latitude, anchor.Longitude, 500));
            remaining.RemoveAll(item => Gravity.CompetitiveUniverseService.IsWithinBroadPrefilter(
                item.Latitude, item.Longitude, anchor.Latitude, anchor.Longitude, 500 - OriginRadiusMiles));
        }
        return envelopes;
    }
}
