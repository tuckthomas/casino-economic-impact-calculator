# Tax Allocation Scenario Selector Checklist

Created: 2026-04-02  
Completed: 2026-04-02  
Last Modified: 2026-04-02

Status: Completed

## Goal

- [x] Replace the current hard-coded tax allocation logic with a scenario-driven allocation model in the frontend.
- [x] Keep `NE Indiana Casino` as the default preset for current visitors.
- [x] Add a scenario selector dropdown so future county/state casino projects can use different allocation presets.
- [x] Add a `Custom` scenario that unlocks editable allocation inputs instead of forcing fixed preset values.
- [x] Preserve the municipal-boundary trigger so city allocations only apply when the site is actually inside an incorporated `PLACE`.

## Scenario Model

- [x] Define a frontend tax-allocation scenario object instead of embedding Indiana-specific assumptions directly in calculation branches.
- [x] Include scenario metadata:
- [x] scenario id
- [x] display label
- [x] default / preset ordering
- [x] optional bill name or public-facing scenario name
- [x] Include recipient labels in the scenario model:
- [x] state recipient
- [x] county recipient
- [x] municipality recipient
- [x] regional recipient
- [x] Split allocation rules into separate components:
- [x] regular wagering-tax allocation
- [x] supplemental wagering-tax allocation
- [x] Include conditional logic flags in the scenario model:
- [x] municipal carve-out requires municipal containment
- [x] county fallback when no municipal containment exists
- [x] whether the scenario even has a municipality carve-out concept

## Default Preset

- [x] Create a default preset named `NE Indiana Casino`.
- [x] Make the webpage load that preset automatically on first render.
- [x] Encode the current HB 1038-style assumptions inside that preset rather than in free-floating calculator branches.
- [x] Ensure the preset covers the current Allen / DeKalb / Steuben behavior without requiring manual edits from the user.

## Scenario Selector UI

- [x] Add a dropdown selector for tax-allocation scenario choice.
- [x] Set the default selected option to `NE Indiana Casino`.
- [x] Add a `Custom` option at the end of the selector.
- [x] Ensure changing the scenario re-runs the calculator immediately.
- [x] Keep the selector wording clear enough that users understand they are changing the tax-allocation regime, not the social-cost model.

## Custom Mode

- [x] Make preset scenarios read-only.
- [x] Make allocation fields editable only when `Custom` is selected.
- [x] Pre-populate `Custom` from the currently selected preset so users can tweak an existing scenario instead of starting from zeros.
- [x] Validate that editable percentages remain numerically sane.
- [x] Decide whether validation should require:
- [x] each allocation bucket to sum to 100% within its tax component
- [x] municipality and county fallback branches to each balance independently
- [x] no negative allocations

## Municipal Containment Logic

- [x] Keep using TIGER `PLACE` containment to determine whether the site is inside an incorporated municipality.
- [x] Apply the municipal carve-out only when the active scenario says it depends on municipal containment.
- [x] Preserve county fallback logic for unincorporated sites in eligible counties.
- [x] Avoid hard-coding Fort Wayne in calculation logic once the scenario system is in place.
- [x] Ensure Allen, DeKalb, and Steuben remain covered by the default preset while allowing future presets to define different eligible areas.

## Calculator Refactor

- [x] Replace the current direct revenue split math with scenario-driven recipient calculation.
- [x] Refactor table labels so they come from the active scenario / resolved municipality instead of static strings.
- [x] Refactor written analysis text so it describes the active scenario and actual recipients.
- [x] Keep the host, regional, and consolidated tables working without changing their current structural layout beyond what is needed for scenario support.

## Data and Persistence Decisions

- [x] Decide whether scenario presets should live:
- [x] directly in frontend code for now
- [x] in a shared JSON config
- [x] in backend storage later
- [x] Keep the first implementation simple enough to ship without requiring a new database-backed rule editor.
- [x] Document how future presets for other counties/states would be added.

## Validation

- [x] Verify the default `NE Indiana Casino` preset reproduces the intended current behavior.
- [x] Verify municipal-site behavior:
- [x] inside Fort Wayne
- [x] inside a DeKalb municipality
- [x] inside a Steuben municipality
- [x] Verify unincorporated-site behavior in those same counties.
- [x] Verify `Custom` mode updates the tables and written analysis consistently.
- [x] Verify switching scenarios does not break hot reload, table rendering, or generated analysis text.

## Documentation

- [x] Document the purpose of the scenario selector in public-facing calculator notes/tooltips.
- [x] Document how `Custom` differs from preset scenarios.
- [x] Document that municipality-triggered city allocations depend on actual incorporated-place containment, not just county selection.

## Implementation Notes

- Scenario presets now live directly in frontend code in `SaveFW.Client/wwwroot/js/economics/calculator.js`.
- The first shipped preset is `NE Indiana Casino`, with `Custom` cloning the active preset and unlocking editable branch percentages.
- Custom-mode validation requires each of the four tax branches to total `100%` with no negative allocations. If the draft is invalid, the UI keeps showing the last valid custom allocation until the percentages balance again.
- Municipality-triggered allocations still depend on the backend `PLACE` lookup endpoint, so future presets for other counties or states can be added in the frontend scenario list now, while broader municipal eligibility will require extending the backend containment coverage as those presets are introduced.
- Runtime validation completed with:
- `node -c SaveFW.Client/wwwroot/js/economics/calculator.js`
- live watcher hot-reload confirmation from `/root/SaveFW/dev/local-dev-server.log`
- live municipality endpoint checks for:
- Fort Wayne / Allen municipal site
- Altona / DeKalb municipal site
- Angola / Steuben municipal site
- unincorporated Allen / DeKalb / Steuben test points
