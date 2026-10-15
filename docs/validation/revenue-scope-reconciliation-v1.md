# Matching revenue observations and predictions

The resident gravity component predicts gaming revenue from the protocol's selected origin population. A regulator's property total covers all patrons. Fitting the resident spending coefficient directly to that total can absorb omitted demand into resident spending. A percentage difference between these unmatched quantities is not a reconciled estimate of component accuracy.

New component calibration requires one of two explicit comparisons:

| Scope | Observation | Prediction |
|---|---|---|
| `modeled-origin-ggr` | Actual gaming revenue attributed to exactly the selected origins | Resident gravity allocation from those origins |
| `total-property-ggr` | Unchanged regulator-reported property gaming revenue | Resident allocation plus independently sourced, deduplicated tourism and traffic predictions |

Consultant forecasts, theoretical player value, patron counts and residency percentages do not become observed origin-attributed GGR. A missing component does not become zero, and a revenue shortfall does not become a predicted tourism input. Additional demand outside these components must be resolved in the independent coverage audit before declaring a complete total prediction.

## Evidence contract

The protocol declares `revenueComparisonScope`, `revenueScopeEvidencePath` and the raw-byte `revenueScopeEvidenceSha256`. Evidence and audit paths are relative and must remain within the protocol's directory. The evidence JSON uses `CalibrationRevenueScopeBundle` and `CalibrationRevenueScopeCase` from the shared validation service:

- Version `revenue-scope-v1`, observation year, origin/performance snapshot identities, and exactly the frozen case identities.
- Each case identifies its source URL, source-content hash, metric, basis, complete observation period and explicit `dollarYear`. Monetary values use the observed year's USD; both source and adjusted performance dollar years must match.
- `originSetSha256` binds the exact selected origin membership. It hashes the UTF-8 text of unique stable origin IDs sorted ordinally and joined by LF, without a trailing LF.
- Each case references a retained `scopeAuditPath` and its byte checksum. Source/audit hashes bind documents; checking them does not establish the truth or independence of their conclusions. Source review must verify demand coverage, exclusion of modeled residents, tourism/traffic overlap, units and exclusion of target-revenue leakage.
- Origin-only evidence uses source metric `land-based-ggr-usd`, basis `observed-origin-attributed-ggr`, and `observedOriginGgr`. The amount cannot exceed the unchanged property total. Visitor additions are excluded from this component comparison.
- Total-property evidence uses metric `nonresident-demand-inputs`, basis `independent-site-demand-coverage-audit`, and the existing `TourismDemandInput` / `TrafficInterceptInput` contracts. Direct observed-origin target adjustments and duplicate input identities are rejected. The existing shared demand services calculate both layers.

For total-property fitting, `prediction = expenditure * residentUnitPrediction + independentlyPredictedNonresidentGgr`. The nonresident term remains outside the fitted resident expenditure coefficient. The training-only WAPE optimum is the weighted median of `(observedTotal - fixedNonresidentPrediction) / residentUnitPrediction`, with resident-unit weights and the existing coefficient bounds. A negative residual remains a model overprediction; the case is not dropped. Selection outcomes do not enter this fit.

Missing scope evidence produces `not-scored-revenue-scope-unreconciled`, null accuracy metrics, no fitted expenditure, zero candidate evaluations and no holdout scoring. Explicit historical replay remains available and is labeled as an unreconciled arithmetic diagnostic. Legacy results and previously inspected property identities are preserved.

## What can still be learned without a component split

Under the existing nonnegative added-demand equations, let `R` be resident prediction, `N >= 0` omitted demand, and `Y` observed property total. Then `min_N |R + N - Y| = max(R - Y, 0)`. Consequently, a resident prediction above the property total proves an overprediction that adding tourism cannot fix. A resident prediction below the total does not identify the component error or a visitor forecast.

`audit-unreconciled-revenue-scope.mjs` retains those optimistic error bounds separately from unavailable point accuracy. A low bound is not a passing result. The latest frozen specification still has definite property-limit failures and its selection signed bias is already above the acceptance limit before any nonnegative visitor revenue is added.

The acceptance thresholds, independent holdout requirements and Northeast Indiana production scope are unchanged. Matching the quantities repairs validation mechanics; passing source, empirical, competitive-coverage and downstream acceptance checks remains required.
