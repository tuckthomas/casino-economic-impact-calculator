# National Casino Gravity, Revenue Projection, Cannibalization, and Comprehensive Economic Impact Model

## Governing AI Agent Implementation Checklist

> **Status:** In Progress — Foundation committed (commits `f30229d`, `0846e71`). Core services, entity schema, migrations, selected authoritative providers, validation/calibration infrastructure, the stored-run web workbench, scenario comparison, deterministic server-side HTML/PDF/CSV report rendering, and the first three stored-run Indiana public-benchmark comparisons are implemented. National provider coverage, production-quality calibration/holdouts, and production benchmark acceptance remain pending.
>
> **Primary objective:** Build a transparent, empirically calibrated, nationally reusable casino gravity and economic-impact engine that can evaluate a proposed casino or major gaming development anywhere in the United States. The engine must estimate site-specific gaming revenue, patron origins, market expansion, cannibalization, repatriation/leakage, tourism and through-traffic demand, sector displacement, fiscal effects, employment effects, and downstream social/economic costs. The same immutable model run must power the interactive web application, APIs, sensitivity analysis, and a server-generated full analytical report comparable in structure and rigor to professional casino feasibility and impact studies.
>
> **Current Indiana use case:** Allen County, DeKalb County, Steuben County, and the surrounding Northeast Indiana market are the first production use case and an important validation suite. They are **not** the model's hard-coded geography. Indiana-specific assumptions, tax rules, origin groupings, competitors, benchmark reports, and labels must live in jurisdiction/scenario configuration and validation data rather than in the core model.
>
> **Implementation posture:** Do not retain `RevenueHeuristicService`, `CompetitionScoringService`, `ZipSwitchingModelService`, or any parallel prototype path as a legacy model. Replace and remove unsupported assumptions, fixed Fort Wayne logic, hand-set competitive weights, and straight-line travel approximations in favor of the single calibrated, auditable, configurable national model. Haversine may only prefilter a broad candidate region; exact Valhalla travel time is authoritative for modeled travel friction.

> **Verified implementation evidence and checklist cadence (2026-08-13):**
>
> - Checklist state was independently reviewed against commit `cf37a1a`; items are checked only when they meet the completion rule in this document, not merely when an interface, entity, or provisional provider exists.
> - **SUCCESS:** `SaveNEIN.Server.Tests` passes 161/161 tests in the isolated feature worktree, including stored-run report integration, dynamic origin reconciliation, artifact immutability/cache identity, draft-run rejection, model-foundation seed metadata/default upgrades, distinct casino/racino rule seeding and obsolete-fixture removal, Indiana conditional base/supplemental gaming-tax schedules and component distributions, schema defaults, multi-provider jurisdiction composition, Indiana annual facility-profile parsing, Michigan/Ohio/Pokagon source discovery and evidence validation, candidate/incumbent route cache identity/reuse, exact incumbent-to-held-out-scenario coordinate reuse, radial ZCTA study-region selection, official-missing-value handling, market-scoped IRS reconciliation, INDOT stable-identity handling, annual inventory composition, Census CBP local-inventory/labor resolution, observed unit-day capacity benchmarking, pair-specific Valhalla matrix eligibility/unroutable isolation, and the numerical-robustness cases below.
> - **SUCCESS:** The gravity robustness tranche proves a user-overridden facility-attraction coefficient flows through parameter resolution, structural attraction, and allocation; a 5,000-facility choice set remains finite and demand-conserving; sparse rural origins preserve unreachable alternatives as excluded/auditable while the outside option absorbs demand; and zero plus `1e-12` demand remain finite and exactly conserved.
> - **SUCCESS:** A disposable remote PostGIS/Valhalla integration run persisted exact routed travel and finalized 45 calibration candidates. Its selected holdout MAPE improved from 98.20% to 68.85%, but still fails the production-quality gate; calibration and production acceptance items therefore remain unchecked.
> - **SUCCESS:** Live Illinois regulator ingestion returned 17 facilities, 34 performance rows, and $1.9437 billion in reconciled annual AGR; a multi-jurisdiction Indiana/Illinois ingestion persisted sealed source snapshots.
> - **SUCCESS:** Live Michigan inventory ingestion returned 27 facilities (24 tribal and 3 commercial), stable identities, geocoded coordinates, provenance checksum, and an explicit structural-attraction fallback warning where audited tribal GGR is unavailable. Michigan performance-history coverage remains incomplete.
> - **SUCCESS:** Live Michigan commercial-performance ingestion discovered and parsed the official 2025 MGCB Excel workbook: 72 rows covering 12 months and three Detroit casinos, with regulator-specific and comparable metric definitions reconciling to $1,265,324,361.46. Tribal GGR and retail sportsbook QAGR remain explicitly excluded.
> - **SUCCESS:** The exact Michigan provider outputs were ingested through the deployed server stack into a disposable VPS PostGIS database: 27 facility rows and 72 performance rows produced two sealed snapshots with matching source checksums and the exact $1,265,324,361.46 comparable-revenue total. Direct `michigan.gov` downloads from the VPS remain blocked by an upstream HTTP 403, so the official provider outputs were fetched locally and transferred by checksum; the validation database and temp files were removed by the guarded harness.
> - **SUCCESS:** Live Ohio Casino Control Commission ingestion parsed and reconciled the official 2025 cumulative PDF at facility, annual, and statewide levels: four commercial casinos, 96 regulator/comparable performance rows, $1,033,920,366 in comparable casino revenue, 357 month-end tables, and 5,953 month-end slots. A disposable VPS PostGIS run persisted the exact four facility rows and 96 performance rows into two sealed snapshots with matching source checksums, then removed the validation database and temp files. Ohio Lottery-regulated video-lottery racinos remain excluded from this OCCC dataset and are handled by the separate provider below.
> - **SUCCESS:** Live Ohio Lottery ingestion combined the exact overlapping FY2025/FY2026 facility PDFs into calendar 2025 and reconciled every shared fiscal month to the statewide reports: seven racinos, 168 regulator/comparable performance rows, $1,424,001,339 in comparable VLT net win, and 10,109 average VLTs in the December inventory. The composite provider now supports multiple non-overlapping regulators in one jurisdiction; OCCC plus Ohio Lottery yields 11 facilities and $2,457,921,705 in comparable 2025 land-based revenue.
> - **SUCCESS:** The checksum-pinned combined Ohio provider bundle (`45a9259cf1598c9fd0c316bded59980abca2f41677ebd86384ad11ef985150f7`) was ingested through the deployed server stack into a disposable VPS PostGIS database: 11 facility rows and 264 performance rows produced sealed `competitors` and `observed-performance` snapshots with exact provider checksums `9336da5a568470dd401db3a75cad544897d1d08fd5a21adb8b7f793cd33a452c` and `aa9dd24095b8fcb348590c5d44e51cb88a1bb8a73c39e8989630299cefdfc653`, and reconciled to $2,457,921,705. Direct `ohiolottery.com` downloads from the VPS remain blocked by upstream HTTP 403, so official outputs were fetched locally and transferred by checksum; the guarded validator removed database `savenein_provider_validation_ohiobundle20260811b` and `/tmp/savenein-provider-validation.e294TA` after success.
> - **SUCCESS:** Stored-run report integration now verifies dynamic ZCTA, state, and county/parish reconciliation and one immutable cached HTML/PDF/JSON/CSV artifact per normalized presentation. County/parish composition is rendered in HTML/PDF and exported in CSV.
> - **SUCCESS:** A checksum-pinned release artifact (`b8d9ad80e87363158a762974260320401090d3dd856459ecd57c641f2b48e3ef`) booted against a fresh disposable VPS PostGIS database after all model migrations and catalog seeding. The in-app browser created and selected an immutable 2,000-slot/50-table/200-room development program, exposed standard and advanced parameter metadata, rejected an incompatible versioned scenario document, and successfully restored a valid sealed-snapshot scenario.
> - **SUCCESS:** The same browser workbench submitted two finalized runs through the authoritative backend and persisted live Valhalla routes (2.23 minutes/1,445 meters to the proposed facility; 110.7 minutes/165,232 meters to the incumbent). Beta `1.55` produced $1,012,625 stabilized GGR; allowed-but-warned beta `1.7` produced $869,000. The generated report JSON preserved `gravity.beta = 1.7` and the exact validated-range warning, while immutable comparison rendered both runs alongside the integration baseline.
> - **SUCCESS:** A follow-up checksum-pinned artifact (`b376e1de0ba8dc30da238b0ecfbfc833fa3df183e545b270ef1e9d1b09c1c5b7`) exposed source names and checksum prefixes on sealed-snapshot choices and loaded dynamic result detail from the stored-run APIs. Browser verification rendered origin `USA-ZCTA-46802` with $7,260,000 resident demand and $1,064,683 proposed GGR, plus incumbent `USA-IN-SOUTH-BEND-SMOKE` changing from $6,145 baseline resident GGR to $5,243 with project (-$901), without a separate calculation path.
> - **SUCCESS:** Report template `professional-v3` was generated from the same finalized browser run as immutable artifact `41c3076e-b968-40cd-a7c4-0d25fea03d1f` with PDF SHA-256 `c59583ba35094e22e1b70b3e98bd9d9a14dc0398bb27edf77ee36677fcb1f16a`. Poppler rendered all 12 letter-size pages for visual inspection. The report correctly discloses the stored `agi-share`, `observed-ggr`, and `inverse-power` specifications despite production input JSON casing. The executive warning flood is compressed from 93 raw warnings (84 parameter-calibration notices) into a concise disclosure plus decision-use warnings; exact warnings remain in JSON and the parameter appendix. The report adds data-backed revenue-composition, origin-contribution, competitor-impact, and sector-displacement charts and a complete replay appendix containing the jurisdiction effective-rule fingerprint, parameter sets, overrides, source vintages, Valhalla graph hash/costing profile, site, program, run timestamps, and generation timestamp.
> - **SUCCESS:** Migration `018_candidate_location_travel_cache.sql` was applied to disposable VPS PostGIS database `savenein_ui_validation_20260811`, and a checksum-pinned corrected release artifact completed two in-app browser runs at the exact candidate coordinate `41.0793, -85.1394`. Finalized runs `eeb06a92-2c9b-4361-b70d-9ca9803fc8e1` and `55979daf-6c77-4c14-88d4-72a13fa5bf88` persisted distinct immutable scenario-route rows while sharing one SHA-256 coordinate-cache row (`f989a8eab8da6baf7050aedd3767395cae00ab2c3a1661cadd6ce5dfe420b5d7`) for routing graph `ebaf3afb590388a0e9ba5c64b9e5d3b1014b17c61f1dcb58ede89955e55b82bd`. Both per-run routes preserve the cache calculation timestamp `2026-08-11 10:04:09.858572+00`, 2.233333 minutes, and 1,445 routed meters, proving the second run reused Valhalla output without sacrificing exact run coordinates or replayable route materialization.
> - **SUCCESS:** Report template `professional-v4` was generated from finalized run `0022ed41-9c1d-435a-bb9d-2942913a9101` and stored sensitivity analysis `4411b3f5-8ad2-428d-964d-9d0dba1635b0` as immutable artifact `dec6ed81-cf88-4389-b7a6-d553f853a619`. The server returned PDF SHA-256 `38363e781b57d560ffd988e36f3102f1363f5c3de9d43ad60b9777bab36b5cf5`; Poppler confirmed and rendered all 14 letter-size pages for visual inspection. The PDF/HTML now contain WGS84 proposed-site/competitor and proportional-symbol patron-origin maps, a revenue-composition waterfall, social-cost bridge, net host-local impact waterfall, and a signed-output-delta sensitivity tornado with an explicit low/high setting legend. The tornado was verified against nine complete stored runs, including the inverse `gravity.beta` relationship (low setting: $50,099; baseline: $47,195; high setting: $44,762). CSV preserves facility/origin coordinates, waterfall/bridge source components, inclusion flags, point-run UUIDs, and exact low/high deltas behind the exhibits.
> - **SUCCESS:** Report template `professional-v5` derives a top-N patron-origin polygon choropleth and origin-to-candidate travel-time map from the same immutable run. Live artifact `3f90c001-42f1-44b3-b042-ec6f3a022fb5` has PDF SHA-256 `346edb7a63fe11ba3d6e7ee0c4d8e83393f39b2e7d6bcb905015a6b9c4db469f`; Poppler confirmed and rendered all 15 letter-size pages. Visual QA corrected an orphaned exhibit heading and verified compact pagination. The stored ZCTA polygon is simplified by a disclosed 0.002° presentation tolerance, while exact contribution values remain unchanged; the travel-time map uses the finalized run's persisted Valhalla `auto` route of 2.233333 minutes and 1,445 meters at the origin representative point and explicitly states that it is not an interpolated isochrone. HTML, report JSON, and CSV preserve source geometry, route-found state, routed minutes, and routed meters behind both exhibits.
> - **SUCCESS:** The three primary Indiana benchmark PDFs were freshly downloaded from the canonical URLs and independently matched the checksum-pinned migration evidence byte-for-byte: Spectrum `915F30300F5240252D020FF3F7E91A734982C5E18D4B7DCF25EDD4C2F05B27F6`, CBRE/Union Gaming `1A00F19766BA0361D4E8A6514D32701727BEDEFCADA73CCFA90729DB8107A510`, and Steinberg `68D62E0EABA0619197DE14F3E24C484132CDD4CF73390F48CAA2C563C89A7E1E`. Source-text verification confirmed the registered demand methods, priors, data advantages/limitations, traffic/ramp/comparable concepts, and published output anchors. Disposable VPS PostGIS inspection confirmed all three generic registry rows persist market/geography, study date, consultant, candidate description, program, outputs, assumptions, methodological notes, URL/checksum provenance, and `extracted` state. This validates the benchmark registry and source extraction only; public benchmark model-run reconciliation remains open.
> - **SUCCESS:** Census ZCTA origin ingestion now accepts either an explicit code universe or a mutually exclusive broad center/radius candidate-region filter. A checksum-pinned release deployed to the disposable VPS stack ingested sealed snapshot `2e18861c-26c7-4b6b-96ef-8487d11f0b75` from the live 2020 Census boundary and relationship files: 1,788 ZCTAs within 150 miles of `41.35, -85.05`, spanning 529 Indiana, 562 Michigan, 539 Ohio, and 158 Illinois origins across 145 dominant counties, with zero invalid persisted polygons. The snapshot stores source hash `32906894267127899acd4a8092f33356a39c90f183a82af122ccdf8753f4b768`, transform checksum `8f1736079f3c141be5b643e35bc3c6238138797ccc773056df077d1c62fc44c6`, exact filter provenance, and a warning that Haversine is only a study-region prefilter; the existing persisted Valhalla route engine remains authoritative for travel friction and final reachability.
> - **SUCCESS:** Official 2024 ACS table-based Summary Files were ingested live against that exact origin universe. Sealed age snapshot `b7eddeeb-dded-4353-8cd3-7399725bc9f7` contains 41,124 validated rows (1,788 ZCTAs × 23 native B01001 age bins) totaling 23,433,909 residents, with source hash `1637b18a96881b81e050df1cd3d5ac38a33208b9b69b40e1dbeb3c4e13718f0e`. Live B19013 ingestion exposed 98 official unavailable-value sentinels; the provider now omits those observations with every affected ZCTA disclosed instead of aborting, coercing to zero, or imputing unsupported values. Sealed income snapshot `fa80f877-f3c7-4f29-a8ad-e5f3f3dfb1a4` preserves 1,690 usable median-household-income observations (range $14,093–$250,001), source hash `b25a176b0e6c339b6f3a2a0d3d8446bf06f5f080b4395993ec9a8313efb1c229`, exact transform checksum, and the complete omission warning.
> - **SUCCESS:** The IRS SOI adapter now applies the same explicit ZCTA market universe as the linked origin snapshot and hashes raw inputs separately from the versioned transform/subset. Live four-state ingestion produced sealed 2022 AGI snapshot `c1424d2e-9949-42b6-952f-66cca0b0e2c1`: 1,545 exact-code IRS ZIP/ZCTA matches (Illinois 149, Indiana 445, Michigan 525, Ohio 426), 11,029,940 returns, and $917,867,066,000 AGI ($83,215.96 per return). It discloses all 243 requested ZCTAs with no retained IRS total and performs no zero-fill or imputation. Raw-workbook-plus-gazetteer hash `66280970563b029ff6fda05476ee9078d8d338ed4f3c0a7c0a1ff044a29f32f8` is distinct from transform/subset checksum `aac291d337b1ab43575a5ecc97f70549111a35e3733b9db106838f86a47d3873`; dollar warnings are culture-invariant and deterministic.
> - **SUCCESS:** Live Indiana tourism and traffic ingestion now persists authoritative nonresident-demand inputs instead of relying on test fixtures. IDDC/Rockport snapshot `211ba561-a0e7-4432-9143-de1403a20432` preserves the published 81.7 million 2023 statewide person-trips and explicitly warns that they are neither unique visitors nor site-addressable demand. Official INDOT inspection found 136 duplicated `EVENT_ID` keys but 33,993 unique `GLOBALID_1` values; the adapter now uses the unique published global ID with event/site fallbacks. Live Northeast Indiana snapshot `0f597c70-cc1c-46d8-8973-4f8122af1587` seals 4,933 unique 2024 count-zone rows across 2,413 route designations (AADT 24–76,755), including the highest-volume I-69 observations around Fort Wayne, with source hash `452015043f35055c1147806477098aeddab3cbef717cd7d2a0b0f1f6a28c6f59` and only nine source records skipped for missing roadway geometry.
> - **SUCCESS:** One official 2025 four-state gaming market bundle now composes Indiana, Illinois, Michigan, and both Ohio regulators without assuming a same-state competitive field. Indiana annual inventory requests use the disclosed December month-end unit table, and composite source coverage stores normalized jurisdiction codes rather than overflowing the source-catalog contract with verbose component descriptions. Checksum-pinned bundle `946e78a479e4353e551531aa19559400c84253520d69373a0a27bce591a072bd` persisted through the same ingestion services as sealed competitor snapshot `339ec3ce-e2af-4780-8a6f-6189c1140ad0` (68 facilities: IL 17, IN 13, MI 27, OH 11; checksum `a193600f5f643c41ad6bb3f057bb2d5194ae2ff73924077eab8e553181724a14`) and linked observed-performance snapshot `59cf73f5-35d8-43c2-ae4d-2ad7c8d88b55` (838 rows; checksum `8366f47ba83102d415edcaf9be88b10cfdf3423c7a2845e02f345e6ae1ae818a`; $8,103,900,624.82 comparable land-based revenue). Michigan/Ohio official outputs were transferred by exact checksum because their upstream sites block the VPS source IP; Indiana/Illinois were fetched live.
> - **SUCCESS:** Regional matrix execution now applies a disclosed 200-mile Haversine eligibility prefilter separately to each origin/facility pair, sends every retained pair to exact Valhalla routing, and never substitutes straight-line distance for travel friction. When Valhalla rejects a multi-target request with pair-specific error 442 or 154, the service bisects only that batch until the exact unavailable pair is isolated and persisted; unrelated HTTP 400 configuration errors still fail the run. Three regression tests prove far-pair isolation, exact-pair unroutable isolation, and unknown-error propagation. Checksum-pinned VPS artifact `434ba901a52a29a94ecc9c1b4c1c9b5eec4809224d5c055b0d4286d3aa891122` finalized live run `2af27fd3-3e0b-4c24-b431-527c79d2c00d` across 330 sealed ZCTA origins and 49 cross-border incumbents using routing graph `ebaf3afb590388a0e9ba5c64b9e5d3b1014b17c61f1dcb58ede89955e55b82bd`; 9,143 unavailable pairs were explicitly excluded and no Haversine travel value entered the gravity equation.
> - **WARNING:** That first CBRE/Union Gaming reconciliation run produced only $2,608,229 resident GGR against the public $216.7 million local-gravity anchor because provisional outside-option/attraction values captured almost all resident demand outside the modeled facilities. A bounded six-run parameter grid using the identical immutable routes produced $86.4 million–$194.9 million, but no calibration item is checked: the result remains below the CBRE anchor, uses a scenario program with assumed gaming counts, and has not yet passed multi-market incumbent holdouts or the Spectrum/Steinberg cases.
> - **SUCCESS:** Migration `019_indiana_benchmark_reconciliation_outputs.sql` exposes CBRE's checksum-gated $216.7 million local-gravity and $215.0 million local-regression components through the generic benchmark metric reader. Live restart first caught and then corrected an idempotency defect in migration 007: a venue identity may repeat across immutable snapshots, so only migration 008's snapshot-scoped `(dataset_snapshot_id, stable_venue_id)` uniqueness is valid. Checksum-pinned release `9d73013e12201ad074638e2fcea20f3c003f7b2b5fb3e2ca5ee4038b848527a1` applied the complete migration chain cleanly to disposable PostGIS database `savenein_ui_validation_20260811`; the full local suite remains 130/130 and the focused schema suite passes 2/2.
> - **SUCCESS:** Three finalized, source-extracted public benchmark cases are persisted without accepting observed values from the request body. The corrected 69-facility universe includes Four Winds South Bend. Fort Wayne run `4cc7e06b-919d-41ed-a987-a2492b43c197` produces $186.67 million resident/total GGR versus CBRE's $216.7 million local-gravity component (-13.86%) and $282.3 million total including traffic (-33.88%). DeKalb run `ae288dea-14ba-4523-901d-2621570d2bd8` produces $185.60 million versus Spectrum's $204.3 million adjusted receipts (-9.15%). Steuben run `8e919097-d73a-4ff4-b82f-9e4bc1aabede` produces $182.71 million versus Steinberg's $194.5 million resident base (-6.06%) and $203.1 million stabilized 2030 total including tourism (-10.04%). All three use exact Valhalla routes and a common disclosed pre-calibration surface (`beta=1.5`, outside option `1e-8`, proposed structural scale `4.0`); those values were not fitted separately to each target.
> - **WARNING:** Benchmark differences are explainable but not yet production-accepted. The stored runs currently add no site-addressable tourism or traffic amount, Steuben uses current-vintage demand without the planned 2030 population projection, and Spectrum had confidential operator rated-play data unavailable to this project. State-level proposed resident GGR is generated dynamically: Fort Wayne is IN $147.80M / MI $6.29M / OH $32.57M; DeKalb is IN $125.65M / MI $26.74M / OH $33.21M; Steuben is IN $97.17M / MI $50.73M / OH $34.81M. Production calibration, incumbent holdouts, projection, and nonresident-demand calibration remain open.
> - **SUCCESS:** A dedicated Pokagon/NIGC provider closes the Four Winds South Bend competitive-universe gap without a hand-entered compatibility row. It checksum-gates the NIGC-published Indiana Class III compact (`c8132524525a0baef4bec6873ff3126ef5d922416c3bf6271e2a08565ffcdef9`) and verifies current operator evidence for over 1,900 slots, 27 table games, 12 poker tables, 175,000 square feet of gaming, six restaurants, a 317-room hotel, 800-seat ballroom, and the published South Bend address. Composite provider checksum `b5900f8626226f24bd6f9d7a44fa84d6ed6d5e1e242e02037aa60b0524b6dbc8` sealed snapshot `932acfed-303e-4d3e-ace5-1463c0dca1d9` with 69 facilities (IL 17, IN 14, MI 27, OH 11); linked performance snapshot `4a7ffddb-5d7f-47ee-848c-541075d5c95d` preserves 838 rows and $8,103,900,624.82 without imputing tribal GGR. The corrected runs now estimate Four Winds South Bend impacts of -$13.10M Fort Wayne, -$13.38M DeKalb, and -$12.95M Steuben; CBRE publishes -$11.6M for its Fort Wayne case.
> - **SUCCESS:** Migration `020_coordinate_versioned_incumbent_travel_cache.sql` prevents a stable venue ID from reusing travel after its coordinates move between immutable snapshots. Every newly persisted route carries exact facility latitude/longitude and a SHA-256 coordinate identity. The runtime now rejects every pre-versioned cache identity, even when its stored coordinates happen to match bit-for-bit, and resolves a fresh exact Valhalla route instead; only an exact coordinate hash, routing graph, and costing profile may be reused. The focused travel-cache suite passes 7/7 and proves both same-coordinate fail-closed behavior and moved-coordinate separation. Historical checksum-pinned VPS artifact `134d4232c348ef1ba71f9a66b24a3a42f6c7c96aeafd0e4bbe85095b3a39eaa5` applied the schema migration cleanly and backfilled 27,318 pre-versioned rows; those rows are no longer accepted as reusable route evidence.
> - **SUCCESS:** Exact-coordinate route reuse now crosses facility roles without conflating run evidence: a held-out scenario placed at an incumbent's audited coordinates may reuse that incumbent Valhalla result only when origin, bit-exact latitude/longitude, SHA-256 coordinate identity, routing graph, and costing profile all match. The service still materializes a distinct scenario `origin_facility_travel` row and candidate-cache row for replay. A regression test proves one Valhalla request serves the incumbent and held-out scenario while preserving the scenario run ID and facility kind. Regional tile caches and background grid warming remain open.
> - **SUCCESS:** The four-state calibration harness now refuses a frozen provider bundle unless its SHA-256 is exactly `fccb73b93a777f49af7ed82b5ccba376d68631e5443038873a1cb29dcf4c9d50`, preventing an unrecorded source-payload change from entering calibration. The corrected harness build succeeds with zero warnings/errors, and the complete Release suite passes 134/134 tests. The active disposable VPS calibration uses that independently checksum-verified bundle; candidate-quality results remain pending until every run and the held-out evaluation finish.
> - **WARNING:** Disposable VPS evaluation `06261f59-60be-4d92-8b98-8d516c9c6285` finalized all 126 predeclared candidate runs over four training properties and the independent three-property Chicago-side Indiana holdout, publishing immutable parameter set `7`. The selected `destination-beta-1.4-alpha-1.00` surface stores beta `1.4`, alpha `1.0`, outside option `1e-8`, comparable scale `4.0`, positions coefficient `1.0`, tables coefficient `0.4`, and regional intensity `1.1`. Aggregate gravity holdout MAPE/SMAPE are 23.40%/18.91%, but the quality gate fails: Horseshoe Hammond has 60.11% APE, training Blue Chip and Terre Haute have 41.86% and 64.94% APE, training SMAPE is 37.60%, rank correlation is only 0.5 on holdout, and the independent comparable model has 62.96% holdout MAPE. Phase G and calibrated-default items remain unchecked; average holdout error is not being used to hide property-level failure. The sealed competitor data report 68 of 69 hotel counts unknown, so facility-attribute coverage and the independent comparable specification must be repaired before rerunning calibration and public benchmarks.
> - **SUCCESS:** The first calibration remediation replaces Indiana's unknown facility-mass placeholders with regulator-published evidence. The Indiana facility adapter now checksum-composes the IGC location page, December gaming-unit workbook, and FY2025 Annual Report facility profiles; all 13 commercial properties carry gaming-floor area, restaurant count, and an explicit hotel-room count or no-hotel value. Live official-source validation returned 13 rows, two racinos, zero unknown hotel statuses, and checksum `df8f16a821f15ae1b710a98096a94aa0e9e8ce0df5a7ef4a75beaae32c30a022`; focused provider tests pass 9/9, including complete-inventory rejection. Poppler rendered all 13 source profile pages, and visual inspection confirmed representative hotel, gaming-space, and restaurant labels for Ameristar, Hoosier Park, Hard Rock, and Terre Haute. The failed calibration remains failed until a new sealed competitor bundle and rerun demonstrate improved property-level errors.
> - **SUCCESS:** Structural attraction now exposes regulator-observable gaming-floor area and food/beverage venue count as normalized, neutral-by-default, versioned parameter families alongside positions, tables, hotel, event capacity, capital scale, and highway access. The same feature construction applies to incumbent snapshots and immutable proposed development programs; missing attributes remain an explicit neutral-reference fallback rather than a fabricated zero. The full suite passes 140/140. Coefficient fitting and production-default designation remain open because the corrected calibration rerun failed its quality gate.
> - **SUCCESS:** Upstream Ohio Lottery publication-link drift is not allowed to rewrite frozen evidence. A guarded bundle-refresh command requires the exact prior four-state bundle SHA-256 `fccb73b93a777f49af7ed82b5ccba376d68631e5443038873a1cb29dcf4c9d50`, replaces only `USA-IN-IGC-*` facility rows with today's live IGC dataset, and preserves all other frozen facility/performance rows. Refreshed bundle SHA-256 `31d2fc3f3762ec02a97e18996ff680a276cc4d76b5016b3f388a5b287dd08396` contains 69 facilities and 838 performance rows. Remote startup reused unchanged sealed traffic/origin/income/age/tourism snapshots and created refreshed facility snapshot `e56873b1-6aea-4884-a052-2db3f6207687` plus facility-linked performance snapshot `7d258b75-991b-46cb-8294-aadaa5c76de5`. Its performance transform checksum binds both raw performance and facility checksums so foreign-key identity changes cannot masquerade as a duplicate raw dataset. All 126 namespaced rerun candidates finalized; the following warning records the failed result.
> - **WARNING:** Corrected facility-aware evaluation `64f4ffc7-0e1f-47d2-80b4-3fb032d7cf80` finalized all 126 namespaced runs and immutable parameter set `8`, but failed more severely and is not a production calibration. The selected `balanced-beta-1.5-alpha-0.75` surface produced training MAPE/SMAPE of 65.36%/98.50% and holdout MAPE/SMAPE of 55.23%/90.90%. Property APEs were Ameristar 6.38%, Hard Rock 85.15%, Hammond 74.15%, Blue Chip 49.18%, Hoosier Park 70.36%, Horseshoe Indianapolis 73.99%, and Terre Haute 67.89%. The rerun proves attribute availability alone cannot repair a misspecified shared attraction surface; calibrated-default, Phase G, and public-benchmark acceptance items remain unchecked pending model remediation and a new independent rerun.
> - **SUCCESS:** Indiana's effective state gaming-tax schedules are now implemented as validated jurisdiction data, not hard-coded national logic. Official 2026 Indiana Code sources govern the post-June-2021 riverboat schedule, the prior-fiscal-year-under-$75M schedule and one-time $2.5M crossing tax, and the 25%/30% racino schedule. Rule identity now includes the serialized payload so same-source casino/racino rules cannot suppress each other, and seeding removes the superseded provisional FY2025 fixture. Release tests pass 140/140. Checksum-pinned harness archive `5ce37d0ec0832bdb944e70a56bf83448d60d16ba7258a7efe2d9708c758d14f3` applied every migration and seeded the rules in disposable VPS PostGIS database `savenein_fiscal_validation_20260813`, reproducing $12.125M low-prior-year casino tax on $80M, $15.25M ordinary casino tax on $80M, $40M racino tax on $150M, age 21 for both regimes, and zero obsolete/provisional gaming-tax rules. The guarded validation database and temporary server artifacts were removed after success. This base-schedule checkpoint is extended by the component-allocation tranche below.
> - **SUCCESS:** Northeast Indiana's enacted 2026 supplemental-tax and recipient allocation now replace the stale flat-share/HB 1038 configuration. Effective-dated jurisdiction rules implement IC 4-33-12-1.5's 3.5% supplemental tax for the IC 4-33-6.8 casino, current IC 4-33-13-5 after P.L.157-2026 (no invented 25% northeast host share), and IC 4-33-12-8.7's 45% city / 45% county / 10% Northeast Indiana RDA distribution. Candidate county and incorporated place are resolved from exact TIGER polygon containment; a site outside an active place fails because the enacted text supplies no county fallback. Migration `021_component_gaming_fiscal_allocation.sql` persists base tax, supplemental tax, and municipality/county/regional/state components with reconciliation constraints, and those components flow through stored-run APIs and professional reports. The dead `TaxAllocationOptions`, stale appsettings scenario, flat `ILocalRevenueShareCalculator`, and tax-scenario endpoints are removed. Checksum-pinned artifact `8f9e62e8e70f1c5ee252a5444307777f648f39f5179b16d805aa88026987e4e6` ran against disposable VPS PostGIS database `savenein_fiscal_validation_20260813_v6` using 92 Indiana county and 974 Indiana place geometries copied read-only from the server TIGER store. At $80M taxable revenue it reproduced $15.25M base tax, $2.8M supplemental tax, $1.26M city, $1.26M county, $280k RDA, and $15.25M host-state allocation; Fort Wayne/Allen County GEOIDs resolved exactly, all six component columns existed, all five rules were validated official-code rules, and zero flat/obsolete rules remained. The guarded database and remote artifacts were removed. Historical incumbent supplemental quotients, recipient-level statewide set-asides outside the northeast host allocation, admission/device taxes, and broader jurisdiction coverage remain open, so production fiscal acceptance stays unchecked.
> - **SUCCESS:** Official 2023 Census County Business Patterns state/county archives now supply the production `local-economic-inventory` provider instead of disposable fixtures. The adapter maps only NAICS 72 restaurants/hospitality, 44-45 retail, and 71 arts/entertainment/recreation into displacement sectors; preserves establishments, March employment, annual payroll, noise flags, raw-source hash, and transform checksum; and leaves receipts/sales null because CBP does not publish them. Geography integrity is fail-closed before network access: a state file may populate only a host-state scope and a county file only its host-county scope, so broad source data cannot be mislabeled as municipality, metro, or custom-area evidence. Checksum-pinned remote probe artifact `3651a55180df93d56316bb21970ae7fc38c471ae73e9a67722dbaa499cf8b88c` reproduced Indiana state transform checksum `e780cad8a59394b52328cd0ee4e6fc68fb3bf8c382bf4c4958c8464814601edd` and Allen County checksum `3e81c63908233dde891378875ac7d671e79d7b886f44580f4e6faf47dccdd610`. Indiana state rows contain 291,843 hospitality, 335,879 retail, and 36,609 arts/recreation employees; Allen County contains 18,505, 22,348, and 2,581 respectively. State casino-gambling payroll/employment resolve a $43,264.02 direct/incumbent annual wage and all-industries data resolve a $57,497.32 indirect wage. Allen County has no published casino row, so direct/incumbent wages remain explicit versioned fallbacks while its all-industries payroll resolves a $56,031.45 indirect wage; state wages are not mislabeled as county-specific. Migration `022_employment_assumption_provenance.sql` persisted all three resolved wages plus their source basis and passed on disposable VPS schema `savenein_employment_validation_20260813_v1` with four columns and a validated nonnegative constraint; the database was removed after success. Report HTML/PDF/CSV disclose the stored wages and provenance. Occupation mix, direct jobs/GGR, construction job-years, and regional indirect/induced multipliers still require production evidence, so Phase E employment remains open.
> - **SUCCESS:** Production capacity diagnostics now activate automatically when a run's immutable performance snapshot contains a complete regulator-observed productivity sample. The OCCC transform persists monthly slot revenue with monthly slot count and table revenue with monthly table count; the benchmark divides component revenue by exact observed unit-days rather than assuming the December inventory applied all year. Incomplete components, missing unit counts, anomalous periods, discontinuous coverage, or fewer than three comparable facilities fail closed without synthesizing a split or inventory. The dataset checksum binds the raw regulator source to transform version `occc-casino-revenue-pdf-v2-unit-day-components`, preventing a pre-transform snapshot from colliding with the new rows. Checksum-pinned OCCC bundle `4d78e46b69bc48165469e3c8250a554d4443c1bc02d510378138b8b388bd10e4` and harness artifact `ae18d0e537aa439aa1b64ad30528fbc7654016ccd925c6d6e44fb3ef8cdd44b7` persisted four facilities, 192 performance rows, 96 component/unit-count rows, and $1,033,920,366 reconciled comparable GGR through disposable VPS PostGIS database `savenein_provider_validation_capacity_20260813_v2`. Performance snapshot `4b426714-4345-4952-a13d-2ba5355e44c0` has checksum `1a5e01a241ec3ceb7d54bec0f4f36e2c1c8c7e8a34170b468e1ecf6200eb9caf` and resolved a four-facility 2025 observed range of $287.07–$382.08 slot win/unit/day and $1,635.42–$2,574.88 table win/table/day. Migration `023_capacity_productivity_benchmark_provenance.sql` exposed all nine new unit/benchmark columns and three validated constraints; stored diagnostics and HTML/PDF/JSON/CSV preserve the exact snapshot, method, sample, range, and facility values. The diagnostic supports table-only programs without inventing residual slot productivity and still flags rather than caps GGR. Explicit operating-hour normalization and hotel/event-specific capacity remain open because the regulator source does not publish comparable evidence for those dimensions. The guarded validation database was removed after success.
> - **SUCCESS:** Stored-run origin presentation now uses one backend `origin-summaries` contract over the immutable origin results rather than UI-only truncation. It groups by native origin/ZCTA, county/parish, state/territory, MSA, CSA, country, candidate-containing host region, persisted scenario jurisdiction, and in-state/out-of-state/international relationship; candidate host codes come from exact stored origin polygons and in-jurisdiction membership comes from the run's persisted local-origin set. Configurable top-N and minimum-share thresholds produce one `Other origins` residual that exactly reconciles resident demand, induced demand, proposed GGR, jurisdiction captures, and outside-option capture to all underlying rows. The web workbench uses the service with a 25-origin/0.1% display threshold, while detailed paging and CSV retain every origin. HTML/PDF reports now append an explicit reconciled residual beyond their configured top-N, and the server refuses to synthesize a ZIP/ZCTA summary from non-ZCTA computational origins without a versioned crosswalk. Focused service/controller/report tests pass 9/9 as part of the 161/161 Release suite.
> - **SUCCESS:** The dead browser-side `EconomicModals` simulator and its unreferenced JavaScript were removed rather than retained as a legacy path. That code accepted arbitrary fixed/custom AGR, applied stale HB 1038 tax percentages, and multiplied social costs in the browser. Repository-wide search confirms no simulator markup/functions remain, and `SaveNEIN.Client` builds with zero warnings/errors. The stored-run `EconomicImpact` workbench remains the only model UI: it submits scenario inputs and parameter overrides to the authoritative backend and renders immutable run outputs.
> - Update this checklist immediately after every verified implementation tranche, including the evidence used to justify each newly completed item and any failed quality gate that keeps an item open.

---

# 0. Read this first: non-negotiable agent instructions

- [x] Read this entire document before changing production code.
- [ ] Inspect the current repository before implementing each section.
  - [ ] Reuse working infrastructure where it is technically sound.
  - [ ] Refactor prototype logic rather than creating duplicate parallel systems unless separation is intentional and documented.
  - [ ] Follow `AGENTS.md` and all repository-specific UI and engineering guardrails.
- [ ] Build the core model to be geographically neutral.
  - [ ] Do not hard-code Fort Wayne, Allen County, DeKalb County, Steuben County, Indiana, or any specific competitor into reusable model services.
  - [ ] Do not hard-code a fixed list of patron-origin counties or states into reports.
  - [ ] Do not hard-code Indiana tax treatment into the national fiscal engine.
  - [ ] Do not assume every casino market uses age 21 as the legal gaming age.
  - [x] Do not assume all relevant competitors are located in the same state or even the same country. *(The first live market snapshot spans four states; provider and origin country/state fields remain generic for future international border markets.)*
- [x] Treat Indiana-specific public reports as benchmark and validation cases only.
  - [x] Spectrum Gaming Group, CBRE/Union Gaming Analytics, and A.M. Steinberg Advisors are useful methodological references and validation anchors.
  - [x] Their site-specific outputs are not universal model constants.
- [ ] Do not mark a checklist item complete merely because code was written.
  - [ ] A data task is complete only after ingestion, provenance, validation, persistence, and reproducibility are complete.
  - [ ] A model task is complete only after tests, calibration/validation output, documented assumptions, and failure handling exist.
  - [ ] A UI task is complete only after the server integration works and the user can distinguish defaults, calibrated values, overrides, and outputs.
  - [ ] A report task is complete only when a stored `ModelRun` can reproduce the same report deterministically.
- [x] Do not hard-code a coefficient solely because a public consultant used it.
  - [x] Public consultant assumptions are priors and validation anchors, not automatic truth.
  - [x] Any adopted coefficient must have a source, calibration rationale, validation result, sensitivity range, or a documented combination of these.
- [ ] Do not tune the model only until it reproduces one desired consultant result.
  - [ ] It must explain differences across multiple benchmark markets.
  - [ ] It must validate against actual incumbent performance where public data exist.
  - [ ] It must behave sensibly at deliberately strong and weak candidate sites.
- [ ] Never hide manual overrides or silently replace user inputs.
- [ ] Every economically meaningful model parameter must have:
  - [ ] a system/calibrated default;
  - [ ] units;
  - [ ] a description;
  - [ ] provenance/calibration notes;
  - [ ] a validated or recommended range when one exists;
  - [ ] a hard computational bound if necessary for numerical safety;
  - [ ] an explicit override policy;
  - [ ] a UI exposure level such as standard, advanced, or expert.
- [ ] Preserve reproducibility.
  - [ ] Every production run must identify model version, parameter set, all user overrides, data vintages, candidate location, development program, jurisdiction profile, route graph/version, and run timestamp.
- [ ] Preserve auditability.
  - [ ] Every displayed revenue number must be traceable to origin demand, facility attraction, travel friction, choice share, market expansion, tourism/traffic additions, and subsequent downstream adjustments.
- [ ] Preserve separation of concerns.
  - [ ] Demand generation, gravity allocation, facility attractiveness, tourism, traffic intercept, cannibalization accounting, economic displacement, fiscal rules, social costs, report generation, and UI presentation must not collapse into one monolithic service.

---

# 1. Verified current repository foundation and deficiencies

## 1.1 Existing foundation already present

- [x] A persistent `casino_competitors` entity/table exists.
- [x] `CasinoCompetitorSeeder` exists with a starter set of Indiana, Michigan, and Ohio properties.
- [x] `CompetitionScoringService` exists.
- [x] `RevenueHeuristicService` exists.
- [x] `ZipSwitchingModelService` exists and implements a prototype origin-to-casino multinomial share calculation.
- [x] `RevenueController` exposes prototype revenue endpoints.
- [x] Valhalla routing/isochrone infrastructure exists elsewhere in the project.
- [x] Census/block-group spatial infrastructure exists.
- [x] The existing application already models location-sensitive downstream social/economic costs.
- [x] A sector-weighted displacement concept exists and must be retained and materially improved.

## 1.2 Deficiency: current revenue heuristic is not a gravity model

- [x] Retire `RevenueHeuristicService` as the primary revenue estimator after the gravity engine is production-ready.
- [x] Preserve only useful explainability/site-quality concepts if desired.
- [x] Remove or replace these structures from production revenue estimation:
  - [x] fixed distance penalties from downtown Fort Wayne;
  - [x] fixed 30-mile and 50-mile Haversine thresholds;
  - [x] market depth based on approximate degree-radius queries;
  - [x] `population × 0.75` as an adult population proxy;
  - [x] fixed $65,000 income normalization;
  - [x] fixed `benchmarkDepth = 400000`;
  - [x] `accessScore × depthScore - competitionPenalty` as a revenue multiplier;
  - [x] arbitrary high/moderate/low revenue-potential cutoffs.
- [x] If retained, expose the heuristic only as a diagnostic and never as the gravity-model GGR forecast.

## 1.3 Deficiency: competition scoring is hand-weighted and market-center-centric

- [x] Replace hand-set values such as full-service casino `1.00`, racino `0.70`, hotel `+0.15`, tables `+0.20`, etc. as the principal competitive-mass mechanism.
- [x] Do not infer competitive overlap from each competitor's distance to one central city.
- [x] Calculate competition from every modeled origin to every relevant facility.
- [x] Use network travel time as primary travel friction.
- [x] Use observed and/or structurally calibrated facility scale for attraction.
- [x] Preserve simple feature scores only as fallback diagnostics or explanatory metadata.

## 1.4 Deficiency: current ZIP switching model is an uncalibrated prototype

- [x] Keep the useful origin-to-facility share concept but replace unsupported defaults and mechanics.
- [x] Replace Haversine miles with cached network travel time and network distance.
- [x] Replace unsupported defaults such as:
  - [x] `ParticipationRate = 0.28`;
  - [x] `AnnualGgrPerParticipant = 1200`;
  - [x] `DistanceBeta = 0.06` in a linear utility specification;
  - [x] arbitrary proposed venue quality of `1.0`.
- [x] Do not force the full modeled demand pool across an incomplete list of casinos.
  - [x] Build a sufficiently complete competitive field and/or a calibrated outside option.
- [x] Use numerically stable share calculations.
- [x] Do not derive incumbent attractiveness solely from current feature-addition scoring.
- [x] Do not use request-body ZIP inputs as the long-term production source of market demand.
  - [x] Production inputs must come from versioned persisted datasets.
  - [x] API request overrides may remain for testing and expert scenarios.

## 1.5 Deficiency: current data model is too shallow for a national mass-weighted model

- [x] Expand competitor records beyond boolean amenities.
- [x] Add historical observed performance.
- [x] Add physical/development scale.
- [x] Add jurisdiction/regulator identity.
- [x] Add source-level provenance per material attribute where feasible.
- [x] Replace short hand-entered competitor lists with rule-driven regional competitive-universe assembly.

## 1.6 Deficiency: current architecture is too Indiana-specific for the intended product

- [x] Extract jurisdiction-specific rules from core services.
- [x] Replace hard-coded Indiana tax assumptions with effective-dated jurisdiction profiles.
- [x] Replace hard-coded 21+ assumptions with legal-gaming-age-aware population calculations.
- [x] Replace fixed county/state patron-origin report categories with dynamic geographic aggregation.
- [x] Treat Indiana as the first jurisdiction adapter and validation case, not the model definition.

---

# 2. Public benchmark studies and validation cases

These studies are required methodological references for the initial Indiana validation suite. They do not define the national model architecture.

## 2.1 Spectrum Gaming Group: Indiana Gaming Commission relocation study

Primary source:

- `https://www.in.gov/igc/files/publications/Spectrum-Relocation-Report-to-Indiana-Gaming-Commission-9-30-2025-Final.pdf`

- [x] Understand Spectrum's public-data demand construction.
  - [x] ZIP-level adjusted gross income was used to estimate gaming-market potential.
  - [x] National casino-revenue-to-AGI reference was approximately 0.58% using cited 2022 data.
  - [x] Indiana mature-market reference was approximately 0.66%.
  - [x] Treat these as benchmark priors, not immutable constants.
- [x] Understand Spectrum's data advantage.
  - [x] It obtained rated/tracked play by ZIP from Indiana operators through the Indiana Gaming Commission.
  - [x] This project does not possess equivalent patron-level operator data unless lawfully obtained later.
- [x] Retain Spectrum's drive-time and capture concepts.
- [x] Retain its Northeast Indiana proxy result as a validation anchor, not a forced target.

## 2.2 CBRE / Union Gaming Analytics: Greater Fort Wayne Area Casino Analysis

Primary source:

- `https://cdn.insideindianabusiness.com/wp-content/uploads/2026/01/GFWI-Casino-Analysis-Presentation-Final-2025-12-03.pdf`

- [x] Retain its public gravity-model concepts:
  - [x] population;
  - [x] income;
  - [x] project and competitor attractiveness/development scale;
  - [x] distance/travel friction.
- [x] Retain its development-program concept.
  - [x] The proposed development program must affect attraction independently of latitude/longitude.
- [x] Retain separate out-of-market highway traffic demand.
- [x] Retain stabilization/ramp analysis.
- [x] Retain independent regression/comparable-market reasonableness testing.
- [x] Retain its published Northeast Indiana outputs and competitor impacts as validation targets, not required outcomes.

## 2.3 A.M. Steinberg Advisors: Steuben County Gaming Market Feasibility Study

Primary source:

- `https://www.steubenedc.com/media/userfiles/subsite_259/files/SCEDC_Feasibility_Study_FINAL.pdf`

- [x] Retain its explicitly described mass-weighted gravity concepts:
  - [x] projected casino-eligible adult population;
  - [x] income-adjusted gaming expenditure;
  - [x] travel-time/distance decay;
  - [x] base beta around `1.5`;
  - [x] sensitivity around `1.4` to `1.6`;
  - [x] incumbent competitive mass materially informed by observed GGR;
  - [x] broad competitive inclusion;
  - [x] separate tourism demand.
- [x] Treat beta `1.5` as an initial prior/default candidate for the Indiana base parameter set, not a universal national constant.
- [x] Retain its low/base/high revenue outputs as Indiana validation benchmarks.

## 2.4 Benchmark-study reconciliation

- [x] Build a benchmark registry that can hold any public or private study used for validation.
- [x] Store:
  - [x] benchmark ID;
  - [x] market/geography;
  - [x] study date;
  - [x] consultant/source;
  - [x] candidate location;
  - [x] development program;
  - [x] reported revenue outputs;
  - [x] reported model assumptions;
  - [x] methodological notes;
  - [x] source URL/file provenance.
- [x] Explain differences rather than forcing equality.
- [x] Allow future benchmark suites for other states and markets without code changes.

---

# 3. National jurisdiction abstraction

## 3.1 Create jurisdiction profiles

- [x] Create `jurisdictions` and effective-dated `jurisdiction_rules` or equivalent.
- [x] Support at minimum:
  - [x] federal/national context;
  - [x] state;
  - [x] county/parish/borough where applicable;
  - [x] municipality where fiscal sharing depends on local location;
  - [x] tribal jurisdiction/compact context where applicable.
- [x] A jurisdiction profile must not assume that every casino is a commercial state-regulated casino.

## 3.2 Required jurisdiction rule fields

- [x] Legal gaming age by facility/regime where applicable.
- [x] Gaming product types permitted.
- [x] Applicable gaming revenue definition.
- [x] Gaming/wagering tax rates and brackets.
- [x] Promotional-credit/free-play treatment.
- [ ] Admission or device taxes where applicable.
- [x] Local revenue-sharing rules.
- [x] State/local sales tax treatment of non-gaming revenue.
- [x] State/local income or business tax assumptions relevant to impact analysis.
- [x] Effective dates for every fiscal rule.
- [x] Source/provenance links.
- [ ] Tribal compact or revenue-sharing treatment where public and applicable.

## 3.3 Jurisdiction provider/adaptor pattern

- [x] Implement jurisdiction fiscal rules behind a service interface rather than giant `switch(state)` logic.
- [x] Example conceptual services:
  - [x] `IJurisdictionProfileService`;
  - [x] `IGamingTaxCalculator`;
  - [x] `ILocalRevenueShareCalculator`;
  - [x] `IGamingAgeResolver`.
- [ ] Implement Indiana first. *(Legal gaming age, commercial-casino/racino base schedules, and the enacted northeast supplemental tax plus host recipient distribution are validated and remotely verified. Historical incumbent supplemental quotients, recipient-level statewide set-asides outside the northeast host allocation, admission/device taxes, and other production fiscal rules remain incomplete.)*
- [x] Make adding a new state primarily a data/configuration exercise unless the state's rules genuinely require custom logic.
- [x] Throw a clear unsupported-jurisdiction warning when fiscal rules are incomplete rather than applying Indiana defaults.

---

# 4. Model terminology and accounting identities

## 4.1 Do not use GGR and AGR interchangeably

- [x] Define and use consistently:
  - [x] **GGR / casino win:** patron wagers minus gaming payouts before jurisdiction-specific taxable adjustments.
  - [x] **Taxable gaming revenue/base:** jurisdiction-defined amount used for gaming-tax calculation.
  - [x] **Non-gaming revenue:** hotel, food and beverage, entertainment, retail, and other property revenue.
  - [x] **Total property revenue:** gaming plus non-gaming revenue.
- [x] Resolve terminology through the selected jurisdiction profile.
- [x] Do not label a generic national output `AGR` if the underlying jurisdiction uses another statutory definition.

## 4.2 Origin/facility notation

- [x] `i` = origin zone.
- [x] `j` = casino/facility alternative.
- [x] `D_i` = annual resident gaming-expenditure pool generated by origin `i`.
- [x] `T_ij` = network travel time from origin `i` to facility `j`.
- [x] `L_ij` = network travel distance.
- [x] `A_j` = calibrated attraction/competitive mass of facility `j`.
- [x] `F_ij` = travel-friction function.
- [x] `W_ij` = unnormalized attraction weight.
- [x] `P_ij` = modeled share/probability of origin `i` gaming expenditure allocated to facility `j`.
- [x] `R_j,resident` = resident GGR captured by facility `j`.
- [x] `R_j,tourism` = incremental tourism GGR.
- [x] `R_j,traffic` = incremental through-traffic/intercept GGR.
- [x] `R_j,total` = stabilized total GGR.

---

# 5. Data provenance and reproducibility

## 5.1 Create a source catalog

- [x] Add `data_sources`.
- [x] Include:
  - [x] ID;
  - [x] name;
  - [x] publisher;
  - [x] URL;
  - [x] source type;
  - [x] geographic coverage;
  - [x] vintage/period;
  - [x] retrieved timestamp;
  - [x] license/terms notes;
  - [x] content hash;
  - [x] authoritative-source flag;
  - [x] notes.

## 5.2 Create immutable dataset snapshots

- [x] Add `dataset_snapshots`.
- [x] Store source, period, ingestion time, row count, checksum, transform version, validation state, and warnings/errors.
- [x] Never overwrite a dataset used by a prior model run without preserving the original snapshot identity.

## 5.3 Model runs must reference exact data

- [x] Add immutable `model_runs`.
- [x] Store:
  - [x] run UUID;
  - [x] model version;
  - [x] jurisdiction profile/version;
  - [x] base parameter-set ID/version;
  - [x] resolved parameter values after all overrides;
  - [x] override audit records;
  - [x] scenario ID;
  - [x] site coordinates;
  - [x] development-program ID/version;
  - [x] origin-demographic snapshot;
  - [x] income/AGI snapshot;
  - [x] competitor snapshot;
  - [x] observed-performance snapshot;
  - [x] travel-matrix graph/version hash;
  - [x] tourism/traffic snapshot IDs;
  - [x] economic/social-cost assumption versions;
  - [x] creation timestamp;
  - [x] execution duration;
  - [x] warning/error summary.

---

# 6. National origin geography and patron-market definition

## 6.1 Use flexible origin geographies

- [x] Support ZCTA/ZIP-compatible origin zones as a primary U.S. demand geography.
- [x] Do not assume USPS ZIP Codes and Census ZCTAs are identical.
- [x] Retain Census block groups for higher-resolution demographic allocation.
- [x] Support tract/county aggregation where source data or performance requires it.
- [x] Design origin IDs generically enough to support future non-U.S. border-market data if needed.

## 6.2 Create `origin_zones`

- [x] Store:
  - [x] origin ID/type;
  - [x] geography code;
  - [x] state/territory;
  - [x] county/parish equivalents;
  - [x] MSA/CSA or other regional identifiers where available;
  - [x] representative population-weighted point;
  - [x] area geometry;
  - [x] source snapshot IDs.

## 6.3 Use legal-gaming-age-aware population

- [x] Do not globally hard-code age 21+.
- [x] Resolve the relevant legal gaming age from the facility/jurisdiction scenario.
- [x] Derive eligible population for common thresholds such as 18+ and 21+ from ACS age bins.
- [x] Preserve raw age-bin data and interpolation method.
- [ ] Validate totals against county/state controls.
- [ ] Support projection to scenario year using explicit population-growth assumptions and data sources.

## 6.4 Dynamic patron-origin reporting

- [x] Patron-origin analysis must be generated from the actual stored model run.
- [x] Do **not** hard-code report categories such as Allen County, DeKalb County, Steuben County, Rest of Indiana, Michigan, Ohio, Other.
- [x] Generate origin summaries dynamically using relevant dimensions:
  - [x] top origin counties/parishes by modeled GGR;
  - [x] top origin ZIP/ZCTA zones;
  - [x] state/territory totals;
  - [x] host county and host state;
  - [x] host MSA/CSA where appropriate;
  - [x] in-jurisdiction vs out-of-jurisdiction;
  - [x] in-state vs out-of-state for state-regulated projects;
  - [x] cross-border international origins when relevant;
  - [x] resident vs tourism vs through-traffic components.
- [x] Use configurable top-N thresholds and group immaterial residual origins as `Other origins` only after preserving the full detail in data/export.
- [x] For the current Indiana scenario, Allen, DeKalb, Steuben, Michigan, and Ohio may naturally appear because they contribute material demand. They must appear because the model finds them, not because report code knows their names.

## 6.5 Optional higher-resolution block-group modeling and ZIP/ZCTA reporting

Block-group resolution is a supported analytical mode, not a mandatory universal internal geography. Use it when the additional spatial precision is supported by the available inputs, validation results, and acceptable runtime. Do not force ZIP/ZCTA-native data through a synthetic block-group allocation merely for apparent precision.

### Origin-resolution governance

- [ ] Make origin resolution explicit and versioned in the scenario/model-run configuration.
- [ ] Support at minimum:
  - [ ] ZCTA/ZIP-compatible origins for nationally available ZIP-level demand inputs such as IRS SOI AGI;
  - [ ] Census block-group origins for higher-resolution demographic and accessibility analysis;
  - [ ] tract/county origins where data availability or performance warrants coarser resolution.
- [ ] Do not make raw Census blocks the primary demand-origin target unless a later validated requirement justifies the added complexity.
- [ ] Do not use Valhalla isochrone grid points or other arbitrary routing grid marks as demand origins.
- [ ] Select a production default resolution from empirical validation and performance evidence rather than assuming finer geography is automatically more accurate.
- [ ] Permit different validated demand specifications to use different native source resolutions while preserving a common aggregation/reporting contract.

### Block-group analytical mode

- [ ] Define the canonical Census/block-group source tables and snapshot versions used when block-group mode is active.
- [ ] Use a defensible representative point for each block group, preferably population-weighted where feasible rather than a naive geometric centroid when the distinction is material.
- [ ] Derive legal-gaming-age-eligible population at the block-group level from the same age-bin methodology used elsewhere in this plan.
- [ ] Apply scenario-year population projections consistently at block-group level when block-group mode is selected.
- [ ] Preserve provenance for any income, gaming-intensity, or other demand modifier allocated from a coarser geography to block groups.
- [ ] Do not imply that a coarse ZIP-level variable becomes genuinely block-group-specific merely because it was allocated downward.
- [ ] Calculate origin-to-proposed-site and origin-to-incumbent travel impedance using the same network-travel framework as all other origin resolutions.
- [ ] Run the actual gravity/share calculation at block-group level when block-group mode is selected, then aggregate outputs upward only after origin-level allocation is complete.

### ZIP/ZCTA crosswalk and aggregation

- [ ] Treat USPS ZIP Codes and Census ZCTAs as distinct concepts and name them accurately in technical outputs.
- [ ] Define and version the authoritative crosswalk used to map block groups into ZIP/ZCTA-compatible reporting.
- [ ] Document boundary cases in which a block group intersects more than one ZIP/ZCTA reporting area.
- [ ] If a many-to-many allocation is required, define the weighting basis explicitly, such as population-weighted or residential-address-weighted allocation where supported.
- [ ] Preserve enough crosswalk detail to reproduce every reported ZIP/ZCTA total from the underlying origin results.
- [ ] Aggregate block-group results to:
  - [ ] ZIP/ZCTA-compatible summaries;
  - [ ] county/parish equivalents;
  - [ ] state/territory;
  - [ ] configured local/regional study areas;
  - [ ] national or cross-border totals where applicable.
- [ ] Require all aggregation levels to reconcile mathematically to the same underlying model-run totals, subject only to explicitly documented rounding.

### API and data-contract requirements

- [ ] Design model-result contracts so the same backend can return summary and detailed origin results without separate economic formulas.
- [ ] Make ZIP/ZCTA-compatible summaries the normal human-readable reporting payload where appropriate.
- [ ] Support optional block-group detail for audit, expert analysis, and drill-down when the underlying run used block-group origins.
- [ ] Decide whether block-group detail is:
  - [ ] returned inline for small result sets;
  - [ ] paged;
  - [ ] fetched on demand when a ZIP/ZCTA is expanded;
  - [ ] available only through a detailed export for very large runs.
- [ ] Preserve complete underlying origin results in the immutable `ModelRun` or associated result tables even when the default API/UI response is aggregated.
- [ ] Do not expose synthetic block-group detail when the underlying model actually ran at ZIP/ZCTA resolution.

### Web and report presentation

- [ ] Use ZIP/ZCTA-compatible summaries as the default public-facing origin view when they provide the clearest understandable geography.
- [ ] Allow expandable ZIP/ZCTA rows, cards, map selections, or equivalent drill-down to contributing block groups when the underlying run supports it.
- [ ] Show enough block-group detail to audit a result without overwhelming the normal interface.
- [ ] Do not expose raw Census blocks in the initial drill-down design.
- [ ] Clearly state the actual computational origin resolution used by the run.
- [ ] Clearly distinguish:
  - [ ] computational origin geography;
  - [ ] source-data geography;
  - [ ] display/reporting geography.
- [ ] If a value was allocated from a coarser geography before block-group modeling, disclose that limitation in methodology text rather than implying unsupported precision.

### Performance and validation

- [ ] Benchmark ZCTA/ZIP-compatible, block-group, and any coarser supported origin modes on representative urban, suburban, and rural markets.
- [ ] Measure:
  - [ ] origin count;
  - [ ] origin-facility route count;
  - [ ] travel-matrix storage;
  - [ ] model execution time;
  - [ ] memory usage;
  - [ ] API payload size;
  - [ ] UI drill-down latency where applicable.
- [ ] Compare a ZIP/ZCTA-native run with a block-group run aggregated back to ZIP/ZCTA for the same market and parameter set.
- [ ] Quantify whether the finer resolution materially improves:
  - [ ] incumbent back-testing;
  - [ ] candidate-site differentiation;
  - [ ] patron-origin accuracy;
  - [ ] boundary-area treatment;
  - [ ] downstream fiscal/social-impact allocation.
- [ ] Do not make block-group mode the default merely because it produces different results; require demonstrable validation or analytical value that justifies the added computational cost and any down-allocation assumptions.
- [ ] Add reconciliation tests confirming:
  - [ ] block-group totals aggregate correctly to ZIP/ZCTA;
  - [ ] ZIP/ZCTA totals aggregate correctly to county/state/region totals;
  - [ ] projected eligible population is applied consistently at the selected origin resolution;
  - [ ] UI/report drill-down totals reconcile with summary totals;
  - [ ] crosswalk edge cases do not duplicate or omit demand.
- [ ] Document known limitations including centroid/representative-point assignment, ZIP/ZCTA crosswalk uncertainty, source-resolution mismatch, and any allocated-down demand variables.

---

# 7. Resident demand models

A professional-grade model must not depend on one unexplained demand formula.

## 7.1 Specification A: AGI-share demand model

- [x] Implement:

```text
D_i_AGI = RealIncomeMass_i × GamingIncomeShare_region × OriginAdjustment_i
```

- [x] Use IRS SOI ZIP-level AGI for U.S. origins where available.
- [x] Treat Spectrum's national and Indiana ratios as initial benchmark priors, not universal constants.
- [x] Allow state/region-specific gaming intensity.
- [x] Calibrate against observed market gaming revenue where possible.
- [x] Prevent double income weighting.

## 7.2 Specification B: eligible-adult per-capita expenditure model

- [x] Implement:

```text
D_i_PCE = EligibleAdults_i × BaseGamingExpenditurePerAdult × IncomeAdjustment_i
IncomeAdjustment_i = (IncomeMetric_i / RegionalReferenceIncome)^epsilon_income
```

- [x] Resolve `EligibleAdults_i` using the scenario's gaming-age rule.
- [x] Calibrate `BaseGamingExpenditurePerAdult` by market/regime.
- [x] Make `epsilon_income` a versioned configurable parameter.
- [x] Bound extreme origin adjustments.
- [x] Treat consultant per-adult values only as priors/benchmarks.

## 7.3 Demand-model reconciliation

- [ ] Produce both demand specifications for validation runs.
- [ ] Compare total demand, state totals, distance bands, and largest origin differences.
- [ ] Select base specification based on validation performance.
- [ ] Support optional validated ensemble with versioned weights.
- [ ] Never add two alternative demand specifications together as if they represent separate demand pools.

---

# 8. Competitive casino universe

## 8.1 Define inclusion rules before collecting properties

- [x] Build a competitive field based on origin accessibility, not simply distance from the proposed site.
- [x] Include commercial casinos, racinos, tribal casinos, and sufficiently substitutable facilities.
- [x] Treat sportsbook-only, OTB, charity gaming, distributed gaming, and other limited products separately unless evidence supports material substitution with casino-floor GGR.
- [x] Include facilities outside the host state when they compete for the same origins.
- [x] Permit Canada/Mexico or other cross-border facilities when they materially compete with a U.S. border market.
- [x] Include a geographic/attraction margin beyond the nominal candidate trade area or calibrate an outside option.

## 8.2 Expand `casino_competitors`

- [x] Add stable identity and regulatory fields.
- [x] Add commercial/tribal/racino status.
- [x] Add regulator/jurisdiction.
- [x] Add opening/closure/operator-change history.
- [x] Add physical scale:
  - [x] slots/VLT positions;
  - [x] table games;
  - [x] poker where material;
  - [x] gaming floor size;
  - [x] hotel rooms;
  - [x] event/entertainment capacity;
  - [x] food/beverage scale;
  - [x] resort/spa/golf/destination amenities;
  - [x] development cost and dollar year when public.
- [x] Add access/context:
  - [x] interstate/limited-access proximity;
  - [x] interchange access;
  - [x] urban/local/destination orientation;
  - [x] border-market indicator.

## 8.3 Create observed performance history

- [x] Create `casino_gaming_revenue_periods`.
- [x] Store monthly data when available and derive annual/trailing values.
- [x] Store exact metric definition because states report GGR/AGR/win differently.
- [x] Store source snapshot and inflation-adjusted values.
- [x] Flag pandemic, construction, opening, closure, labor-disruption, and other anomalous periods.
- [x] Build regulator/provider adapters by jurisdiction rather than one Indiana-only ingestion service.
- [x] Prefer authoritative gaming regulators and tribal/public filings where available.

---

# 9. Network travel-time matrix

## 9.1 Use Valhalla as primary travel friction

- [x] Build origin-to-facility network travel times.
- [x] Use Haversine distance only for prefiltering, fallback diagnostics, and tests.
- [x] Capture travel time and routed distance.
- [x] Use a consistent automobile routing profile unless scenario-specific evidence requires another profile.

## 9.2 Persist the matrix

- [x] Create `origin_facility_travel`.
- [x] Key by origin, facility/scenario facility, routing graph hash/version, and costing profile.
- [x] Store minutes, distance, route-found flag, and timestamp.
- [x] Precompute stable incumbent routes.
- [x] For a movable proposed site, compute/cached only the new facility column where practical.

## 9.3 National performance requirements

- [x] Do not precompute the entire United States against every casino if not needed.
- [x] Determine a candidate study region from broad Haversine/accessibility filters, then compute exact routes for relevant origins/facilities.
- [ ] Cache reusable matrices by regional tile/market.
- [ ] Support offline/background matrix warming for likely candidate grids.

---

# 10. Facility attractiveness and competitive mass

## 10.1 Replace arbitrary feature-addition weights

- [x] Do not use `1.00 + 0.15 + 0.20` style scoring as the production attraction model.
- [x] Build `FacilityAttractivenessService`.
- [x] Separate physical/development attraction from travel friction.
- [x] Avoid double counting correlated scale measures.

## 10.2 Structural physical-mass specification

- [ ] Estimate facility attraction from observable features.
- [ ] Candidate variables may include:
  - [ ] gaming positions;
  - [ ] table-game breadth;
  - [ ] hotel rooms;
  - [ ] entertainment/event capacity;
  - [ ] resort amenities;
  - [ ] capital/development scale;
  - [ ] direct highway access;
  - [ ] defensible brand/loyalty proxy if available.
- [ ] Fit coefficients against observed incumbent performance/market shares.
- [ ] Save coefficients in the model parameter system.
- [ ] Normalize attraction to a reference facility for interpretability.

## 10.3 Observed-GGR incumbent-mass specification

- [x] Implement a second attraction specification where incumbent competitive mass is anchored materially to stabilized observed gaming revenue.
- [x] Do not define proposed-casino mass from the same projected GGR the model is solving for.
- [x] Map the proposed development program to equivalent competitive mass using comparable-property relationships.
- [ ] Use this specification as primary or reconciliation model based on validation performance.

## 10.4 Proposed development program

- [x] Create versioned `development_programs`.
- [x] Include:
  - [x] slots/VLTs;
  - [x] tables;
  - [x] poker if applicable;
  - [x] sportsbook;
  - [x] hotel rooms;
  - [x] gaming floor size;
  - [x] restaurants/bars;
  - [x] entertainment/event capacity;
  - [x] resort amenities;
  - [x] announced/estimated capital cost;
  - [x] opening/stabilization assumptions.
- [x] Let users configure the proposed program independently from the site.

---

# 11. True gravity/Huff allocation model

## 11.1 Primary attraction equation

- [x] Support an inverse-power Huff/gravity formulation:

```text
W_ij = A_j^alpha × B_ij / (T_ij + t0)^beta
```

where:

- `A_j` = facility attraction/competitive mass;
- `alpha` = attraction elasticity;
- `B_ij` = evidence-based origin-facility modifiers;
- `T_ij` = network travel time;
- `t0` = positive regularization constant if required;
- `beta` = travel-time decay.

- [x] Support an exponential-decay alternative for validation:

```text
W_ij = A_j^alpha × B_ij × exp(-lambda × T_ij)
```

- [ ] Select production friction form based on validation, not preference.

## 11.2 Market-share equation

- [x] Calculate:

```text
P_ij = W_ij / (W_i0 + Σ_k W_ik)
```

- [x] `W_i0` is the optional outside/unmodeled alternative.
- [x] Ensure all shares are non-negative and sum to 1 including outside share.
- [x] Use numerically stable denominator math.

## 11.3 Distance-decay beta

- [ ] Give beta a calibrated system default.
- [x] For the initial Northeast Indiana base parameter set, beta around `1.5` may be seeded from the Steinberg study before calibration.
- [x] Test the published `1.4` to `1.6` range and expand validation search where needed. *(The live search expanded beyond this range; its holdout result still fails the production-quality gate.)*
- [ ] Determine whether one beta is adequate nationally.
- [ ] Support market/facility-segment parameter sets when evidence supports materially different travel behavior.
- [x] **Expose beta as a front-end user-overridable parameter.**
- [x] Show the default, current value, validated/recommended range, units/interpretation, and source/calibration note.
- [x] Permit values outside the validated range unless a numerical-safety bound is crossed, but show a visible warning in the UI and report.

## 11.4 Attraction elasticity and facility weights

- [ ] Give `alpha` and all active facility-attractiveness coefficients calibrated defaults.
- [x] **Expose active facility weights/coefficients in the advanced/expert front-end parameter panel.**
- [x] Do not force ordinary users to edit raw regression coefficients in the standard workflow.
- [x] Permit expert override and preserve every override in the model run.
- [x] Flag out-of-calibration-range values without silently resetting them.

---

# 12. First-class model parameter system

This is a required production subsystem, not a developer-only configuration file.

## 12.1 Create a parameter catalog

- [x] Create `model_parameter_definitions` or equivalent.
- [x] Each definition must include:
  - [x] stable parameter key;
  - [x] category;
  - [x] display name;
  - [x] technical description;
  - [x] plain-language interpretation;
  - [x] units;
  - [x] data type;
  - [x] system fallback default;
  - [x] hard min/max for computational safety if required;
  - [x] validated/recommended low/high range where applicable;
  - [x] UI step/precision;
  - [x] UI exposure level: standard, advanced, expert, hidden/internal;
  - [x] user-overridable flag;
  - [x] model-version applicability;
  - [x] provenance/calibration notes.

## 12.2 Create versioned parameter sets

- [x] Add `model_parameter_sets` and `model_parameter_set_values` or equivalent.
- [x] Support:
  - [x] national/base calibrated sets;
  - [x] jurisdiction/market-specific calibrated sets;
  - [x] conservative/base/high scenario presets;
  - [x] benchmark-study parameter sets for validation;
  - [x] experimental sets.
- [x] A parameter set must be immutable once referenced by a finalized model run.
- [x] A changed calibration creates a new version.

## 12.3 Parameter precedence

- [x] Resolve parameters in an explicit precedence order such as:

```text
System fallback
    < National calibrated set
    < Jurisdiction/market calibrated set
    < Scenario preset
    < User override
```

- [x] Persist the final resolved value and the source layer for every parameter.
- [x] Do not make reports reconstruct parameter precedence after the fact.

## 12.4 Required overrideable parameter families

- [x] Gravity/travel:
  - [x] beta;
  - [x] alpha;
  - [x] exponential lambda if that specification is active;
  - [x] outside-option parameter(s);
  - [x] regularization `t0` where exposed.
- [x] Demand:
  - [x] gaming-income share;
  - [x] base per-eligible-adult gaming spend;
  - [x] income elasticity;
  - [x] regional/state intensity adjustments.
- [x] Facility attraction:
  - [x] structural feature coefficients/weights;
  - [x] proposed property scale adjustment;
  - [x] comparable-property scaling assumptions.
- [x] Market expansion:
  - [x] accessibility-induced-demand elasticity;
  - [x] maximum induced-demand cap if used.
- [x] Tourism/traffic:
  - [x] tourism participation/capture;
  - [x] traffic intercept rate;
  - [x] eligible vehicle/passenger assumptions;
  - [x] overlap/deduplication factors.
- [x] Ramp/stabilization:
  - [x] first-year percentage;
  - [x] second-year percentage;
  - [x] stabilized year.
- [x] Displacement:
  - [x] local share;
  - [x] displacement coefficient;
  - [x] sector priors;
  - [x] taxability/margin assumptions where not jurisdiction-derived.
- [x] Social/economic cost:
  - [x] prevalence assumptions;
  - [x] exposure/risk-response coefficients;
  - [x] per-case cost assumptions;
  - [x] crime/public-safety/productivity parameters when modeled.

## 12.5 Override audit trail

- [x] Create `model_run_parameter_values` or equivalent snapshot table.
- [x] Store for every parameter:
  - [x] default/base value;
  - [x] scenario preset value if any;
  - [x] user override if any;
  - [x] final value used;
  - [x] source layer;
  - [x] validated-range status;
  - [x] warning text if outside the recommended range.
- [x] The report must include an appendix table of overridden parameters.

---

# 13. Front-end parameter and scenario controls

## 13.1 Standard controls

- [x] Provide understandable controls for common scenario decisions without exposing raw model internals by default.
- [x] Examples:
  - [x] proposed facility size/program;
  - [x] travel sensitivity preset;
  - [x] gaming-demand intensity;
  - [x] tourism contribution;
  - [x] traffic-intercept contribution;
  - [x] local patron share;
  - [x] displacement severity;
  - [x] social-cost/prevalence scenario;
  - [x] ramp/stabilization scenario.

## 13.2 Advanced model parameters

- [x] Add expandable `Advanced Model Parameters` UI.
- [x] Expose beta directly.
- [x] Expose alpha directly.
- [x] Expose active facility-attractiveness coefficients/weights.
- [x] Expose demand elasticity and outside-option controls.
- [x] Expose other calibrated technical parameters whose override materially changes results.
- [x] For every parameter show:
  - [x] model default/base value;
  - [x] current scenario value;
  - [x] recommended/validated range;
  - [x] interpretation;
  - [x] provenance/calibration reference.

## 13.3 Override behavior

- [x] User changes must trigger model recalculation through the same backend engine used for default scenarios.
- [x] Never implement a front-end-only multiplier that bypasses the model.
- [x] Show changed/overridden state clearly.
- [ ] Provide `Reset to calibrated defaults`. *(The UI resets to the selected versioned parameter-set defaults; this item remains open until those defaults pass the calibration gate.)*
- [x] Provide `Reset this section` where useful.
- [x] Allow user values outside recommended ranges unless unsafe.
- [x] Show clear warning for values outside validation range.
- [x] Include the warning in generated reports.

## 13.4 Presets and scenario comparison

- [ ] Support at minimum:
  - [ ] calibrated/base; *(A clearly disclosed provisional base preset exists; the calibrated designation remains open.)*
  - [x] conservative;
  - [x] high/aggressive;
  - [x] custom.
- [x] Allow side-by-side comparison of multiple model runs.
- [x] Preserve each comparison scenario as its own immutable run.
- [x] Allow export/import of scenario definitions as versioned JSON where practical.

---

# 14. Outside option and incomplete market capture

- [x] Do not omit an outside option simply because the prototype omitted it.
- [x] Use it to represent unmodeled relevant supply/leakage only when needed.
- [x] Do not use it as an unexplained balancing plug.
- [ ] Calibrate it against observed market totals and/or holdout properties.
- [ ] Allow the outside-option parameter to vary by market segment/region when validation supports it.
- [x] Make expert override possible and auditable through the front-end control, backend parameter-resolution, and stored-run audit trail.

---

# 15. Accessibility-induced market expansion

A fixed share model redistributes a constant gaming pool. Improved access may also increase gaming frequency/participation.

- [x] Compute baseline accessibility for each origin using incumbent facilities.
- [x] Compute with-project accessibility after adding the proposed casino.
- [x] Estimate induced demand as a separate, transparent layer.
- [x] Do not bury induced demand inside beta or facility attraction.
- [ ] Calibrate accessibility-expansion elasticity where possible.
- [x] Expose it as an advanced overrideable front-end/backend parameter.
- [x] Report resident demand as:
  - [x] baseline resident gaming pool;
  - [x] redistributed/captured amount;
  - [x] induced incremental resident gaming amount.

---

# 16. Tourism and destination demand

- [x] Model tourism separately from resident demand to prevent double counting.
- [x] Build pluggable tourism inputs because relevant tourism data differ by market.
- [x] Candidate sources include state/local tourism agencies, lodging statistics, park/lake visitation, airport volumes, convention/event data, and other defensible sources. *(The first live Indiana adapter uses the state tourism agency's Rockport person-trip series; scenario-local sources remain pluggable rather than assumed.)*
- [x] Define visitor-days/person-trips rather than applying one arbitrary annual tourist count.
- [x] Estimate casino-eligible visitor share, participation, capture, and spend.
- [x] Deduplicate visitors already represented as resident origins.
- [x] Allow front-end/backend user overrides for tourism capture assumptions.
- [x] Report tourism GGR separately.

---

# 17. Highway and through-traffic intercept

- [x] Build a separate traffic-intercept module.
- [x] Use relevant federal/state/local traffic datasets through jurisdiction-specific providers, not Indiana-specific core-model code.
- [x] For Indiana, INDOT is a provider implementation.
- [ ] For other states, use corresponding DOT/traffic providers.
- [x] Model:
  - [x] AADT or comparable flows;
  - [x] vehicle occupancy where used;
  - [x] eligible traveler share;
  - [x] directional accessibility/interchange friction;
  - [x] stop/intercept probability;
  - [x] duplication with resident/tourism pools.
- [x] Keep traffic GGR separate in output and report.

---

# 18. Baseline vs with-project market equilibrium

- [x] Run the gravity model twice:
  - [x] baseline competitive market without the proposed project;
  - [x] with-project market including the proposed facility.
- [x] For every incumbent calculate change in modeled GGR.
- [x] Decompose proposed GGR into:
  - [x] captured from host-state incumbents;
  - [x] captured from out-of-state incumbents;
  - [x] captured from tribal/other-jurisdiction incumbents where relevant;
  - [x] captured from outside/unmodeled leakage;
  - [x] newly induced resident demand;
  - [x] tourism;
  - [x] traffic intercept.
- [x] This decomposition must drive fiscal and economic-impact accounting.

---

# 19. Repatriation, cannibalization, and geographic accounting

## 19.1 Do not treat all proposed casino revenue as new economic activity

- [x] Classify every dollar by source to the extent model structure permits.
- [x] Distinguish transfer effects from incremental activity.

## 19.2 Dynamic jurisdiction accounting

- [x] Generalize concepts such as `Indiana repatriation` into host-jurisdiction accounting.
- [x] For any scenario calculate:
  - [x] revenue newly retained within host state/jurisdiction that previously flowed out;
  - [x] revenue cannibalized from existing host-state facilities;
  - [x] revenue captured from other states/jurisdictions;
  - [x] newly induced gaming demand;
  - [x] tourism/traffic import demand.
- [x] Do not label cross-border capture as local household displacement unless the patron origin actually belongs to the local household market.

---

# 20. Capacity and feasibility checks

- [x] Do not allow unconstrained GGR predictions to pass without a visible facility-capacity diagnostic when validated productivity benchmarks are active.
- [x] Develop capacity diagnostics using:
  - [x] gaming positions;
  - [x] win per unit/day benchmarks;
  - [x] table productivity;
  - [ ] operating hours; *(The current diagnostic uses effective operating days; explicit operating-hour productivity remains pending.)*
  - [ ] hotel/event capacity where relevant.
- [x] Flag when demand forecast implies implausible per-position productivity.
- [x] Do not automatically cap without showing the constraint and rationale.
- [x] Allow development-program resizing sensitivity through immutable facility-program versions and stored-run comparison.

---

# 21. Ramp-up and stabilization

- [x] Separate stabilized revenue from opening-year revenue.
- [x] Use versioned ramp parameters.
- [ ] Allow market-specific calibration.
- [x] Expose ramp assumptions on the front end.
- [x] Report at minimum:
  - [x] opening/partial year if applicable;
  - [x] first full year;
  - [x] second year;
  - [x] stabilized year;
  - [x] optional long-term growth case.

---

# 22. Independent validation and calibration framework

## 22.1 Incumbent back-testing

- [x] Temporarily treat existing casinos as if they were proposed projects.
- [x] Estimate their GGR while excluding the observed target value from the held-out facility's attraction and competitive field.
- [x] Compare prediction to actual stabilized revenue.
- [ ] Measure MAE, MAPE/SMAPE, RMSE, bias, rank correlation, and geographic residual patterns. *(All listed numerical metrics are implemented and persisted; geographic residual analysis remains pending.)*

## 22.2 Holdout validation

- [x] Do not calibrate and evaluate on the same full property set only.
- [x] Hold out casinos or markets.
- [x] Prefer market-level holdout where data volume permits.
- [x] Document training/calibration and validation periods.

## 22.3 Regression/comparable-market reasonableness model

- [x] Build at least one independent non-gravity revenue model.
- [ ] Candidate predictors:
  - [ ] accessible eligible population;
  - [ ] income/AGI;
  - [ ] gaming positions;
  - [ ] tables;
  - [ ] hotel rooms;
  - [ ] competitive density;
  - [ ] tourism intensity;
  - [ ] urban/destination type;
  - [ ] state/market fixed effects when justified.
- [x] Use it as a reasonableness check, not automatically as additive revenue.

## 22.4 Calibration governance

- [x] Save every finalized calibration as a versioned immutable parameter set.
- [x] Store objective function and validation metrics.
- [x] Store sample inclusion/exclusion rules.
- [ ] Store chosen/default beta, alpha, facility coefficients, demand coefficients, and outside-option values. *(The mechanism stores arbitrary selected parameters, but the completed live calibration did not yet select every listed family.)*
- [x] Do not overwrite old calibration versions.

---

# 23. Sector-weighted local spending displacement

## 23.1 Define the displacement base correctly

- [x] Do not apply displacement to all proposed GGR.
- [x] Determine the portion attributable to local resident spending that plausibly substitutes for other local discretionary expenditure.
- [x] Exclude or separately handle:
  - [x] imported out-of-area spending;
  - [x] out-of-state repatriated casino spending where the relevant alternative would already have left the local economy;
  - [x] pure cannibalization from another local casino when analyzing local household spending displacement;
  - [x] tourism/traffic spending unless local substitution evidence exists.

## 23.2 Core definitions

- [x] `LocalResidentGamingBase` = modeled gaming spend from defined local origins.
- [x] `DisplacementEligibleBase` = portion of local resident gaming spend plausibly shifted from local discretionary sectors.
- [x] `k` = displacement coefficient.
- [x] `D_total = DisplacementEligibleBase × k`.
- [x] `w_s` = sector allocation weight.
- [x] `D_s = D_total × w_s`.

## 23.3 Dynamic local geography

- [x] The meaning of `local` must be configurable by report/scenario.
- [x] Support host municipality, host county, custom multi-county region, MSA/CSA, and host state analyses.
- [x] Do not hard-code Northeast Indiana as the local economic area.

## 23.4 Sector inventory

- [x] Use relevant local business/economic datasets where available.
- [x] At minimum consider discretionary substitutes such as:
  - [x] restaurants/hospitality;
  - [x] retail;
  - [x] arts/entertainment/recreation.
- [x] Avoid implausible substitute sectors.
- [x] Allow local inventory/employment/sales measures to modulate baseline sector priors.

## 23.5 Tax and income-loss waterfall

- [x] Calculate displaced sales-tax base by sector.
- [x] Calculate displaced business income/profit proxy by sector.
- [x] Apply jurisdiction-specific taxability and rates when a validated general-fiscal rule is available; otherwise fail rather than substituting another jurisdiction's rates.
- [x] Avoid double counting retail sectors or pass-through/corporate effects through mutually exclusive sector keys and an explicit fiscal bridge.

---

# 24. Employment and labor-market effects

- [x] Separate:
  - [x] direct casino employment;
  - [x] construction employment where modeled;
  - [x] indirect/induced employment;
  - [x] displaced employment in local sectors;
  - [x] incumbent-casino employment cannibalization where material;
  - [x] net employment.
- [x] Do not report gross casino jobs as net jobs.
- [ ] Use wage/occupation assumptions tied to relevant geography where available. *(Geography-matched CBP payroll-per-employee wages are implemented and persisted when available; occupation mix and remaining job-density/multiplier inputs are still open.)*
- [x] Allow user overrides with provenance.

---

# 25. Fiscal impact engine

- [x] Use the jurisdiction profile to calculate gaming taxes and revenue sharing.
- [x] Separately calculate non-gaming sales taxes, income/business taxes, property taxes where supported, and other applicable public revenue.
- [x] Deduct or separately present lost fiscal revenue from displaced local business activity.
- [x] Distinguish host-local, host-state, and other-jurisdiction fiscal impacts.
- [x] Distinguish gross gaming tax receipts from net fiscal benefit.
- [x] Version all tax rules by effective date.

---

# 26. Social and downstream economic costs

The application's existing strength is that it already attempts to quantify downstream costs. Preserve this advantage and integrate it with the new origin/revenue model.

- [ ] Keep exposure population location-sensitive.
- [ ] Make social-cost geography configurable nationally.
- [ ] Use national research defaults only when local/regional evidence is unavailable and label them clearly.
- [ ] Support user override of major social-cost parameters.
- [ ] Preserve parameter sources and ranges.
- [ ] Potential modeled domains include, subject to evidence quality:
  - [ ] problem/disordered gambling prevalence;
  - [ ] treatment and health costs;
  - [ ] bankruptcy/debt stress;
  - [ ] crime/public-safety costs;
  - [ ] productivity/employment losses;
  - [ ] family/household effects;
  - [ ] public-assistance or administrative costs where defensibly measurable.
- [x] Avoid combining overlapping study estimates that represent the same underlying cost through unique domain keys and explicit component persistence.
- [x] Present gross social-cost estimate, uncertainty/sensitivity, and included/excluded domains.

---

# 27. Net economic-impact accounting

- [x] Build explicit accounting bridges rather than a single `benefits minus costs` black box.
- [ ] At minimum distinguish: *(All listed bridges except a distinct broader-regional result are persisted.)*
  - [x] gross casino/property revenue;
  - [x] revenue transferred from incumbent casinos;
  - [x] out-of-jurisdiction spending repatriated/imported;
  - [x] newly induced gaming expenditure;
  - [x] local discretionary displacement;
  - [x] direct/indirect/induced economic activity;
  - [x] fiscal gains;
  - [x] displaced fiscal losses;
  - [x] social/public costs;
  - [x] net local impact;
  - [x] net host-state impact;
  - [ ] broader regional impact where requested.
- [x] Clearly identify transfer payments/effects so they are not mislabeled as net new production.

---

# 28. API and service-layer architecture

## 28.1 Suggested core services

- [x] `OriginDemandService`.
- [x] `CompetitiveUniverseService`.
- [x] `TravelMatrixService`.
- [x] `FacilityAttractivenessService`.
- [x] `GravityModelService`.
- [x] `MarketExpansionService`.
- [x] `TourismDemandService`.
- [x] `TrafficInterceptService`.
- [x] `CannibalizationAccountingService`.
- [x] `DisplacementModelService`.
- [x] `EmploymentImpactService`.
- [x] `FiscalImpactService`.
- [x] `SocialCostService`.
- [x] `NetImpactService`.
- [x] `ModelParameterService`.
- [x] `JurisdictionProfileService`.
- [x] `ModelRunService`.
- [x] `ReportCompilationService`.

## 28.2 Model execution pipeline

- [x] Implement one authoritative backend pipeline:

```text
Scenario Definition
      |
      v
Jurisdiction + Data Snapshot Resolution
      |
      v
Parameter Set + User Override Resolution
      |
      v
Origin Demand
      |
      v
Competitive Universe + Travel Matrix
      |
      v
Facility Attraction + Gravity Allocation
      |
      v
Baseline vs With-Project Equilibrium
      |
      +--> Cannibalization / Repatriation / Induced Demand
      +--> Tourism
      +--> Through Traffic
      |
      v
Stabilized and Ramp Revenue
      |
      v
Displacement + Employment + Fiscal + Social Costs
      |
      v
Net Impact
      |
      v
Immutable ModelRun
      |
      +--> Web UI
      +--> API
      +--> Scenario Comparison
      +--> Full Report
```

- [x] The web UI and report generator must never use separate economic formulas; both consume the stored run, and renderers perform presentation aggregation only.

---

# 29. Full .NET 10 server-side report architecture

The long-term deliverable is not merely an interactive calculator. The .NET 10 backend must compile a complete professional analytical report from the same stored `ModelRun` used by the web application.

## 29.1 Report source of truth

- [x] Create an immutable `ReportModel` derived entirely from a finalized `ModelRun` plus report-presentation options.
- [x] Do not recalculate economics independently inside the report renderer.
- [x] Report generation must be deterministic for a given model run and report template version.
- [x] Store report template/version and generation timestamp.

## 29.2 Report output formats

- [x] Generate server-side PDF as the primary publication format.
- [x] Generate HTML using the same report data for preview/accessibility.
- [x] Preserve underlying machine-readable JSON/CSV exports for tables and audit.
- [x] Use a repository-compatible .NET rendering stack (QuestPDF) with deterministic HTML/CSV renderers.
- [x] Do not make PDF generation depend on screenshots of the live front-end.

## 29.3 Dynamic report structure

- [x] The report must adapt to the selected geography and jurisdiction.
- [x] Do not hard-code Indiana-specific section labels unless the active run is Indiana.
- [x] Recommended major sections:
  - [x] Executive Summary;
  - [x] Proposed Development and Site;
  - [x] Study Area and Market Definition;
  - [x] Demographics, Eligible Population, and Income;
  - [x] Competitive Gaming Supply;
  - [x] Gravity Model Methodology;
  - [x] Gaming Revenue Projection;
  - [x] Patron Origin Analysis;
  - [x] Tourism and Through-Traffic Demand;
  - [x] Competitive Impact and Cannibalization;
  - [x] Repatriation / Cross-Jurisdiction Capture;
  - [x] Local Spending Displacement;
  - [x] Employment and Labor-Market Effects;
  - [x] Fiscal Impact;
  - [x] Social and Downstream Economic Costs;
  - [x] Net Economic Impact;
  - [x] Sensitivity and Scenario Analysis;
  - [x] Benchmark/Comparable Study Reconciliation where configured;
  - [x] Methodology and Limitations;
  - [x] Data Sources;
  - [x] Model Parameters and Overrides;
  - [x] Technical Appendices.

## 29.4 Dynamic patron-origin section

- [x] Build origin tables/charts from actual contribution data.
- [x] Report top contributing counties/parishes dynamically.
- [x] Report state/territory composition dynamically.
- [x] Show host jurisdiction vs external capture dynamically.
- [x] Show tourism/traffic separately.
- [x] Map origin intensity where data density permits.
- [x] Never assume the categories are Allen, DeKalb, Steuben, Michigan, Ohio, etc.

## 29.5 Parameter disclosure in report

- [x] Include a model-parameter summary for every report.
- [x] Clearly distinguish:
  - [x] calibrated/default parameters;
  - [x] scenario preset changes;
  - [x] user overrides.
- [x] For every override show default, used value, units, recommended range, and out-of-range warning.
- [x] Include beta, alpha, active facility weights, demand assumptions, tourism/traffic assumptions, displacement assumptions, and social-cost assumptions when those active parameters are stored on the run.

## 29.6 Report exhibits

- [x] Generate publication-quality maps, tables, and charts from model data.
- [x] Candidate exhibits:
  - [x] proposed site and competitor map;
  - [x] travel-time/isoline map;
  - [x] patron-origin choropleth;
  - [x] revenue composition waterfall;
  - [x] baseline vs with-project competitor GGR;
  - [x] county/state origin contribution chart;
  - [x] ramp-up table;
  - [x] displacement by sector;
  - [x] fiscal bridge;
  - [x] social-cost bridge;
  - [x] net-impact waterfall;
  - [x] sensitivity tornado/spider chart;
  - [x] benchmark comparison table.
- [x] Preserve exact numeric source values behind every exhibit.

## 29.7 Report reproducibility statement

- [x] Include a technical appendix containing:
  - [x] model version;
  - [x] report-template version;
  - [x] run UUID;
  - [x] jurisdiction profile/version;
  - [x] parameter-set version;
  - [x] user overrides;
  - [x] source data vintages;
  - [x] route graph hash/version;
  - [x] candidate coordinates;
  - [x] development program;
  - [x] generated timestamp.

---

# 30. UI result architecture

- [x] Separate inputs/assumptions from model outputs visually.
- [x] Show default-vs-custom state on model parameters.
- [x] Show site/development configuration independently.
- [x] Show headline stabilized GGR with resident/tourism/traffic decomposition.
- [x] Show patron-origin map/table dynamically.
- [x] Show incumbent impacts.
- [x] Show local/state net economic-impact summaries.
- [x] Show methodology and data provenance without burying key assumptions.
- [x] Add explicit `Generate Full Report` workflow only after a model run is complete.
- [x] The report action must reference the stored run ID, not submit a second independent set of calculations.

---

# 31. Scenario and sensitivity engine

- [x] Permit one-click low/base/high scenarios using versioned parameter presets. *(The base preset remains explicitly provisional until calibration passes.)*
- [x] Permit custom backend scenarios with arbitrary valid override combinations.
- [x] Support one-at-a-time sensitivity for beta, alpha, gaming intensity, tourism, traffic intercept, local share, displacement coefficient, and major social-cost assumptions.
- [x] Support multi-parameter scenario comparison through side-by-side immutable stored runs.
- [x] Store every sensitivity scenario result as a separate immutable model run.
- [x] Build tornado/sensitivity tables from server-computed runs.
- [x] Do not fake sensitivities by multiplying final GGR or net impact by a percentage after the model has run.

---

# 32. National data-provider architecture

- [x] Build provider interfaces for data sources that vary by jurisdiction.
- [x] Candidate provider categories:
  - [x] gaming regulator performance;
  - [x] gaming facility inventory;
  - [x] state DOT traffic;
  - [x] tourism/visitor statistics;
  - [x] tax/fiscal rules;
  - [x] local economic/business inventory.
- [x] Common federal/national sources may include Census/ACS, IRS SOI, BEA, BLS, FHWA, and other authoritative datasets.
- [x] State-specific adapters should supply additional detail without changing gravity-engine code.
- [x] Persist provider/source provenance with each dataset snapshot.

---

# 33. Caching and interactive performance

- [ ] Precompute stable competitor and demographic data.
- [x] Cache origin-to-incumbent travel matrices by origin, facility, routing graph hash, and costing profile.
- [x] Dynamically calculate candidate-site routes only for the run's selected relevant origins.
- [x] Cache candidate locations by reasonable coordinate grid/hash while preserving exact run coordinates.
- [ ] Separate fast interactive preview from full report-caliber run only if both use the same equations and clearly indicate preview status.
- [ ] Full report runs must resolve all required data and warnings before finalization.

---

# 34. Testing requirements

## 34.1 Unit tests

- [x] Travel-friction calculations.
- [x] Attraction normalization.
- [x] Share-sum identity.
- [x] Outside option.
- [x] Parameter precedence.
- [x] Override range warnings.
- [x] Jurisdiction rule effective dates.
- [x] Gaming-age population selection.
- [x] Baseline vs with-project delta accounting.
- [x] Displacement eligibility.
- [x] Fiscal calculations.

## 34.2 Integration tests

- [x] Full model run with stored data snapshots.
- [x] Default vs overridden beta.
- [x] Default vs overridden facility weights.
- [x] Scenario reset to defaults.
- [x] Indiana jurisdiction profile failure/selection behavior. *(Legal age and state gaming-tax schedules are validated; the remaining Indiana fiscal-rule gaps are tracked under section 3.3 and Phase E.)*
- [x] At least one non-Indiana mock/test jurisdiction proving no Indiana hard-coding.
- [x] Dynamic origin aggregation with different counties/states.
- [x] Full report generation from stored run.
- [x] Regeneration of the same run/presentation returns the same immutable artifact and numeric tables.

## 34.3 Numerical robustness

- [x] Extreme travel times.
- [x] Very high/low beta.
- [x] Very high/low attraction.
- [x] Large competitive sets.
- [x] Missing route.
- [x] Missing facility attribute.
- [x] Sparse rural origin data.
- [x] Missing state-specific fiscal rules.
- [x] Zero/near-zero demand.

---

# 35. Initial Indiana benchmark test suite

Indiana is the first real validation suite, not the core architecture.

- [x] Recreate the Northeast Indiana market with current candidate scenarios. *(Three stored runs use one sealed four-state competitive field, site-centered 75-mile ZCTA markets, common parameters, and exact Valhalla routes.)*
- [x] Test an Allen/Fort Wayne-area development program. *(Run `4cc7e06b-919d-41ed-a987-a2492b43c197` is registered against CBRE's source-extracted local-gravity component using the corrected Pokagon-inclusive universe; production calibration remains open.)*
- [x] Test an I-69/DeKalb/Spectrum-like proxy. *(Run `ae288dea-14ba-4523-901d-2621570d2bd8` uses the disclosed I-69/SR 8 analyst proxy and corrected Pokagon-inclusive universe, not parcel precision.)*
- [x] Test a Steuben scenario. *(Run `8e919097-d73a-4ff4-b82f-9e4bc1aabede` uses the disclosed I-69/US 20 analyst proxy and corrected Pokagon-inclusive universe, not parcel precision.)*
- [x] Compare resident GGR, total GGR, origin composition, and incumbent impacts with public study outputs. *(The evidence ledger above records all three revenue comparisons, dynamic state composition, and leading incumbent deltas; it also records the missing Four Winds South Bend comparison.)*
- [x] Explain model differences rather than forcing exact agreement. *(Separate traffic/tourism, current-vintage Steuben demand, Spectrum's unavailable rated-play data, proxy-site precision, and competitive-universe coverage are disclosed; the public cases remain a benchmark partition rather than calibration targets.)*
- [x] Verify that patron-origin categories are generated dynamically.
- [x] Verify that the same code can run a non-Indiana synthetic or real validation market without changes to core model logic.

---

# 36. Implementation phases

## Phase A: architecture and parameterization

- [x] Generalize jurisdiction concepts.
- [x] Implement parameter catalog and versioned parameter sets.
- [x] Implement user override persistence and precedence.
- [x] Remove Fort Wayne hard-coding from reusable model services.

## Phase B: data foundation

- [x] Origin geographies and eligible-age population.
- [x] Income/AGI.
- [x] National/jurisdiction competitor schema.
- [x] Observed GGR history.
- [x] Source catalog and dataset snapshots.

## Phase C: gravity engine

- [x] Travel matrix.
- [x] Facility attractiveness.
- [x] Demand engines.
- [x] Gravity allocation.
- [x] Outside option.
- [x] Baseline vs with-project equilibrium.

## Phase D: incremental demand

- [x] Accessibility-induced demand.
- [ ] Tourism. *(The separate module and Indiana provider exist; market-specific provider coverage and calibrated capture/spend assumptions remain incomplete.)*
- [ ] Through traffic. *(The separate module and INDOT provider exist; non-Indiana coverage and calibrated intercept/spend assumptions remain incomplete.)*
- [x] Ramp/stabilization.
- [x] Capacity checks. *(Regulator-derived monthly unit-day slot/table ranges are validated and persisted; the narrower operating-hour and hotel/event-capacity extensions remain explicitly open in section 20.)*

## Phase E: comprehensive impact

- [x] Cannibalization/repatriation accounting.
- [x] Sector displacement.
- [ ] Employment. *(The tested bridge now consumes and discloses geography-matched CBP wage evidence when available; occupation mix, direct jobs/GGR, construction job-years, and regional indirect/induced multipliers remain incomplete.)*
- [ ] Fiscal impact. *(The tested engine, Indiana base schedules, and enacted northeast supplemental/host distribution are validated; historical incumbent supplemental quotients, recipient-level statewide set-asides outside the northeast host allocation, admission/device taxes, other state/local fiscal inputs, and broader jurisdiction coverage remain incomplete.)*
- [ ] Social/downstream costs. *(The tested domain engine exists; evidence-backed active assumptions remain incomplete.)*
- [ ] Net economic impact. *(The tested bridge exists, but it cannot be production-complete until the open employment, fiscal, and social inputs are complete.)*

## Phase F: front-end configurability

- [x] Standard scenario controls.
- [x] Advanced parameter panel.
- [x] Beta override.
- [x] Alpha override.
- [x] Facility weight overrides.
- [x] Range warnings.
- [x] Default reset.
- [x] Scenario comparison.

## Phase G: validation

- [ ] Incumbent back-testing.
- [ ] Holdouts.
- [ ] Independent regression/comparable model.
- [ ] Indiana public benchmark suite.
- [x] At least one non-Indiana validation case. *(The disposable Ohio synthetic holdout proves portability; a production-quality non-Indiana empirical benchmark remains desirable.)*

## Phase H: report engine

- [x] Immutable `ReportModel` from `ModelRun`.
- [x] Server-side report rendering.
- [x] Dynamic geography/origin sections.
- [x] Dynamic jurisdiction fiscal sections.
- [x] Parameter/override appendix.
- [x] Data/methodology appendix.
- [x] Publication-quality exhibits.

---

# 37. Production acceptance criteria

The gravity/revenue model is not production-complete until:

- [ ] Core model contains no Fort Wayne/Allen/DeKalb/Steuben/Indiana assumptions except through scenario, benchmark, or jurisdiction configuration.
- [x] Competitive effects are computed origin-by-origin.
- [x] Network travel time drives friction.
- [ ] Facility attraction is empirically/structurally calibrated rather than hand-scored.
- [ ] Beta has a calibrated default and is user-overridable on the front end.
- [ ] Alpha and active facility coefficients have calibrated defaults and are user-overridable in advanced/expert controls.
- [x] User overrides are persisted and disclosed.
- [x] Out-of-range overrides produce warnings but are not silently replaced.
- [x] Patron-origin reporting is dynamic.
- [x] Legal gaming age is jurisdiction-aware.
- [x] Baseline vs with-project equilibrium exists.
- [x] Cannibalization and cross-jurisdiction capture are decomposed.
- [x] Tourism and through-traffic are separate from resident demand.
- [x] Local spending displacement is applied only to an economically eligible base.
- [ ] Fiscal rules are jurisdiction-specific and effective-dated.
- [x] Social/downstream costs remain location-sensitive and configurable.
- [x] A complete immutable model run can be reproduced.
- [x] A full .NET 10 server-generated report can be produced from that model run without recalculating through separate formulas.
- [ ] Indiana benchmark cases are validated.
- [x] A non-Indiana case demonstrates national portability.

---

# 38. Non-negotiable failure conditions

Do **not** call the model complete if any of the following remain true:

- [ ] Revenue is still driven primarily by `RevenueHeuristicService`.
- [ ] Competition still depends on arbitrary `1.00`, `0.70`, `+0.15`, `+0.20` feature points.
- [ ] Straight-line distance remains the primary travel-friction input.
- [ ] Beta or facility weights are buried in code and cannot be versioned/overridden.
- [ ] Front-end overrides change only displayed numbers rather than rerunning the backend model.
- [ ] Patron-origin report categories are hard-coded to Northeast Indiana.
- [ ] Age 21+ is assumed nationally without jurisdiction resolution.
- [ ] Indiana tax rules are applied to non-Indiana markets.
- [ ] Proposed-casino attraction is circularly derived from its own projected GGR.
- [ ] All origin demand is forced across an incomplete casino set without an outside option or proven comprehensive field.
- [ ] Tourism/traffic are mixed into resident demand with no deduplication.
- [ ] Gross casino jobs are labeled net jobs.
- [ ] Gross casino GGR is labeled net new economic activity.
- [ ] Cross-border/out-of-area patron revenue is incorrectly treated as local household displacement.
- [ ] Social costs are fixed rather than location/scenario responsive.
- [ ] Report generation uses separate formulas from the web/API model.
- [ ] A report cannot disclose the exact parameter values and overrides used.

---

# 39. Final directive to the implementing AI agent

- [x] Build a **nationally reusable casino gravity model**, not a Northeast-Indiana-only calculator.
- [ ] Use Indiana as the first calibrated jurisdiction and benchmark suite, not the hard-coded model definition.
- [ ] Give beta, alpha, facility weights, demand parameters, tourism/traffic assumptions, displacement assumptions, and social-cost parameters defensible defaults while making economically meaningful parameters configurable through the front end.
- [x] Preserve every override and rerun the authoritative backend model rather than applying superficial front-end multipliers.
- [ ] Calculate competition from each origin to each relevant facility using network travel time and calibrated facility attraction.
- [x] Dynamically determine patron-origin counties, states, regions, and external-market shares from the model run.
- [x] Separate resident demand, induced demand, tourism, traffic, cannibalization, repatriation/imported demand, and displacement.
- [x] Integrate revenue projections with employment, fiscal, sector-displacement, and social/downstream cost analysis.
- [x] Make the immutable `ModelRun` the single source of truth for the web UI, API, scenario comparison, and full .NET 10 server-generated professional report.
- [ ] Favor transparent, reproducible, testable modeling over false precision.
