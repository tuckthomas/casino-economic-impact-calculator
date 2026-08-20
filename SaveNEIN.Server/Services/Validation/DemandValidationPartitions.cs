// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

namespace SaveNEIN.Server.Services.Validation;

/// <summary>
/// Demand-specification governance uses an explicit selection partition between
/// calibration/training and the untouched final holdout. Existing generic
/// validation partitions remain unchanged for backward compatibility.
/// </summary>
public static class DemandValidationPartitions
{
    public const string Selection = "selection";
}
