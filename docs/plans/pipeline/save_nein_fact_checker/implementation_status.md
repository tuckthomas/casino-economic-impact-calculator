# SaveNEIN Fact Checker Implementation Status

Branch: `codex/consolidated-active-work`
Issue: #35
Updated: August 21, 2026 ET

## Implemented

- [x] Copied the fact-check implementation plan from `main` into this branch.
- [x] Copied `Yes_for_Allen_Counter_Analysis_Fact_Check.docx` from `main` into the plan folder for agent/reference use.
- [x] Preserved the original `SaveNEIN.Client/Pages/ProponentsCritique.razor` byte-for-byte at `SaveNEIN.Client/Pages/Archive/ProponentsCritiqueLegacy.razor.txt`.
- [x] Added archive/rollback documentation in `SaveNEIN.Client/Pages/Archive/README.md`.
- [x] Kept the live `ProponentsCritique.razor` component and `#proponents-critique` anchor in place.
- [x] Added a reusable circular HTML/CSS `FactCheckGauge` with verdict-driven needle position and indicator color.
- [x] Added the four verdict states: `TRUE`, `MOSTLY TRUE`, `MOSTLY FALSE`, and `FALSE`.
- [x] Added secondary issue-tag support including projection, unsupported, outdated, geography error, definition error, missing context, and policy promise.
- [x] Added expandable, keyboard-native `<details>` evidence/source sections.
- [x] Avoided raster verdict assets and external mockup/CDN dependencies.
- [x] Added `prefers-reduced-motion` handling for the gauge needle transition.

## August 21 reusable JSON refactor and requested visual-format swap

- [x] Preserved/restored the existing centered alternating fact-check timeline on desktop and left-spine stacked timeline on mobile. The temporary two-column grid redesign was reverted because it was not requested.
- [x] Implemented the requested visual-role swap inside the existing card format rather than introducing a new information architecture.
- [x] `ShortFinding` — the evidence/corrective content that previously appeared inside the smaller evidence box — now receives the **same large, bold, uppercase, red-left-border treatment previously used by the claimant statement**.
- [x] `ClaimText` — the claimant statement that previously received the dominant headline treatment — now receives the **same smaller bordered/surface-box treatment previously used by the evidence content**.
- [x] The evidence label occupies the former claimant/framing label position above the dominant corrective text.
- [x] The claimant/framing label now appears inside the smaller claim box above the claimant text.
- [x] The verdict gauge remains attached to the timeline entry and remains verdict-driven; it was not repurposed as a rating of the evidence.
- [x] Replaced the page-specific timeline renderer with a reusable `FactCheckCard` Blazor component while retaining the original timeline presentation.
- [x] Kept `FactCheckGauge` as a separate reusable verdict component consumed by `FactCheckCard`.
- [x] Removed the superseded `FactCheckTimelineItem` component and its scoped CSS so there is one maintained card-rendering path.
- [x] Moved the currently displayed fact-check content out of `ProponentsCritique.razor` into `SaveNEIN.Client/wwwroot/data/fact-checks.json`.
- [x] Added a versioned `FactCheckDocument` JSON contract and JSON-string verdict deserialization.
- [x] `ProponentsCritique.razor` is now a thin JSON loader/container; card order and content are controlled by JSON order rather than hardcoded Razor constructors.
- [x] No database dependency was introduced. This content is intentionally repository-local because it is campaign/location-specific and should be replaceable by a downstream clone without schema migrations or seed-data cleanup.
- [x] The same `FactCheckDocument` / `FactCheckClaim` contract can later be populated from an API or PostgreSQL JSONB payload without changing `FactCheckCard` or `FactCheckGauge`.
- [x] Replaced the misleading flat `ClaimCapturedDate` concept in the JSON-driven contract with separate provenance fields: `ClaimSourceObservedOn`, `ClaimSourceObservationType`, and nullable `ClaimSourceArchivedUrl`.
- [x] August 8 Yes for Allen entries are identified as `ReportScrape` observations, not archived-web captures.
- [x] The card labels a live claimant hyperlink as `View current claimant page`; when `ClaimSourceArchivedUrl` is populated later, the same component automatically changes the primary link label to `View archived claimant source`.

## Current JSON content scope

`SaveNEIN.Client/wwwroot/data/fact-checks.json` currently contains the six fact checks that were already being rendered by the pre-refactor `FalseFactChecks` selection, in the same display order:

1. Allen County resident-jobs framing
2. Keep Dollars Local framing
3. Tribal-casino approval framing
4. Steuben taxpayer-liability claim
5. Steuben nonprofit/LDA framing
6. Allen County NO-vote framing

Additional cards can be added, removed, reordered, or replaced by editing this JSON document; no Razor component changes are required.

## Pending validation in the active development environment

- [ ] Run `npm run check:ui-text`.
- [ ] Run the Tailwind/CSS asset build.
- [ ] Run `dotnet build SaveNEIN.sln`.
- [ ] Inspect the restored alternating timeline at narrow mobile widths, tablet, and desktop.
- [ ] Verify light/dark theme presentation.
- [ ] Confirm at a glance that the corrective evidence receives the former dominant claim typography and the claimant statement receives the former evidence-box treatment.
- [ ] Confirm JSON deserialization succeeds in the production WebAssembly build.
- [ ] Confirm all external claimant/source links resolve as intended.
- [ ] Perform final editorial review of verdict assignments and exact campaign wording before production publication.

The implementation remains in `docs/plans/pipeline` until the visual/build/editorial validation above is complete. The separate immutable claimant-source archiving plan remains in `docs/plans/in-progress/immutable_claim_source_web_archiving` and governs the future ArchiveBox integration.
