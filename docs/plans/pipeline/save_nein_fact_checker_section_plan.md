# SaveNEIN Fact Checker / Casino Proponents Critique Replacement Plan

Status: Pipeline

## Objective

- [ ] Replace the **current visible content** inside the existing `Casino Proponents Critique` section with a source-driven SaveNEIN Fact Checker experience.
- [ ] **Do not delete the `Casino Proponents Critique` section, component, anchor, or navigation target.** Preserve the existing section shell so current links to `#proponents-critique` continue to work.
- [ ] Preserve the current `ProponentsCritique.razor` implementation in an archive folder before replacing its visible contents, so the existing version can be restored quickly if the new design is rejected or needs to be rolled back.
- [ ] Keep the current production page stable while the replacement is being built; archive first, then refactor.
- [ ] Use the August 2026 `Yes for Allen Counter-Analysis and Fact Check` research as the initial content basis for the first fact checks.
- [ ] Build the fact checker as a reusable system rather than a one-off Yes for Allen page so future claims from casino campaigns, officials, developers, consultants, studies, advertisements, mailers, or media coverage can be added without redesigning the feature.
- [ ] Use the supplied **vertical timeline mockup as the layout/information-hierarchy reference only**.
- [ ] **Do not copy the supplied mockup HTML, Tailwind configuration, colors, spacing, typography, or component implementation.** Recreate the concept using the existing SaveNEIN design tokens, shared components, typography, spacing system, dark/light behavior, and accessibility conventions already present in the application.

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

## Timeline Layout Direction

- [ ] Replace the existing four-card `Casino Proponents Critique` presentation with a **vertical fact-check timeline** inspired by the supplied mockup.
- [ ] Treat the mockup as a layout reference, not a visual-style specification.
- [ ] Keep the section integrated into the existing SaveNEIN homepage rather than adding the mockup's standalone mobile app bar or bottom navigation.
- [ ] Preserve the existing SaveNEIN page shell, global header/navigation, background system, typography, section widths, responsive breakpoints, buttons, borders, shadows, and design tokens.
- [ ] Do not import the mockup's Tailwind CDN configuration, `Public Sans`, hard-coded palette, spacing scale, bottom tab bar, top app bar, or external image URLs.
- [ ] Use a centered vertical timeline spine on desktop.
- [ ] Alternate fact-check entries left/right of the spine on desktop to reduce repetitive scanning and create visual rhythm.
- [ ] Use the verdict meter as the timeline marker positioned on the central spine.
- [ ] On mobile, move the timeline spine to the left edge of the content and stack every claim/evidence block to the right of it.
- [ ] Keep the mobile marker compact enough that the meter remains identifiable without consuming excessive horizontal space.
- [ ] Ensure the timeline remains usable with long claim titles, long source names, multiple issue tags, and more than four fact checks.
- [ ] Do not call the page/section `Timeline of Deception`; retain neutral SaveNEIN fact-check naming consistent with the research-oriented editorial standard.
- [ ] Use a concise section intro that communicates that claims are being checked against primary studies, statutes, government sources, and empirical evidence.

## Timeline Entry Information Hierarchy

- [ ] Each timeline item should present information in approximately this order:
  - [ ] category/topic eyebrow, e.g. `ECONOMIC`, `TAX`, `TRIBAL`, `REFERENDUM`, `PUBLIC SAFETY`;
  - [ ] claimant label, e.g. `YES FOR ALLEN CLAIM`;
  - [ ] exact quoted claim in large high-contrast text;
  - [ ] primary verdict represented by the circular meter marker;
  - [ ] secondary issue tag(s);
  - [ ] concise `THE EVIDENCE` summary card;
  - [ ] source attribution / page reference;
  - [ ] control or link to expand into the full evidence trail.
- [ ] Keep the evidence summary concise enough for timeline scanning; move detailed methodology, tables, legal analysis, and corrected wording into an expandable detail panel/modal or linked fact-check detail view.
- [ ] Allow the timeline summary card to show one or two decisive quantitative corrections where useful, e.g. `2,001 statewide` versus `1,676 Allen County`.
- [ ] Use real HTML text for the claim, verdict, issue tags, evidence, and sources; do not bake text into images.
- [ ] Ensure source controls are visibly interactive and provide an accessible label describing whether they open a primary source, evidence detail, or citation view.

## Circular CSS / HTML Fact-Check Meter

- [ ] **Do not use the current square/rectangular mock meter shape in the new timeline.** Redesign it as a compact circular instrument suitable for use directly on the timeline spine.
- [ ] Build one reusable circular meter component for all four verdicts rather than maintaining separate raster assets.
- [ ] Prefer HTML/CSS for the meter, using existing SaveNEIN variables/tokens wherever practical.
- [ ] SVG may be used selectively for geometry that would otherwise make the CSS implementation unnecessarily complex, but the component must remain reusable and verdict-driven rather than four separate static images.
- [ ] Do not use the uploaded/mockup PNG meters as production timeline assets.
- [ ] Preserve the recognizable visual language of the original meter concept:
  - [ ] dark navy housing;
  - [ ] red-to-neutral-to-navy/positive scale;
  - [ ] central needle/pivot;
  - [ ] compact verdict text or abbreviation where legible;
  - [ ] verdict-specific indicator/glow treatment.
- [ ] Adapt that language to a **true circular dial** rather than clipping the existing square graphic into a circle.
- [ ] The circle should look intentionally designed as a round gauge, not like a square asset masked by `border-radius: 50%`.
- [ ] Use a radial/conic construction where appropriate:
  - [ ] `conic-gradient()` for verdict zones or outer scale;
  - [ ] `radial-gradient()` for dial face, hub, depth, and indicator lighting;
  - [ ] pseudo-elements for bezel/ticks;
  - [ ] one transformed needle element driven by a verdict variable.
- [ ] Represent verdict state through component data/CSS variables, e.g.:
  - [ ] `--needle-angle`;
  - [ ] `--verdict-color`;
  - [ ] `--verdict-label` where useful;
  - [ ] optional glow intensity.
- [ ] Keep the needle animation optional and restrained.
- [ ] If animated, animate only from a neutral/rest position to the verdict angle and honor `prefers-reduced-motion`.
- [ ] Favor `transform`/`opacity` animation rather than layout-affecting animation.
- [ ] Do not add continuous movement, blinking, pulsing, casino-like flashing, or attention-demanding motion.
- [ ] Provide an accessible text equivalent outside/inside the visual component so screen readers do not need to infer verdict from needle position or color.
- [ ] Ensure color is never the sole indicator of the verdict.
- [ ] Design at least two size modes:
  - [ ] compact timeline marker for mobile and dense views;
  - [ ] larger desktop/detail version.
- [ ] Verify the circular meter remains crisp at high-DPI/zoom levels with no raster scaling artifacts.

## Responsive Timeline Behavior

- [ ] Desktop/tablet wide layout:
  - [ ] center the timeline spine within the section content area;
  - [ ] alternate entries left/right;
  - [ ] align each circular meter marker to its corresponding entry rather than arbitrarily centering it vertically against an oversized card;
  - [ ] keep claim text and evidence card visually associated as one entry;
  - [ ] avoid excessive dead space between alternating sides.
- [ ] Mobile layout:
  - [ ] place the timeline spine near the left content gutter;
  - [ ] place all timeline content on the right side of the spine;
  - [ ] use the circular meter at each timeline node;
  - [ ] preserve readable claim width at narrow viewport sizes;
  - [ ] avoid forcing badge/tag text into extremely narrow columns;
  - [ ] stack source metadata when necessary rather than shrinking it below accessible reading size.
- [ ] Ensure the timeline line begins and ends cleanly at the first/last marker rather than extending arbitrarily beyond the content.
- [ ] Test 320px-class phones, typical modern mobile widths, tablet widths, and wide desktop layouts.

## Initial Fact Checks

- [ ] Seed the first version from the Yes for Allen counter-analysis rather than inventing new claims.
- [ ] Include at minimum the following initial checks, subject to final exact-wording validation against the captured campaign source:
  - [ ] `2,000 New Permanent Jobs` — likely `MOSTLY FALSE`; issue tags should reflect statewide-vs-Allen County geography and job-definition context.
  - [ ] jobs `for Allen County residents` — likely `FALSE` / `UNSUPPORTED`; source tables do not establish worker residence.
  - [ ] `Approximately 5,500 Construction Jobs` — likely `MOSTLY FALSE`; statewide figure versus Allen County figure.
  - [ ] `Nearly $550 Million in Annual Economic Output` — likely `MOSTLY FALSE`; statewide gross output, not Allen County/net economic gain.
  - [ ] `Keep Dollars Local` — likely `MOSTLY FALSE` or another restrained rating based on exact wording; some cross-border recapture exists, but it is not most of the forecast.
  - [ ] `More Than $100 Million in New Annual Revenue` — primary verdict plus `PROJECTION`/`OUTDATED` tags as warranted by exact campaign wording and the enacted 2026 tax structure.
  - [ ] `More Than $41 Million Stays Local` — rating based on the broader governing-bodies/stakeholders composition of the cited number.
  - [ ] `Tax Relief for Residents` — distinguish a statutory option from a guaranteed outcome; likely use `POLICY PROMISE` and/or `MISSING CONTEXT` depending on exact phrasing.
  - [ ] `A YES Vote Does Not Itself Approve a Casino` — `TRUE` or `MOSTLY TRUE` depending on exact full wording.
  - [ ] `Indiana Casinos Are Regulated by the Indiana Gaming Commission` — `TRUE` for the core statement.
  - [ ] `One of the Most Highly Regulated Gaming Industries in the Country` — likely `UNSUPPORTED` / `MOSTLY FALSE` because the comparative superlative is not demonstrated.
  - [ ] `A NO Vote Gives the Casino to DeKalb or Steuben` — likely `MOSTLY FALSE`; multiple contingencies remain.
  - [ ] `A Tribal Casino Would Not Require State or Local Approval` — likely `MOSTLY FALSE`; federal pathways and Class III compact requirements are more complex.
  - [ ] `Tribal Gaming Revenue Cannot Benefit Local Government` or equivalent exact campaign wording — likely `MOSTLY FALSE`; distinguish Indiana commercial tax distribution from permissible tribal/local-government funding arrangements.
  - [ ] `Tribal Casino Inevitable` / equivalent urgency claim — rating must be tied to exact quoted wording; the public record does not establish an announced or approved Miami Tribe Fort Wayne casino project.
- [ ] Do not publish a paraphrased claim as though it were a direct quote; exact claim text must be verified before quotation marks are used.
- [ ] Keep the data model flexible enough to reorder, add, remove, or re-rate checks without redesigning the timeline.

## Evidence Presentation

- [ ] Make `THE EVIDENCE` a consistent visual subcomponent within each timeline entry.
- [ ] Use a compact card treatment derived from existing SaveNEIN card/border tokens rather than the mockup's hard-coded styles.
- [ ] Show the strongest corrective fact first.
- [ ] Use short comparisons where possible rather than paragraphs of prose.
- [ ] Support accessible HTML tables for source data when the full evidence view is opened.
- [ ] Reproduce critical source values directly from the underlying report/table and label geography and metric explicitly.
- [ ] Where possible, provide a direct source link and page/table reference.
- [ ] Distinguish:
  - [ ] observed data;
  - [ ] model output;
  - [ ] legal/statutory requirements;
  - [ ] campaign promise/policy preference;
  - [ ] analyst interpretation.
- [ ] Add a compact `Corrected Version` or `More Accurate Wording` field in the detailed evidence view when a claim is partly rooted in a real source but overstated.

## Structured Data / Component Architecture

- [ ] Do not hardcode all fact checks as repeated bespoke markup in `ProponentsCritique.razor`.
- [ ] Create a reusable fact-check model/data structure capable of supporting at least:
  - [ ] `Id`;
  - [ ] `Slug`;
  - [ ] `Claimant`;
  - [ ] `ClaimText`;
  - [ ] `ClaimSourceUrl`;
  - [ ] `ClaimCapturedDate`;
  - [ ] `Category`;
  - [ ] `Verdict`;
  - [ ] `IssueTags`;
  - [ ] `ShortFinding` / timeline evidence summary;
  - [ ] `DetailedExplanation`;
  - [ ] `CorrectedClaim`;
  - [ ] `KeyFacts`;
  - [ ] `Sources`;
  - [ ] `EvidenceTables` or structured evidence blocks;
  - [ ] `FirstPublished`;
  - [ ] `LastReviewed`;
  - [ ] optional revision history.
- [ ] Create reusable UI components rather than mixing all logic into the page, with likely responsibilities such as:
  - [ ] timeline container;
  - [ ] timeline entry;
  - [ ] circular verdict meter;
  - [ ] issue-tag group;
  - [ ] evidence summary card;
  - [ ] detailed evidence view/modal;
  - [ ] methodology/legend.
- [ ] Reuse existing SaveNEIN shared components for buttons/tooltips/modals where suitable rather than recreating parallel UI primitives.
- [ ] Keep fact-check content separate from presentation so future checks can be added through data changes rather than markup duplication.

## Section-Level User Experience

- [ ] Keep the section title/navigation identity compatible with the existing `Casino Proponents Critique` anchor while presenting the user-facing feature as a SaveNEIN `FACT CHECK` / `FACT CHECKER` experience.
- [ ] Add a compact explanation of the rating methodology near the beginning or behind a clearly labeled methodology control.
- [ ] Consider a category filter only if the initial number of checks makes it useful; do not add filter UI merely for decoration.
- [ ] If filtering is added, support categories such as:
  - [ ] `ECONOMIC`;
  - [ ] `JOBS`;
  - [ ] `TAX`;
  - [ ] `TRIBAL`;
  - [ ] `PUBLIC SAFETY`;
  - [ ] `REFERENDUM PROCESS`.
- [ ] Preserve chronological/curated timeline ordering independently from category filtering.
- [ ] Make the fact checker scannable in the collapsed/default state and deep enough for source auditing in the expanded state.

## Accessibility

- [ ] Maintain semantic heading order within the existing homepage.
- [ ] Use semantic list/article structure for the timeline rather than relying only on positioned `<div>` elements.
- [ ] Expose each verdict as real text to assistive technology.
- [ ] Ensure the circular gauge is `aria-hidden` when the same verdict is already exposed as adjacent text, or otherwise provide an explicit accessible label.
- [ ] Ensure issue tags have sufficient contrast and are not differentiated only by color.
- [ ] Ensure source/detail controls are keyboard reachable and have visible focus states consistent with existing SaveNEIN components.
- [ ] Ensure all expandable evidence content is operable by keyboard and communicates expanded/collapsed state.
- [ ] Honor `prefers-reduced-motion` for any needle or reveal animation.
- [ ] Verify the timeline does not produce an incoherent reading order when desktop entries alternate left/right visually.

## Performance

- [ ] Avoid loading four separate high-resolution verdict images for every fact-check entry.
- [ ] Prefer one CSS/HTML or hybrid SVG/CSS circular gauge component whose state is controlled by verdict data.
- [ ] Avoid expensive continuous filter/blur animations.
- [ ] Use decorative shadows/glows sparingly, especially around multiple timeline markers on mobile.
- [ ] Prevent off-screen detail content from loading heavy assets unnecessarily.
- [ ] Keep the default timeline content primarily text/CSS/HTML so it remains fast on mobile connections.
- [ ] Confirm no external mockup/CDN dependencies are introduced.

## Testing / Validation

- [ ] Add component/unit coverage for verdict-to-meter configuration where practical.
- [ ] Verify each verdict maps to the correct:
  - [ ] label;
  - [ ] needle angle;
  - [ ] verdict styling;
  - [ ] accessibility text.
- [ ] Verify all initial claims have:
  - [ ] exact claim text;
  - [ ] claimant;
  - [ ] category;
  - [ ] verdict;
  - [ ] issue tag(s) where applicable;
  - [ ] evidence summary;
  - [ ] source citation/link.
- [ ] Verify `#proponents-critique` navigation still lands at the replacement fact-check section.
- [ ] Verify existing global SaveNEIN navigation remains unchanged unless explicitly required by implementation.
- [ ] Verify mobile timeline marker/content alignment at narrow widths.
- [ ] Verify desktop alternating layout has correct source order in the DOM.
- [ ] Verify the timeline remains visually coherent with an odd or even number of entries.
- [ ] Verify long claims and long source labels wrap without overlapping the circular meter or timeline spine.
- [ ] Run existing client/server test suites and build validation after implementation.

## Acceptance Criteria

- [ ] The existing `Casino Proponents Critique` source is archived before replacement.
- [ ] The live `#proponents-critique` section remains present and reachable from existing navigation.
- [ ] The visible old pitch/reality four-card content is replaced by the new fact-check timeline.
- [ ] The supplied timeline mockup's **layout concept** is recognizable in the new implementation, but its HTML, standalone app chrome, fonts, colors, and custom Tailwind tokens are not copied into SaveNEIN.
- [ ] Desktop uses a centered timeline with alternating entries.
- [ ] Mobile uses a left-side timeline with stacked entries.
- [ ] Each timeline node uses a deliberately designed **circular** fact-check meter, not a square image clipped into a circle.
- [ ] The meter is reusable across `TRUE`, `MOSTLY TRUE`, `MOSTLY FALSE`, and `FALSE` states.
- [ ] Primary verdict and secondary issue classification remain separate concepts.
- [ ] Initial ratings are tied to exact quoted claims and documented evidence.
- [ ] Users can see a concise evidence correction without opening detail content.
- [ ] Users can access deeper evidence/source information from each timeline item.
- [ ] True/mostly true findings are included where supported.
- [ ] The new section conforms to existing SaveNEIN design tokens and shared UI conventions.
- [ ] No external mockup image URLs, Tailwind CDN config, or mockup-specific app navigation are added.
- [ ] The replacement passes responsive, accessibility, build, and regression checks.
- [ ] A documented rollback path exists to restore the archived original section.

## Rollback Procedure

- [ ] If the replacement is rejected or needs to be withdrawn, restore the archived `ProponentsCritique` markup to `SaveNEIN.Client/Pages/ProponentsCritique.razor`.
- [ ] Retain any reusable fact-check data/components in an inactive branch or archive only if explicitly desired; do not leave a partially rendered replacement in production.
- [ ] Re-run the client build/tests after restoration.
- [ ] Verify the `#proponents-critique` anchor and existing navigation still function after rollback.
