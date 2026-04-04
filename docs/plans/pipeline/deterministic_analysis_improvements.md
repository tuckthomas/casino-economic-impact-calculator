# Deterministic / Automated Analysis Improvement Ideas

This document captures follow-on ideas for improving the deterministic analysis section after the current table and scenario-model refactors.

## Recently Addressed

- [x] Expand the Tax Revenue Analysis section to explain the revenue-base terminology, the currently modeled Indiana tax system, and the scenario-driven recipient results.

## Pipeline Ideas

- [ ] Tie every narrative subsection to the exact statement rows now shown in the UI.
  The analysis should explicitly reference the same Host, Regional, and Consolidated totals the tables display, so future table changes cannot silently drift away from the narrative again.

- [ ] Add a tax-model descriptor layer that changes by subject state.
  Right now the narrative correctly explains Indiana's structure, but once the work in [casino_tax_dynamic_plan.md](/root/SaveFW/docs/plans/pipeline/casino_tax_dynamic_plan.md) lands, the analysis should explain the active state's actual tax base, deductions, brackets, and special supplements instead of hardcoding Indiana wording.

- [ ] Separate "tax engine output" from "allocation scenario output" more explicitly.
  The analysis should explain the sequence as:
  1. taxable base
  2. state tax calculation
  3. scenario branch selection
  4. recipient allocations
  5. statement-level net results

- [ ] Add a short branch-eligibility explanation block.
  The narrative should state why the run used the municipal branch, the unincorporated branch, or no host-local branch at all, and it should name the specific PLACE containment result that triggered that outcome.

- [ ] Add a top drivers summary for the selected county and for the regional spillover area.
  Instead of listing only raw cost categories, the analysis should rank the largest cost drivers and explain which categories are doing the most work in pushing the net result negative or positive.

- [ ] Add a county ranking summary for regional spillover.
  The regional section should name the highest-burden spillover counties and summarize how much of the spillover total they represent.

- [ ] Add statement-specific break-even language.
  The deterministic analysis should explain:
  - host break-even AGR
  - regional break-even AGR
  - consolidated break-even AGR
  This would align the written narrative with the AGR sensitivity tooling.

- [ ] Add sensitivity-aware narrative output.
  The written analysis should react not only to the current point estimate, but also to the selected sensitivity mode so the narrative can summarize what happens across a lower-to-higher AGR range.

- [ ] Add a source-footnote registry for each major claim.
  The narrative currently links some sources, but it should eventually assign each major methodological statement to a source bucket such as Census/TIGER, Grinols-based cost inputs, Spectrum-derived revenue assumptions, or tax-model source records.

- [ ] Add explicit "modeled but not yet included" disclosure blocks.
  The substitution effect, local-business displacement, leakage, and multiplier treatment should have a structured disclosure format with:
  - current status
  - likely directional effect
  - planned implementation reference

- [ ] Support layered output modes.
  The deterministic analysis should offer:
  - concise summary mode
  - standard public-facing mode
  - detailed technical mode
  That would let casual readers avoid overlong text while still giving technical readers the full deterministic explanation.

- [ ] Add scenario-comparison narrative support.
  Once multiple presets matter, the analysis should be able to compare the active scenario against another preset or against Custom so users can see how allocation changes affect each statement.

- [ ] Add export-safe structured analysis data before HTML assembly.
  The current analysis is built directly as HTML strings. A better next step is to generate a structured analysis object first, then render it for the webpage, PDF export, and any future API output from the same data source.
