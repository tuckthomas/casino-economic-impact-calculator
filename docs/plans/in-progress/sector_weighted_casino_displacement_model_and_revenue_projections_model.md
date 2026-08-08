# Casino Gravity, Revenue Projection, Cannibalization, and Downstream Impact Model

## Governing AI Agent Implementation Checklist

> **Status:** In progress. This document supersedes the earlier revenue-heuristic-only implementation plan.
>
> **Primary objective:** Build a transparent, empirically calibrated casino gravity model that can produce defensible site-specific gaming revenue projections for Northeast Indiana, reconcile those projections against the publicly released Allen County, Northeast/DeKalb, and Steuben County studies, and feed the resulting patron-origin and revenue estimates into the application's existing downstream economic, displacement, fiscal, and social-cost framework.
>
> **Implementation posture:** Do not treat the existing `RevenueHeuristicService`, `CompetitionScoringService`, or `ZipSwitchingModelService` as the finished model. They are prototypes and scaffolding. Preserve useful code where appropriate, but replace unsupported assumptions and straight-line approximations with a calibrated, auditable model.

---

# 0. Read this first: non-negotiable agent instructions

- [ ] Read this entire document before changing production code.
- [ ] Inspect the current repository before implementing each section.
  - [ ] Do not assume a service, table, endpoint, migration, data file, component, or test does not exist merely because it is not described here.
  - [ ] Reuse working infrastructure where it is technically sound.
  - [ ] Refactor prototype logic rather than creating duplicate parallel systems unless separation is intentional and documented.
- [ ] Follow `AGENTS.md` and all repository-specific UI and engineering guardrails.
- [ ] Do not mark a checklist item complete merely because code was written.
  - [ ] A database task is complete only after schema/migration, ingestion, provenance, and verification are complete.
  - [ ] A model task is complete only after unit tests, integration tests, validation output, and documented assumptions exist.
  - [ ] A UI task is complete only after the API integration works and the user can distinguish model estimates from manual assumptions.
- [ ] Do not hard-code a number solely because a public consultant used it.
  - [ ] Public consultant assumptions are benchmark priors and validation anchors, not automatic truth.
  - [ ] Any adopted coefficient must have a source, calibration rationale, sensitivity range, or all three.
- [ ] Do not tune the model solely until it reproduces one desired public projection.
  - [ ] The model must be capable of explaining why Allen, DeKalb/Northeast, and Steuben projections differ.
  - [ ] It must also behave sensibly at existing casino locations and at deliberately weak candidate locations.
- [ ] Never hide manual overrides or silently replace user inputs.
- [ ] Preserve reproducibility.
  - [ ] Every production model run must identify the model version, parameter set, data vintages, candidate-site coordinates, development program, and run timestamp.
- [ ] Preserve auditability.
  - [ ] Every displayed revenue number must be decomposable back to geographic origin, demand pool, attraction/share calculation, tourism/traffic increment, and applicable adjustment.

---

# 1. Verified current repository foundation and deficiencies

## 1.1 Existing foundation already present

The following items were verified in the repository and should be treated as scaffolding rather than reimplemented blindly.

- [x] A persistent `casino_competitors` entity/table exists.
- [x] A `CasinoCompetitorSeeder` exists with a starter set of Indiana, Michigan, and Ohio properties.
- [x] `CompetitionScoringService` exists.
- [x] `RevenueHeuristicService` exists.
- [x] `ZipSwitchingModelService` exists and already implements a basic origin-to-casino multinomial share calculation.
- [x] `RevenueController` exposes prototype revenue endpoints.
- [x] Valhalla routing/isochrone infrastructure exists elsewhere in the project.
- [x] Census/block-group spatial infrastructure exists.
- [x] The existing application already models downstream costs and location-sensitive population exposure.
- [x] A sector-weighted displacement concept already exists in this plan and must be retained and improved.

## 1.2 Deficiency: the current revenue heuristic is not a gravity model

- [ ] Retire `RevenueHeuristicService` as the primary revenue estimator once the gravity engine is production-ready.
- [ ] Preserve only useful explainability/site-quality concepts from it, if desired.
- [ ] Remove or replace the following unsupported structures from production revenue estimation:
  - [ ] fixed Fort Wayne-center distance penalties;
  - [ ] 30-mile and 50-mile straight-line threshold deductions;
  - [ ] nearby market depth based on an approximate degree-radius query;
  - [ ] `population × 0.75` as the adult population proxy;
  - [ ] a hard-coded $65,000 median-income normalization;
  - [ ] a hard-coded `benchmarkDepth = 400000`;
  - [ ] a final multiplier formed from `accessScore × depthScore - competitionPenalty`;
  - [ ] classification cutoffs that are not empirically calibrated.
- [ ] If the heuristic remains available for UI diagnostics, label it explicitly as a site-quality diagnostic and never use its multiplier as the gravity-model GGR forecast.

## 1.3 Deficiency: current competition scoring is hand-weighted and Fort-Wayne-centric

- [ ] Replace hand-set venue-type plus feature-adders as the principal competitive-mass measure.
- [ ] Do not use Haversine distance as the main travel-friction measure when Valhalla drive time is available.
- [ ] Do not approximate catchment overlap by measuring each competitor's distance from a single Fort Wayne point.
- [ ] Compute overlap at the origin-zone level so that each ZIP/ZCTA/block-group chooses among the facilities that are realistically accessible to that origin.
- [ ] Preserve simple feature scores only as fallback metadata or explanatory components.

## 1.4 Deficiency: the current ZIP switching model is an uncalibrated prototype

- [ ] Keep the conceptual origin-to-facility share structure, but replace the present defaults and mechanics.
- [ ] Replace Haversine miles with cached network travel time and network distance.
- [ ] Replace unsupported defaults such as:
  - [ ] `ParticipationRate = 0.28`;
  - [ ] `AnnualGgrPerParticipant = 1200`;
  - [ ] `DistanceBeta = 0.06` in a linear utility formulation;
  - [ ] arbitrary proposed quality of `1.0`.
- [ ] Do not force all modeled demand to be allocated across an incomplete set of casinos.
  - [ ] Include a sufficiently complete competitive field and/or an explicit outside option.
- [ ] Use numerically stable share calculations.
  - [ ] Implement log-sum-exp or an equivalent stable denominator calculation.
  - [ ] Test extreme utilities and very large competitive sets for overflow/underflow.
- [ ] Do not derive incumbent attractiveness solely from the current feature-addition score.
- [ ] Do not treat the current `ZipDemandInput` request payload as the long-term source of origin demand.
  - [ ] Production demand should be loaded from versioned persisted public datasets and model parameter sets.
  - [ ] The API may still allow expert overrides for testing.

## 1.5 Deficiency: competitor records are too shallow for a mass-weighted model

- [ ] Expand the competitor schema beyond boolean amenities.
- [ ] Add time-varying observed performance and physical-scale data.
- [ ] Add source-level provenance for material attributes.
- [ ] Build an authoritative inclusion rule instead of relying on a short hand-entered seed list.

## 1.6 Deficiency: the earlier plan prematurely marked the revenue work complete

- [ ] Treat all full-gravity-model tasks in this document as open until validated.
- [ ] Do not infer completion from the prior document's old `[x]` implementation-order or acceptance section.
- [ ] The prototype services demonstrate progress but do not satisfy the revised acceptance criteria.

---

# 2. Public benchmark studies the implementation must understand

## 2.1 Spectrum Gaming Group: Indiana Gaming Commission relocation study

Primary source:

- `https://www.in.gov/igc/files/publications/Spectrum-Relocation-Report-to-Indiana-Gaming-Commission-9-30-2025-Final.pdf`

Required model lessons:

- [ ] Understand Spectrum's public-data demand construction.
  - [ ] Spectrum estimated casino revenue potential by ZIP using casino gaming revenue relative to IRS adjusted gross income (AGI).
  - [ ] Spectrum reported a national casino-revenue-to-AGI ratio of approximately 0.58% using 2022 data.
  - [ ] Spectrum increased the Indiana benchmark to approximately 0.66% as a mature-market adjustment.
  - [ ] Treat 0.58% and 0.66% as benchmark priors, not immutable constants.
- [ ] Understand Spectrum's private/state-assisted advantage.
  - [ ] Spectrum received rated/tracked play by ZIP from Indiana casino operators through the Indiana Gaming Commission.
  - [ ] That data allowed it to observe current visits and theoretical gaming value by origin ZIP.
  - [ ] This project does not possess equivalent patron-level operator capture data unless such a dataset is lawfully obtained later.
- [ ] Understand Spectrum's drive-time logic.
  - [ ] It analyzed 0-15, 16-30, and 31-60 minute catchment bands for Indiana commercial casinos.
  - [ ] It explicitly considered existing capture, unmet demand, and retained revenue at competing casinos.
- [ ] Retain the Northeast proxy as a validation anchor.
  - [ ] Spectrum's Northeast proxy at I-69 / SR 8 reported estimated market potential of approximately $219.9 million and proxy AGR potential of approximately $204.3 million after its capture/retention adjustments.
  - [ ] Do not force the new model to equal $204.3 million. Explain variance based on site, development program, competitive mass, data vintage, and model structure.

## 2.2 CBRE / Union Gaming Analytics: Greater Fort Wayne Area Casino Analysis

Primary source:

- `https://cdn.insideindianabusiness.com/wp-content/uploads/2026/01/GFWI-Casino-Analysis-Presentation-Final-2025-12-03.pdf`

Required model lessons:

- [ ] Understand CBRE's public description of its gravity model.
  - [ ] It considers population.
  - [ ] It considers per-capita income.
  - [ ] It considers project and competitor attractiveness/development scale.
  - [ ] It considers distance from the proposed project and competitors.
- [ ] Incorporate a development-program concept.
  - [ ] CBRE modeled approximately 1,600 slots, 50 tables, and 200 hotel rooms for its proxy project.
  - [ ] A proposed casino's attractiveness must therefore be a scenario input, not a universal constant attached only to latitude/longitude.
- [ ] Incorporate non-local demand explicitly.
  - [ ] CBRE added incremental out-of-market GGR using a traffic-intercept approach based on INDOT highway data.
  - [ ] Highway traffic cannot simply be counted as resident population demand.
- [ ] Model stabilization/ramp separately from stabilized demand.
  - [ ] CBRE treated Year 3 as stabilized.
  - [ ] It reported first full-year performance of roughly 87% of stabilized GGR as an observed competitive-market benchmark.
  - [ ] Use this only as a ramp prior subject to validation.
- [ ] Build an independent reasonableness test.
  - [ ] CBRE reports that a regression across nearly 200 casinos produced a local-driven GGR estimate close to its gravity estimate.
  - [ ] CBRE also compared casino GGR as a percentage of total market income.
  - [ ] The new project must likewise include at least one independent validation model rather than trusting one gravity specification.
- [ ] Retain public CBRE outputs as benchmark checks.
  - [ ] Stabilized Year 3 casino GGR: approximately $282.3 million.
  - [ ] Local-driven gravity GGR used in reasonableness testing: approximately $216.7 million.
  - [ ] Regression reasonableness estimate: approximately $215.0 million.
  - [ ] Comparable-market income allocation average: approximately 0.64%.
  - [ ] Greater Fort Wayne modeled income allocation: approximately 0.63%.
- [ ] Retain the published competitor-impact vector as a cannibalization/repatriation validation target, not a required outcome.
  - [ ] FireKeepers: about -$35.5 million.
  - [ ] Hollywood Toledo: about -$9.1 million.
  - [ ] Hollywood Gaming at Dayton Raceway: about -$3.8 million.
  - [ ] Hollywood Columbus: about -$1.7 million.
  - [ ] Four Winds South Bend: about -$11.6 million.
  - [ ] Harrah's Hoosier Park: about -$8.0 million.
  - [ ] Proposed Greater Fort Wayne casino: about +$282.3 million.
  - [ ] CBRE's stated incremental GGR to Indiana commercial casinos: about +$274.3 million.

## 2.3 A.M. Steinberg Advisors: Steuben County Gaming Market Feasibility Study

Primary source:

- `https://www.steubenedc.com/media/userfiles/subsite_259/files/SCEDC_Feasibility_Study_FINAL.pdf`

This is the separate Steuben County report that must be used as a benchmark.

Required model lessons:

- [ ] Understand its explicitly described mass-weighted gravity model.
  - [ ] 2030 projected population age 21+.
  - [ ] Income-adjusted per-capita gaming expenditure.
  - [ ] Travel-time/distance decay.
  - [ ] Base distance-decay parameter β = 1.5.
  - [ ] Sensitivity range reported around β = 1.4 to 1.6.
  - [ ] Competitive mass based materially on observed 2024 incumbent GGR.
  - [ ] Full competitive inclusion within a 120-minute trade area.
  - [ ] Primary 0-30 minute, secondary 30-60 minute, tertiary 60-120 minute resident markets.
- [ ] Understand its market-share posture.
  - [ ] Revenue is a share-of-market/capture problem, not an unconstrained local population multiplication exercise.
  - [ ] Larger and higher-performing casinos exert more pull than small satellite facilities.
  - [ ] Non-gaming amenities and game types modify competitive attractiveness.
- [ ] Retain its reported per-adult gaming-spend scenarios as validation priors only.
  - [ ] Conservative: approximately $350 per adult 21+.
  - [ ] Base: approximately $375.
  - [ ] High: approximately $390.
- [ ] Retain its tourism separation principle.
  - [ ] Lake tourism was modeled outside the resident gravity pool to reduce double counting.
  - [ ] The report's base resident GGR was approximately $194.5 million and base induced lake-tourism GGR approximately $8.6 million, for approximately $203.1 million total.
- [ ] Retain the low/base/high total benchmarks.
  - [ ] Low: approximately $188.6 million.
  - [ ] Base: approximately $203.1 million.
  - [ ] High: approximately $214.0 million.

## 2.4 Benchmark studies are not apples-to-apples

- [ ] Add a benchmark reconciliation note in code documentation and UI methodology.
- [ ] Explicitly explain that reported values differ because of:
  - [ ] different proxy locations;
  - [ ] different modeled development programs;
  - [ ] different competitive fields;
  - [ ] different source years;
  - [ ] different treatment of tourism and highway intercept;
  - [ ] different definitions of GGR, AGR, and taxable gaming revenue;
  - [ ] private operator data available to Spectrum but not to this project;
  - [ ] different stabilization years and population forecasts.
- [ ] Never imply that disagreement with one report automatically means this model is wrong or that the public report is wrong.
- [ ] Use the three studies to define a validation envelope and to identify structural reasons for divergence.

---

# 3. Model terminology and accounting identities

## 3.1 Stop using GGR and AGR interchangeably

- [ ] Create explicit domain definitions and use them consistently across server, shared DTOs, UI, reports, and tests.
- [ ] At minimum distinguish:
  - [ ] **GGR / casino win:** patron wagers minus gaming payouts, before jurisdiction-specific taxable adjustments.
  - [ ] **Taxable AGR / taxable gaming base:** the amount defined by applicable Indiana law for wagering-tax calculation after any legally permitted adjustments.
  - [ ] **Non-gaming revenue:** hotel, food and beverage, entertainment, retail/other property revenue.
  - [ ] **Total property revenue:** gaming plus non-gaming revenue.
- [ ] Verify current Indiana statutory terminology and promotional-credit treatment from authoritative Indiana sources at implementation time.
- [ ] Never convert GGR to taxable AGR with a stale hard-coded deduction.

## 3.2 Required origin/facility notation

Use consistent notation in source comments and methodology documentation.

- [ ] `i` = origin zone, preferably ZCTA/ZIP-compatible demand geography with supporting block-group allocation.
- [ ] `j` = casino/facility alternative.
- [ ] `D_i` = annual resident casino gaming expenditure pool generated by origin `i` under the selected demand specification.
- [ ] `T_ij` = network drive time from origin `i` to facility `j`.
- [ ] `L_ij` = network drive distance from origin `i` to facility `j`.
- [ ] `A_j` = calibrated attraction/competitive mass of facility `j`.
- [ ] `F_ij` = travel-friction function.
- [ ] `W_ij` = unnormalized attraction weight for origin `i` and facility `j`.
- [ ] `P_ij` = modeled share/probability of origin `i` gaming expenditure allocated to facility `j`.
- [ ] `R_j` = modeled resident GGR captured by facility `j`.
- [ ] `R_j,tourism` = incremental tourism GGR.
- [ ] `R_j,traffic` = incremental through-traffic/intercept GGR.
- [ ] `R_j,total` = total stabilized GGR before ramp-up.

---

# 4. Data provenance and reproducibility layer

## 4.1 Create a source catalog

- [ ] Add a persistent source/dataset catalog rather than embedding URLs in arbitrary service code.
- [ ] Suggested entity: `data_sources`.
- [ ] Include:
  - [ ] `id`;
  - [ ] `name`;
  - [ ] `publisher`;
  - [ ] `source_url`;
  - [ ] `dataset_type`;
  - [ ] `vintage_or_period`;
  - [ ] `retrieved_at`;
  - [ ] `license_or_terms_notes`;
  - [ ] `content_hash`;
  - [ ] `is_authoritative`;
  - [ ] `notes`.

## 4.2 Create immutable dataset snapshots

- [ ] Add `dataset_snapshots` or equivalent.
- [ ] Store:
  - [ ] source ID;
  - [ ] source period/vintage;
  - [ ] ingestion timestamp;
  - [ ] row count;
  - [ ] checksum/hash;
  - [ ] transform version;
  - [ ] validation status;
  - [ ] error/warning summary.
- [ ] Do not overwrite a prior calibrated dataset without preserving its snapshot identity.

## 4.3 Every model run must reference its data

- [ ] Add `model_runs` with:
  - [ ] run UUID;
  - [ ] model version;
  - [ ] parameter-set ID;
  - [ ] scenario ID;
  - [ ] candidate coordinates;
  - [ ] development-program ID;
  - [ ] origin-demographic snapshot;
  - [ ] income/AGI snapshot;
  - [ ] competitor snapshot;
  - [ ] observed-GGR snapshot;
  - [ ] travel-time matrix version/hash;
  - [ ] tourism/traffic snapshot IDs where used;
  - [ ] created timestamp;
  - [ ] execution duration;
  - [ ] warnings.

---

# 5. Origin geography and resident market data

## 5.1 Select a production origin geography deliberately

- [ ] Prefer a ZIP/ZCTA-compatible origin layer because the public benchmark studies are materially ZIP-based and IRS SOI AGI is ZIP-based.
- [ ] Do not assume USPS ZIP Codes and Census ZCTAs are identical.
- [ ] Build an explicit crosswalk and document limitations.
- [ ] Where better spatial precision is needed:
  - [ ] retain Census block groups as the demographic base;
  - [ ] allocate block-group population/income to ZCTA using an explicit geographic or population-weighted crosswalk;
  - [ ] store the allocation weights.
- [ ] Avoid a naive ZIP centroid when the centroid is unrepresentative.
  - [ ] Use a population-weighted representative point or point-on-surface of populated subareas where feasible.
  - [ ] Flag rural/large ZCTAs with poor centroid representation.

## 5.2 Create `origin_zones`

- [ ] Store at minimum:
  - [ ] origin ID;
  - [ ] ZCTA/ZIP identifier;
  - [ ] state;
  - [ ] county allocation(s);
  - [ ] representative latitude/longitude;
  - [ ] population-weighted point geometry;
  - [ ] area geometry if retained;
  - [ ] urban/rural classification optional;
  - [ ] source snapshot IDs.

## 5.3 Use casino-eligible adult population, not a blanket adult proxy

- [ ] Ingest or derive age 21+ population.
- [ ] Do not use `Population × 0.75` in the production model.
- [ ] Store age-21+ population by origin and source year.
- [ ] If 21+ must be estimated from ACS age bins:
  - [ ] document the interpolation method for the 20-24 age bin;
  - [ ] preserve the raw age-bin values;
  - [ ] test totals against county/state controls.
- [ ] Support projection to a scenario year using an explicit population-growth source/method.
  - [ ] Do not silently call current ACS population “2030 population.”

## 5.4 Add income/AGI measures

- [ ] Ingest IRS SOI ZIP-level AGI where legally/publicly available.
- [ ] Ingest ACS income measures needed for independent demand specifications and missing-data fallback.
- [ ] Store:
  - [ ] total AGI;
  - [ ] number of returns;
  - [ ] AGI per return;
  - [ ] median household income;
  - [ ] optional per-capita income/disposable-income proxy;
  - [ ] inflation year/dollar basis.
- [ ] Normalize all dollar inputs to a declared model-dollar year.
- [ ] Preserve nominal raw values as well as inflation-adjusted model values.

---

# 6. Build two independent resident-demand specifications

A professional-grade model should not depend on a single unexplained demand formula. Implement one primary demand engine and one independent reasonableness engine. They must not be added together.

## 6.1 Specification A: AGI-share demand model

- [ ] Implement a Spectrum-like public-data specification:

```text
D_i_AGI = AGI_i_real × gaming_income_share_state_or_region × optional_origin_adjustment_i
```

- [ ] Make `gaming_income_share` a calibrated/versioned parameter.
- [ ] Seed priors from public evidence, including Spectrum's roughly 0.58% national and 0.66% Indiana mature-market references.
- [ ] Do not mechanically apply Indiana's rate to Michigan, Ohio, or every other origin.
- [ ] Estimate/validate state or regional intensity using observed public gaming revenue where possible.
- [ ] Prevent double income weighting.
  - [ ] If total AGI is already the demand mass, do not multiply it again by an ACS income index without a specific modeled reason.

## 6.2 Specification B: age-21+ per-capita expenditure model

- [ ] Implement a Steuben-like independent specification:

```text
D_i_PCE = Adults21_i × BaseGamingExpenditurePerAdult × IncomeAdjustment_i
IncomeAdjustment_i = (IncomeMetric_i / RegionalReferenceIncome)^epsilon_income
```

- [ ] Make `BaseGamingExpenditurePerAdult` configurable and calibrated.
- [ ] Treat the Steuben $350 / $375 / $390 scenarios as benchmark priors, not production constants.
- [ ] Estimate `epsilon_income` rather than assuming a linear one-for-one relationship unless validation supports it.
- [ ] Bound extreme income adjustments to avoid implausible ZIP-level demand.
- [ ] Document how students, institutions, prisons, military populations, and other unusual population concentrations are handled if they materially affect a zone.

## 6.3 Demand-model reconciliation

- [ ] Produce both AGI-based and per-adult-based demand totals for every benchmark scenario.
- [ ] Add a reconciliation report showing:
  - [ ] total market demand;
  - [ ] demand by state;
  - [ ] demand by drive-time band;
  - [ ] largest origin-zone differences between specifications.
- [ ] Select the production/base specification using validation performance, not preference.
- [ ] Optionally support an ensemble only after both models are independently validated.
  - [ ] If an ensemble is used, document and version its weights.

---

# 7. Competitive casino universe

## 7.1 Define the inclusion rule before collecting properties

- [ ] Build a complete competitive field for all origins that can materially contribute to the candidate site's demand.
- [ ] Candidate-site trade-area cutoffs and competitor inclusion cutoffs are different concepts.
  - [ ] An origin may be within 120 minutes of the proposed casino while its competing casino is more than 120 minutes from the proposed casino.
  - [ ] Therefore do not filter competitors solely by distance from the candidate site.
- [ ] Include a margin outside the candidate trade area or use an explicit outside alternative so edge-origin demand is not artificially forced inward.
- [ ] Include commercial casinos, racinos, and tribal casinos that offer a sufficiently substitutable gaming product.
- [ ] Treat limited gaming, sportsbook-only, OTB, charity gaming, and distributed gaming separately unless evidence shows material substitution with casino-floor GGR.

## 7.2 Expand `casino_competitors`

- [ ] Add stable identity fields:
  - [ ] canonical property ID;
  - [ ] regulatory license ID if available;
  - [ ] tribal/commercial status;
  - [ ] state regulator;
  - [ ] opening date;
  - [ ] closure date;
  - [ ] operator changes over time.
- [ ] Add physical-scale fields:
  - [ ] slot/VLT positions;
  - [ ] table-game count;
  - [ ] poker tables where material;
  - [ ] total gaming positions or derived equivalent positions;
  - [ ] casino gaming-floor square footage if available;
  - [ ] hotel rooms;
  - [ ] major event/entertainment capacity;
  - [ ] food-and-beverage outlet count or qualitative index;
  - [ ] resort/spa/golf/destination components where material;
  - [ ] estimated/announced development cost and dollar year where public.
- [ ] Add accessibility/context fields where useful:
  - [ ] interstate/limited-access-highway proximity;
  - [ ] direct interchange access flag;
  - [ ] urban/destination/local orientation;
  - [ ] border-market indicator.

## 7.3 Create observed casino performance history

- [ ] Create `casino_ggr_periods` or equivalent.
- [ ] Store monthly data when available; annual aggregates may be derived.
- [ ] Include:
  - [ ] property ID;
  - [ ] period;
  - [ ] reported GGR/AGR/win value;
  - [ ] metric definition;
  - [ ] slots/VLT win if separately reported;
  - [ ] table win if separately reported;
  - [ ] source ID/snapshot;
  - [ ] inflation-adjusted value;
  - [ ] anomalous-period flag.
- [ ] Ingest authoritative public data from relevant regulators.
  - [ ] Indiana Gaming Commission.
  - [ ] Michigan Gaming Control Board and appropriate tribal/public sources where available.
  - [ ] Ohio Casino Control Commission and Ohio Lottery for racinos/VLT facilities as applicable.
- [ ] Treat 2020-2021 pandemic distortions and openings/closures explicitly during calibration.
- [ ] Prefer trailing 12-month or stabilized multi-year performance over a single anomalous month.

## 7.4 Do not create circular proposed-casino attractiveness

- [ ] Never define the proposed property's competitive mass directly from the GGR that the same model is attempting to predict.
- [ ] Create a proposed `development_program` entity/scenario with physical and capital inputs.
- [ ] Map the program to an attractiveness/mass measure using calibrated relationships or comparable-property scaling.

---

# 8. Network travel-time matrix

## 8.1 Use Valhalla as the primary travel-friction source

- [ ] Build origin-to-facility network travel times.
- [ ] Do not use Haversine distance except as:
  - [ ] a cheap prefilter;
  - [ ] fallback diagnostics;
  - [ ] a test comparison.
- [ ] Capture both:
  - [ ] travel time in minutes;
  - [ ] routed distance in miles/kilometers.
- [ ] Use consistent costing/profile settings for ordinary passenger vehicles.

## 8.2 Persist and cache the matrix

- [ ] Create `origin_facility_travel`.
- [ ] Key by:
  - [ ] origin-zone ID;
  - [ ] facility ID or scenario-facility ID;
  - [ ] routing graph/version hash;
  - [ ] costing profile.
- [ ] Store:
  - [ ] seconds/minutes;
  - [ ] distance;
  - [ ] route-found flag;
  - [ ] calculated timestamp.
- [ ] Precompute all stable origin-to-incumbent routes.
- [ ] For a draggable proposed site, compute only the proposed-site column dynamically and cache it by a coordinate grid/rounded key where appropriate.

## 8.3 Performance requirements

- [ ] Do not call Valhalla separately for thousands of individual origin/facility pairs if matrix/batched routing can be used.
- [ ] Precompute Northeast regional origins and incumbent facilities offline.
- [ ] Keep interactive candidate-site response latency acceptable.
- [ ] Support background warming of candidate-grid travel data.

---

# 9. Gravity/Huff attraction model

## 9.1 Implement a true origin-specific attraction equation

At minimum support an inverse-power Huff/gravity formulation:

```text
W_ij = A_j^alpha × B_ij / (T_ij + t0)^beta
```

where:

- `A_j` = facility attraction/competitive mass;
- `alpha` = attraction elasticity;
- `B_ij` = optional origin-facility modifiers with documented evidence;
- `T_ij` = network travel time;
- `t0` = small positive regularization term if required;
- `beta` = calibrated travel-time decay.

- [ ] Keep parameters in a versioned parameter set, not source-code literals.
- [ ] Support an exponential-decay alternative for validation if useful:

```text
W_ij = A_j^alpha × B_ij × exp(-lambda × T_ij)
```

- [ ] Select the production friction form based on out-of-sample validation.
- [ ] Do not mix inverse-power and exponential decay ad hoc in one formula.

## 9.2 Calibrate distance decay

- [ ] Seed β around the publicly reported Steuben value of 1.5 only as a prior/start point.
- [ ] Evaluate at minimum a 1.4-1.6 sensitivity band because that public study reports it.
- [ ] Expand the search range if observed-property validation indicates a materially different value.
- [ ] Test whether one β works across all markets.
  - [ ] If not, consider a hierarchical or segment-specific formulation for local convenience versus destination facilities.
- [ ] Prefer travel time over raw distance for the primary model.

## 9.3 Model facility attractiveness using observable scale

- [ ] Build a `FacilityAttractivenessService` separate from distance decay.
- [ ] At minimum evaluate:
  - [ ] gaming positions;
  - [ ] table-game breadth;
  - [ ] hotel room count;
  - [ ] major entertainment/event capacity;
  - [ ] resort/non-gaming amenities;
  - [ ] development scale/capital where comparable and available;
  - [ ] brand/loyalty strength only if a defensible proxy exists.
- [ ] Avoid double counting correlated size measures.
  - [ ] Example: slots, gaming floor area, development cost, and observed GGR may all represent overlapping scale.
- [ ] Normalize attractiveness to a reference facility so values remain interpretable.

## 9.4 Implement and compare two competitive-mass approaches

### Approach 1: structural physical mass

- [ ] Estimate attraction from physical/development features for both incumbents and proposed properties.
- [ ] Fit/validate coefficients against observed incumbent GGR or market shares.
- [ ] Advantage: proposed facility can be scored without circular use of projected GGR.

### Approach 2: observed-GGR incumbent mass with proposed comparable scaling

- [ ] Implement a Steuben-style reconciliation model where incumbent mass is anchored partly to observed GGR.
- [ ] Map the proposed development program to equivalent competitive mass using comparable facilities rather than its own forecast.
- [ ] Use this as a benchmark/reconciliation model if it validates better or helps explain public studies.

- [ ] Do not silently switch between the two approaches.
- [ ] Save the selected attraction specification in the model run.

---

# 10. Market-share allocation and outside option

## 10.1 Basic share equation

For a comprehensive competitive set:

```text
P_ij = W_ij / (W_i0 + Σ_k W_ik)
```

where `W_i0` is an optional outside/unmodeled alternative.

- [ ] Ensure shares are non-negative.
- [ ] Ensure the sum of facility shares plus outside share equals 1 within numerical tolerance.
- [ ] Use a stable denominator implementation.

## 10.2 Outside option requirements

- [ ] Do not omit an outside option simply because the prototype did not have one.
- [ ] The outside option may represent:
  - [ ] relevant casinos beyond the explicitly modeled competitive boundary;
  - [ ] other gaming supply not represented as individual facilities;
  - [ ] leakage required to reconcile modeled origin demand with observed regional casino capture.
- [ ] Do **not** use the outside option as an unexplained fudge factor.
- [ ] Calibrate it against observed total casino GGR and/or holdout properties.
- [ ] If the competitor universe is broad enough that no outside option is required for a particular specification, prove that through validation and document it.

## 10.3 Do not conflate casino participation with facility share

- [ ] Decide whether `D_i` represents:
  - [ ] all adult discretionary spending potentially available to gaming, or
  - [ ] an already-calibrated total casino gaming expenditure pool.
- [ ] If `D_i` is already total casino expenditure, do not multiply it by an independent arbitrary casino participation rate again.
- [ ] If participation is modeled separately, calibrate participation and spend-per-participant from data and make the accounting identity explicit.

---

# 11. Accessibility-induced market expansion

A fixed market-share model only redistributes a constant gaming pool. Public studies and basic consumer behavior suggest that substantially improved convenience may also increase gaming participation/frequency. Model that separately and transparently.

## 11.1 Baseline versus proposed accessibility

- [ ] Calculate each origin's baseline accessibility/inclusive-value index using incumbent facilities.
- [ ] Recalculate after adding the proposed casino.
- [ ] Store the change in accessibility by origin.

## 11.2 Optional induced-demand formulation

- [ ] Implement an accessibility-elasticity layer only if it can be calibrated or bounded defensibly.
- [ ] Candidate form:

```text
D_i_with = D_i_base × exp(eta_access × (IV_i_with - IV_i_base))
```

- [ ] Keep `eta_access` at zero in the conservative specification unless empirical calibration supports induced expansion.
- [ ] Cap implausible growth.
- [ ] Report induced gaming demand separately from reallocated existing gaming demand.
- [ ] Never hide induced demand inside a higher attraction score.

---

# 12. Baseline/with-project simulation and cannibalization accounting

## 12.1 Always run two market equilibria

- [ ] **Baseline run:** current competitive field without proposed casino.
- [ ] **With-project run:** identical assumptions plus proposed casino and any induced-demand effect.

## 12.2 Compute facility-level deltas

For every incumbent:

```text
DeltaGGR_j = GGR_j_with_project - GGR_j_baseline
```

- [ ] Report deltas by property and state.
- [ ] Reconcile total losses plus market expansion against proposed-casino GGR.

## 12.3 Required decomposition of proposed GGR

- [ ] Break proposed GGR into at least:
  - [ ] diverted from existing Indiana commercial casinos;
  - [ ] repatriated from Michigan casinos;
  - [ ] repatriated from Ohio casinos;
  - [ ] repatriated from other out-of-state casinos;
  - [ ] shifted from outside/unmodeled casino alternatives;
  - [ ] accessibility-induced incremental gaming demand;
  - [ ] imported gaming demand generated by non-Indiana residents;
  - [ ] local-county resident demand;
  - [ ] other Indiana resident demand.
- [ ] These categories must reconcile exactly or have a documented residual.

## 12.4 Validate against CBRE's published competitor deltas

- [ ] Run the Allen/Greater Fort Wayne proxy scenario with a development program comparable to CBRE's public assumptions.
- [ ] Compare direction and magnitude of competitor impacts with CBRE's public vector.
- [ ] Investigate large disagreements rather than tuning property-by-property multipliers until they disappear.

---

# 13. Tourism, destination demand, and through-traffic

Resident gravity demand and nonresident incremental demand must be separate modules to prevent double counting.

## 13.1 Tourism demand module

- [ ] Create `TourismDemandService`.
- [ ] Model tourism as visitor-person exposure or explicit pseudo-origin markets, not as permanent resident population.
- [ ] Candidate public inputs may include:
  - [ ] county tourism bureau visitation studies;
  - [ ] hotel room demand/occupancy;
  - [ ] seasonal second-home occupancy;
  - [ ] attraction attendance;
  - [ ] visitor origin studies;
  - [ ] state/local tourism statistics.
- [ ] Identify visitor origins where possible.
- [ ] Remove visitor demand already captured by the resident gravity model.
- [ ] Store a clear double-counting adjustment.

## 13.2 Steuben-specific lake tourism scenario

- [ ] Implement a Steuben-style tourism module that can separately model lake/seasonal demand.
- [ ] Use the public A.M. Steinberg $7.1m / $8.6m / $11.8m tourism figures only as benchmark checks.
- [ ] Do not hard-code those numbers into arbitrary locations.

## 13.3 Highway traffic-intercept module

- [ ] Create `TrafficInterceptService` for candidate sites with meaningful interstate/highway pass-by exposure.
- [ ] Ingest authoritative INDOT AADT/traffic-count data.
- [ ] Determine traffic segments that actually pass the site/interchange.
- [ ] Avoid counting local commuters as incremental out-of-market casino visitors when they are already represented in resident demand.
- [ ] Separate:
  - [ ] resident/local recurring traffic;
  - [ ] regional through trips;
  - [ ] long-distance/nonresident traffic where estimable.
- [ ] Use capture-rate assumptions as calibrated scenario parameters.
- [ ] Report traffic-intercept GGR separately.
- [ ] Validate the Allen scenario against the fact that CBRE explicitly adds traffic-intercept GGR.

## 13.4 Destination amenity uplift

- [ ] Do not represent hotel/event-center effects twice through both facility attractiveness and an added revenue uplift.
- [ ] Decide whether an amenity:
  - [ ] increases resident facility share;
  - [ ] increases nonresident/destination demand;
  - [ ] produces non-gaming revenue;
  - [ ] does more than one of these for defensible reasons.
- [ ] Document each channel separately to prevent double counting.

---

# 14. Proposed development program and capacity constraints

## 14.1 Create a development-program scenario

- [ ] Add `casino_development_programs` or an equivalent scenario object.
- [ ] Include:
  - [ ] slots/VLT positions;
  - [ ] table games;
  - [ ] poker where applicable;
  - [ ] hotel rooms;
  - [ ] event/entertainment capacity;
  - [ ] restaurants/F&B scale;
  - [ ] resort amenities;
  - [ ] projected capital investment and dollar year;
  - [ ] parking/access assumptions where material;
  - [ ] opening/stabilization year.
- [ ] Provide named templates for public-report reconciliation.
  - [ ] Allen/CBRE-like program.
  - [ ] Spectrum Northeast proxy generic program only if public assumptions can be established.
  - [ ] Steuben destination-program template based on public report assumptions.
- [ ] Do not imply a template is the user's prediction unless selected.

## 14.2 Capacity/productivity sanity checks

- [ ] Calculate implied GGR per gaming position/day and per slot/table where possible.
- [ ] Compare with observed regional facilities.
- [ ] Flag forecasts that imply implausible productivity for the proposed physical program.
- [ ] If capacity is likely binding, support a capacity-constrained scenario rather than allowing infinite capture from a small facility.
- [ ] Do not impose a capacity cap without showing the assumption and comparable basis.

---

# 15. Stabilization and year-by-year revenue ramp

## 15.1 Separate stabilized market potential from opening-year performance

- [ ] The gravity model should first estimate stabilized GGR under normalized conditions.
- [ ] Add a separate ramp service/configuration for Years 1-N.
- [ ] Do not change gravity attraction merely to mimic opening-year novelty.

## 15.2 Ramp scenarios

- [ ] Support configurable Year 1, Year 2, Year 3 stabilization percentages.
- [ ] Include CBRE's public observation that competitive-market first-year properties can be around 87% of Year 3 as a benchmark prior.
- [ ] Add opening novelty upside as a separate sensitivity, not as permanent stabilized demand.
- [ ] Support nominal growth after stabilization separately from real market growth and inflation.

---

# 16. Independent validation and triangulation model

## 16.1 Build a casino-level regression reasonableness model

- [ ] Create an offline calibration/validation workflow, not necessarily a production interactive endpoint.
- [ ] Target observed stabilized casino GGR using variables such as:
  - [ ] population age 21+ within drive-time bands;
  - [ ] aggregate income/AGI within drive-time bands;
  - [ ] gaming positions;
  - [ ] hotel rooms;
  - [ ] development/amenity scale;
  - [ ] competition intensity;
  - [ ] nearest-competitor drive time;
  - [ ] state/regulatory fixed effects where justified;
  - [ ] tourism/destination indicators.
- [ ] Use regularization or parsimonious specification to limit overfit.
- [ ] Report coefficient signs, uncertainty, and out-of-sample metrics.
- [ ] Do not expose a black-box ML model as the sole revenue forecast.

## 16.2 Comparable-market income-allocation check

- [ ] Calculate modeled GGR as a percentage of total relevant market income/AGI.
- [ ] Compare against observed regional casino markets and the public CBRE comparable analysis.
- [ ] Flag extreme values.
- [ ] Do not simply cap at 0.64%; use the comparison as a diagnostic.

## 16.3 Three-way reconciliation

For each benchmark site, produce:

- [ ] gravity-model estimate;
- [ ] regression reasonableness estimate;
- [ ] income-allocation/comparable estimate;
- [ ] public-study estimate where available;
- [ ] explanation of differences.

---

# 17. Calibration and backtesting

## 17.1 Calibrate on existing casinos, not just proposed sites

- [ ] Build a calibration dataset of existing Indiana/Michigan/Ohio regional properties with observed GGR and facility attributes.
- [ ] For each calibration property, construct a synthetic scenario treating it as the target property within the existing competitive field.
- [ ] Estimate how well the model reproduces observed GGR.

## 17.2 Use holdout validation

- [ ] Split calibration and validation properties or use cross-validation/leave-one-property-out methods.
- [ ] Do not report only in-sample fit.
- [ ] Report at minimum:
  - [ ] MAE;
  - [ ] RMSE;
  - [ ] MAPE or sMAPE with appropriate handling of low-GGR properties;
  - [ ] log-RMSE where useful;
  - [ ] rank correlation for site-strength ordering;
  - [ ] bias by state and property type.

## 17.3 Parameter calibration

- [ ] Calibrate a small, identifiable set of parameters first:
  - [ ] distance decay;
  - [ ] attraction elasticity;
  - [ ] income elasticity for the PCE specification;
  - [ ] market gaming-intensity parameter(s);
  - [ ] outside-option scale if used.
- [ ] Add amenity coefficients only when the dataset can support them.
- [ ] Penalize or reject parameter sets that produce economically nonsensical behavior even if headline RMSE improves.

## 17.4 Store calibration results

- [ ] Create `model_parameter_sets`.
- [ ] Create `calibration_runs` / `validation_results`.
- [ ] Store:
  - [ ] parameter values;
  - [ ] calibration sample;
  - [ ] holdout sample;
  - [ ] objective function;
  - [ ] fit metrics;
  - [ ] source snapshot IDs;
  - [ ] code/model version;
  - [ ] timestamp.
- [ ] Only mark a parameter set `production` after validation criteria are satisfied.

---

# 18. Benchmark scenario suite

## 18.1 Allen County / Greater Fort Wayne benchmark

- [ ] Create a reproducible Allen benchmark scenario with documented proxy coordinates and a CBRE-like development program.
- [ ] Compare:
  - [ ] resident/local-driven GGR;
  - [ ] traffic-intercept GGR;
  - [ ] destination/amenity uplift if separately modeled;
  - [ ] total stabilized GGR;
  - [ ] competitor losses;
  - [ ] Indiana versus out-of-state repatriation.
- [ ] Explain differences from approximately $282.3m CBRE stabilized GGR rather than force-fitting.

## 18.2 Northeast / DeKalb Spectrum benchmark

- [ ] Create a reproducible I-69 / SR 8 benchmark scenario.
- [ ] Compare origin demand and total forecast with Spectrum's approximately $204.3m Northeast proxy AGR potential.
- [ ] Show the effect of using the AGI-share demand specification versus the per-adult specification.
- [ ] Explicitly document that the model cannot replicate Spectrum's operator ZIP-level theoretical-win subtraction.

## 18.3 Steuben benchmark

- [ ] Create a reproducible I-69 / I-80/90 Steuben proxy scenario.
- [ ] Use a 120-minute resident trade area for reconciliation.
- [ ] Run β = 1.4 / 1.5 / 1.6.
- [ ] Run per-adult expenditure benchmark scenarios around $350 / $375 / $390.
- [ ] Model tourism separately.
- [ ] Compare with $188.6m / $203.1m / $214.0m public totals.
- [ ] Do not simply set the tourism module to the published numbers and call the model validated.

## 18.4 Deliberately weak-site tests

- [ ] Test multiple rural or poorly accessed sites.
- [ ] Confirm that lower population/income access and longer travel times reduce predicted GGR without arbitrary county penalties.
- [ ] Confirm that a site can be geographically near Fort Wayne yet score weakly if network access or competition is poor.
- [ ] Confirm that a farther destination-scale site can outperform a closer small facility when attraction and tourism justify it.

---

# 19. Uncertainty and sensitivity

## 19.1 Deterministic sensitivity first

- [ ] Expose structured sensitivity for:
  - [ ] β / travel decay;
  - [ ] attraction elasticity;
  - [ ] per-adult gaming expenditure or AGI share;
  - [ ] income elasticity;
  - [ ] proposed facility scale;
  - [ ] tourism capture;
  - [ ] traffic-intercept capture;
  - [ ] induced-demand elasticity;
  - [ ] stabilization ramp;
  - [ ] outside-option scale.

## 19.2 Probabilistic uncertainty once parameters are defensible

- [ ] Add Monte Carlo or Latin Hypercube simulation only after reasonable parameter distributions can be justified.
- [ ] Report P10/P50/P90 or similar forecast ranges.
- [ ] Do not present probabilistic intervals if parameter distributions are arbitrary.
- [ ] Separate:
  - [ ] parameter uncertainty;
  - [ ] scenario uncertainty;
  - [ ] data uncertainty.

## 19.3 Never use a fake confidence score

- [ ] Do not show “92% confidence” or a similar pseudo-statistical number unless it has a formal definition and validation basis.
- [ ] Prefer forecast ranges, sensitivity tornadoes, and validation-error summaries.

---

# 20. Revenue result contract

## 20.1 Create a production `CasinoRevenueProjectionResult`

- [ ] Include top-level values:
  - [ ] stabilized resident GGR;
  - [ ] tourism GGR;
  - [ ] traffic-intercept GGR;
  - [ ] accessibility-induced incremental GGR;
  - [ ] total stabilized GGR;
  - [ ] taxable AGR/tax base if calculated;
  - [ ] non-gaming revenue by category if calculated;
  - [ ] total property revenue;
  - [ ] Year 1-Year N ramp.
- [ ] Include patron-source decomposition:
  - [ ] target county;
  - [ ] other Northeast Indiana;
  - [ ] other Indiana;
  - [ ] Michigan;
  - [ ] Ohio;
  - [ ] other states;
  - [ ] tourism/traffic.
- [ ] Include competition decomposition:
  - [ ] incumbent baseline GGR;
  - [ ] with-project GGR;
  - [ ] delta by property;
  - [ ] repatriated out-of-state total;
  - [ ] cannibalized in-state total.
- [ ] Include diagnostics:
  - [ ] top origin ZIPs/ZCTAs;
  - [ ] capture by drive-time band;
  - [ ] market income allocation;
  - [ ] implied GGR per gaming position;
  - [ ] model warnings;
  - [ ] data completeness warnings.
- [ ] Include provenance:
  - [ ] model version;
  - [ ] parameter-set ID;
  - [ ] data snapshot IDs;
  - [ ] run ID.

---

# 21. Revenue API redesign

## 21.1 Preserve prototype endpoints only for compatibility where needed

- [ ] Do not make `/api/revenue/potential` the primary production projection endpoint after gravity implementation.
- [ ] Deprecate or clearly label heuristic endpoints.
- [ ] Keep `/zip-switching` only as a debug/legacy endpoint if useful.

## 21.2 Add production endpoints

- [ ] `POST /api/revenue/project`
  - [ ] coordinates;
  - [ ] development-program ID or inline scenario;
  - [ ] model parameter-set ID/default;
  - [ ] projection year;
  - [ ] optional expert overrides.
- [ ] `GET /api/revenue/runs/{id}` for reproducibility.
- [ ] `GET /api/revenue/runs/{id}/origins` for origin decomposition.
- [ ] `GET /api/revenue/runs/{id}/competitor-impacts`.
- [ ] `GET /api/revenue/benchmarks`.
- [ ] `POST /api/revenue/compare-sites` to evaluate multiple candidate points under one common development program/parameter set.
- [ ] Add request validation and sensible limits.

## 21.3 Avoid huge synchronous payloads

- [ ] Return summarized projection output by default.
- [ ] Load detailed origin contribution tables on demand.
- [ ] Consider async/background run persistence for heavy calibration or multi-site sweeps.

---

# 22. UI: model estimate versus manual scenario

## 22.1 Preserve current user flexibility

- [ ] Add explicit revenue-mode selection:
  - [ ] **Model projection**;
  - [ ] **Manual scenario**;
  - [ ] optional **Public-report benchmark** for educational comparison.
- [ ] Never silently replace a manual amount when the map marker moves.
- [ ] In model mode, moving the site should trigger/retrieve a new projection under the same development-program assumptions.

## 22.2 Model projection panel

- [ ] Show:
  - [ ] stabilized GGR;
  - [ ] Year 1-Year N ramp;
  - [ ] resident versus tourism/traffic split;
  - [ ] local versus out-of-state patron share;
  - [ ] forecast range/sensitivity where available;
  - [ ] selected development program;
  - [ ] model/data version.
- [ ] Show a short “Why this changed” explanation when moving the site.
  - [ ] drive-time market access changed;
  - [ ] income-weighted demand changed;
  - [ ] competitor share changed;
  - [ ] traffic/tourism assumptions changed.

## 22.3 Expert detail drawer/panel

- [ ] Allow advanced users to inspect:
  - [ ] β;
  - [ ] demand specification;
  - [ ] origin totals;
  - [ ] facility attractiveness;
  - [ ] outside-option share;
  - [ ] top competitors;
  - [ ] top origin zones;
  - [ ] benchmark reconciliation.
- [ ] Expert controls must not clutter the default public experience.

## 22.4 Map layers and existing marker assets

- [ ] Add optional map layers for:
  - [ ] competing casinos;
  - [ ] candidate drive-time rings/isochrones;
  - [ ] origin contribution heatmap;
  - [ ] modeled market share by origin;
  - [ ] competitor loss/repatriation detail where practical.
- [ ] Reuse the repository's existing casino/racetrack/tribal SVG markers in `SaveFW.Client/wwwroot/assets/map-markers` rather than creating duplicate marker sets.
- [ ] Competitor markers remain static/non-draggable.

---

# 23. Fiscal model integration

## 23.1 Convert model revenue to applicable tax bases correctly

- [ ] Build a versioned Indiana gaming-tax rules module.
- [ ] Source rates/brackets/distributions from current Indiana Code, IGC, or other authoritative state material.
- [ ] Include effective dates.
- [ ] Do not hard-code one year's tax structure permanently.
- [ ] Distinguish:
  - [ ] wagering tax;
  - [ ] supplemental wagering tax;
  - [ ] promotional-credit treatment;
  - [ ] local distributions;
  - [ ] state distributions.

## 23.2 Location-sensitive jurisdiction rules

- [ ] Determine whether the selected point is inside a municipality or unincorporated county.
- [ ] Apply local tax/distribution logic based on the actual scenario location.
- [ ] Do not automatically attribute all local revenue to Fort Wayne for a site outside the city.

## 23.3 Non-gaming taxes

- [ ] Calculate only when the non-gaming revenue/property assumptions support them.
- [ ] Separate:
  - [ ] sales tax;
  - [ ] innkeeper/lodging taxes;
  - [ ] food-and-beverage taxes where applicable;
  - [ ] property tax only when assessed-value assumptions are present;
  - [ ] corporate income tax only with an explicit taxable-income/profit assumption.
- [ ] Do not infer property tax directly from GGR.

---

# 24. Use patron origins to improve the displacement model

The gravity model should make the downstream model materially better rather than merely producing a headline revenue number.

## 24.1 Endogenize local share where possible

Existing concept:

```text
Base_local = AGR × LocalShare
D_total = Base_local × k
```

Improve it:

- [ ] Derive a modeled local-resident share directly from origin contributions.
- [ ] Let the user define the relevant “local economy” boundary:
  - [ ] host county;
  - [ ] Allen-DeKalb-Steuben Northeast Indiana region;
  - [ ] custom jurisdiction group if supported.
- [ ] Calculate:

```text
LocalResidentCasinoGGR = Σ proposed_GGR_i for origins inside local boundary
```

- [ ] Preserve a manual Local Share override for sensitivity/testing.
- [ ] Show the modeled share next to the override so the user can see divergence.

## 24.2 Decompose revenue before applying displacement

- [ ] Classify proposed gaming revenue into economically different source categories:
  - [ ] local resident spend newly induced by improved access;
  - [ ] local resident spend repatriated from out-of-region casinos;
  - [ ] local resident spend diverted from another Indiana casino;
  - [ ] imported spend from nonlocal patrons;
  - [ ] tourism/through-traffic spend.
- [ ] Do not apply the same local-displacement rate to every category.
  - [ ] Imported patron spending is not displaced from local resident household budgets.
  - [ ] Repatriated local casino spending was already leaving the local economy to the extent patrons previously spent it out of region.
  - [ ] Newly induced local gambling is more directly relevant to local discretionary-spending displacement.
- [ ] Build a transparent source-category displacement matrix.

## 24.3 Revisit the fixed displacement coefficient

- [ ] Do not retain `k = 0.243` as an unexplained immutable constant.
- [ ] Document its empirical/source basis.
- [ ] Create a versioned parameter with low/base/high sensitivity.
- [ ] If stronger literature or local calibration supports a replacement, update it and preserve backward-compatible scenario documentation.

---

# 25. Sector-weighted local business displacement

## 25.1 Preserve sector weighting, but make it data-driven

- [ ] Retain the core at-risk categories:
  - [ ] NAICS 72: accommodation and food services;
  - [ ] NAICS 44-45 combined: retail trade;
  - [ ] NAICS 71: arts, entertainment, and recreation.
- [ ] Do not double count retail by treating 44 and 45 as independent full sectors.
- [ ] Expand sectors only with documented substitution logic.

## 25.2 Replace unsupported fixed priors where better local data exist

Existing priors may remain starting assumptions:

- Dining/Hospitality 0.60
- Retail 0.30
- Entertainment 0.10

But:

- [ ] Treat these as priors, not facts.
- [ ] Modulate them using local establishment/employment/sales capacity.
- [ ] Use Census County Business Patterns, Economic Census, BLS/QCEW, or other authoritative sources as available.
- [ ] Normalize final sector weights to 1.0.

## 25.3 Sector displacement formula

- [ ] Preserve an inspectable structure such as:

```text
AtRiskLocalSpend = LocalResidentRelevantGGR × displacement_coefficient
rawWeight_s = priorWeight_s × localPresenceIndex_s × scenarioSubstitutionFactor_s
w_s = rawWeight_s / Σ rawWeight_s
DisplacedSales_s = AtRiskLocalSpend × w_s
```

- [ ] Do not claim precision beyond the quality of the substitution parameter.

## 25.4 Non-gaming casino displacement

- [ ] Add a separate optional displacement channel for on-property food, hotel, retail, and entertainment revenue.
- [ ] Avoid double counting gaming bankroll displacement and non-gaming property spending.
- [ ] Use patron origin to distinguish imported non-gaming spend from local substitution.

---

# 26. Net economic impact: do not count transfers as wholly incremental

## 26.1 Separate gross impact from net incremental impact

- [ ] If the app adds direct/indirect/induced casino output, also subtract displaced local-sector output where applicable.
- [ ] Do not apply economic multipliers to all local-resident casino revenue as if that money appeared from outside the region.
- [ ] Distinguish:
  - [ ] imported visitor spending;
  - [ ] repatriated local spending previously spent at out-of-region casinos;
  - [ ] displaced local discretionary spending;
  - [ ] newly induced gambling spend;
  - [ ] in-state casino cannibalization.

## 26.2 Input-output multipliers

- [ ] If BEA RIMS II or another input-output model is used:
  - [ ] document geography;
  - [ ] document industry codes;
  - [ ] document multiplier vintage;
  - [ ] avoid applying the same multiplier to gross casino revenue and then separately to overlapping non-gaming revenue;
  - [ ] present direct, indirect, and induced effects separately when the source supports it.
- [ ] Make clear that input-output models estimate economic activity, not social welfare.

---

# 27. Social-cost model integration

## 27.1 Preserve the existing cost model while adding reconciliation

- [ ] Do not break or silently replace the current social-cost calculations merely to make them track GGR.
- [ ] Keep the existing prevalence/cost methodology as an independently documented module until a better evidence-based replacement is explicitly adopted.
- [ ] Add reconciliation between the cost-side geographic exposure and gravity-model patron origins.

## 27.2 Move toward origin-aware exposure

- [ ] Calculate modeled gaming contribution/capture by origin zone.
- [ ] Allow the methodology layer to compare:
  - [ ] current proximity/isochrone population exposure;
  - [ ] modeled origin-zone gaming exposure;
  - [ ] incremental accessibility change.
- [ ] Investigate whether future social-cost attribution should use the gravity model's accessibility and origin mix rather than only Euclidean/drive-time population tiers.
- [ ] Do not implement a causal prevalence increase without source support.

## 27.3 Baseline problem-gambling assumptions

- [ ] Keep prevalence assumptions versioned and source-cited.
- [ ] Do not label 2.3% “conservative” unless the source context and definition support that description.
- [ ] Distinguish:
  - [ ] baseline prevalence;
  - [ ] incremental casino-attributable prevalence, if modeled;
  - [ ] problem gambling versus gambling disorder definitions where sources differ.
- [ ] Add low/base/high sensitivity and show the direct effect on cost estimates.

---

# 28. Tax-loss waterfall from displaced local business spending

- [ ] Preserve/implement sector-specific taxability rather than a flat sales-tax loss.
- [ ] For each sector `s`, maintain:
  - [ ] sales-taxability fraction `t_s`;
  - [ ] net-income margin `m_s`;
  - [ ] effective applicable income-tax rate `r_inc,s` or documented blended rate;
  - [ ] local tax add-ons where relevant.
- [ ] Calculate inspectably:

```text
SalesTaxLoss_s = DisplacedSales_s × t_s × salesTaxRate
NetIncomeLost_s = DisplacedSales_s × m_s
IncomeTaxLoss_s = NetIncomeLost_s × effectiveIncomeTaxRate_s
```

- [ ] Do not apply corporate tax rates to all small-business income if pass-through treatment is more appropriate.
- [ ] Version tax assumptions by effective date.
- [ ] Separate state, county, municipal, and special-district effects.

---

# 29. Data quality and missing-data hierarchy

## 29.1 Define fallback rules before runtime

- [ ] For each material model field, define an ordered fallback hierarchy.
- [ ] Example for competitor GGR scale:
  1. authoritative property-level trailing-12-month GGR;
  2. authoritative latest full-year GGR;
  3. regulator-reported gaming win by category summed consistently;
  4. physical-scale comparable estimate;
  5. explicit flagged fallback.
- [ ] Never quietly replace missing data with zero.

## 29.2 Add completeness scores

- [ ] For every competitor, calculate data completeness for:
  - [ ] observed GGR;
  - [ ] gaming positions;
  - [ ] tables;
  - [ ] hotel;
  - [ ] amenities;
  - [ ] location;
  - [ ] source recency.
- [ ] Surface material missing-data warnings in model-run diagnostics.

---

# 30. Automated tests

## 30.1 Gravity mathematics unit tests

- [ ] Increasing a facility's travel time while all else is equal must not increase its share.
- [ ] Increasing attraction while all else is equal must not reduce its share.
- [ ] Facility shares plus outside share must sum to 1 within tolerance.
- [ ] Adding a strong nearby competitor must not increase the proposed site's resident share unless another modeled mechanism explicitly explains it.
- [ ] Zero/negative population, income, AGI, GGR, or malformed parameters must fail validation or be handled explicitly.
- [ ] Very large/small utilities must not create `NaN`, infinity, or overflow.
- [ ] The same inputs and model/data versions must produce deterministic results.

## 30.2 Network-routing tests

- [ ] Known origin/facility pairs return plausible Valhalla times.
- [ ] Cached and live travel-time results agree within expected graph/version tolerance.
- [ ] Route failure does not become zero travel time.
- [ ] Haversine prefilter never excludes a route that could materially contribute because of an overly tight threshold.

## 30.3 Accounting identity tests

- [ ] Proposed GGR equals the sum of origin contributions plus separately modeled tourism/traffic components.
- [ ] Baseline-to-with-project incumbent deltas reconcile with proposed capture and induced demand.
- [ ] Patron-source shares sum to 100%.
- [ ] Local/nonlocal decomposition sums to resident GGR.
- [ ] Displacement categories do not exceed the associated local-resident revenue bases.
- [ ] Fiscal totals reconcile to component taxes.

## 30.4 Benchmark regression tests

- [ ] Save versioned benchmark-output snapshots for Allen, I-69/SR8, and Steuben scenarios.
- [ ] Fail tests only on unexplained large structural changes, not tiny floating-point changes.
- [ ] Require an intentional benchmark-update note when changing model parameters or source vintages.

---

# 31. Performance and caching tests

- [ ] Benchmark candidate-site projection latency with warm incumbent travel matrix.
- [ ] Benchmark cold route computation.
- [ ] Ensure origin-detail output is paged/lazy when large.
- [ ] Add indexes for:
  - [ ] origin IDs;
  - [ ] property IDs;
  - [ ] period dates;
  - [ ] geometry/geography fields;
  - [ ] model run IDs;
  - [ ] travel matrix composite keys.
- [ ] Verify no N+1 database pattern when evaluating hundreds/thousands of origins against the competitive field.

---

# 32. Required calibration/diagnostic artifacts

The AI agent must generate machine-readable calibration outputs in the repository or an intentional generated-data location. Do not leave validation only in console logs.

- [ ] `docs/model/gravity_model_methodology.md`
- [ ] `docs/model/data_dictionary.md`
- [ ] `docs/model/data_sources.md`
- [ ] `docs/model/calibration_methodology.md`
- [ ] `docs/model/benchmark_reconciliation.md`
- [ ] A machine-readable parameter-set file or database seed.
- [ ] A benchmark scenario file containing exact coordinates and development-program assumptions.
- [ ] Validation metrics export, preferably CSV/JSON generated by a repeatable command.
- [ ] A repeatable calibration command/script documented in README or model docs.

---

# 33. Agent implementation sequence

Do these in order unless a repository dependency requires a documented deviation.

## Phase A: audit and data architecture

- [ ] Step A1: inventory all existing revenue, competitor, census, isochrone, tax, displacement, and social-cost code.
- [ ] Step A2: document which prototype components will be retained, refactored, deprecated, or deleted.
- [ ] Step A3: add source catalog and dataset snapshots.
- [ ] Step A4: expand competitor schema and add performance-history schema.
- [ ] Step A5: create development-program and parameter-set schema.
- [ ] Step A6: create origin-zone and travel-matrix schema.

## Phase B: data ingestion

- [ ] Step B1: ingest/derive age-21+ origin population.
- [ ] Step B2: ingest IRS SOI ZIP AGI and supporting ACS income.
- [ ] Step B3: build ZIP/ZCTA/block-group crosswalk and representative points.
- [ ] Step B4: build authoritative competitor universe.
- [ ] Step B5: ingest historical GGR and physical property scale.
- [ ] Step B6: validate totals, vintages, missing fields, and hashes.

## Phase C: routing

- [ ] Step C1: build batched Valhalla origin-to-incumbent routing.
- [ ] Step C2: persist travel matrix.
- [ ] Step C3: build candidate-site dynamic/cached routing.
- [ ] Step C4: validate travel-time bands against known map routes.

## Phase D: demand engines

- [ ] Step D1: implement AGI-share demand.
- [ ] Step D2: implement age-21+ per-capita/income-elasticity demand.
- [ ] Step D3: build reconciliation output.
- [ ] Step D4: normalize all model dollars to one declared real-dollar year.

## Phase E: attraction and gravity engine

- [ ] Step E1: implement structural property mass.
- [ ] Step E2: implement observed-GGR-mass reconciliation approach.
- [ ] Step E3: implement inverse-power travel decay.
- [ ] Step E4: implement outside alternative / broad competitor treatment.
- [ ] Step E5: implement stable share allocation.
- [ ] Step E6: implement baseline and with-project equilibria.
- [ ] Step E7: implement competitor delta decomposition.

## Phase F: calibration and validation

- [ ] Step F1: create existing-property calibration dataset.
- [ ] Step F2: optimize a parsimonious parameter set.
- [ ] Step F3: run holdout/cross-validation.
- [ ] Step F4: implement regression reasonableness model.
- [ ] Step F5: implement comparable income-allocation check.
- [ ] Step F6: reject/repair structurally bad parameters even if fit metrics appear good.

## Phase G: nonresident demand

- [ ] Step G1: implement tourism module.
- [ ] Step G2: implement Steuben lake-tourism benchmark scenario.
- [ ] Step G3: ingest INDOT traffic and implement traffic-intercept module.
- [ ] Step G4: prevent resident/tourist/traffic double counting.

## Phase H: benchmark reconciliation

- [ ] Step H1: Allen/CBRE scenario.
- [ ] Step H2: Spectrum I-69/SR8 scenario.
- [ ] Step H3: Steuben/AMS scenario.
- [ ] Step H4: produce one written reconciliation explaining the spread in estimates.

## Phase I: fiscal and downstream integration

- [ ] Step I1: connect projected GGR to tax-base calculations.
- [ ] Step I2: derive modeled local-resident share from origin contributions.
- [ ] Step I3: classify proposed GGR by repatriation/cannibalization/induced/imported source.
- [ ] Step I4: refactor sector-weighted displacement around those source categories.
- [ ] Step I5: add non-gaming displacement separately where defensible.
- [ ] Step I6: reconcile with existing social-cost geographic model without silently rewriting its methodology.

## Phase J: API and UI

- [ ] Step J1: add production projection API.
- [ ] Step J2: add run persistence and detail APIs.
- [ ] Step J3: add model/manual revenue modes.
- [ ] Step J4: add projection explanation and diagnostics.
- [ ] Step J5: add map origin/catchment layers.
- [ ] Step J6: follow repository UI minimum-text-size and dropdown guardrails.

## Phase K: final QA

- [ ] Step K1: run all unit tests.
- [ ] Step K2: run benchmark integration tests.
- [ ] Step K3: run performance tests.
- [ ] Step K4: verify no stale prototype endpoint is presented as the production model.
- [ ] Step K5: verify methodology and source links are visible from the user-facing model.
- [ ] Step K6: verify every headline result reconciles to origin/component detail.

---

# 34. Production acceptance criteria

The full model is not complete until every applicable item below passes.

## 34.1 Data

- [ ] Age-21+ population is available by production origin geography.
- [ ] AGI/income is available by production origin geography with documented crosswalks.
- [ ] Competitor field is substantially complete for the modeled Northeast Indiana trade area and edge origins.
- [ ] Major incumbent properties have observed GGR and physical-scale records.
- [ ] Material model inputs carry source/vintage provenance.

## 34.2 Gravity model

- [ ] Uses network travel time rather than Haversine distance for primary allocation.
- [ ] Uses calibrated facility mass rather than hand-set venue-type weights as the principal attraction measure.
- [ ] Calculates origin-specific facility shares.
- [ ] Handles outside/unmodeled capture explicitly.
- [ ] Runs baseline and with-project scenarios.
- [ ] Produces competitor cannibalization/repatriation deltas.
- [ ] Does not contain circular proposed-property GGR attraction.

## 34.3 Validation

- [ ] Existing-property validation metrics are recorded.
- [ ] Holdout performance is recorded.
- [ ] Regression/comparable-market reasonableness check exists.
- [ ] Allen, Spectrum Northeast, and Steuben benchmark reconciliations exist.
- [ ] No parameter was selected solely to hit one consultant's headline forecast.

## 34.4 Revenue output

- [ ] Resident GGR is separated from tourism/traffic GGR.
- [ ] Stabilized GGR is separated from opening-year ramp.
- [ ] GGR is distinguished from taxable AGR.
- [ ] Origin contribution and local/nonlocal shares are available.
- [ ] Forecast sensitivities/ranges are available.

## 34.5 Downstream integration

- [ ] Gravity origin data feeds modeled local share.
- [ ] Imported, repatriated, cannibalized, and induced revenue are distinguishable.
- [ ] Sector displacement does not treat imported patron spending as local household displacement.
- [ ] Economic multipliers do not turn local spending transfers into fake wholly incremental output.
- [ ] Social-cost logic remains separately sourced and auditable.

## 34.6 User experience

- [ ] User can choose model projection or manual scenario.
- [ ] No hidden revenue overrides occur.
- [ ] User can see why two locations produce different GGR.
- [ ] User can inspect assumptions and data vintage.
- [ ] Public-report benchmarks are clearly labeled as comparisons, not endorsements or ground truth.

---

# 35. Non-negotiable failure conditions

The implementation is unacceptable if any of the following remain in the production forecast path without a documented, temporary compatibility reason.

- [ ] Do not use straight-line distance as the principal travel friction when network routing is available.
- [ ] Do not use `Population × 0.75` as the age-eligible population estimate.
- [ ] Do not use arbitrary fixed participation and GGR-per-participant defaults as the production demand model.
- [ ] Do not allocate 100% of an incomplete origin demand pool among only the listed casinos.
- [ ] Do not use a fixed Fort Wayne point as a substitute for origin-specific competitive overlap.
- [ ] Do not use hand-entered amenity adders as the sole competitive-mass model.
- [ ] Do not define proposed-casino attraction from its own predicted GGR.
- [ ] Do not add tourism/traffic demand without subtracting overlap with resident demand.
- [ ] Do not call a benchmark-calibrated multiplier a statistical forecast.
- [ ] Do not call GGR, AGR, and taxable gaming revenue the same thing.
- [ ] Do not treat all proposed GGR as economically incremental to the host region.
- [ ] Do not treat all local-resident GGR as equally displaced local business spending.
- [ ] Do not apply positive economic multipliers to transferred local spending without the offsetting displacement side.
- [ ] Do not claim replication of Spectrum's operator-data model.
- [ ] Do not overfit to CBRE, Spectrum, or A.M. Steinberg headline estimates.
- [ ] Do not mark this plan complete until the validation artifacts exist.

---

# 36. Final directive to the implementing AI agent

- [ ] Transform the existing prototype revenue logic into a genuine mass-weighted, origin-based casino gravity model.
- [ ] Build the model around network travel time, age-eligible population, income/AGI, calibrated casino scale, and a complete competitive field.
- [ ] Use public Allen/CBRE, Spectrum Northeast/DeKalb, and Steuben/AMS reports as independent benchmark anchors.
- [ ] Triangulate the gravity output using a separate regression/comparable-income reasonableness model.
- [ ] Model resident demand, tourism, and highway traffic through separate auditable channels.
- [ ] Run baseline and with-project equilibria so cannibalization, repatriation, and incremental demand can be measured rather than guessed.
- [ ] Feed patron-origin results into the downstream displacement model so local-share assumptions become evidence-informed.
- [ ] Preserve the application's existing strength: it evaluates costs and downstream effects that the public market-feasibility reports either omit or treat much more narrowly.
- [ ] Present gross benefits and downstream costs in the same scenario framework so a user can see the full net-impact picture.
- [ ] Favor transparent equations, versioned assumptions, source provenance, and out-of-sample validation over apparent precision.
