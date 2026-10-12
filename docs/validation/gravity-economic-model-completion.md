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

Detroit commercial jurisdiction support: **299 .NET tests pass**, with a clean harness build and a live PostGIS containment/allocation check. The [MGCB reported land-based levy](https://www.michigan.gov/mgcb/Detroit-Casinos/resources/revenues-and-wagering-tax-information) is modeled on its combined 19% reporting basis, allocating 10.9% of AGR to Detroit and 8.1% to Michigan school aid. The [MGCB age guidance](https://www.michigan.gov/mgcb/news/2024/10/02/reminder-to-wager-responsibly) supports age 21 for Detroit commercial patrons. Support starts in 2025, requires Wayne County and exact Detroit place GEOID `2622000`, and does not apply to tribal, retail sports, online gaming, additional fees or licensing eligibility. On $100M test AGR the live check returned $19M combined levy, $10.9M municipality, $8.1M state and zero duplicated supplemental levy. Seeding twice did not duplicate the five rules. Broader Michigan and other-state fiscal acceptance remains open.

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

The historical corrected-attribute evaluation `64f4ffc7-0e1f-47d2-80b4-3fb032d7cf80` failed with 55.23% holdout MAPE and 90.90% SMAPE. Earlier checkpoints were 68.85% and 23.40% MAPE. The new evaluation below uses a different sample and is not a like-for-like accuracy trend against those historical values.

## Multi-market calibration and defect remediation — 2026-10-12

- [x] Freeze [the calibration protocol](multimarket-calibration-2026-10-12.json) before scoring: eight training, four selection and twelve held-out calendar-2025 properties across Cleveland, central Ohio and Cincinnati, with Indiana and Ohio represented. Protocol SHA-256: `aefcc73c9f62872330a1dd0e06d87ae67ae8325cda92db52bb8b35d764878b29`.
- [x] Prepare sealed 400-mile origin/income inputs around Fort Wayne: 7,643 income-backed origins of 9,204 full Census origins. [Independent coverage evidence](bundles/multimarket-income-coverage-2026-10-12.json) measures 99.2862% eligible-population coverage; 1,561 origins lack exact IRS income. This does not establish complete competitive or fiscal coverage.
- [x] Execute the declared 360-candidate search and a new twelve-property revenue-component evaluation using shared server feature calculations and gravity log weights. [Original retained result](bundles/multimarket-calibration-results-2026-10-12.json): **MAPE 73.5195%, WAPE 50.2134%, normalized bias +50.2134%**. All twelve properties were overpredicted. This fails the policy; no defaults were published. Induced, tourism and traffic additions are zero, matching the server's zero-safe settings. This is resident-revenue evidence, not finalized full economic-run or causal-impact acceptance.
- [x] Correct a structural normalization defect: comparable scaling previously affected the candidate but left unobserved/withheld incumbent structural mass unscaled. Apply the same normalization to every structural mass; keep proposal-specific scaling separate. New run fingerprints use `consistent-physical-v2`; paired validation distinguishes legacy behavior.
- [x] Correct inconsistent gaming-position fallback: use observed total positions when available, otherwise combine known slot and table counts consistently with the candidate program. Preserve missing slot counts and guard overflow.
- [x] Diagnose Toledo's zero prediction: the original routing configuration yielded no reachable candidate routes. At the identical geocoded coordinate, a bounded 100-metre facility correlation radius produces actual Valhalla driving routes. Explicitly request that radius and include `target-radius100-v1` in routing cache identity. Old routes are retained and cannot be reused under the corrected identity. No straight-line travel-time substitution is used. [Valhalla location documentation](https://github.com/valhalla/valhalla/blob/master/docs/docs/api/route/api-reference.md).
- [x] Register the original inspected sample in immutable component inspection history in the dedicated validation database. The record identifies all 24 development/holdout properties and the result checksum, creates no economic model runs and publishes no parameters. Stable venue identities carry across snapshots, preventing these properties from being reused as untouched holdouts by either publication workflow.

The first development-only scaling correction reduced selection MAPE from 42.3389% to 28.4477%, and WAPE from 43.9545% to 32.2629%, before correcting Toledo routing. [Retained development evidence](bundles/multimarket-development-remediation-2026-10-12.json). These selection metrics are not independent release evidence. The original failed holdout artifact and frozen protocol remain intact. Development corrections use training and selection data; any later replay of inspected properties must be labeled diagnostic and cannot establish freshness.

Origin snapshot: `0e277abe-37c1-4049-a5b6-33be29ceab1e`; income: `9232145d-5f95-4860-b9c4-c13b9977f6a5`; age: `862a1815-8438-47bb-94de-7b210b044711`. Facility/performance snapshot identities remain those recorded above. The income checksum is `ad737dd86f1fb3ec8d8d08d6690a0253728ee3f1607b45ee9d03fe72c6e6f764`; the origin checksum is `3d0bed93de4375501002dfd455b6507700e077a4d3d0a210bc1d8f3a780e842a`.

With all three corrections, [final development selection](bundles/multimarket-development-final-2026-10-12.json) gives **10.9630% MAPE, 12.3829% WAPE and +5.3205% normalized bias** across four selection properties. Training MAPE is 14.2464%. The intermediate [routing correction](bundles/multimarket-development-routing-remediation-2026-10-12.json) is retained separately.

[Diagnostic replay of the already inspected twelve-property sample](bundles/multimarket-inspected-sample-diagnostic-2026-10-12.json), using parameters selected only on development data, gives **20.0663% MAPE, 15.8637% WAPE and -0.7047% bias**. Compared with the original evaluation on these same properties, the three defects account for substantial excess error and systematic overprediction. The corrected averages still miss the declared limits; Belterra Park and Rising Star remain above the 40% individual-property limit (57.36% and 61.55%). This replay is explicitly labeled diagnostic, has `holdoutPreviouslyInspected=true` and `releaseAccepted=false`, and cannot establish untouched-sample acceptance. No failed routes were replaced with synthetic times.

Verification: **307 .NET tests pass**; the Release harness builds with zero errors (existing warnings remain); model-license, UI text-size and tooltip guards pass. Live inspection registration is immutable, identifies 24 stable properties, and has no published parameter set. The original protocol hash and original result checksum `204a4f86bdcc807e2b9bf2d118caa83a8995a8fce181aa2113c4229306f13357` are preserved.

Next development experiment: [predeclared offset-sensitivity protocol](multimarket-development-offset-protocol-2026-10-12.json). The existing 2,000-position offset strongly smooths smaller/larger facility differences; training residuals overpredict Ameristar and underpredict Hard Rock Northern Indiana. Broaden this existing parameter and travel-decay range, fit income share on training only, and select on the four selection properties. Previously inspected properties remain excluded from this experiment's scoring; it cannot publish defaults or satisfy release freshness.

Required release work remains: pass the declared metrics on a new untouched sample, complete independent input/fiscal/reproducibility audits and finish the public stored-run visualization/report integration. No merge or deployment.
