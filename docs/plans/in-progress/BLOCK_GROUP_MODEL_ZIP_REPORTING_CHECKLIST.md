# Block Group Model and ZIP Reporting Conversion Checklist

Status: In Progress

## Goal

- [ ] Convert the revenue switching model to operate at the block group level internally.
- [ ] Report results by ZIP by default in the webpage.
- [ ] Allow drill-down from ZIP to block group.
- [ ] Avoid using raw blocks as the primary implementation target unless a later requirement justifies the added complexity.

## Data Model

- [ ] Define the internal demand-origin unit as block group centroids with adult population and any needed revenue modifiers.
- [ ] Keep ZIP as an aggregation and reporting layer rather than the core switching unit.
- [ ] Confirm the canonical source table(s) for internal demand origins and adult population inputs.
- [ ] Define how projected adult population will be applied to block group demand origins.
- [ ] Document any assumptions used to map block groups into ZIP summaries.

## Revenue Model Conversion

- [ ] Replace ZIP-level switching inputs with block-group-level switching inputs in the server-side revenue model.
- [ ] Ensure each block group computes venue utility from the same core inputs:
- [ ] travel distance or travel time to the proposed site
- [ ] travel distance or travel time to competing venues
- [ ] venue quality weights
- [ ] adult population
- [ ] participation and spend assumptions
- [ ] Aggregate block-group switching outputs upward into ZIP totals after the switching calculation completes.
- [ ] Preserve county- and regional-level aggregation paths for downstream reporting.

## API Design

- [ ] Update or add API contracts so the backend can return:
- [ ] ZIP summaries for default reporting
- [ ] block group detail for drill-down
- [ ] totals for proposed-site share, demand, and contribution
- [ ] Keep the response structure transparent enough for methodology review and debugging.
- [ ] Decide whether block group detail should be returned inline, paged, or fetched on demand when a ZIP is expanded.

## Webpage Reporting

- [ ] Make ZIP the default reporting view in the webpage.
- [ ] Add expandable ZIP rows or cards that reveal contributing block groups.
- [ ] Show enough block group detail to audit the result without overwhelming the main UI.
- [ ] Avoid exposing raw census blocks in the first version of the drill-down UI.
- [ ] Update copy/tooltips so the page clearly states:
- [ ] switching is modeled at block group level
- [ ] ZIP is a presentation aggregate
- [ ] drill-down shows the underlying contributing block groups

## Isochrone and Spatial Logic

- [ ] Keep isochrone grid marks out of the switching unit definition.
- [ ] Use population-bearing geographies as the demand origins and use network/isochrone outputs only to inform impedance or accessibility.
- [ ] Confirm whether distance decay should remain straight-line for the first pass or move to drive-time-based impedance in a later pass.

## Performance and Validation

- [ ] Measure the runtime impact of switching from ZIP to block group origins.
- [ ] Add tests or validation checks to confirm that:
- [ ] block group totals aggregate correctly to ZIP
- [ ] ZIP totals aggregate correctly to county and regional totals
- [ ] projected adult population is applied consistently at the block group level
- [ ] UI drill-down totals reconcile with summary totals
- [ ] Compare old ZIP-native outputs against new block-group-aggregated ZIP outputs to quantify the change in results.

## Documentation

- [ ] Update methodology documentation to explain why block group is the internal model geography.
- [ ] Update public-facing notes to explain why ZIP remains the default reporting layer.
- [ ] Document any known limitations, especially around centroid-based demand assignment and ZIP crosswalk assumptions.
