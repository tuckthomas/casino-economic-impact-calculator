# SaveNEIN Fact Checker Implementation Status

Branch: `codex/consolidated-active-work`
Issue: #35
Updated: August 20, 2026 ET / August 21, 2026 UTC

## Implemented

- [x] Copied the fact-check implementation plan from `main` into this branch.
- [x] Copied `Yes_for_Allen_Counter_Analysis_Fact_Check.docx` from `main` into the plan folder for agent/reference use.
- [x] Preserved the original `SaveNEIN.Client/Pages/ProponentsCritique.razor` byte-for-byte at `SaveNEIN.Client/Pages/Archive/ProponentsCritiqueLegacy.razor.txt`.
- [x] Added archive/rollback documentation in `SaveNEIN.Client/Pages/Archive/README.md`.
- [x] Kept the live `ProponentsCritique.razor` component and `#proponents-critique` anchor in place.
- [x] Replaced the old four-card pitch/reality content with a data-driven vertical fact-check timeline.
- [x] Added a centered alternating desktop timeline and left-spine stacked mobile layout.
- [x] Added a reusable circular HTML/CSS fact-check gauge with verdict-driven needle position and indicator color.
- [x] Added the four verdict states: `TRUE`, `MOSTLY TRUE`, `MOSTLY FALSE`, and `FALSE`.
- [x] Added secondary issue tags including projection, unsupported, outdated, geography error, definition error, missing context, and policy promise.
- [x] Added a structured `FactCheckClaim` model with sources, key facts, evidence tables, correction wording, capture/review metadata, and revision-history support.
- [x] Added reusable `FactCheckTimelineItem` and `FactCheckGauge` components.
- [x] Added expandable, keyboard-native `<details>` evidence/source sections.
- [x] Added concise methodology/rating definitions at the top of the section.
- [x] Added true/mostly-true findings alongside negative ratings.
- [x] Seeded the section from the August 2026 Yes for Allen counter-analysis, including jobs, construction jobs, gross output, cross-border recapture, tax/fiscal claims, referendum process, regulation, and tribal-gaming claims.
- [x] Kept paraphrased campaign implications out of quotation marks and labeled them as framing rather than direct claims.
- [x] Avoided raster verdict assets and external mockup/CDN dependencies.
- [x] Kept visible supporting text at 14px / `text-sm` equivalent or larger.
- [x] Added `prefers-reduced-motion` handling for the gauge needle transition.

## Pending validation in the active development environment

- [ ] Run `npm run check:ui-text`.
- [ ] Allow/re-run the Tailwind CSS build so utilities introduced by the new Razor markup are present in generated `wwwroot/css/app.css`.
- [ ] Run `dotnet build SaveNEIN.sln`.
- [ ] Inspect the live section at narrow mobile widths, tablet, and desktop.
- [ ] Verify light/dark theme presentation after the Tailwind rebuild.
- [ ] Confirm all external claimant/source links resolve as intended.
- [ ] Perform final editorial review of verdict assignments and exact campaign wording before production publication.

The implementation is intentionally left in `docs/plans/pipeline` until the local visual/build/editorial validation above is complete. After acceptance, move this subfolder to `docs/plans/completed` and close issue #35.
