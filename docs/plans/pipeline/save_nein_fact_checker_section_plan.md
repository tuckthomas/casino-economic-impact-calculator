# SaveNEIN Fact Checker / Casino Proponents Critique Replacement Plan

Status: Pipeline

## Objective

- [ ] Replace the **current visible content** inside the existing `Casino Proponents Critique` section with a source-driven SaveNEIN Fact Checker experience.
- [ ] **Do not delete the `Casino Proponents Critique` section, component, anchor, or navigation target.** Preserve the existing section shell so current links to `#proponents-critique` continue to work.
- [ ] Preserve the current `ProponentsCritique.razor` implementation in an archive folder before replacing its visible contents, so the existing version can be restored quickly if the new design is rejected or needs to be rolled back.
- [ ] Keep the current production page stable while the replacement is being built; archive first, then refactor.
- [ ] Use the August 2026 `Yes for Allen Counter-Analysis and Fact Check` research as the initial content basis for the first fact checks.
- [ ] Build the fact checker as a reusable system rather than a one-off Yes for Allen page so future claims from casino campaigns, officials, developers, consultants, studies, advertisements, mailers, or media coverage can be added without redesigning the feature.

## Non-Negotiable Preservation / Rollback Requirements

- [ ] Before changing `SaveNEIN.Client/Pages/ProponentsCritique.razor`, copy its current contents verbatim into an archive location.
- [ ] Preferred archive location:
  - [ ] `SaveNEIN.Client/Pages/Archive/ProponentsCritiqueLegacy.razor.txt` or another non-routable/non-compiled archival extension.
  - [ ] If the project already establishes a different archive convention by implementation time, use that convention instead.
- [ ] Add a short header comment to the archived copy identifying:
  - [ ] original source path;
  - [ ] archive date;
  - [ ] reason for archival: replaced by the SaveNEIN Fact Checker design;
  - [ ] restoration note.
- [ ] Do not delete or overwrite the archive during subsequent fact-checker revisions.
- [ ] Keep `SaveNEIN.Client/Pages/ProponentsCritique.razor` as the active component invoked by `Home.razor`.
- [ ] Keep the existing `<section id="proponents-critique">` anchor or preserve an equivalent wrapper with exactly the same `id`.
- [ ] Keep existing home/header navigation to `#proponents-critique` functional unless a later explicit plan changes the navigation model.
- [ ] Add a rollback checklist to the implementation PR explaining how to restore the archived section if requested.

## Editorial Principle

- [ ] Design the section around a transparent audit trail rather than generic anti-casino rhetoric.
- [ ] For each fact check, show:
  - [ ] the exact claim;
  - [ ] who made it;
  - [ ] where it appeared;
  - [ ] when it was captured;
  - [ ] the underlying source(s);
  - [ ] what those sources actually say;
  - [ ] the reason for the rating;
  - [ ] a corrected or more precise formulation where appropriate.
- [ ] Preserve the exact definition, geography, timeframe, and methodological limitations of each source.
- [ ] Do not classify a projection as false merely because it is a projection.
- [ ] Distinguish between a claim that is factually wrong and a claim that is technically traceable but materially overstated, geographically broadened, outdated, or presented with excessive certainty.
- [ ] Include true and mostly true findings prominently when the evidence supports them; the fact checker should not appear to be a system that mechanically labels every pro-casino statement false.

## Rating System

### Primary Verdict Scale

- [ ] Use four primary verdicts:
  - [ ] `TRUE`
  - [ ] `MOSTLY TRUE`
  - [ ] `MOSTLY FALSE`
  - [ ] `FALSE`
- [ ] Define `TRUE` as: the factual assertion is supported by the best available evidence with no material missing context.
- [ ] Define `MOSTLY TRUE` as: the central assertion is supported, but an important qualification, scope limitation, or minor imprecision is omitted.
- [ ] Define `MOSTLY FALSE` as: there is a factual kernel behind the statement, but the wording materially changes the geography, definition, certainty, magnitude, implication, or context.
- [ ] Define `FALSE` as: the assertion is contradicted by the evidence or lacks an adequate factual basis as stated.
- [ ] Publish these definitions in a compact methodology/legend element accessible from the fact-check section.

### Secondary Issue Tags

- [ ] Keep the primary verdict separate from the reason/category tag.
- [ ] Support short secondary tags such as:
  - [ ] `PROJECTION`
  - [ ] `UNSUPPORTED`
  - [ ] `OUTDATED`
  - [ ] `GEOGRAPHY ERROR`
  - [ ] `DEFINITION ERROR`
  - [ ] `MISSING CONTEXT`
  - [ ] `POLICY PROMISE`
  - [ ] `UNPROVEN CAUSATION`
- [ ] Avoid long badge text such as `CAUSATION NOT ESTABLISHED`; use `UNPROVEN CAUSATION` and explain the full reasoning in the body.
- [ ] Permit more than one secondary tag when a claim has multiple independent problems.
- [ ] Do not let a secondary tag substitute for the actual explanation.

## CSS / HTML Fact-Check Gauge

- [ ] Do **not** ship the four current mock PNGs as the primary production verdict component unless the CSS/SVG approach proves materially inferior during implementation.
- [ ] Build a single reusable gauge component rendered from HTML/CSS, with one component supporting all four verdicts.
- [ ] Prefer real HTML text for:
  - [ ] dial labels;
  - [ ] large verdict text;
  - [ ] `SAVE NEIN FACT CHECK` label;
  - [ ] accessible fallback content.
- [ ] Build the visual instrument from lightweight CSS where practical:
  - [ ] navy enclosure;
  - [ ] metallic/silver bezel;
  - [ ] red accent border;
  - [ ] white dial face;
  - [ ] red / neutral / navy scale segments;
  - [ ] tick marks;
  - [ ] needle;
  - [ ] center pivot;
  - [ ] indicator lamp;
  - [ ] restrained shadows/highlights.
- [ ] Use CSS custom properties to drive verdict-specific values, for example:
  - [ ] `--needle-angle`;
  - [ ] `--indicator-color`;
  - [ ] `--verdict-accent`.
- [ ] Rotate the needle with `transform: rotate(...)` rather than repositioning it with layout properties.
- [ ] Keep the gauge scalable and crisp across phone, tablet, desktop, and high-DPI displays.
- [ ] Ensure the verdict remains understandable if gradients, shadows, or animation are unavailable.
- [ ] Do not depend on red/green color alone; the verdict text must always be visible.
- [ ] Respect `prefers-reduced-motion`.
- [ ] If a small needle animation is added, animate only on first reveal and keep it subtle; avoid slot-machine/carnival behavior.
- [ ] Measure actual render/paint performance before adding expensive blur filters, large stacked box shadows, or excessive pseudo-elements.
- [ ] If recreating the curved dial precisely becomes unnecessarily complex in CSS, allow a hybrid reusable inline SVG for the static dial geometry while retaining HTML text and CSS-controlled needle/color state.
- [ ] Do not fall back to four separate raster assets unless there is a demonstrated reason.

## Component Architecture

- [ ] Keep `ProponentsCritique.razor` as the active homepage section component.
- [ ] Refactor its inner content into reusable fact-checker components rather than building one monolithic Razor file.
- [ ] Proposed component structure:
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckGauge.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckCard.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckFilters.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckEvidence.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckSourceTable.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckCorrection.razor`
  - [ ] `SaveNEIN.Client/Components/FactCheck/FactCheckMethodology.razor`
- [ ] Keep verdict and issue-tag styling centralized so every fact check uses the same semantics and visual language.
- [ ] Reuse existing SaveNEIN typography, spacing, dark-theme tokens, buttons, tooltips, and responsive conventions wherever practical.
- [ ] Avoid making the surrounding evidence cards skeuomorphic; the gauge can be the branded visual element while the evidence UI remains clean and analytical.

## Data Model

- [ ] Do not hardcode every full fact check directly into `ProponentsCritique.razor`.
- [ ] Create a structured `FactCheck` model in the appropriate client/shared layer.
- [ ] Support at least these fields:
  - [ ] `Id`
  - [ ] `Slug`
  - [ ] `Claimant`
  - [ ] `CampaignOrOrganization`
  - [ ] `ClaimText`
  - [ ] `ClaimSourceUrl`
  - [ ] `ClaimCapturedDate`
  - [ ] `Category`
  - [ ] `Verdict`
  - [ ] `IssueTags`
  - [ ] `ShortFinding`
  - [ ] `Explanation`
  - [ ] `CorrectedClaim`
  - [ ] `KeyFacts`
  - [ ] `Sources`
  - [ ] `EvidenceTables`
  - [ ] `RelatedFactCheckIds`
  - [ ] `FirstPublished`
  - [ ] `LastReviewed`
  - [ ] `RevisionHistory`
- [ ] Decide during implementation whether the first version should use strongly typed C# seed data, JSON under `wwwroot/data`, or an existing repository content pattern.
- [ ] Keep the content format portable enough that the same data can later drive:
  - [ ] homepage cards;
  - [ ] standalone detail pages;
  - [ ] search/filtering;
  - [ ] social share cards;
  - [ ] structured metadata;
  - [ ] downloadable reports;
  - [ ] future API output.

## Homepage Section Layout

- [ ] Preserve the existing `Casino Proponents Critique` section wrapper and anchor.
- [ ] Replace the current four pitch-vs-reality category cards with the new Fact Checker interface.
- [ ] Use a heading treatment such as `SaveNEIN Fact Check` or `Casino Claims: Fact Checked` while retaining the underlying `#proponents-critique` section identifier.
- [ ] Add a short methodology statement near the top explaining that public claims are traced back to studies, government data, statutes, and original research.
- [ ] Display the four verdict gauges together as a compact legend on larger screens.
- [ ] Collapse or simplify the legend on mobile so it does not consume excessive vertical space.
- [ ] Add filter controls for:
  - [ ] `ALL`
  - [ ] `JOBS`
  - [ ] `TAXES`
  - [ ] `ECONOMY`
  - [ ] `TRIBAL GAMING`
  - [ ] `PUBLIC SAFETY`
  - [ ] `REFERENDUM PROCESS`
- [ ] Add verdict filtering once enough claims exist to make it useful.
- [ ] Add claimant/source filtering once the fact-check corpus expands beyond Yes for Allen.
- [ ] Render fact checks as responsive cards with:
  - [ ] claimant/source label;
  - [ ] exact short claim;
  - [ ] compact CSS gauge;
  - [ ] verdict;
  - [ ] issue tag(s);
  - [ ] one- or two-sentence finding;
  - [ ] `See the Evidence` action.
- [ ] Keep the card gauge compact; do not allow a 1:1 instrument graphic to dominate each card.
- [ ] On card view, target a gauge size roughly proportional to the card rather than a fixed large raster-style footprint.

## Evidence / Detail Experience

- [ ] Each fact check must expose a deeper evidence view.
- [ ] Choose one of these implementation patterns after checking route/navigation fit:
  - [ ] preferred: dedicated detail route such as `/fact-check/{slug}`;
  - [ ] acceptable initial version: accessible modal/drawer with deep-link support.
- [ ] If dedicated routes are used, add `/fact-check` as the index/landing page while continuing to surface featured fact checks inside `#proponents-critique` on the homepage.
- [ ] Individual fact-check detail should show, in order:
  - [ ] exact claim;
  - [ ] claimant;
  - [ ] original URL/source;
  - [ ] capture/review date;
  - [ ] full verdict gauge;
  - [ ] concise finding;
  - [ ] `What the source actually says` section;
  - [ ] evidence table or source excerpt where appropriate;
  - [ ] `Why we rated it this way` section;
  - [ ] corrected/more precise wording;
  - [ ] sources;
  - [ ] related fact checks;
  - [ ] revision history or last-reviewed date.
- [ ] Prefer accessible HTML reproductions of source tables over screenshot-only evidence.
- [ ] Where an original table is critical, allow an optional source screenshot alongside the HTML table for visual verification.
- [ ] Clearly distinguish SaveNEIN calculations or interpretations from quoted/source-reported values.

## Initial Fact-Check Content Set

- [ ] Seed the first release primarily from the Yes for Allen counter-analysis document.
- [ ] Treat the suggested ratings below as implementation candidates that must be checked against the **exact published wording** before final publication.
- [ ] Create an initial fact check for `2,000 New Permanent Jobs`.
  - [ ] Candidate verdict: `MOSTLY FALSE`.
  - [ ] Candidate tags: `GEOGRAPHY ERROR`, `DEFINITION ERROR`.
  - [ ] Explain that CBRE's 2,001 total is statewide direct + indirect + induced employment, while the Allen County total is 1,676, including 947 direct jobs.
  - [ ] Explain that RIMS II employment is not an FTE count.
- [ ] Create an initial fact check for jobs described as being `for Allen County residents`.
  - [ ] Candidate verdict: `FALSE` or `MOSTLY FALSE`, depending on exact quoted wording.
  - [ ] Candidate tag: `UNSUPPORTED`.
  - [ ] Explain that the cited economic-impact tables do not establish the residence of workers filling the modeled jobs.
- [ ] Create an initial fact check for `approximately 5,500 construction jobs`.
  - [ ] Candidate verdict: `MOSTLY FALSE` when presented in Allen County context.
  - [ ] Candidate tag: `GEOGRAPHY ERROR`.
  - [ ] Explain statewide 5,520 versus Allen County 3,383.
- [ ] Create an initial fact check for `nearly $550 million in annual economic output`.
  - [ ] Candidate verdict: `MOSTLY FALSE`.
  - [ ] Candidate tags: `GEOGRAPHY ERROR`, `DEFINITION ERROR`.
  - [ ] Explain that $549.3 million is the statewide RIMS II gross-output figure, while Allen County gross output is $471.6 million.
  - [ ] Explain that gross output is not synonymous with GDP, household income, net economic welfare, or net new community wealth.
- [ ] Create an initial fact check for `Keep Dollars Local` / Michigan and Ohio recapture framing.
  - [ ] Candidate verdict: `MOSTLY FALSE` when the implication is that most projected revenue is existing out-of-state leakage returning home.
  - [ ] Candidate tags: `MISSING CONTEXT`, `DEFINITION ERROR`.
  - [ ] Explain that CBRE explicitly models roughly $50.2 million of Michigan/Ohio recapture against approximately $282.3 million of stabilized GGR, roughly 17.8%.
- [ ] Create an initial fact check for `more than $100 million in new annual revenue`.
  - [ ] Candidate verdict: `MOSTLY FALSE` or `MOSTLY TRUE` depending on exact wording and whether the claim clearly says `projected`.
  - [ ] Candidate tags: `PROJECTION`, `OUTDATED`.
  - [ ] Explain that the $107.4 million value is a modeled stabilized-year fiscal forecast, not an observed annual result.
  - [ ] Explain that the CBRE fiscal-allocation table predates the finalized 2026 Northeast Indiana supplemental wagering-tax allocation.
- [ ] Create an initial fact check for `more than $41 million stays local`.
  - [ ] Candidate verdict: `MOSTLY FALSE` if presented as Fort Wayne/Allen County unrestricted revenue.
  - [ ] Candidate tags: `DEFINITION ERROR`, `MISSING CONTEXT`.
  - [ ] Explain that the CBRE total includes Fort Wayne, Allen County, and other governing bodies/stakeholders.
- [ ] Create an initial fact check for `tax relief for residents`.
  - [ ] Candidate verdict: rating depends on exact wording.
  - [ ] Candidate tags: `POLICY PROMISE`, `MISSING CONTEXT`.
  - [ ] Explain that current Indiana law permits certain casino revenue to be used for levy reduction at the unit's discretion but does not require household tax relief.
- [ ] Create an initial fact check for `A YES vote itself approves a casino` / the campaign's statement that it does not.
  - [ ] Candidate verdict for the campaign's `No` answer: `TRUE`.
  - [ ] Explain that a successful referendum permits the county to enter the application/licensing process; it does not itself issue a casino license.
- [ ] Create an initial fact check for Indiana Gaming Commission regulation.
  - [ ] Candidate verdict for the core statement: `TRUE`.
  - [ ] Separate the unsupported superlative `one of the most highly regulated gaming industries in the country` into its own check or secondary finding.
- [ ] Create an initial fact check for `Voting NO gives the casino to DeKalb or Steuben County`.
  - [ ] Candidate verdict: `MOSTLY FALSE`.
  - [ ] Candidate tag: `MISSING CONTEXT`.
  - [ ] Explain the additional referendum, application, and Indiana Gaming Commission contingencies.
- [ ] Create an initial fact check for `A tribal casino would not require approval from state or local officials`.
  - [ ] Candidate verdict: `MOSTLY FALSE`.
  - [ ] Candidate tags: `MISSING CONTEXT`, `DEFINITION ERROR`.
  - [ ] Explain that the federal legal pathway depends on parcel-specific gaming-land eligibility; the two-part Secretarial route requires state/local consultation and Governor concurrence, and Class III gaming ordinarily operates through a Tribal-State compact approved by Interior.
- [ ] Create an initial fact check for the implication that tribal gaming revenue cannot benefit local government.
  - [ ] Candidate verdict: `MOSTLY FALSE`.
  - [ ] Candidate tag: `MISSING CONTEXT`.
  - [ ] Explain the distinction between Indiana's commercial-casino statutory tax distribution and permissible tribal/local-government funding or agreement structures.
- [ ] Create an initial fact check for an imminent/inevitable Miami Tribe Fort Wayne casino if that claim is actually published or quoted in campaign messaging.
  - [ ] Candidate verdict: `FALSE` or `MOSTLY FALSE` depending on exact wording.
  - [ ] Candidate tag: `UNSUPPORTED`.
  - [ ] Explain that the public record reviewed in the counter-analysis did not establish an announced or approved Fort Wayne casino project on the parcel.

## Projection Handling

- [ ] Add a prominent `FORECAST, NOT OBSERVED RESULT` disclosure for model-derived claims when appropriate.
- [ ] Evaluate whether the claimant accurately represents the forecast rather than treating forecast status itself as evidence of falsity.
- [ ] Show important forecast dependencies when relevant, including:
  - [ ] geography;
  - [ ] stabilization year;
  - [ ] assumed gaming revenue;
  - [ ] tax structure;
  - [ ] competition assumptions;
  - [ ] multiplier methodology;
  - [ ] any material analyst adjustment identified in the underlying source.
- [ ] Differentiate `the source projects X` from `X will happen`.

## `Why We Rated It This Way` Pattern

- [ ] Use short, scannable finding rows rather than dense paragraphs whenever possible.
- [ ] For mixed claims, explicitly show both the supported and unsupported portions.
- [ ] Example pattern for jobs:
  - [ ] `SUPPORTED: CBRE does contain a 2,001-job estimate.`
  - [ ] `NOT SUPPORTED: 2,001 is not the Allen County total.`
  - [ ] `CORRECTION: Allen County total is 1,676.`
  - [ ] `CONTEXT: Only 947 are direct jobs.`
  - [ ] `DEFINITION: RIMS II employment is not FTE employment.`
- [ ] Avoid vague phrases such as `the truth is...`; point directly to the source discrepancy.

## Corrected-Claim Pattern

- [ ] Where possible, include `A more accurate way to say it` beneath the analysis.
- [ ] Construct the corrected wording from the same source used for the rating.
- [ ] Do not rewrite a claim more aggressively than the evidence supports.
- [ ] Use this feature to demonstrate exactly which definition, geography, or certainty qualifier was lost in the original messaging.

## Search / Filtering

- [ ] Add client-side filtering once the first content set is implemented.
- [ ] Support topic filtering from launch if the number of cards warrants it.
- [ ] Add text search when there are enough claims to make search useful.
- [ ] Future search should match:
  - [ ] claim text;
  - [ ] claimant;
  - [ ] organization/campaign;
  - [ ] study/source name;
  - [ ] topic;
  - [ ] issue tag.
- [ ] Preserve filter state in the URL if practical so filtered views can be shared.

## Standalone Fact-Check Route

- [ ] Evaluate adding a dedicated `/fact-check` route after or alongside the homepage replacement.
- [ ] If added, use it as the complete searchable fact-check index while `#proponents-critique` shows featured/recent checks.
- [ ] Use detail routes such as `/fact-check/{slug}` for durable deep links.
- [ ] Do not require the standalone route for the first implementation if it would delay replacing the current critique section; the homepage section can ship first if its data/component architecture is reusable.

## Navigation

- [ ] Preserve `#proponents-critique` links during the replacement.
- [ ] Update the visible navigation label only if the design requires it, e.g. from `Casino Proponents Critique` to `Fact Check`, while keeping the destination stable.
- [ ] If `/fact-check` is later added, decide whether the main nav should link directly to the route or scroll to the homepage section.
- [ ] Do not break existing deep links to `#proponents-critique`.

## Accessibility

- [ ] Render verdicts as text, not image-only labels.
- [ ] Give the gauge an accessible name such as `Fact-check verdict: Mostly False`.
- [ ] Hide purely decorative dial geometry from assistive technologies.
- [ ] Ensure all secondary tags have sufficient contrast in light and dark modes.
- [ ] Ensure keyboard users can open evidence/details and navigate filters.
- [ ] Do not use color as the only carrier of verdict meaning.
- [ ] Respect reduced-motion preferences.
- [ ] Ensure the layout remains understandable at 200% zoom.
- [ ] Use semantic headings and tables for source comparisons.

## Performance

- [ ] Prefer the reusable CSS/HTML gauge over downloading four large raster verdict images.
- [ ] Keep decorative shadows/filters restrained and benchmark paint cost.
- [ ] Avoid repeated heavy gradients or blur effects on dozens of simultaneously visible cards.
- [ ] Lazy-render or defer non-visible detail content if the fact-check corpus becomes large.
- [ ] Do not load full evidence screenshots until requested/visible if screenshots are used.
- [ ] Keep card-level data compact and defer heavy evidence data to detail views if needed.
- [ ] Measure with browser performance tooling and Lighthouse before declaring the CSS gauge faster than image assets.

## SEO / Shareability

- [ ] Give each standalone fact check a stable slug if detail routes are implemented.
- [ ] Use unique page titles/descriptions derived from the exact claim and verdict.
- [ ] Add canonical URLs for fact-check detail pages.
- [ ] Add structured metadata only after verifying the correct schema and semantics for editorial fact-check content.
- [ ] Include claimant, reviewed date, verdict, and source names in share metadata where practical.
- [ ] Ensure share cards do not overstate a nuanced verdict by dropping the secondary issue or key qualifier.

## Source Integrity / Change Tracking

- [ ] Record the capture date for mutable campaign webpages.
- [ ] Where practical, preserve an archival screenshot or text snapshot of mutable claims so later edits do not erase what was reviewed.
- [ ] Store `LastReviewed` separately from `ClaimCapturedDate`.
- [ ] Add revision history when:
  - [ ] a claimant changes the wording;
  - [ ] a source is updated;
  - [ ] legislation changes;
  - [ ] a rating changes;
  - [ ] SaveNEIN corrects an error.
- [ ] Never silently change a verdict without updating the review/revision metadata.

## Styling Direction

- [ ] Keep the existing SaveNEIN dark navy / red visual identity.
- [ ] Use the mock gauge design as the aesthetic reference for the verdict instrument, not as a requirement to reproduce every bevel or glow exactly.
- [ ] Make the gauge the distinctive branded `stamp` of the fact checker.
- [ ] Keep the surrounding cards, filters, evidence tables, and source blocks flatter and more restrained.
- [ ] Avoid transforming the full page into a skeuomorphic dashboard.
- [ ] Maintain strong hierarchy:
  - [ ] claim first;
  - [ ] verdict second;
  - [ ] concise reason third;
  - [ ] evidence on demand.

## Testing

### Component Tests

- [ ] Test all four primary verdict states.
- [ ] Test every supported secondary tag.
- [ ] Test multiple tags on one fact check.
- [ ] Test long and short claim text.
- [ ] Test missing optional fields.
- [ ] Test source tables with large values and long source names.

### Responsive Tests

- [ ] Test narrow phone width.
- [ ] Test standard mobile width.
- [ ] Test tablet portrait/landscape.
- [ ] Test desktop.
- [ ] Test ultrawide layouts.
- [ ] Verify gauges do not overflow or dominate small cards.
- [ ] Verify filters remain usable without horizontal clipping.

### Accessibility Tests

- [ ] Keyboard-only navigation.
- [ ] Screen-reader verdict announcement.
- [ ] Light/dark contrast.
- [ ] 200% zoom.
- [ ] Reduced motion.

### Regression Tests

- [ ] Verify `Home.razor` still renders `ProponentsCritique` in the same page sequence unless explicitly changed.
- [ ] Verify `#proponents-critique` still scrolls to the replacement section.
- [ ] Verify current header/home navigation remains functional.
- [ ] Verify archived legacy content is present in the repository and is not compiled/routed accidentally.
- [ ] Verify no existing section below the critique is deleted or displaced unintentionally.

## Implementation Sequence

### Phase 1 — Preserve Existing Version

- [ ] Create the archive directory if needed.
- [ ] Copy current `ProponentsCritique.razor` verbatim into the archive location.
- [ ] Add archive metadata/comment without changing the active component yet.
- [ ] Commit the archive separately or verify it exists before replacing active content.

### Phase 2 — Data + Verdict Foundation

- [ ] Add verdict enum/model.
- [ ] Add secondary issue tags.
- [ ] Add structured FactCheck model.
- [ ] Add initial data source/seed mechanism.
- [ ] Add rating definitions/methodology data.

### Phase 3 — CSS Gauge

- [ ] Build one reusable gauge.
- [ ] Implement `TRUE`.
- [ ] Implement `MOSTLY TRUE`.
- [ ] Implement `MOSTLY FALSE`.
- [ ] Implement `FALSE`.
- [ ] Validate the needle positions and color states visually against the mock concepts.
- [ ] Add responsive sizing.
- [ ] Add accessible text state.
- [ ] Add reduced-motion behavior.

### Phase 4 — Fact-Check Cards

- [ ] Build the compact card component.
- [ ] Add exact claim display.
- [ ] Add claimant/source label.
- [ ] Add gauge.
- [ ] Add issue tags.
- [ ] Add short finding.
- [ ] Add evidence action.

### Phase 5 — Replace Visible Proponents Critique Content

- [ ] Keep the existing section/component identity.
- [ ] Remove the old four visible `Pitch vs. Reality` cards from the active component only after the archive exists.
- [ ] Insert the new fact-check heading, legend, filters, and cards.
- [ ] Preserve neighboring homepage section order.
- [ ] Verify the section anchor and navigation.

### Phase 6 — Seed Initial Research Content

- [ ] Add the jobs checks.
- [ ] Add the economic-output check.
- [ ] Add the Michigan/Ohio recapture check.
- [ ] Add tax/fiscal checks.
- [ ] Add referendum-process checks.
- [ ] Add gaming-regulation checks.
- [ ] Add tribal-gaming checks.
- [ ] Include at least one clear `TRUE` rating in the initial visible set.
- [ ] Reconcile every published rating to the exact claim wording before release.

### Phase 7 — Evidence Views

- [ ] Add source tables.
- [ ] Add `Why we rated it this way` blocks.
- [ ] Add corrected wording.
- [ ] Add sources and capture/review dates.
- [ ] Add related fact checks.

### Phase 8 — Optional Full `/fact-check` Index

- [ ] Add `/fact-check` route.
- [ ] Add searchable/filterable full index.
- [ ] Add detail routes.
- [ ] Link homepage featured checks to detail routes.

### Phase 9 — QA / Release

- [ ] Run component/regression tests.
- [ ] Run responsive QA.
- [ ] Run accessibility QA.
- [ ] Run Lighthouse/performance comparison.
- [ ] Verify source links.
- [ ] Verify rating definitions are visible and consistent.
- [ ] Verify the archived legacy component can be restored without reconstructing it from Git history.

## Acceptance Criteria

- [ ] The current `Casino Proponents Critique` content is no longer displayed in production after the replacement ships.
- [ ] The `Casino Proponents Critique` section itself remains present in the homepage architecture.
- [ ] The `#proponents-critique` anchor remains functional.
- [ ] A verbatim/restorable copy of the pre-replacement section exists in an archive folder in the repository.
- [ ] The replacement uses a reusable four-state fact-check gauge rather than four hardcoded raster verdict images, unless implementation testing documents a reason to use a hybrid SVG approach.
- [ ] Verdict meaning is conveyed as real text and is accessible without color.
- [ ] Fact-check content is driven by structured data rather than being embedded as repeated markup in a single Razor component.
- [ ] The initial release contains both negative and positive/accurate findings where supported by the source material.
- [ ] Claims involving modeled outputs clearly distinguish forecasts from observed results.
- [ ] Each published claim includes enough source information for a reader to verify the correction.
- [ ] Ratings are tied to the exact quoted wording, not to a generalized interpretation of the campaign's position.
- [ ] Mobile, dark mode, keyboard navigation, reduced motion, and 200% zoom are tested.
- [ ] Existing homepage navigation and downstream sections continue to function.

## Rollback Procedure

- [ ] If the replacement is rejected after implementation, restore the active `ProponentsCritique.razor` from the archived legacy copy.
- [ ] Remove or disable only the new fact-check rendering path; do not destroy fact-check data/components during rollback unless explicitly requested.
- [ ] Re-run the homepage regression checks after restoration.
- [ ] Keep the fact-check implementation in source control for future reconsideration even if the legacy section is temporarily restored.
