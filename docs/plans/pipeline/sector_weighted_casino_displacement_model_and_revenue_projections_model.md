# National Casino Gravity, Revenue Projection, Cannibalization, and Comprehensive Economic Impact Model

## Governing AI Agent Implementation Checklist

> **Status:** Pipeline / Not Started. This document supersedes the earlier Northeast-Indiana-specific revenue heuristic plan and the first gravity-model expansion.
>
> **Primary objective:** Build a transparent, empirically calibrated, nationally reusable casino gravity and economic-impact engine that can evaluate a proposed casino or major gaming development anywhere in the United States. The engine must estimate site-specific gaming revenue, patron origins, market expansion, cannibalization, repatriation/leakage, tourism and through-traffic demand, sector displacement, fiscal effects, employment effects, and downstream social/economic costs. The same immutable model run must power the interactive web application, APIs, sensitivity analysis, and a server-generated full analytical report comparable in structure and rigor to professional casino feasibility and impact studies.
>
> **Current Indiana use case:** Allen County, DeKalb County, Steuben County, and the surrounding Northeast Indiana market are the first production use case and an important validation suite. They are **not** the model's hard-coded geography. Indiana-specific assumptions, tax rules, origin groupings, competitors, benchmark reports, and labels must live in jurisdiction/scenario configuration and validation data rather than in the core model.
>
> **Implementation posture:** Do not treat `RevenueHeuristicService`, `CompetitionScoringService`, or `ZipSwitchingModelService` as the finished model. They are prototypes and scaffolding. Preserve technically useful code where appropriate, but replace unsupported assumptions, fixed Fort Wayne logic, hand-set competitive weights, and straight-line travel approximations with a calibrated, auditable, configurable national model.

---

# 0. Read this first: non-negotiable agent instructions

- [ ] Read this entire document before changing production code.
- [ ] Inspect the current repository before implementing each section.
  - [ ] Reuse working infrastructure where it is technically sound.
  - [ ] Refactor prototype logic rather than creating duplicate parallel systems unless separation is intentional and documented.
  - [ ] Follow `AGENTS.md` and all repository-specific UI and engineering guardrails.
- [ ] Build the core model to be geographically neutral.
  - [ ] Do not hard-code Fort Wayne, Allen County, DeKalb County, Steuben County, Indiana, or any specific competitor into reusable model services.
  - [ ] Do not hard-code a fixed list of patron-origin counties or states into reports.
  - [ ] Do not hard-code Indiana tax treatment into the national fiscal engine.
  - [ ] Do not assume every casino market uses age 21 as the legal gaming age.
  - [ ] Do not assume all relevant competitors are located in the same state or even the same country.
- [ ] Treat Indiana-specific public reports as benchmark and validation cases only.
  - [ ] Spectrum Gaming Group, CBRE/Union Gaming Analytics, and A.M. Steinberg Advisors are useful methodological references and validation anchors.
  - [ ] Their site-specific outputs are not universal model constants.
- [ ] Do not mark a checklist item complete merely because code was written.
  - [ ] A data task is complete only after ingestion, provenance, validation, persistence, and reproducibility are complete.
  - [ ] A model task is complete only after tests, calibration/validation output, documented assumptions, and failure handling exist.
  - [ ] A UI task is complete only after the server integration works and the user can distinguish defaults, calibrated values, overrides, and outputs.
  - [ ] A report task is complete only when a stored `ModelRun` can reproduce the same report deterministically.
- [ ] Do not hard-code a coefficient solely because a public consultant used it.
  - [ ] Public consultant assumptions are priors and validation anchors, not automatic truth.
  - [ ] Any adopted coefficient must have a source, calibration rationale, validation result, sensitivity range, or a documented combination of these.
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

- [ ] Retire `RevenueHeuristicService` as the primary revenue estimator after the gravity engine is production-ready.
- [ ] Preserve only useful explainability/site-quality concepts if desired.
- [ ] Remove or replace these structures from production revenue estimation:
  - [ ] fixed distance penalties from downtown Fort Wayne;
  - [ ] fixed 30-mile and 50-mile Haversine thresholds;
  - [ ] market depth based on approximate degree-radius queries;
  - [ ] `population × 0.75` as an adult population proxy;
  - [ ] fixed $65,000 income normalization;
  - [ ] fixed `benchmarkDepth = 400000`;
  - [ ] `accessScore × depthScore - competitionPenalty` as a revenue multiplier;
  - [ ] arbitrary high/moderate/low revenue-potential cutoffs.
- [ ] If retained, expose the heuristic only as a diagnostic and never as the gravity-model GGR forecast.

## 1.3 Deficiency: competition scoring is hand-weighted and market-center-centric

- [ ] Replace hand-set values such as full-service casino `1.00`, racino `0.70`, hotel `+0.15`, tables `+0.20`, etc. as the principal competitive-mass mechanism.
- [ ] Do not infer competitive overlap from each competitor's distance to one central city.
- [ ] Calculate competition from every modeled origin to every relevant facility.
- [ ] Use network travel time as primary travel friction.
- [ ] Use observed and/or structurally calibrated facility scale for attraction.
- [ ] Preserve simple feature scores only as fallback diagnostics or explanatory metadata.

## 1.4 Deficiency: current ZIP switching model is an uncalibrated prototype

- [ ] Keep the useful origin-to-facility share concept but replace unsupported defaults and mechanics.
- [ ] Replace Haversine miles with cached network travel time and network distance.
- [ ] Replace unsupported defaults such as:
  - [ ] `ParticipationRate = 0.28`;
  - [ ] `AnnualGgrPerParticipant = 1200`;
  - [ ] `DistanceBeta = 0.06` in a linear utility specification;
  - [ ] arbitrary proposed venue quality of `1.0`.
- [ ] Do not force the full modeled demand pool across an incomplete list of casinos.
  - [ ] Build a sufficiently complete competitive field and/or a calibrated outside option.
- [ ] Use numerically stable share calculations.
- [ ] Do not derive incumbent attractiveness solely from current feature-addition scoring.
- [ ] Do not use request-body ZIP inputs as the long-term production source of market demand.
  - [ ] Production inputs must come from versioned persisted datasets.
  - [ ] API request overrides may remain for testing and expert scenarios.

## 1.5 Deficiency: current data model is too shallow for a national mass-weighted model

- [ ] Expand competitor records beyond boolean amenities.
- [ ] Add historical observed performance.
- [ ] Add physical/development scale.
- [ ] Add jurisdiction/regulator identity.
- [ ] Add source-level provenance per material attribute where feasible.
- [ ] Replace short hand-entered competitor lists with rule-driven regional competitive-universe assembly.

## 1.6 Deficiency: current architecture is too Indiana-specific for the intended product

- [ ] Extract jurisdiction-specific rules from core services.
- [ ] Replace hard-coded Indiana tax assumptions with effective-dated jurisdiction profiles.
- [ ] Replace hard-coded 21+ assumptions with legal-gaming-age-aware population calculations.
- [ ] Replace fixed county/state patron-origin report categories with dynamic geographic aggregation.
- [ ] Treat Indiana as the first jurisdiction adapter and validation case, not the model definition.

---

# 2. Public benchmark studies and validation cases

These studies are required methodological references for the initial Indiana validation suite. They do not define the national model architecture.

## 2.1 Spectrum Gaming Group: Indiana Gaming Commission relocation study

Primary source:

- `https://www.in.gov/igc/files/publications/Spectrum-Relocation-Report-to-Indiana-Gaming-Commission-9-30-2025-Final.pdf`

- [ ] Understand Spectrum's public-data demand construction.
  - [ ] ZIP-level adjusted gross income was used to estimate gaming-market potential.
  - [ ] National casino-revenue-to-AGI reference was approximately 0.58% using cited 2022 data.
  - [ ] Indiana mature-market reference was approximately 0.66%.
  - [ ] Treat these as benchmark priors, not immutable constants.
- [ ] Understand Spectrum's data advantage.
  - [ ] It obtained rated/tracked play by ZIP from Indiana operators through the Indiana Gaming Commission.
  - [ ] This project does not possess equivalent patron-level operator data unless lawfully obtained later.
- [ ] Retain Spectrum's drive-time and capture concepts.
- [ ] Retain its Northeast Indiana proxy result as a validation anchor, not a forced target.

## 2.2 CBRE / Union Gaming Analytics: Greater Fort Wayne Area Casino Analysis

Primary source:

- `https://cdn.insideindianabusiness.com/wp-content/uploads/2026/01/GFWI-Casino-Analysis-Presentation-Final-2025-12-03.pdf`

- [ ] Retain its public gravity-model concepts:
  - [ ] population;
  - [ ] income;
  - [ ] project and competitor attractiveness/development scale;
  - [ ] distance/travel friction.
- [ ] Retain its development-program concept.
  - [ ] The proposed development program must affect attraction independently of latitude/longitude.
- [ ] Retain separate out-of-market highway traffic demand.
- [ ] Retain stabilization/ramp analysis.
- [ ] Retain independent regression/comparable-market reasonableness testing.
- [ ] Retain its published Northeast Indiana outputs and competitor impacts as validation targets, not required outcomes.

## 2.3 A.M. Steinberg Advisors: Steuben County Gaming Market Feasibility Study

Primary source:

- `https://www.steubenedc.com/media/userfiles/subsite_259/files/SCEDC_Feasibility_Study_FINAL.pdf`

- [ ] Retain its explicitly described mass-weighted gravity concepts:
  - [ ] projected casino-eligible adult population;
  - [ ] income-adjusted gaming expenditure;
  - [ ] travel-time/distance decay;
  - [ ] base beta around `1.5`;
  - [ ] sensitivity around `1.4` to `1.6`;
  - [ ] incumbent competitive mass materially informed by observed GGR;
  - [ ] broad competitive inclusion;
  - [ ] separate tourism demand.
- [ ] Treat beta `1.5` as an initial prior/default candidate for the Indiana base parameter set, not a universal national constant.
- [ ] Retain its low/base/high revenue outputs as Indiana validation benchmarks.

## 2.4 Benchmark-study reconciliation

- [ ] Build a benchmark registry that can hold any public or private study used for validation.
- [ ] Store:
  - [ ] benchmark ID;
  - [ ] market/geography;
  - [ ] study date;
  - [ ] consultant/source;
  - [ ] candidate location;
  - [ ] development program;
  - [ ] reported revenue outputs;
  - [ ] reported model assumptions;
  - [ ] methodological notes;
  - [ ] source URL/file provenance.
- [ ] Explain differences rather than forcing equality.
- [ ] Allow future benchmark suites for other states and markets without code changes.

---

# 3. National jurisdiction abstraction

## 3.1 Create jurisdiction profiles

- [ ] Create `jurisdictions` and effective-dated `jurisdiction_rules` or equivalent.
- [ ] Support at minimum:
  - [ ] federal/national context;
  - [ ] state;
  - [ ] county/parish/borough where applicable;
  - [ ] municipality where fiscal sharing depends on local location;
  - [ ] tribal jurisdiction/compact context where applicable.
- [ ] A jurisdiction profile must not assume that every casino is a commercial state-regulated casino.

## 3.2 Required jurisdiction rule fields

- [ ] Legal gaming age by facility/regime where applicable.
- [ ] Gaming product types permitted.
- [ ] Applicable gaming revenue definition.
- [ ] Gaming/wagering tax rates and brackets.
- [ ] Promotional-credit/free-play treatment.
- [ ] Admission or device taxes where applicable.
- [ ] Local revenue-sharing rules.
- [ ] State/local sales tax treatment of non-gaming revenue.
- [ ] State/local income or business tax assumptions relevant to impact analysis.
- [ ] Effective dates for every fiscal rule.
- [ ] Source/provenance links.
- [ ] Tribal compact or revenue-sharing treatment where public and applicable.

## 3.3 Jurisdiction provider/adaptor pattern

- [ ] Implement jurisdiction fiscal rules behind a service interface rather than giant `switch(state)` logic.
- [ ] Example conceptual services:
  - [ ] `IJurisdictionProfileService`;
  - [ ] `IGamingTaxCalculator`;
  - [ ] `ILocalRevenueShareCalculator`;
  - [ ] `IGamingAgeResolver`.
- [ ] Implement Indiana first.
- [ ] Make adding a new state primarily a data/configuration exercise unless the state's rules genuinely require custom logic.
- [ ] Throw a clear unsupported-jurisdiction warning when fiscal rules are incomplete rather than applying Indiana defaults.

---

# 4. Model terminology and accounting identities

## 4.1 Do not use GGR and AGR interchangeably

- [ ] Define and use consistently:
  - [ ] **GGR / casino win:** patron wagers minus gaming payouts before jurisdiction-specific taxable adjustments.
  - [ ] **Taxable gaming revenue/base:** jurisdiction-defined amount used for gaming-tax calculation.
  - [ ] **Non-gaming revenue:** hotel, food and beverage, entertainment, retail, and other property revenue.
  - [ ] **Total property revenue:** gaming plus non-gaming revenue.
- [ ] Resolve terminology through the selected jurisdiction profile.
- [ ] Do not label a generic national output `AGR` if the underlying jurisdiction uses another statutory definition.

## 4.2 Origin/facility notation

- [ ] `i` = origin zone.
- [ ] `j` = casino/facility alternative.
- [ ] `D_i` = annual resident gaming-expenditure pool generated by origin `i`.
- [ ] `T_ij` = network travel time from origin `i` to facility `j`.
- [ ] `L_ij` = network travel distance.
- [ ] `A_j` = calibrated attraction/competitive mass of facility `j`.
- [ ] `F_ij` = travel-friction function.
- [ ] `W_ij` = unnormalized attraction weight.
- [ ] `P_ij` = modeled share/probability of origin `i` gaming expenditure allocated to facility `j`.
- [ ] `R_j,resident` = resident GGR captured by facility `j`.
- [ ] `R_j,tourism` = incremental tourism GGR.
- [ ] `R_j,traffic` = incremental through-traffic/intercept GGR.
- [ ] `R_j,total` = stabilized total GGR.

---

# 5. Data provenance and reproducibility

## 5.1 Create a source catalog

- [ ] Add `data_sources`.
- [ ] Include:
  - [ ] ID;
  - [ ] name;
  - [ ] publisher;
  - [ ] URL;
  - [ ] source type;
  - [ ] geographic coverage;
  - [ ] vintage/period;
  - [ ] retrieved timestamp;
  - [ ] license/terms notes;
  - [ ] content hash;
  - [ ] authoritative-source flag;
  - [ ] notes.

## 5.2 Create immutable dataset snapshots

- [ ] Add `dataset_snapshots`.
- [ ] Store source, period, ingestion time, row count, checksum, transform version, validation state, and warnings/errors.
- [ ] Never overwrite a dataset used by a prior model run without preserving the original snapshot identity.

## 5.3 Model runs must reference exact data

- [ ] Add immutable `model_runs`.
- [ ] Store:
  - [ ] run UUID;
  - [ ] model version;
  - [ ] jurisdiction profile/version;
  - [ ] base parameter-set ID/version;
  - [ ] resolved parameter values after all overrides;
  - [ ] override audit records;
  - [ ] scenario ID;
  - [ ] site coordinates;
  - [ ] development-program ID/version;
  - [ ] origin-demographic snapshot;
  - [ ] income/AGI snapshot;
  - [ ] competitor snapshot;
  - [ ] observed-performance snapshot;
  - [ ] travel-matrix graph/version hash;
  - [ ] tourism/traffic snapshot IDs;
  - [ ] economic/social-cost assumption versions;
  - [ ] creation timestamp;
  - [ ] execution duration;
  - [ ] warning/error summary.

---

# 6. National origin geography and patron-market definition

## 6.1 Use flexible origin geographies

- [ ] Support ZCTA/ZIP-compatible origin zones as a primary U.S. demand geography.
- [ ] Do not assume USPS ZIP Codes and Census ZCTAs are identical.
- [ ] Retain Census block groups for higher-resolution demographic allocation.
- [ ] Support tract/county aggregation where source data or performance requires it.
- [ ] Design origin IDs generically enough to support future non-U.S. border-market data if needed.

## 6.2 Create `origin_zones`

- [ ] Store:
  - [ ] origin ID/type;
  - [ ] geography code;
  - [ ] state/territory;
  - [ ] county/parish equivalents;
  - [ ] MSA/CSA or other regional identifiers where available;
  - [ ] representative population-weighted point;
  - [ ] area geometry;
  - [ ] source snapshot IDs.

## 6.3 Use legal-gaming-age-aware population

- [ ] Do not globally hard-code age 21+.
- [ ] Resolve the relevant legal gaming age from the facility/jurisdiction scenario.
- [ ] Derive eligible population for common thresholds such as 18+ and 21+ from ACS age bins.
- [ ] Preserve raw age-bin data and interpolation method.
- [ ] Validate totals against county/state controls.
- [ ] Support projection to scenario year using explicit population-growth assumptions and data sources.

## 6.4 Dynamic patron-origin reporting

- [ ] Patron-origin analysis must be generated from the actual model run.
- [ ] Do **not** hard-code report categories such as Allen County, DeKalb County, Steuben County, Rest of Indiana, Michigan, Ohio, Other.
- [ ] Generate origin summaries dynamically using relevant dimensions:
  - [ ] top origin counties/parishes by modeled GGR;
  - [ ] top origin ZIP/ZCTA zones;
  - [ ] state/territory totals;
  - [ ] host county and host state;
  - [ ] host MSA/CSA where appropriate;
  - [ ] in-jurisdiction vs out-of-jurisdiction;
  - [ ] in-state vs out-of-state for state-regulated projects;
  - [ ] cross-border international origins when relevant;
  - [ ] resident vs tourism vs through-traffic components.
- [ ] Use configurable top-N thresholds and group immaterial residual origins as `Other origins` only after preserving the full detail in data/export.
- [ ] For the current Indiana scenario, Allen, DeKalb, Steuben, Michigan, and Ohio may naturally appear because they contribute material demand. They must appear because the model finds them, not because report code knows their names.

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

- [ ] Implement:

```text
D_i_AGI = RealIncomeMass_i × GamingIncomeShare_region × OriginAdjustment_i
```

- [ ] Use IRS SOI ZIP-level AGI for U.S. origins where available.
- [ ] Treat Spectrum's national and Indiana ratios as initial benchmark priors, not universal constants.
- [ ] Allow state/region-specific gaming intensity.
- [ ] Calibrate against observed market gaming revenue where possible.
- [ ] Prevent double income weighting.

## 7.2 Specification B: eligible-adult per-capita expenditure model

- [ ] Implement:

```text
D_i_PCE = EligibleAdults_i × BaseGamingExpenditurePerAdult × IncomeAdjustment_i
IncomeAdjustment_i = (IncomeMetric_i / RegionalReferenceIncome)^epsilon_income
```

- [ ] Resolve `EligibleAdults_i` using the scenario's gaming-age rule.
- [ ] Calibrate `BaseGamingExpenditurePerAdult` by market/regime.
- [ ] Make `epsilon_income` a versioned configurable parameter.
- [ ] Bound extreme origin adjustments.
- [ ] Treat consultant per-adult values only as priors/benchmarks.

## 7.3 Demand-model reconciliation

- [ ] Produce both demand specifications for validation runs.
- [ ] Compare total demand, state totals, distance bands, and largest origin differences.
- [ ] Select base specification based on validation performance.
- [ ] Support optional validated ensemble with versioned weights.
- [ ] Never add two alternative demand specifications together as if they represent separate demand pools.

---

# 8. Competitive casino universe

## 8.1 Define inclusion rules before collecting properties

- [ ] Build a competitive field based on origin accessibility, not simply distance from the proposed site.
- [ ] Include commercial casinos, racinos, tribal casinos, and sufficiently substitutable facilities.
- [ ] Treat sportsbook-only, OTB, charity gaming, distributed gaming, and other limited products separately unless evidence supports material substitution with casino-floor GGR.
- [ ] Include facilities outside the host state when they compete for the same origins.
- [ ] Permit Canada/Mexico or other cross-border facilities when they materially compete with a U.S. border market.
- [ ] Include a geographic/attraction margin beyond the nominal candidate trade area or calibrate an outside option.

## 8.2 Expand `casino_competitors`

- [ ] Add stable identity and regulatory fields.
- [ ] Add commercial/tribal/racino status.
- [ ] Add regulator/jurisdiction.
- [ ] Add opening/closure/operator-change history.
- [ ] Add physical scale:
  - [ ] slots/VLT positions;
  - [ ] table games;
  - [ ] poker where material;
  - [ ] gaming floor size;
  - [ ] hotel rooms;
  - [ ] event/entertainment capacity;
  - [ ] food/beverage scale;
  - [ ] resort/spa/golf/destination amenities;
  - [ ] development cost and dollar year when public.
- [ ] Add access/context:
  - [ ] interstate/limited-access proximity;
  - [ ] interchange access;
  - [ ] urban/local/destination orientation;
  - [ ] border-market indicator.

## 8.3 Create observed performance history

- [ ] Create `casino_gaming_revenue_periods`.
- [ ] Store monthly data when available and derive annual/trailing values.
- [ ] Store exact metric definition because states report GGR/AGR/win differently.
- [ ] Store source snapshot and inflation-adjusted values.
- [ ] Flag pandemic, construction, opening, closure, labor-disruption, and other anomalous periods.
- [ ] Build regulator/provider adapters by jurisdiction rather than one Indiana-only ingestion service.
- [ ] Prefer authoritative gaming regulators and tribal/public filings where available.

---

# 9. Network travel-time matrix

## 9.1 Use Valhalla as primary travel friction

- [ ] Build origin-to-facility network travel times.
- [ ] Use Haversine distance only for prefiltering, fallback diagnostics, and tests.
- [ ] Capture travel time and routed distance.
- [ ] Use a consistent automobile routing profile unless scenario-specific evidence requires another profile.

## 9.2 Persist the matrix

- [ ] Create `origin_facility_travel`.
- [ ] Key by origin, facility/scenario facility, routing graph hash/version, and costing profile.
- [ ] Store minutes, distance, route-found flag, and timestamp.
- [ ] Precompute stable incumbent routes.
- [ ] For a movable proposed site, compute/cached only the new facility column where practical.

## 9.3 National performance requirements

- [ ] Do not precompute the entire United States against every casino if not needed.
- [ ] Determine a candidate study region from broad Haversine/accessibility filters, then compute exact routes for relevant origins/facilities.
- [ ] Cache reusable matrices by regional tile/market.
- [ ] Support offline/background matrix warming for likely candidate grids.

---

# 10. Facility attractiveness and competitive mass

## 10.1 Replace arbitrary feature-addition weights

- [ ] Do not use `1.00 + 0.15 + 0.20` style scoring as the production attraction model.
- [ ] Build `FacilityAttractivenessService`.
- [ ] Separate physical/development attraction from travel friction.
- [ ] Avoid double counting correlated scale measures.

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

- [ ] Implement a second attraction specification where incumbent competitive mass is anchored materially to stabilized observed gaming revenue.
- [ ] Do not define proposed-casino mass from the same projected GGR the model is solving for.
- [ ] Map the proposed development program to equivalent competitive mass using comparable-property relationships.
- [ ] Use this specification as primary or reconciliation model based on validation performance.

## 10.4 Proposed development program

- [ ] Create versioned `development_programs`.
- [ ] Include:
  - [ ] slots/VLTs;
  - [ ] tables;
  - [ ] poker if applicable;
  - [ ] sportsbook;
  - [ ] hotel rooms;
  - [ ] gaming floor size;
  - [ ] restaurants/bars;
  - [ ] entertainment/event capacity;
  - [ ] resort amenities;
  - [ ] announced/estimated capital cost;
  - [ ] opening/stabilization assumptions.
- [ ] Let users configure the proposed program independently from the site.

---

# 11. True gravity/Huff allocation model

## 11.1 Primary attraction equation

- [ ] Support an inverse-power Huff/gravity formulation:

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

- [ ] Support an exponential-decay alternative for validation:

```text
W_ij = A_j^alpha × B_ij × exp(-lambda × T_ij)
```

- [ ] Select production friction form based on validation, not preference.

## 11.2 Market-share equation

- [ ] Calculate:

```text
P_ij = W_ij / (W_i0 + Σ_k W_ik)
```

- [ ] `W_i0` is the optional outside/unmodeled alternative.
- [ ] Ensure all shares are non-negative and sum to 1 including outside share.
- [ ] Use numerically stable denominator math.

## 11.3 Distance-decay beta

- [ ] Give beta a calibrated system default.
- [ ] For the initial Northeast Indiana base parameter set, beta around `1.5` may be seeded from the Steinberg study before calibration.
- [ ] Test the published `1.4` to `1.6` range and expand validation search where needed.
- [ ] Determine whether one beta is adequate nationally.
- [ ] Support market/facility-segment parameter sets when evidence supports materially different travel behavior.
- [ ] **Expose beta as a front-end user-overridable parameter.**
- [ ] Show the default, current value, validated/recommended range, units/interpretation, and source/calibration note.
- [ ] Permit values outside the validated range unless a numerical-safety bound is crossed, but show a visible warning in the UI and report.

## 11.4 Attraction elasticity and facility weights

- [ ] Give `alpha` and all active facility-attractiveness coefficients calibrated defaults.
- [ ] **Expose active facility weights/coefficients in the advanced/expert front-end parameter panel.**
- [ ] Do not force ordinary users to edit raw regression coefficients in the standard workflow.
- [ ] Permit expert override and preserve every override in the model run.
- [ ] Flag out-of-calibration-range values without silently resetting them.

---

# 12. First-class model parameter system

This is a required production subsystem, not a developer-only configuration file.

## 12.1 Create a parameter catalog

- [ ] Create `model_parameter_definitions` or equivalent.
- [ ] Each definition must include:
  - [ ] stable parameter key;
  - [ ] category;
  - [ ] display name;
  - [ ] technical description;
  - [ ] plain-language interpretation;
  - [ ] units;
  - [ ] data type;
  - [ ] system fallback default;
  - [ ] hard min/max for computational safety if required;
  - [ ] validated/recommended low/high range where applicable;
  - [ ] UI step/precision;
  - [ ] UI exposure level: standard, advanced, expert, hidden/internal;
  - [ ] user-overridable flag;
  - [ ] model-version applicability;
  - [ ] provenance/calibration notes.

## 12.2 Create versioned parameter sets

- [ ] Add `model_parameter_sets` and `model_parameter_set_values` or equivalent.
- [ ] Support:
  - [ ] national/base calibrated sets;
  - [ ] jurisdiction/market-specific calibrated sets;
  - [ ] conservative/base/high scenario presets;
  - [ ] benchmark-study parameter sets for validation;
  - [ ] experimental sets.
- [ ] A parameter set must be immutable once referenced by a finalized model run.
- [ ] A changed calibration creates a new version.

## 12.3 Parameter precedence

- [ ] Resolve parameters in an explicit precedence order such as:

```text
System fallback
    < National calibrated set
    < Jurisdiction/market calibrated set
    < Scenario preset
    < User override
```

- [ ] Persist the final resolved value and the source layer for every parameter.
- [ ] Do not make reports reconstruct parameter precedence after the fact.

## 12.4 Required overrideable parameter families

- [ ] Gravity/travel:
  - [ ] beta;
  - [ ] alpha;
  - [ ] exponential lambda if that specification is active;
  - [ ] outside-option parameter(s);
  - [ ] regularization `t0` where exposed.
- [ ] Demand:
  - [ ] gaming-income share;
  - [ ] base per-eligible-adult gaming spend;
  - [ ] income elasticity;
  - [ ] regional/state intensity adjustments.
- [ ] Facility attraction:
  - [ ] structural feature coefficients/weights;
  - [ ] proposed property scale adjustment;
  - [ ] comparable-property scaling assumptions.
- [ ] Market expansion:
  - [ ] accessibility-induced-demand elasticity;
  - [ ] maximum induced-demand cap if used.
- [ ] Tourism/traffic:
  - [ ] tourism participation/capture;
  - [ ] traffic intercept rate;
  - [ ] eligible vehicle/passenger assumptions;
  - [ ] overlap/deduplication factors.
- [ ] Ramp/stabilization:
  - [ ] first-year percentage;
  - [ ] second-year percentage;
  - [ ] stabilized year.
- [ ] Displacement:
  - [ ] local share;
  - [ ] displacement coefficient;
  - [ ] sector priors;
  - [ ] taxability/margin assumptions where not jurisdiction-derived.
- [ ] Social/economic cost:
  - [ ] prevalence assumptions;
  - [ ] exposure/risk-response coefficients;
  - [ ] per-case cost assumptions;
  - [ ] crime/public-safety/productivity parameters when modeled.

## 12.5 Override audit trail

- [ ] Create `model_run_parameter_values` or equivalent snapshot table.
- [ ] Store for every parameter:
  - [ ] default/base value;
  - [ ] scenario preset value if any;
  - [ ] user override if any;
  - [ ] final value used;
  - [ ] source layer;
  - [ ] validated-range status;
  - [ ] warning text if outside the recommended range.
- [ ] The report must include an appendix table of overridden parameters.

---

# 13. Front-end parameter and scenario controls

## 13.1 Standard controls

- [ ] Provide understandable controls for common scenario decisions without exposing raw model internals by default.
- [ ] Examples:
  - [ ] proposed facility size/program;
  - [ ] travel sensitivity preset;
  - [ ] gaming-demand intensity;
  - [ ] tourism contribution;
  - [ ] traffic-intercept contribution;
  - [ ] local patron share;
  - [ ] displacement severity;
  - [ ] social-cost/prevalence scenario;
  - [ ] ramp/stabilization scenario.

## 13.2 Advanced model parameters

- [ ] Add expandable `Advanced Model Parameters` UI.
- [ ] Expose beta directly.
- [ ] Expose alpha directly.
- [ ] Expose active facility-attractiveness coefficients/weights.
- [ ] Expose demand elasticity and outside-option controls.
- [ ] Expose other calibrated technical parameters whose override materially changes results.
- [ ] For every parameter show:
  - [ ] model default/base value;
  - [ ] current scenario value;
  - [ ] recommended/validated range;
  - [ ] interpretation;
  - [ ] provenance/calibration reference.

## 13.3 Override behavior

- [ ] User changes must trigger model recalculation through the same backend engine used for default scenarios.
- [ ] Never implement a front-end-only multiplier that bypasses the model.
- [ ] Show changed/overridden state clearly.
- [ ] Provide `Reset to calibrated defaults`.
- [ ] Provide `Reset this section` where useful.
- [ ] Allow user values outside recommended ranges unless unsafe.
- [ ] Show clear warning for values outside validation range.
- [ ] Include the warning in generated reports.

## 13.4 Presets and scenario comparison

- [ ] Support at minimum:
  - [ ] calibrated/base;
  - [ ] conservative;
  - [ ] high/aggressive;
  - [ ] custom.
- [ ] Allow side-by-side comparison of multiple model runs.
- [ ] Preserve each comparison scenario as its own immutable run.
- [ ] Allow export/import of scenario definitions as versioned JSON where practical.

---

# 14. Outside option and incomplete market capture

- [ ] Do not omit an outside option simply because the prototype omitted it.
- [ ] Use it to represent unmodeled relevant supply/leakage only when needed.
- [ ] Do not use it as an unexplained balancing plug.
- [ ] Calibrate it against observed market totals and/or holdout properties.
- [ ] Allow the outside-option parameter to vary by market segment/region when validation supports it.
- [ ] Make expert override possible and auditable.

---

# 15. Accessibility-induced market expansion

A fixed share model redistributes a constant gaming pool. Improved access may also increase gaming frequency/participation.

- [ ] Compute baseline accessibility for each origin using incumbent facilities.
- [ ] Compute with-project accessibility after adding the proposed casino.
- [ ] Estimate induced demand as a separate, transparent layer.
- [ ] Do not bury induced demand inside beta or facility attraction.
- [ ] Calibrate accessibility-expansion elasticity where possible.
- [ ] Expose it as an advanced overrideable parameter.
- [ ] Report resident demand as:
  - [ ] baseline resident gaming pool;
  - [ ] redistributed/captured amount;
  - [ ] induced incremental resident gaming amount.

---

# 16. Tourism and destination demand

- [ ] Model tourism separately from resident demand to prevent double counting.
- [ ] Build pluggable tourism inputs because relevant tourism data differ by market.
- [ ] Candidate sources include state/local tourism agencies, lodging statistics, park/lake visitation, airport volumes, convention/event data, and other defensible sources.
- [ ] Define visitor-days/person-trips rather than applying one arbitrary annual tourist count.
- [ ] Estimate casino-eligible visitor share, participation, capture, and spend.
- [ ] Deduplicate visitors already represented as resident origins.
- [ ] Allow user overrides for tourism capture assumptions.
- [ ] Report tourism GGR separately.

---

# 17. Highway and through-traffic intercept

- [ ] Build a separate traffic-intercept module.
- [ ] Use relevant federal/state/local traffic datasets, not Indiana-specific code.
- [ ] For Indiana, INDOT is a provider implementation.
- [ ] For other states, use corresponding DOT/traffic providers.
- [ ] Model:
  - [ ] AADT or comparable flows;
  - [ ] vehicle occupancy where used;
  - [ ] eligible traveler share;
  - [ ] directional accessibility/interchange friction;
  - [ ] stop/intercept probability;
  - [ ] duplication with resident/tourism pools.
- [ ] Keep traffic GGR separate in output and report.

---

# 18. Baseline vs with-project market equilibrium

- [ ] Run the gravity model twice:
  - [ ] baseline competitive market without the proposed project;
  - [ ] with-project market including the proposed facility.
- [ ] For every incumbent calculate change in modeled GGR.
- [ ] Decompose proposed GGR into:
  - [ ] captured from host-state incumbents;
  - [ ] captured from out-of-state incumbents;
  - [ ] captured from tribal/other-jurisdiction incumbents where relevant;
  - [ ] captured from outside/unmodeled leakage;
  - [ ] newly induced resident demand;
  - [ ] tourism;
  - [ ] traffic intercept.
- [ ] This decomposition must drive fiscal and economic-impact accounting.

---

# 19. Repatriation, cannibalization, and geographic accounting

## 19.1 Do not treat all proposed casino revenue as new economic activity

- [ ] Classify every dollar by source to the extent model structure permits.
- [ ] Distinguish transfer effects from incremental activity.

## 19.2 Dynamic jurisdiction accounting

- [ ] Generalize concepts such as `Indiana repatriation` into host-jurisdiction accounting.
- [ ] For any scenario calculate:
  - [ ] revenue newly retained within host state/jurisdiction that previously flowed out;
  - [ ] revenue cannibalized from existing host-state facilities;
  - [ ] revenue captured from other states/jurisdictions;
  - [ ] newly induced gaming demand;
  - [ ] tourism/traffic import demand.
- [ ] Do not label cross-border capture as local household displacement unless the patron origin actually belongs to the local household market.

---

# 20. Capacity and feasibility checks

- [ ] Do not allow unconstrained GGR predictions that exceed plausible facility capacity.
- [ ] Develop capacity diagnostics using:
  - [ ] gaming positions;
  - [ ] win per unit/day benchmarks;
  - [ ] table productivity;
  - [ ] operating hours;
  - [ ] hotel/event capacity where relevant.
- [ ] Flag when demand forecast implies implausible per-position productivity.
- [ ] Do not automatically cap without showing the constraint and rationale.
- [ ] Allow development-program resizing sensitivity.

---

# 21. Ramp-up and stabilization

- [ ] Separate stabilized revenue from opening-year revenue.
- [ ] Use versioned ramp parameters.
- [ ] Allow market-specific calibration.
- [ ] Expose ramp assumptions on the front end.
- [ ] Report at minimum:
  - [ ] opening/partial year if applicable;
  - [ ] first full year;
  - [ ] second year;
  - [ ] stabilized year;
  - [ ] optional long-term growth case.

---

# 22. Independent validation and calibration framework

## 22.1 Incumbent back-testing

- [ ] Temporarily treat existing casinos as if they were proposed projects.
- [ ] Estimate their GGR using only data available before/without their observed target value where possible.
- [ ] Compare prediction to actual stabilized revenue.
- [ ] Measure MAE, MAPE/SMAPE, RMSE, bias, rank correlation, and geographic residual patterns.

## 22.2 Holdout validation

- [ ] Do not calibrate and evaluate on the same full property set only.
- [ ] Hold out casinos or markets.
- [ ] Prefer market-level holdout where data volume permits.
- [ ] Document training/calibration and validation periods.

## 22.3 Regression/comparable-market reasonableness model

- [ ] Build at least one independent non-gravity revenue model.
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
- [ ] Use it as a reasonableness check, not automatically as additive revenue.

## 22.4 Calibration governance

- [ ] Save every calibration as a versioned parameter set.
- [ ] Store objective function and validation metrics.
- [ ] Store sample inclusion/exclusion rules.
- [ ] Store chosen/default beta, alpha, facility coefficients, demand coefficients, and outside-option values.
- [ ] Do not overwrite old calibration versions.

---

# 23. Sector-weighted local spending displacement

## 23.1 Define the displacement base correctly

- [ ] Do not apply displacement to all proposed GGR.
- [ ] Determine the portion attributable to local resident spending that plausibly substitutes for other local discretionary expenditure.
- [ ] Exclude or separately handle:
  - [ ] imported out-of-area spending;
  - [ ] out-of-state repatriated casino spending where the relevant alternative would already have left the local economy;
  - [ ] pure cannibalization from another local casino when analyzing local household spending displacement;
  - [ ] tourism/traffic spending unless local substitution evidence exists.

## 23.2 Core definitions

- [ ] `LocalResidentGamingBase` = modeled gaming spend from defined local origins.
- [ ] `DisplacementEligibleBase` = portion of local resident gaming spend plausibly shifted from local discretionary sectors.
- [ ] `k` = displacement coefficient.
- [ ] `D_total = DisplacementEligibleBase × k`.
- [ ] `w_s` = sector allocation weight.
- [ ] `D_s = D_total × w_s`.

## 23.3 Dynamic local geography

- [ ] The meaning of `local` must be configurable by report/scenario.
- [ ] Support host municipality, host county, custom multi-county region, MSA/CSA, and host state analyses.
- [ ] Do not hard-code Northeast Indiana as the local economic area.

## 23.4 Sector inventory

- [ ] Use relevant local business/economic datasets where available.
- [ ] At minimum consider discretionary substitutes such as:
  - [ ] restaurants/hospitality;
  - [ ] retail;
  - [ ] arts/entertainment/recreation.
- [ ] Avoid implausible substitute sectors.
- [ ] Allow local inventory/employment/sales measures to modulate baseline sector priors.

## 23.5 Tax and income-loss waterfall

- [ ] Calculate displaced sales-tax base by sector.
- [ ] Calculate displaced business income/profit proxy by sector.
- [ ] Apply jurisdiction-specific taxability and rates.
- [ ] Avoid double counting retail sectors or pass-through/corporate effects.

---

# 24. Employment and labor-market effects

- [ ] Separate:
  - [ ] direct casino employment;
  - [ ] construction employment where modeled;
  - [ ] indirect/induced employment;
  - [ ] displaced employment in local sectors;
  - [ ] incumbent-casino employment cannibalization where material;
  - [ ] net employment.
- [ ] Do not report gross casino jobs as net jobs.
- [ ] Use wage/occupation assumptions tied to relevant geography where available.
- [ ] Allow user overrides with provenance.

---

# 25. Fiscal impact engine

- [ ] Use the jurisdiction profile to calculate gaming taxes and revenue sharing.
- [ ] Separately calculate non-gaming sales taxes, income/business taxes, property taxes where supported, and other applicable public revenue.
- [ ] Deduct or separately present lost fiscal revenue from displaced local business activity.
- [ ] Distinguish host-local, host-state, and other-jurisdiction fiscal impacts.
- [ ] Distinguish gross gaming tax receipts from net fiscal benefit.
- [ ] Version all tax rules by effective date.

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
- [ ] Avoid combining overlapping study estimates that represent the same underlying cost.
- [ ] Present gross social-cost estimate, uncertainty/sensitivity, and included/excluded domains.

---

# 27. Net economic-impact accounting

- [ ] Build explicit accounting bridges rather than a single `benefits minus costs` black box.
- [ ] At minimum distinguish:
  - [ ] gross casino/property revenue;
  - [ ] revenue transferred from incumbent casinos;
  - [ ] out-of-jurisdiction spending repatriated/imported;
  - [ ] newly induced gaming expenditure;
  - [ ] local discretionary displacement;
  - [ ] direct/indirect/induced economic activity;
  - [ ] fiscal gains;
  - [ ] displaced fiscal losses;
  - [ ] social/public costs;
  - [ ] net local impact;
  - [ ] net host-state impact;
  - [ ] broader regional impact where requested.
- [ ] Clearly identify transfer payments/effects so they are not mislabeled as net new production.

---

# 28. API and service-layer architecture

## 28.1 Suggested core services

- [ ] `OriginDemandService`.
- [ ] `CompetitiveUniverseService`.
- [ ] `TravelMatrixService`.
- [ ] `FacilityAttractivenessService`.
- [ ] `GravityModelService`.
- [ ] `MarketExpansionService`.
- [ ] `TourismDemandService`.
- [ ] `TrafficInterceptService`.
- [ ] `CannibalizationAccountingService`.
- [ ] `DisplacementModelService`.
- [ ] `EmploymentImpactService`.
- [ ] `FiscalImpactService`.
- [ ] `SocialCostService`.
- [ ] `NetImpactService`.
- [ ] `ModelParameterService`.
- [ ] `JurisdictionProfileService`.
- [ ] `ModelRunService`.
- [ ] `ReportCompilationService`.

## 28.2 Model execution pipeline

- [ ] Implement one authoritative backend pipeline:

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

- [ ] The web UI and report generator must never use separate formulas.

---

# 29. Full .NET 10 server-side report architecture

The long-term deliverable is not merely an interactive calculator. The .NET 10 backend must compile a complete professional analytical report from the same stored `ModelRun` used by the web application.

## 29.1 Report source of truth

- [ ] Create an immutable `ReportModel` derived entirely from a finalized `ModelRun` plus report-presentation options.
- [ ] Do not recalculate economics independently inside the report renderer.
- [ ] Report generation must be deterministic for a given model run and report template version.
- [ ] Store report template/version and generation timestamp.

## 29.2 Report output formats

- [ ] Generate server-side PDF as the primary publication format.
- [ ] Generate HTML using the same report data where practical for preview/accessibility.
- [ ] Preserve underlying machine-readable JSON/CSV exports for tables and audit.
- [ ] Evaluate the most appropriate .NET-compatible rendering stack based on repository constraints.
- [ ] Do not make PDF generation depend on screenshots of the live front-end.

## 29.3 Dynamic report structure

- [ ] The report must adapt to the selected geography and jurisdiction.
- [ ] Do not hard-code Indiana-specific section labels unless the active run is Indiana.
- [ ] Recommended major sections:
  - [ ] Executive Summary;
  - [ ] Proposed Development and Site;
  - [ ] Study Area and Market Definition;
  - [ ] Demographics, Eligible Population, and Income;
  - [ ] Competitive Gaming Supply;
  - [ ] Gravity Model Methodology;
  - [ ] Gaming Revenue Projection;
  - [ ] Patron Origin Analysis;
  - [ ] Tourism and Through-Traffic Demand;
  - [ ] Competitive Impact and Cannibalization;
  - [ ] Repatriation / Cross-Jurisdiction Capture;
  - [ ] Local Spending Displacement;
  - [ ] Employment and Labor-Market Effects;
  - [ ] Fiscal Impact;
  - [ ] Social and Downstream Economic Costs;
  - [ ] Net Economic Impact;
  - [ ] Sensitivity and Scenario Analysis;
  - [ ] Benchmark/Comparable Study Reconciliation where configured;
  - [ ] Methodology and Limitations;
  - [ ] Data Sources;
  - [ ] Model Parameters and Overrides;
  - [ ] Technical Appendices.

## 29.4 Dynamic patron-origin section

- [ ] Build origin tables/charts from actual contribution data.
- [ ] Report top contributing counties/parishes dynamically.
- [ ] Report state/territory composition dynamically.
- [ ] Show host jurisdiction vs external capture dynamically.
- [ ] Show tourism/traffic separately.
- [ ] Map origin intensity where data density permits.
- [ ] Never assume the categories are Allen, DeKalb, Steuben, Michigan, Ohio, etc.

## 29.5 Parameter disclosure in report

- [ ] Include a model-parameter summary for every report.
- [ ] Clearly distinguish:
  - [ ] calibrated/default parameters;
  - [ ] scenario preset changes;
  - [ ] user overrides.
- [ ] For every override show default, used value, units, recommended range, and out-of-range warning.
- [ ] Include beta, alpha, active facility weights, demand assumptions, tourism/traffic assumptions, displacement assumptions, and social-cost assumptions.

## 29.6 Report exhibits

- [ ] Generate publication-quality maps, tables, and charts from model data.
- [ ] Candidate exhibits:
  - [ ] proposed site and competitor map;
  - [ ] travel-time/isoline map;
  - [ ] patron-origin choropleth;
  - [ ] revenue composition waterfall;
  - [ ] baseline vs with-project competitor GGR;
  - [ ] county/state origin contribution chart;
  - [ ] ramp-up table;
  - [ ] displacement by sector;
  - [ ] fiscal bridge;
  - [ ] social-cost bridge;
  - [ ] net-impact waterfall;
  - [ ] sensitivity tornado/spider chart;
  - [ ] benchmark comparison table.
- [ ] Preserve exact numeric source values behind every exhibit.

## 29.7 Report reproducibility statement

- [ ] Include a technical appendix containing:
  - [ ] model version;
  - [ ] report-template version;
  - [ ] run UUID;
  - [ ] jurisdiction profile/version;
  - [ ] parameter-set version;
  - [ ] user overrides;
  - [ ] source data vintages;
  - [ ] route graph hash/version;
  - [ ] candidate coordinates;
  - [ ] development program;
  - [ ] generated timestamp.

---

# 30. UI result architecture

- [ ] Separate inputs/assumptions from model outputs visually.
- [ ] Show default-vs-custom state on model parameters.
- [ ] Show site/development configuration independently.
- [ ] Show headline stabilized GGR with resident/tourism/traffic decomposition.
- [ ] Show patron-origin map/table dynamically.
- [ ] Show incumbent impacts.
- [ ] Show local/state net economic-impact summaries.
- [ ] Show methodology and data provenance without burying key assumptions.
- [ ] Add explicit `Generate Full Report` workflow only after a model run is complete.
- [ ] The report action must reference the stored run ID, not submit a second independent set of calculations.

---

# 31. Scenario and sensitivity engine

- [ ] Permit one-click low/base/high scenarios using versioned parameter presets.
- [ ] Permit custom scenarios with arbitrary override combinations.
- [ ] Support one-at-a-time sensitivity for beta, alpha, gaming intensity, tourism, traffic intercept, local share, displacement coefficient, and major social-cost assumptions.
- [ ] Support multi-parameter scenario comparison.
- [ ] Store every scenario result separately.
- [ ] Build tornado/sensitivity tables from server-computed runs.
- [ ] Do not fake sensitivities by multiplying final GGR or net impact by a percentage after the model has run.

---

# 32. National data-provider architecture

- [ ] Build provider interfaces for data sources that vary by jurisdiction.
- [ ] Candidate provider categories:
  - [ ] gaming regulator performance;
  - [ ] gaming facility inventory;
  - [ ] state DOT traffic;
  - [ ] tourism/visitor statistics;
  - [ ] tax/fiscal rules;
  - [ ] local economic/business inventory.
- [ ] Common federal/national sources may include Census/ACS, IRS SOI, BEA, BLS, FHWA, and other authoritative datasets.
- [ ] State-specific adapters should supply additional detail without changing gravity-engine code.
- [ ] Persist provider/source provenance with each dataset snapshot.

---

# 33. Caching and interactive performance

- [ ] Precompute stable competitor and demographic data.
- [ ] Cache origin-to-incumbent travel matrices.
- [ ] Dynamically calculate candidate-site routes only for relevant origins.
- [ ] Cache candidate locations by reasonable coordinate grid/hash while preserving exact run coordinates.
- [ ] Separate fast interactive preview from full report-caliber run only if both use the same equations and clearly indicate preview status.
- [ ] Full report runs must resolve all required data and warnings before finalization.

---

# 34. Testing requirements

## 34.1 Unit tests

- [ ] Travel-friction calculations.
- [ ] Attraction normalization.
- [ ] Share-sum identity.
- [ ] Outside option.
- [ ] Parameter precedence.
- [ ] Override range warnings.
- [ ] Jurisdiction rule effective dates.
- [ ] Gaming-age population selection.
- [ ] Baseline vs with-project delta accounting.
- [ ] Displacement eligibility.
- [ ] Fiscal calculations.

## 34.2 Integration tests

- [ ] Full model run with stored data snapshots.
- [ ] Default vs overridden beta.
- [ ] Default vs overridden facility weights.
- [ ] Scenario reset to defaults.
- [ ] Indiana jurisdiction profile.
- [ ] At least one non-Indiana mock/test jurisdiction proving no Indiana hard-coding.
- [ ] Dynamic origin aggregation with different top counties/states.
- [ ] Full report generation from stored run.
- [ ] Regeneration of same run produces identical numeric tables.

## 34.3 Numerical robustness

- [ ] Extreme travel times.
- [ ] Very high/low beta.
- [ ] Very high/low attraction.
- [ ] Large competitive sets.
- [ ] Missing route.
- [ ] Missing facility attribute.
- [ ] Sparse rural origin data.
- [ ] Missing state-specific fiscal rules.
- [ ] Zero/near-zero demand.

---

# 35. Initial Indiana benchmark test suite

Indiana is the first real validation suite, not the core architecture.

- [ ] Recreate the Northeast Indiana market with current candidate scenarios.
- [ ] Test an Allen/Fort Wayne-area development program.
- [ ] Test an I-69/DeKalb/Spectrum-like proxy.
- [ ] Test a Steuben scenario.
- [ ] Compare resident GGR, total GGR, origin composition, and incumbent impacts with public study outputs.
- [ ] Explain model differences rather than forcing exact agreement.
- [ ] Verify that patron-origin categories are generated dynamically.
- [ ] Verify that the same code can run a non-Indiana synthetic or real validation market without changes to core model logic.

---

# 36. Implementation phases

## Phase A: architecture and parameterization

- [ ] Generalize jurisdiction concepts.
- [ ] Implement parameter catalog and versioned parameter sets.
- [ ] Implement user override persistence and precedence.
- [ ] Remove Fort Wayne hard-coding from reusable model services.

## Phase B: data foundation

- [ ] Origin geographies and eligible-age population.
- [ ] Income/AGI.
- [ ] National/jurisdiction competitor schema.
- [ ] Observed GGR history.
- [ ] Source catalog and dataset snapshots.

## Phase C: gravity engine

- [ ] Travel matrix.
- [ ] Facility attractiveness.
- [ ] Demand engines.
- [ ] Gravity allocation.
- [ ] Outside option.
- [ ] Baseline vs with-project equilibrium.

## Phase D: incremental demand

- [ ] Accessibility-induced demand.
- [ ] Tourism.
- [ ] Through traffic.
- [ ] Ramp/stabilization.
- [ ] Capacity checks.

## Phase E: comprehensive impact

- [ ] Cannibalization/repatriation accounting.
- [ ] Sector displacement.
- [ ] Employment.
- [ ] Fiscal impact.
- [ ] Social/downstream costs.
- [ ] Net economic impact.

## Phase F: front-end configurability

- [ ] Standard scenario controls.
- [ ] Advanced parameter panel.
- [ ] Beta override.
- [ ] Alpha override.
- [ ] Facility weight overrides.
- [ ] Range warnings.
- [ ] Default reset.
- [ ] Scenario comparison.

## Phase G: validation

- [ ] Incumbent back-testing.
- [ ] Holdouts.
- [ ] Independent regression/comparable model.
- [ ] Indiana public benchmark suite.
- [ ] At least one non-Indiana validation case.

## Phase H: report engine

- [ ] Immutable `ReportModel` from `ModelRun`.
- [ ] Server-side report rendering.
- [ ] Dynamic geography/origin sections.
- [ ] Dynamic jurisdiction fiscal sections.
- [ ] Parameter/override appendix.
- [ ] Data/methodology appendix.
- [ ] Publication-quality exhibits.

---

# 37. Production acceptance criteria

The gravity/revenue model is not production-complete until:

- [ ] Core model contains no Fort Wayne/Allen/DeKalb/Steuben/Indiana assumptions except through scenario, benchmark, or jurisdiction configuration.
- [ ] Competitive effects are computed origin-by-origin.
- [ ] Network travel time drives friction.
- [ ] Facility attraction is empirically/structurally calibrated rather than hand-scored.
- [ ] Beta has a calibrated default and is user-overridable on the front end.
- [ ] Alpha and active facility coefficients have calibrated defaults and are user-overridable in advanced/expert controls.
- [ ] User overrides are persisted and disclosed.
- [ ] Out-of-range overrides produce warnings but are not silently replaced.
- [ ] Patron-origin reporting is dynamic.
- [ ] Legal gaming age is jurisdiction-aware.
- [ ] Baseline vs with-project equilibrium exists.
- [ ] Cannibalization and cross-jurisdiction capture are decomposed.
- [ ] Tourism and through-traffic are separate from resident demand.
- [ ] Local spending displacement is applied only to an economically eligible base.
- [ ] Fiscal rules are jurisdiction-specific and effective-dated.
- [ ] Social/downstream costs remain location-sensitive and configurable.
- [ ] A complete immutable model run can be reproduced.
- [ ] A full .NET 10 server-generated report can be produced from that model run without recalculating through separate formulas.
- [ ] Indiana benchmark cases are validated.
- [ ] A non-Indiana case demonstrates national portability.

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

- [ ] Build a **nationally reusable casino gravity model**, not a Northeast-Indiana-only calculator.
- [ ] Use Indiana as the first calibrated jurisdiction and benchmark suite, not the hard-coded model definition.
- [ ] Give beta, alpha, facility weights, demand parameters, tourism/traffic assumptions, displacement assumptions, and social-cost parameters defensible defaults while making economically meaningful parameters configurable through the front end.
- [ ] Preserve every override and rerun the authoritative backend model rather than applying superficial front-end multipliers.
- [ ] Calculate competition from each origin to each relevant facility using network travel time and calibrated facility attraction.
- [ ] Dynamically determine patron-origin counties, states, regions, and external-market shares from the model run.
- [ ] Separate resident demand, induced demand, tourism, traffic, cannibalization, repatriation/imported demand, and displacement.
- [ ] Integrate revenue projections with employment, fiscal, sector-displacement, and social/downstream cost analysis.
- [ ] Make the immutable `ModelRun` the single source of truth for the web UI, API, scenario comparison, and full .NET 10 server-generated professional report.
- [ ] Favor transparent, reproducible, testable modeling over false precision.
