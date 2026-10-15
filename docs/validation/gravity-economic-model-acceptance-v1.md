# Gravity and economic model acceptance criteria v1

Adopted 2026-10-11 for [issue #38](https://github.com/tuckthomas/casino-economic-impact-calculator/issues/38), following the user's instruction to use recommended thresholds. These are project risk tolerances, not an industry standard or a claim of achieved accuracy. Freeze this policy and the evaluation manifest before the next specification search. All applicable gates must pass; missing evidence means acceptance is pending. Deployment still requires explicit user instruction.

## Revenue accuracy and bias

Evaluate the selected production specification on untouched, comparable annual property GGR observations. Use one property-year observation per case, equal property weights for MAPE, and observed-revenue weights for WAPE. Report each year separately when testing multiple years.

Let `e = predicted - observed`. All percentage metrics below use percent units, not fractions.

| Gate | Required result |
|---|---|
| Average property error | MAPE `mean(100 * abs(e) / observed)` <= 20% |
| Revenue-weighted error | WAPE `100 * sum(abs(e)) / sum(observed)` <= 15% |
| Overall signed bias | `abs(100 * sum(e) / sum(observed))` <= 5% |
| Property failures | Every property APE <= 40%; at least 90% of properties have APE <= 30% |
| Geographic failures | Each predeclared market and jurisdiction: WAPE <= 20% and absolute normalized signed bias <= 10% |
| Baseline improvement | WAPE at least 10% lower than a predeclared simple baseline fitted on training data only; if baseline WAPE is zero, model WAPE must also be zero |

These limits aim to make average error materially smaller than the historical failed results, constrain systematic overstatement/understatement, and prevent cancellation or a few large properties from hiding failures. They do not guarantee that an individual prediction is within 20%.

Retain MAE, RMSE, SMAPE, Spearman rank correlation, signed residuals and property/market/jurisdiction tables as diagnostics. SMAPE alone cannot grant acceptance. The server's existing `Bias` is mean error in dollars; the normalized signed bias above is a separate calculation. Zero-observed cases remain in MAE, RMSE and aggregate metrics, with undefined APE disclosed. They do not count toward the minimum positive-revenue holdout sample; no near-zero case may be dropped after seeing predictions.

## Holdout coverage and leakage prevention

- Final holdout must contain at least `max(12, ceiling(0.20 * N))` distinct properties, where `N` is the frozen eligible property pool, across at least three predeclared competitive markets and two jurisdictions. Each market and each jurisdiction claimed as validated needs at least three distinct holdout properties. Additional property-years do not increase the distinct-property count.
- Freeze eligibility, exclusions, property IDs, market membership, partitions, observation periods, data checksums, baseline and policy version before optimization. Previously inspected holdouts are development evidence, not fresh final holdouts. If no fresh independent sample is available, acceptance stays pending.
- Use separate training, specification-selection and final holdout partitions. Keep all records for a held-out property out of fitting/selection, and reserve at least one entire competitive market from both training and selection. Respect time ordering for future-year claims.
- Held-out target-period GGR cannot appear in its attraction inputs, comparable predictors, productivity ratios, parameter selection or ensemble weights. Reconstruct the held-out target from inputs that would have been available when making the forecast.
- Use compatible observed revenue definitions and complete comparable periods. Incomplete seasons, assumed proposal counts and synthetic cases cannot satisfy empirical holdout coverage. Lock baseline predictions before final holdout scoring; include a prior-period baseline when making future-year claims and those observations are available.
- A failed property, route or data join must remain visible. Do not replace cases, omit failed cases or redraw partitions to obtain a pass. Subsequent remediation needs a new versioned evaluation; an inspected final holdout cannot be reused as untouched evidence.

## Input completeness, reconciliation and scope

- Include 100% of known operating competitors in the declared study region and route every retained demand origin to the candidate and every applicable competitor. Document genuine inaccessibility separately from routing failures. Missing material competition prevents acceptance for that market.
- Resolve required geography, gaming-age population and income for at least 99% of the study area's eligible population, measured against an independently defined denominator. Report missing population, source years and county/state control differences. Population coverage cannot be inferred from the percentage of origin rows retained. Fiscal rules, required target attributes and holdout observations need complete, validated evidence for every acceptance case.
- Re-run frozen inputs and model versions with matching input/checksum/rule provenance. Unrounded numeric outputs must agree within relative tolerance `1e-10` with absolute floor `1e-8`; displayed currency must agree to the cent across UI, API, saved run and reports. Accounting totals and geographic allocations must reconcile within those same tolerances. Record explicit tolerances in the evaluation manifest.
- Passing incumbent GGR gates validates only the declared revenue scope. It does not validate causal displacement, tourism, employment or incremental gambling harm. Those require their own evidence and acceptance checks; unsupported assumptions remain disclosed and block claims about unsupported outcomes. Unsupported jurisdictions are not included in the validated scope.

## Implementation status

- [x] Define and record thresholds before a new calibration search.
- [x] Add a versioned acceptance policy/evidence contract to both existing evaluation workflows; calculate WAPE and normalized bias alongside existing metrics and persist the assessment/evidence in immutable inclusion JSON.
- [x] Enforce numerical limits, sample coverage, stable property identity across snapshots, prior-evaluation freshness, pre-search timestamps and required completeness/leakage/reproducibility evidence before publication. Missing evidence blocks publication; legacy ensemble sets without a passing assessment cannot execute as validated ensembles.
- [ ] Supply fresh holdouts, run the frozen evaluation, and verify reproducibility and downstream evidence.

The evidence contract records the frozen eligible pool, baseline predictions, independent population controls, competitor coverage, failed-case count and audit checksums. Required-input, target-leakage and reproducibility verification flags must be backed by the referenced independent audits; supplying a flag or a checksum does not perform an audit. The code checks the evidence contract and measured acceptance metrics, while independently verifying those source/audit artifacts remains part of the unchecked evidence task.

Incumbent calibration now scores candidate specifications on a separate selection partition when supplied, executes final holdouts only after choosing a candidate, and excludes selection/holdout target-period GGR from observed competitor mass without removing their structural competition. Historical training-only protocols remain usable for development diagnostics and cannot pass release acceptance.

Historical evaluation `64f4ffc7-0e1f-47d2-80b4-3fb032d7cf80` reports 55.23% holdout MAPE and therefore fails v1. Its other unmeasured gates are pending, not assumed passed. Defining this policy does not change that evaluation or publish defaults.

Methodology: [Hyndman and Athanasopoulos, evaluating point forecast accuracy](https://otexts.com/fpp3/toolbox.html#evaluating-point-forecast-accuracy) explains held-out evaluation and percentage-error limitations. [scikit-learn's grouped cross-validation documentation](https://scikit-learn.org/stable/modules/cross_validation.html#group-k-fold) explains keeping dependent groups separate. These sources support evaluation design; the numerical tolerances above are our project judgment.
