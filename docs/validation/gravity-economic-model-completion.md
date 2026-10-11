# Gravity and economic model completion

Implementation tracking: [issue #38](https://github.com/tuckthomas/casino-economic-impact-calculator/issues/38), branch `codex/gravity-economic-model-completion`, draft [PR #39](https://github.com/tuckthomas/casino-economic-impact-calculator/pull/39). Deploy only on the user's explicit instruction. Keep the renovation gate in place.

The canonical requirements remain in `docs/plans/in-progress/sector_weighted_casino_displacement_model_and_revenue_projections_model/`. This ledger records implementation and verification evidence.

## Verified engineering work

- [x] Restore versioned location, active zones, population attribution and exposure assumptions between workflow stages. Reject missing, inconsistent or expired context.
- [x] Use active radius thresholds for drawing and allocation; describe miles and routed minutes correctly.
- [x] Retain fractional cases for costing and use the adult-population denominator.
- [x] Share domain-cost arithmetic between server runs and WebAssembly.
- [x] Share population projection arithmetic and preserve aggregate population without rounding individual points.
- [x] Carry population scenario year and percentage-to-share growth conversion into server inputs and scenario export/import. Keep the fiscal effective date independent.
- [x] Include population year in paired validation; reject pairs that use different years.
- [x] Include population year and fiscal date in report provenance. Report template `professional-v6` preserves prior artifact identities.
- [x] Capture host jurisdiction rule payloads and their version fingerprint in new run inputs; label reconstructed historical report fingerprints.
- [x] Persist each origin's computed eligible population and expose it with coordinates through the origins API and report JSON/CSV. Historical missing population remains null.
- [x] Validate migration 027 twice and run the transactional migration smoke test, including negative, infinite and NaN population rejection.

Latest verification: 276 .NET tests, 21 JavaScript tests, syntax and repository guards. Local synthetic browser fixture: 1,200 adults, 4.5 host cases, 0.775 spillover cases, $59,414/$10,232 rounded social costs; export preserved population year 2030, fiscal date 2026-10-11, and annual growth share 0.01839937614702425. Synthetic fixtures verify execution and handoff.

## Captured provider inputs

Captured 2026-10-11 through the existing provider harness for calendar year 2025:

| Input | Rows | Checksum |
|---|---:|---|
| Facilities | 69 | `bda3066fdb971c9cc3513f058b20282d807c60ba5c8395460b8d6c70d4399362` |
| Performance | 934 | `ca55a00c7c3fa024666aa59daaed4e48a62fbd9c4c08e9fbc875ee5fe26aae9c` |

Facility coverage: Illinois 17, Indiana 14, Michigan 27, Ohio 11. The retained bundle is [four-state-gaming-2025-captured-2026-10-11.json](bundles/four-state-gaming-2025-captured-2026-10-11.json), SHA-256 `0118ab5489982d889b81d584bc3dbf023d21fe65b1021b0ffa727c1f267abb75`.

Ingestion in `savenein_ui_validation_20261011_local` sealed facility snapshot `25995054-9ac0-47aa-9a00-7bcb16e818a7` and performance snapshot `e5b8ccbb-8ca1-4c46-8ff4-af57101db846`. Both retain validation state `warning`, including incomplete tribal performance/attributes. This database was created for this chat's validation work; the production database was not changed.

Live ingestion exposed and repaired an Indiana annual-report URL move and a Michigan property-name geocode failure. Indiana discovery uses the [regulator's annual reports page](https://www.in.gov/igc/publications/annual-reports/) with exact fiscal-year/trusted-host checks. Saganing uses the [operator-published street address](https://soaringeaglecasino.com/saganing-eagles-landing/saganing-eagles-landing-contact-us/).

## Remaining implementation and acceptance

- [ ] Drive the interactive economic visualization from sealed run inputs and outputs: eligible population, exposure response, nonoverlapping cost domains, gravity-derived GGR and effective-dated fiscal results.
- [ ] Carry all supported overrides through saving, reset/default behavior, comparison and sensitivity; reconcile the displayed values with immutable runs.
- [ ] Use authoritative stored-run reports throughout the public workflow, replacing the independent legacy revenue/tax/PDF calculation path.
- [ ] Complete origin, age and income inputs for real paired runs; finish block-group mode, versioned crosswalks, fractional boundary allocation and population control checks. Coordinate cross-border coverage with #32.
- [ ] Complete market-specific tourism/traffic, facility attributes, fiscal/employment/displacement inputs and defensible causal social-cost evidence. Preserve explicit missing-data warnings and zero-safe unsupported assumptions.
- [ ] Establish numerical release criteria for error, bias, property/market residuals and holdout coverage before further optimization. A user clarification is pending.
- [ ] Complete leakage-safe training, specification selection and untouched final holdout validation; accept calibrated defaults only after the declared criteria pass.
- [ ] Reconcile canonical task state and counters with verified evidence while preserving exact requirements.

The latest documented corrected-attribute calibration, evaluation `64f4ffc7-0e1f-47d2-80b4-3fb032d7cf80`, failed with 55.23% holdout MAPE and 90.90% SMAPE. Earlier checkpoints were 68.85% and 23.40% MAPE. The fresh capture above is preparation for new validation; no new forecasting-accuracy result is claimed.
