# Casino Impact Web App — AI Agent Implementation Checklist

## 0. Document purpose and required implementation posture

- [ ] Read this entire document before writing or modifying code.
  - [ ] Treat this file as the governing implementation checklist for the next phase of the casino impact web application.
  - [ ] Use this document to guide database changes, service-layer design, revenue heuristics, competition logic, UI messaging, sensitivity controls, and documentation.
  - [ ] Do not skip ahead to UI polish before the underlying data model and calculation logic exist.

- [ ] Understand the overall goal.
  - [ ] Strengthen the app in a transparent and defensible manner.
  - [ ] Add a persistent dataset of existing casinos and casino-like gambling venues in Indiana and surrounding states.
  - [ ] Add competition-aware location scoring so the app no longer treats all proposed locations as having the same revenue potential.
  - [ ] Add public-data-informed revenue adjustment logic that is heuristic, scenario-based, and transparent.
  - [ ] Add user-facing warnings and scenario tools so users are not misled when they move the marker to a materially weaker market location.
  - [ ] Preserve user trust by clearly distinguishing between user-entered assumptions, app recommendations, heuristics, and outputs.

- [ ] Understand what this project is **not** trying to do.
  - [ ] Do **not** claim to replicate Spectrum’s full proprietary revenue model.
  - [ ] Do **not** imply that this project has access to Indiana Gaming Commission operator database data unless such data is actually present in the codebase and legally usable.
  - [ ] Do **not** build a fake “precision forecast” that presents unsupported dollar values as if they were statistically validated.
  - [ ] Do **not** silently modify AGR assumptions without surfacing that behavior clearly to the user.

- [ ] Understand what this project **can** reasonably do.
  - [ ] Use publicly described elements of Spectrum’s methodology as directional guidance.
  - [ ] Use publicly available data to build a defensible market-depth and competition heuristic.
  - [ ] Use public Spectrum scenarios as calibration anchors for relative site comparison.
  - [ ] Support transparent sensitivity testing rather than pretending to deliver exact forecast certainty.

---

## 1. Required design principles

- [ ] Preserve transparency in all implementation layers.
  - [ ] Clearly label any revenue-location logic as a heuristic, recommendation, scenario aid, or calibration layer.
  - [ ] Avoid labels such as “forecast engine,” “projection model,” or “predicted AGR” unless the math and data genuinely support those labels.
  - [ ] Whenever the app adjusts assumptions or suggests reductions, show that explicitly.

- [ ] Preserve user control.
  - [ ] Let the user keep manual control over AGR assumptions.
  - [ ] The app may recommend a reduced AGR scenario.
  - [ ] The app may provide quick sensitivity presets.
  - [ ] The app may optionally apply a user-clicked preset.
  - [ ] The app must not secretly alter revenue assumptions behind the scenes.

- [ ] Surface uncertainty rather than hide it.
  - [ ] Make it clear that revenue-side estimates are more uncertain than location-to-location relative ranking.
  - [ ] Explain that missing state-supplied ZIP-level visitation/theoretical win data increases model uncertainty.
  - [ ] Explain that the app is stronger at directional site comparison than at precise AGR point estimation.

- [ ] Keep the app scenario-based.
  - [ ] Treat outputs as scenario results under stated assumptions.
  - [ ] Treat baseline problem gambling prevalence and AGR as sensitivity drivers.
  - [ ] Encourage users to test multiple revenue scenarios for weaker-demand locations.
  - [ ] Encourage users to test multiple prevalence assumptions where warranted.

- [ ] Separate conceptually distinct things in both code and UI.
  - [ ] User input
  - [ ] App recommendation
  - [ ] Heuristic adjustment suggestion
  - [ ] Scenario output
  - [ ] Methodology note / disclosure

---

## 2. Required source-model understanding the agent must retain

- [ ] Understand the current app’s analytical gap.
  - [ ] Social-cost outputs change when location changes because the affected population in the three impact tiers changes.
  - [ ] Revenue assumptions, however, can currently be overstated if the proposed marker is moved to a materially weaker market location.
  - [ ] Existing casinos and casino-like venues are not yet being modeled as competition in a structured way.
  - [ ] The app therefore needs a competition-aware, public-data-informed revenue-potential layer.

- [ ] Understand the public Spectrum distinction.
  - [ ] Spectrum’s public report describes a methodology that used:
    - [ ] drive-time segmentation
    - [ ] AGI/income-weighted market potential
    - [ ] competition/cannibalization logic
    - [ ] location/access quality
    - [ ] current market capture using state-supplied data
  - [ ] The project can mirror the **publicly described structure** of that methodology.
  - [ ] The project cannot credibly claim to recreate Spectrum’s **actual hidden inputs**, especially state-supplied ZIP-level visitation and theoretical revenue capture data.

- [ ] Understand the correct uncertainty framing.
  - [ ] Missing private/state data does not merely mean “the confidence interval is wider.”
  - [ ] It also means the revenue side is less structurally identified.
  - [ ] The app may still support reasonable relative ranking between candidate sites.
  - [ ] The app has lower confidence in exact AGR dollar point estimates.
  - [ ] Documentation should explicitly say this.

---

## 3. Database work — create a persistent casino competitor reference dataset

### 3.1 Create the table

- [ ] Create a new persistent table for casino competitors.
  - [ ] Preferred conceptual names:
    - [ ] `casino_reference_sites`
    - [ ] `casino_competitors`
    - [ ] `gaming_venue_reference_sites`
  - [ ] Use the naming convention that best fits the existing project structure.

- [ ] Ensure the table supports geospatial querying and weighted calculations.
  - [ ] Store raw numeric latitude and longitude.
  - [ ] If the project already uses PostGIS or another geospatial extension, also store a geometry/geography field where appropriate.
  - [ ] Make the table easy to query by state, county, feature set, and active status.

### 3.2 Required columns

- [ ] Add the minimum required identity/location fields.
  - [ ] `id`
  - [ ] `name`
  - [ ] `state`
  - [ ] `county`
  - [ ] `city`
  - [ ] `latitude`
  - [ ] `longitude`
  - [ ] `is_active`
  - [ ] `notes`

- [ ] Add venue classification fields.
  - [ ] `venue_type`
  - [ ] `operator_name` if readily available
  - [ ] `market_notes` optional
  - [ ] `source_url` optional
  - [ ] `last_verified_at` optional

- [ ] Add competition/feature fields.
  - [ ] `has_slots`
  - [ ] `has_table_games`
  - [ ] `has_poker`
  - [ ] `has_sportsbook`
  - [ ] `has_racetrack`
  - [ ] `has_hotel`
  - [ ] `has_restaurants`
  - [ ] `has_entertainment`
  - [ ] `has_loyalty_program` optional
  - [ ] `has_resort_amenities` optional
  - [ ] `estimated_competition_weight` optional cached field

### 3.3 Venue type taxonomy

- [ ] Implement a venue-type taxonomy that distinguishes materially different kinds of gambling venues.
  - [ ] Support at minimum:
    - [ ] `full_service_casino`
    - [ ] `racino`
    - [ ] `off_track_betting`
    - [ ] `sportsbook_only`
    - [ ] `slots_only`
    - [ ] `charity_gaming`
    - [ ] `other`

- [ ] Ensure the type system is designed for competition weighting.
  - [ ] A full-service casino should generally carry the highest base competitive relevance.
  - [ ] A limited-feature off-track betting venue should carry minimal or no meaningful competition weight.
  - [ ] The schema must support adding additional venue types later without breaking calculations.

### 3.4 Visual representations (Map Markers)

- [ ] Utilize the existing custom SVG map markers for rendering competitor locations on the interactive map.
  - [ ] Source directory: `SaveFW.Client/wwwroot/assets/existing-locations-map-markers`
  - [ ] Map `full_service_casino` to `EXISTING_CASINO_MARKER.svg`.
  - [ ] Map `racino` to `EXISTING_CASINO_RACETRACK_MARKER.svg`.
  - [ ] Map standalone racetracks to `EXISTING_RACETRACK_MARKER.svg`.
  - [ ] Map tribal operations to `EXISTING_TRIBAL_CASINO_MARKER.svg`.
- [ ] Ensure competitor markers are static (not draggable) on the map interface, unlike the primary proposed casino marker.

### 3.5 Data scope and geography

- [ ] Populate the dataset with:
  - [ ] Indiana casinos
  - [ ] nearby Michigan casinos relevant to Northeast Indiana demand
  - [ ] nearby Ohio casinos relevant to Northeast Indiana demand
  - [ ] any nearby Illinois venues only if they are plausibly relevant to the proposed market
  - [ ] casino-like gambling venues in the surrounding states if they may divert some share of demand

- [ ] Be selective about “casino-like” venues.
  - [ ] Do not treat every low-feature betting location as a major competitor.
  - [ ] Preserve the distinction between destination-style casinos and weak substitutes.

---

## 4. Competition model — score how much a venue should matter

### 4.1 Build a competition score for each existing venue

- [ ] Create a venue-level competition-weight function.
  - [ ] The score must be heuristic and explainable.
  - [ ] The score must not be a black box.
  - [ ] The score must support inspection and future tuning.

- [ ] The competition score must consider at minimum:
  - [ ] venue type
  - [ ] feature richness
  - [ ] substitutability for a destination casino experience
  - [ ] distance from proposed site
  - [ ] distance from primary patron base / Fort Wayne market where relevant
  - [ ] likely catchment overlap

### 4.2 Use weighted logic, not binary logic

- [ ] Do not use simplistic binary rules such as:
  - [ ] “inside X miles = competitor, outside X miles = not competitor”
  - [ ] “all venues count the same”
  - [ ] “all venues with the word casino count equally”

- [ ] Use a weighted scoring structure instead.
  - [ ] Example base type values:
    - [ ] full-service casino = 1.00
    - [ ] racino = 0.70
    - [ ] sportsbook-only = 0.35
    - [ ] off-track betting bar = 0.10
    - [ ] charity/minor gaming = 0.05
  - [ ] Example feature adders:
    - [ ] slots = +0.15
    - [ ] table games = +0.20
    - [ ] poker = +0.10
    - [ ] sportsbook = +0.05
    - [ ] hotel = +0.15
    - [ ] entertainment = +0.05
    - [ ] destination dining = +0.05

- [ ] Preserve transparency.
  - [ ] Store the score components or make them inspectable.
  - [ ] Do not leave future maintainers unable to understand why a venue received its score.

### 4.3 Catchment-overlap logic

- [ ] Add logic that reflects market overlap, not just raw physical distance.
  - [ ] A venue matters more if it competes for the same Fort Wayne demand pool.
  - [ ] A venue matters more if it sits in the same repeat-visit corridor.
  - [ ] A venue matters less if it is geographically close but serves a different travel pattern or experience type.
  - [ ] A venue matters less if it is weakly substitutable for a full-service casino.

- [ ] Implement at minimum:
  - [ ] distance from proposed site to competitor
  - [ ] distance from Fort Wayne or other designated primary market center to competitor
  - [ ] optional corridor membership test if the project has corridor logic

---

## 5. Public-data market-depth layer — build a weighted market depth proxy

### 5.1 Do this because flat population alone is too weak

- [ ] Add a weighted market-depth layer rather than relying only on raw population counts.
  - [ ] Population matters.
  - [ ] Income/AGI depth also matters.
  - [ ] Market depth should be stronger where reachable adults also have greater aggregate income/resources.
  - [ ] This aligns directionally with the public description of Spectrum’s market-potential logic.

### 5.2 Required public data stack

- [ ] Use public data sources as the market-depth foundation.
  - [ ] IRS SOI ZIP-level AGI data where feasible
  - [ ] ACS 5-year data for adult population and supporting demographic fields
  - [ ] HUD USPS ZIP crosswalk if needed to align ZIP and Census geographies
  - [ ] BEA county income data only as a fallback/coarser layer
  - [ ] BLS Consumer Expenditure Survey only as a secondary support layer, not a ZIP-level local capture substitute

- [ ] Structure the code so these sources can be swapped or refreshed later.
  - [ ] Do not hard-code one-off CSV assumptions throughout the codebase.
  - [ ] Centralize data ingestion and transformation.

### 5.3 Compute market depth

- [ ] Build a market-depth computation module that can produce at minimum:
  - [ ] raw adult population by zone
  - [ ] AGI-weighted adult exposure by zone
  - [ ] optional normalized demand score by zone
  - [ ] optional per-ZIP or per-tract contribution records for auditing/debugging

- [ ] Intersect public geographic data with:
  - [ ] Valhalla isochrones
  - [ ] drive-time rings
  - [ ] or the project’s existing three-zone impact structure

- [ ] Support an internal formula such as:
  - [ ] `weighted_market_depth = Σ(zone geography AGI contribution)`
  - [ ] or `adult population × income weight × proximity weight`
  - [ ] or another transparent formulation that can be explained in documentation

- [ ] Keep the formulation inspectable.
  - [ ] The user or developer should be able to understand how the weighted market depth was derived.

---

## 6. Revenue-potential heuristic — build a transparent location scoring framework

### 6.1 Create a revenue-potential heuristic, not a pseudo-statistical forecast

- [ ] Create a dedicated revenue-potential heuristic module.
  - [ ] It must be clearly labeled as heuristic.
  - [ ] It must not be presented as a precise forecast.
  - [ ] It must support relative comparison between candidate locations.

### 6.2 Required conceptual purpose

- [ ] The heuristic must help answer:
  - [ ] Is the proposed site inside the strongest Northeast Indiana demand corridor?
  - [ ] Is it still viable but weaker?
  - [ ] Is it materially weaker and therefore likely to require a downward AGR adjustment?

### 6.3 Required scoring factors

- [ ] Include at minimum:
  - [ ] access to Fort Wayne urban core / primary market
  - [ ] access to I-69 / major highways / corridor quality
  - [ ] adult population within relevant drive-time bands
  - [ ] AGI/income-weighted market depth
  - [ ] competition penalty from overlapping casinos
  - [ ] destination/access quality
  - [ ] optional tourism support if applicable

- [ ] Use a weighted index or similar transparent structure.
  - [ ] Example categories:
    - [ ] Fort Wayne market access = 0–40 points
    - [ ] highway accessibility = 0–25 points
    - [ ] weighted nearby adult market depth = 0–20 points
    - [ ] competition penalty = subtract 0–20 points
    - [ ] tourism/destination support = 0–10 points
  - [ ] These values are placeholders, not hard mandates.

### 6.4 Normalize relative to a benchmark site

- [ ] Normalize the revenue heuristic against a reference Northeast corridor location.
  - [ ] Use the I-69 / SR-8 area or whichever benchmark the project designates.
  - [ ] Treat that benchmark as a calibration anchor rather than as proof of exact state-model parity.
  - [ ] Allow the heuristic to generate a relative multiplier compared to the benchmark site.

- [ ] Example conceptual output:
  - [ ] benchmark corridor site = 1.00
  - [ ] Allen-adjacent comparable site = near 1.00
  - [ ] materially weaker site = below 1.00
  - [ ] competition-heavy but accessible site = below 1.00 for a different reason

---

## 7. Spectrum-based calibration rules

### 7.1 Use Spectrum publicly, but carefully

- [ ] Use publicly available Spectrum outputs as calibration anchors, not as a false claim of full replication.
  - [ ] Public Spectrum scenarios can help anchor the plausibility of a benchmark site.
  - [ ] Public Spectrum methodology can help justify the inclusion of:
    - [ ] drive-time logic
    - [ ] AGI/income weighting
    - [ ] competition/cannibalization logic
    - [ ] access/location quality
  - [ ] Do not state or imply that the app recreated the hidden operator/IGC ZIP-capture database.

### 7.2 Required documentation language

- [ ] Add methodology language equivalent to the following idea:
  - [ ] “This revenue-potential layer is public-data-informed and directionally consistent with publicly described elements of the Spectrum study, but it does not replicate Spectrum’s proprietary or state-assisted model because this project does not possess the same underlying operator capture data.”

### 7.3 Trial-and-error calibration rules

- [ ] Trial-and-error is allowed only as transparent heuristic tuning.
  - [ ] It is acceptable to tune weights so the benchmark site lands in a plausible neighborhood relative to public Spectrum scenarios.
  - [ ] It is acceptable to use Spectrum scenarios as sanity-check anchors.
  - [ ] It is **not** acceptable to claim that trial-and-error recovered the proprietary hidden visitation data.
  - [ ] It is **not** acceptable to label the result a replication of Spectrum’s model.

---

## 8. Revenue uncertainty and confidence disclosures

### 8.1 Required uncertainty concept

- [ ] Build disclosures that explain that the app has:
  - [ ] stronger directional confidence in relative site ranking
  - [ ] weaker confidence in exact AGR point estimates

- [ ] Explain why.
  - [ ] The project lacks state-supplied ZIP-level visitation/theoretical win data.
  - [ ] Public data can estimate potential and relative attractiveness.
  - [ ] Public data cannot reproduce current market capture with the same confidence.

### 8.2 Required wording concept

- [ ] Add documentation or UI notes equivalent to:
  - [ ] “Revenue estimates should be interpreted as heuristic and scenario-based rather than precise forecasts.”
  - [ ] “The model is more reliable for directional comparison between sites than for exact AGR point estimates.”
  - [ ] “Because proprietary/state-supplied capture data is unavailable, uncertainty around exact AGR values is materially higher.”

---

## 9. User-facing warning system — add location-sensitive AGR warnings

### 9.1 Why this is required

- [ ] Prevent users from being misled when they move the marker to a weak-demand county but keep an unrealistically high AGR assumption.
  - [ ] Social costs already fall when the marker moves because the impacted population changes.
  - [ ] Revenue may also need to fall, but only the user or an explicit scenario tool should decide that.
  - [ ] The app therefore needs a visible warning/recommendation layer.

### 9.2 Required behavior

- [ ] When a site is outside the strongest demand corridor, show a visible revenue assumption notice.
  - [ ] Do not hide the message behind a tooltip only.
  - [ ] Place it near the revenue input or scenario controls.
  - [ ] Make it clear that revenue assumptions for corridor sites may overstate AGR for the currently selected location.

### 9.3 Example warning concepts

- [ ] Implement wording close to:
  - [ ] “This location appears to be outside the strongest Northeast Indiana casino demand corridor.”
  - [ ] “Revenue assumptions used for Allen-adjacent or southern DeKalb corridor sites may overstate expected AGR here.”
  - [ ] “Test lower AGR scenarios before interpreting net impact results.”

### 9.4 Classification bands

- [ ] If feasible, classify the proposed site into one of several revenue-potential bands.
  - [ ] High revenue potential
  - [ ] Moderate revenue potential
  - [ ] Lower revenue potential

- [ ] Tie UI behavior to the classification.
  - [ ] High = no warning or minimal note
  - [ ] Moderate = suggest mild sensitivity testing
  - [ ] Lower = suggest substantial sensitivity testing

---

## 10. AGR sensitivity tools — add user-clicked scenario presets

### 10.1 Required feature

- [ ] Add quick revenue sensitivity presets near the AGR input.
  - [ ] No adjustment
  - [ ] Mild reduction
  - [ ] Moderate reduction
  - [ ] Severe reduction

### 10.2 Example preset structure

- [ ] Example percentages:
  - [ ] Mild = -15%
  - [ ] Moderate = -35%
  - [ ] Severe = -50%
  - [ ] These may be tuned later.

### 10.3 Required behavior rules

- [ ] Presets must be explicitly user-applied.
  - [ ] Do not auto-apply them without user action.
  - [ ] Make it obvious what the preset changed.
  - [ ] Show the before/after AGR value if possible.

### 10.4 Strongly recommended enhancement

- [ ] If the location is materially weak, offer a one-click prompt such as:
  - [ ] “Run lower AGR sensitivity scenarios”
  - [ ] “Apply conservative revenue scenario”
  - [ ] “Compare base AGR vs reduced AGR cases”

---

## 11. Baseline problem gambling sensitivity disclosures

### 11.1 Required acknowledgment

- [ ] Explicitly disclose that results are highly sensitive to baseline problem gambling rate assumptions.
  - [ ] The current default 2.3% baseline should be described as conservative if that remains the project assumption.
  - [ ] Make it clear that the baseline is not a fixed truth.
  - [ ] Make it clear that changing the baseline can materially change results.

### 11.2 Required note concept

- [ ] Add UI/disclosure text conceptually equivalent to:
  - [ ] “Results are highly sensitive to both the baseline problem gambling rate and assumed annual gaming revenue.”
  - [ ] “The default 2.3% baseline is intentionally conservative and does not assume an increase in the background prevalence rate.”

### 11.3 Keep the logic aligned with the current model

- [ ] Do not describe social costs as fixed.
  - [ ] They are variable and depend on the affected population across the three tiered zones.
  - [ ] The UI and documentation must reflect that the social-cost side already changes when the marker changes.

---

## 12. Displacement model integration — preserve and integrate the sector-weighted approach

### 12.1 Keep the existing displacement enhancement plan

- [ ] Preserve the sector-weighted displacement model work.
  - [ ] Do not replace it with a flat global deduction once this checklist is implemented.
  - [ ] Integrate the displacement model with the broader net-impact framework.

### 12.2 Core displacement definitions

- [ ] Ensure the model retains or implements the following definitions:
  - [ ] AGR = casino adjusted gross revenue
  - [ ] Local Share % (`LS`) = share of AGR attributable to local residents whose spending would otherwise circulate locally
  - [ ] Local Displacement Base (`Base_local`) = `AGR × LS`
  - [ ] Displacement coefficient (`k`) = default 0.243
  - [ ] Total displaced revenue (`D_total`) = `Base_local × k`
  - [ ] Sector allocation weight (`w_s`) = proportion of displaced spending assigned to each sector
  - [ ] Taxability factor (`t_s`) = share of sector sales subject to sales tax
  - [ ] Net income margin (`m_s`) = sector net income margin proxy
  - [ ] Effective income tax rate (`r_inc`) = applicable personal/corporate blended or pass-through rate

### 12.3 Local-share control

- [ ] Expose Local Share % as a user-controlled input or scenario preset.
  - [ ] Regional convenience casinos: suggested higher local share scenarios
  - [ ] destination/tourism-heavy casinos: suggested lower local share scenarios
  - [ ] Present these as scenarios, not universal truths

### 12.4 At-risk sector inventory

- [ ] Preserve the plan to identify at-risk discretionary businesses using the map/business layer.
  - [ ] NAICS 72
  - [ ] NAICS 44–45 combined
  - [ ] NAICS 71
  - [ ] Exclude sectors that are implausible substitutes for casino spending

- [ ] Preserve the count/proxy approach.
  - [ ] counts by sector
  - [ ] optional employment proxies
  - [ ] optional square-footage proxies
  - [ ] optional sales proxies where available

### 12.5 Sector allocation weighting

- [ ] Preserve the baseline weighting idea while allowing local inventory to modulate weights.
  - [ ] Dining/Hospitality prior = 0.60
  - [ ] Retail prior = 0.30
  - [ ] Entertainment prior = 0.10
  - [ ] Do not accidentally double-count retail by assigning separate full weights to both 44 and 45

- [ ] Use normalized data-driven weighting where feasible.
  - [ ] `rawWeight_s = baselineWeight_s × presence_s`
  - [ ] `w_s = rawWeight_s / Σ(rawWeight)`

### 12.6 Tax waterfall and schema

- [ ] Preserve the tax waterfall structure.
  - [ ] sector sales-tax loss
  - [ ] sector income-tax loss
  - [ ] state-specific taxability overrides
  - [ ] pass-through / corporate tax blend if implemented

- [ ] Preserve the recommended configuration structure.
  - [ ] single sector list
  - [ ] no accidental retail double counting
  - [ ] state-specific tax rules
  - [ ] optional local add-on tax rates
  - [ ] optional sensitivity multipliers

### 12.7 Required integration point

- [ ] Ensure the new revenue-potential layer and the sector-weighted displacement layer can coexist.
  - [ ] Revenue potential affects the assumed AGR scenario.
  - [ ] Local share and displacement affect the downstream local tax-loss calculations.
  - [ ] The code should not entangle these layers so badly that later tuning becomes impossible.

---

## 13. Service-layer architecture requirements

### 13.1 Create dedicated modules/services

- [ ] Do not scatter the new logic across UI components.
  - [ ] Create a casino competitor data service/repository.
  - [ ] Create a competition scoring service.
  - [ ] Create a market-depth computation service.
  - [ ] Create a revenue-potential heuristic service.
  - [ ] Create a warning/recommendation service.
  - [ ] Preserve or extend the displacement model service.

### 13.2 Service responsibilities

- [ ] Casino competitor data service must:
  - [ ] load and query venue records
  - [ ] filter by geography and active status
  - [ ] expose feature sets and classifications

- [ ] Competition scoring service must:
  - [ ] compute venue competition weights
  - [ ] compute site-level competition pressure
  - [ ] expose score components for inspection/debugging

- [ ] Market-depth service must:
  - [ ] ingest public data layers
  - [ ] intersect them with isochrones or zones
  - [ ] compute weighted market depth metrics

- [ ] Revenue heuristic service must:
  - [ ] compute site access/location scores
  - [ ] apply competition penalties
  - [ ] normalize relative to a benchmark site
  - [ ] expose the multiplier/classification/reasons

- [ ] Warning/recommendation service must:
  - [ ] translate heuristic results into user-facing messages
  - [ ] determine whether to suggest sensitivity presets
  - [ ] avoid changing assumptions unless user-invoked

---

## 14. UI requirements

### 14.1 Add a distinct revenue assumptions / site quality panel

- [ ] Create a dedicated UI area for:
  - [ ] revenue potential notice
  - [ ] AGR input
  - [ ] AGR sensitivity presets
  - [ ] methodology note
  - [ ] confidence/disclosure note

- [ ] Keep this visually distinct from the social-cost cards.
  - [ ] The user should understand that assumptions and scenario aids are not the same thing as computed outputs.

### 14.2 Required UI concepts

- [ ] Show a location-based message when appropriate.
- [ ] Show quick AGR sensitivity controls.
- [ ] Show methodology/disclosure language.
- [ ] Show if the site is benchmark-like, moderate, or weak.
- [ ] Show why the classification happened if feasible:
  - [ ] weaker access to Fort Wayne
  - [ ] weaker corridor access
  - [ ] strong competition overlap
  - [ ] weaker weighted market depth

### 14.3 Avoid misleading UI behavior

- [ ] Do not display a confidence level that implies formal statistical calibration if the model does not support it.
- [ ] Do not present a single-point AGR output without also indicating it is assumption-driven.
- [ ] Do not use hidden auto-adjustments.

---

## 15. Methodology and disclaimer text requirements

### 15.1 Required methodology posture

- [ ] Add a methodology note explaining:
  - [ ] the revenue-potential layer is a public-data-informed heuristic
  - [ ] it is directionally informed by public descriptions of Spectrum’s approach
  - [ ] it does not replicate Spectrum’s hidden state/operator data
  - [ ] it is more suitable for relative comparisons than exact point forecasts

### 15.2 Required displacement note

- [ ] Preserve the existing displacement note conceptually equivalent to:
  - [ ] local business income-tax loss is estimated using IRS SOI nonfarm sole proprietorship data as a proxy
  - [ ] margins are scenario-analysis inputs, not precise business-level measurements
  - [ ] results are intended for scenario analysis, not exact forecasting

### 15.3 Required uncertainty note

- [ ] Add wording conceptually equivalent to:
  - [ ] “Without state-supplied ZIP-level visitation/theoretical capture data, revenue estimates are subject to materially higher uncertainty.”
  - [ ] “The model can still support useful relative site comparisons and sensitivity testing.”

---

## 16. Calibration, testing, and validation checklist

### 16.1 Benchmark testing

- [ ] Select a benchmark Northeast site and verify that:
  - [ ] the heuristic score is sensible
  - [ ] nearby corridor sites score similarly
  - [ ] materially weaker sites score lower
  - [ ] competition-heavy locations receive a penalty where appropriate

### 16.2 Sanity-check testing

- [ ] Test a location farther north, such as a Steuben-like scenario.
  - [ ] Verify that social costs fall because affected population exposure falls.
  - [ ] Verify that the app now warns that AGR assumptions may be too high for that location.
  - [ ] Verify that the user can run lower AGR scenarios easily.

- [ ] Test an Allen-adjacent or DeKalb-corridor-like scenario.
  - [ ] Verify that the revenue-potential classification is materially stronger than the Steuben-like case.
  - [ ] Verify that competition overlap is still being accounted for.
  - [ ] Verify that no hidden AGR adjustment occurs.

### 16.3 Competition tests

- [ ] Test a full-service competitor overlapping the same catchment.
  - [ ] Confirm that competition pressure meaningfully increases.

- [ ] Test a weak substitute venue such as a limited-feature betting venue.
  - [ ] Confirm that it receives a low competition weight.

### 16.4 Disclosure tests

- [ ] Verify that:
  - [ ] methodology notes render
  - [ ] warning messages render
  - [ ] sensitivity presets are explicit
  - [ ] the baseline prevalence disclosure is present
  - [ ] revenue uncertainty wording is present

---

## 17. Implementation order — do this in sequence

- [ ] Step 1: create the casino competitor database table and seed structure.
- [ ] Step 2: ingest and normalize venue records for Indiana and surrounding relevant states.
- [ ] Step 3: add venue-type and feature-aware competition scoring.
- [ ] Step 4: add public-data market-depth ingestion and computations.
- [ ] Step 5: add the location-based revenue-potential heuristic.
- [ ] Step 6: add Spectrum-informed calibration anchors and documentation language.
- [ ] Step 7: add user-facing revenue warning logic.
- [ ] Step 8: add AGR sensitivity presets.
- [ ] Step 9: add uncertainty and methodology disclosures.
- [ ] Step 10: integrate with the sector-weighted displacement model.
- [ ] Step 11: run benchmark and edge-case tests.
- [ ] Step 12: document all assumptions in code comments and user-facing methodology notes.

---

## 18. Final acceptance criteria

- [ ] The project contains a persistent casino competitor dataset.
- [ ] Competing venues are not all treated equally.
- [ ] Venue type and feature richness affect competition weight.
- [ ] Catchment overlap affects competition weight.
- [ ] Public-data weighted market depth is incorporated into the revenue-potential logic.
- [ ] A benchmark-relative revenue heuristic exists.
- [ ] Spectrum is used only as public calibration guidance, not as a false replication claim.
- [ ] The app warns users when a proposed site is in a weaker-demand location.
- [ ] The app provides explicit AGR sensitivity presets.
- [ ] The app clearly discloses sensitivity to baseline prevalence and AGR.
- [ ] The app preserves the sector-weighted displacement methodology.
- [ ] The app’s code separates data, calculation, recommendation, and UI concerns cleanly.
- [ ] The final user experience is more transparent, more defensible, and harder to misread than the prior version.

---

## 19. Non-negotiable “do not do this” list

- [ ] Do **not** claim the model replicates Spectrum’s full proprietary/state-assisted model.
- [ ] Do **not** imply access to hidden Indiana operator capture data unless it truly exists in the project.
- [ ] Do **not** silently lower or raise AGR based on location.
- [ ] Do **not** treat every gambling venue as equally competitive.
- [ ] Do **not** describe social costs as fixed.
- [ ] Do **not** use flat population alone if weighted market depth is available.
- [ ] Do **not** present exact AGR figures with faux certainty.
- [ ] Do **not** let the new revenue heuristic overwrite the existing displacement-model logic.
- [ ] Do **not** bury methodology or uncertainty notes where users will never see them.

---

## 20. Summary directive to the AI agent

- [ ] Build a transparent, public-data-informed, competition-aware, benchmark-calibrated revenue-potential layer.
- [ ] Preserve manual user control while adding explicit recommendations and sensitivity tools.
- [ ] Keep social-cost logic variable and zone-driven.
- [ ] Preserve and integrate the sector-weighted displacement model.
- [ ] Use Spectrum’s public logic as directional guidance, not as a false claim of exact replication.
- [ ] Favor clarity, explainability, and defensibility over fake precision.
"""