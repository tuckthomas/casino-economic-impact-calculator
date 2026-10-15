// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Shared.Modeling;

public sealed record FacilityInputAvailability(
    string Key, string DisplayName, string CoefficientKey, string[] ParameterKeys,
    bool Available, string[] MissingFacilityKeys, string? UnavailableReason);

public sealed record FacilityInputAvailabilityRequest(
    Guid DevelopmentProgramId, Guid CompetitorSnapshotId, Guid OriginGeographySnapshotId,
    string[] StableOriginIds, DateOnly EffectiveOn, string AttractionSpecification,
    double CompetitorPrefilterMiles = 300, int[]? CompetitorIds = null, int[]? ExcludedCompetitorIds = null);
