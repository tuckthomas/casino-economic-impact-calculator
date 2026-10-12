# Gravity and economic model completion

Implementation tracking: [issue #38](https://github.com/tuckthomas/casino-economic-impact-calculator/issues/38), branch `codex/gravity-economic-model-completion`, draft [PR #39](https://github.com/tuckthomas/casino-economic-impact-calculator/pull/39). Deploy only on the user's explicit instruction. The user requested disabling the renovation gate on this branch; commit `dc942a5` implements that change without deployment.

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

Prior verification at `798ba48`: 276 .NET tests, 21 JavaScript tests, syntax and repository guards. Local synthetic browser fixture: 1,200 adults, 4.5 host cases, 0.775 spillover cases, $59,414/$10,232 rounded social costs; export preserved population year 2030, fiscal date 2026-10-11, and annual growth share 0.01839937614702425. Synthetic fixtures verify execution and handoff.

Acceptance-workflow verification: **295 .NET tests pass**; the harness builds with zero warnings/errors and repository guards pass. Routing now reads caches without tracking and detaches only newly persisted route/cache rows; tests preserve exact-coordinate reuse, route IDs, rejected legacy identities and caller run state. This bounds tracker growth during repeated calibration and matrix execution.

## Captured provider inputs

Captured 2026-10-11 through the existing provider harness for calendar year 2025:

| Input | Rows | Checksum |
|---|---:|---|
| Facilities | 69 | `bda3066fdb971c9cc3513f058b20282d807c60ba5c8395460b8d6c70d4399362` |
| Performance | 934 | `ca55a00c7c3fa024666aa59daaed4e48a62fbd9c4c08e9fbc875ee5fe26aae9c` |

Facility coverage: Illinois 17, Indiana 14, Michigan 27, Ohio 11. The retained bundle is [four-state-gaming-2025-captured-2026-10-11.json](bundles/four-state-gaming-2025-captured-2026-10-11.json), SHA-256 `0118ab5489982d889b81d584bc3dbf023d21fe65b1021b0ffa727c1f267abb75`.

Ingestion in `savenein_ui_validation_20261011_local` sealed facility snapshot `25995054-9ac0-47aa-9a00-7bcb16e818a7` and performance snapshot `e5b8ccbb-8ca1-4c46-8ff4-af57101db846`. Both retain validation state `warning`, including incomplete tribal performance/attributes. This database was created for this chat's validation work; the production database was not changed.

Live ingestion exposed and repaired an Indiana annual-report URL move and a Michigan property-name geocode failure. Indiana discovery uses the [regulator's annual reports page](https://www.in.gov/igc/publications/annual-reports/) with exact fiscal-year/trusted-host checks. Saganing uses the [operator-published street address](https://soaringeaglecasino.com/saganing-eagles-landing/saganing-eagles-landing-contact-us/).

## Independent study-area population coverage

[Coverage evidence](bundles/fort-wayne-income-coverage-2026-10-11.json), SHA-256 `3f4157ad46f200b0731b1777988d083ffd9cf1041c8b75639c8818c1059005de`, measures all 1,085 Census 2020 study-area ZCTAs against ACS 2022 age controls, using the validated Indiana commercial gaming age of 21. The 907 origins with sealed IRS income cover 7,220,748 of 7,254,701 eligible adults: **99.531986%**. The 178 omitted origins account for 33,953 eligible adults. This passes the 99% income-coverage gate for this declared study area; it does not prove county/state conservation, complete competitive coverage, model accuracy or coverage around other candidate sites.

## Real-input execution and report verification

Dedicated-database run `21211b39-5656-4cc2-abf2-20b6799e3e3b` finalized 907 origins, 68 incumbents and 124,259 baseline/with-project allocation rows. Report generation initially exposed a harness-only QuestPDF initialization ordering error; moving the existing application license setting before early CLI modes generated artifact `bc7a02b4-1dbd-4a36-83e8-baf37d1c453a`, template `professional-v6`, PDF SHA-256 `9688fbd8c8355a6d5205647e0e60ca3b0aaa52edc586a9292e9dd1775df34020`, without recalculating that run.

Cached run `1c50a3da-9f25-464e-a5b6-68109e264571` under the updated routing code reproduced the economic values: 7,819,035.391450547 projected eligible adults and $3,392,665.53 GGR. The assumed program values and sealed data were identical, while new run/program identities and report artifact identities differ. Execution took 57.127 seconds with cached routes versus 20 minutes 27.690 seconds for the earlier partly cold route-cache run; this comparison includes cache warming and is not an isolated performance benchmark. Its report artifact is `9913d54a-3dae-4722-a524-f56b500eafba`, PDF SHA-256 `6ddee29018cad33c296f046863be2793d653b763497323d4cbb699d5d49f77ec`.

Both runs use an explicitly assumed program (2,000 slots, 50 tables, 200 hotel rooms, 60,000 square feet, five venues), population scenario year 2030 and stored fiscal date 2026-10-12 (UTC-derived harness default). These are execution/replay checks, not sourced proposal forecasts, independent incumbent holdouts or empirical accuracy acceptance. No calibrated parameter set was published. The earlier interrupted attempt remains draft and is not acceptance evidence.

## Remaining implementation and acceptance

- [ ] Drive the interactive economic visualization from sealed run inputs and outputs: eligible population, exposure response, nonoverlapping cost domains, gravity-derived GGR and effective-dated fiscal results.
- [ ] Carry all supported overrides through saving, reset/default behavior, comparison and sensitivity; reconcile the displayed values with immutable runs.
- [ ] Use authoritative stored-run reports throughout the public workflow, replacing the independent legacy revenue/tax/PDF calculation path.
- [ ] Complete origin, age and income inputs for real paired runs; finish block-group mode, versioned crosswalks, fractional boundary allocation and population control checks. Coordinate cross-border coverage with #32.
- [ ] Complete market-specific tourism/traffic, facility attributes, fiscal/employment/displacement inputs and defensible causal social-cost evidence. Preserve explicit missing-data warnings and zero-safe unsupported assumptions.
- [x] Establish numerical release criteria for error, bias, property/market residuals and holdout coverage before further optimization. The user authorized recommended thresholds on 2026-10-11; [acceptance policy v1](gravity-economic-model-acceptance-v1.md) records the limits, metric definitions and required evidence.
- [x] Implement versioned acceptance-policy enforcement in both evaluation publication paths: WAPE, normalized bias, property/geographic/year failures, independent holdout coverage and required input/reproducibility evidence. Persist pass/fail reasons before publishing defaults; reject legacy ensemble assessments without a passing policy report.
- [x] Separate training, specification selection and final-holdout execution in incumbent calibration. Execute the holdout only for the selected candidate. Withhold selection/holdout GGR from competitor attraction inputs while retaining structural competitors, and fingerprint the exclusions in paired evaluations.
- [ ] Supply independently reviewed, predeclared acceptance evidence and fresh multi-market holdouts. Audit checksums and verification flags identify required external evidence; statistical tests cannot establish source correctness or causal validity by themselves.
- [ ] Complete leakage-safe training, specification selection and untouched final holdout validation; accept calibrated defaults only after the declared criteria pass.
- [ ] Reconcile canonical task state and counters with verified evidence while preserving exact requirements.

The latest documented corrected-attribute calibration, evaluation `64f4ffc7-0e1f-47d2-80b4-3fb032d7cf80`, failed with 55.23% holdout MAPE and 90.90% SMAPE. Earlier checkpoints were 68.85% and 23.40% MAPE. The fresh capture above is preparation for new validation; no new forecasting-accuracy result is claimed.
