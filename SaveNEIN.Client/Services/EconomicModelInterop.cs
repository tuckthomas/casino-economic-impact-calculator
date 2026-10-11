// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.JSInterop;
using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Client.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "JavaScript invokes this public WebAssembly entry point.")]
public static class EconomicModelInterop
{
    [JSInvokable("CalculateSocialCosts")]
    public static SocialCostTotals CalculateSocialCosts(SocialCostCalculationRequest request) =>
        SocialCostArithmetic.Calculate(request);
}
