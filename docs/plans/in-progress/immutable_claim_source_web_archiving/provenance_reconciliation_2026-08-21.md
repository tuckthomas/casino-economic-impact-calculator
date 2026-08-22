# Claimant Source Provenance Reconciliation — 2026-08-21

## Status

Corrected provenance analysis for the mutable claimant pages used by the SaveNEIN fact-check timeline. This document supplements `immutable_claim_source_web_archiving.md` and is part of the governing implementation handoff for issue #35 on branch `codex/consolidated-active-work`.

## Critical distinction

The August 8, 2026 Yes for Allen counter-analysis report was generated from content scraped from the claimant website. That is useful **derivative evidence of what the report-generation process extracted**, but it is **not an archived copy of the website**.

Do not treat those as equivalent.

A report containing scraped text cannot establish the complete HTML/DOM/JavaScript/resource state of the claimant page on August 8. It also cannot prove that the page was unchanged between August 8 and a later verification date. If the claimant edited or removed language after August 8 and before SaveNEIN began preserving immutable snapshots, SaveNEIN cannot reconstruct that historical page state merely from the report.

Accordingly:

- `August 8, 2026` may be recorded as the date of the **report scrape / source observation used by the analysis**.
- `August 8, 2026` must **not** be represented as an archive capture date unless an actual independently preserved August 8 web artifact is located.
- The existing report must not be used to manufacture or backdate an ArchiveBox snapshot.
- Any self-hosted ArchiveBox snapshot receives the exact timestamp at which ArchiveBox actually captures the page.

## Current implementation problem

`SaveNEIN.Client/Pages/ProponentsCritique.razor` currently assigns:

- Yes for Allen claimant URL: `https://yesforallen.org/facts/`
- Yes for Allen `CapturedDate`: `August 8, 2026`
- Steuben claimant URL: `https://steubenfunwins.com/myths-and-facts/`
- Steuben `CapturedDate`: `August 21, 2026`

The fact-check source renderer still opens the live claimant page. Therefore the displayed historical date is not bound to a preserved historical web artifact.

The current Yes for Allen fact-check content was seeded from `docs/plans/pipeline/save_nein_fact_checker/Yes_for_Allen_Counter_Analysis_Fact_Check.docx`. That report's August 8 scrape establishes the provenance of the analytical input, but not an immutable August 8 website snapshot.

## Historical archive search result

A public-web search was performed on August 21, 2026 for historical/Wayback-style captures of the Yes for Allen facts page around August 8, 2026.

No independently retrievable August 8 web snapshot was located during this pass.

This is not evidence that no snapshot exists anywhere. It means SaveNEIN currently does not possess a verified August 8 archived-site artifact that can be linked to readers as the page state used by the report.

## Current-page verification performed August 21, 2026

Both claimant pages were independently fetched and reviewed on August 21, 2026. Relevant challenged language remains present on the live pages as of that verification.

### Yes for Allen — live page verified 2026-08-21

URL: `https://yesforallen.org/facts/`

The page still includes language corresponding to fact checks such as:

- `2,000 New Permanent Jobs`
- `over 2,000 new permanent jobs`
- `approximately 5,500 construction jobs`
- `jobs for Allen County residents`
- `Tax Relief for Residents`
- `reduce the tax burden on residents over time`
- `more than $100 million in new revenue annually`
- `more than $41 million in new local revenue`
- `nearly $550 million in economic output annually by year three of operations`
- `Keep Dollars Local`

This verifies the live page on August 21. It does **not** retroactively prove that every element of the page was identical on August 8.

### Steuben — Where Fun Wins — live page verified 2026-08-21

URL: `https://steubenfunwins.com/myths-and-facts/`

The page still includes language corresponding to the current Steuben fact checks, including the taxpayer-liability claim, mandated community-payment framing, Local Distribution Agreement/nonprofit language, and statement that a third-party impact study is in progress.

Again, this is a live-page verification, not an immutable archive record until ArchiveBox captures it.

## Evidence taxonomy required by the implementation

The backend and UI must distinguish at least these concepts:

1. **Original URL** — where the claimant publishes the current page.
2. **Report scrape / source observation** — text or data extracted by a research process on a stated date. This is derivative evidence and may be incomplete.
3. **Live-page verification** — a later review confirming what the page currently says.
4. **Immutable web capture** — preserved page artifacts such as WARC/WACZ, original response files, rendered DOM/HTML, SingleFile, screenshot, resources, timestamp, and hashes.

Only item 4 may be presented to readers as an archived-site snapshot.

Recommended model fields:

- `ClaimSourceOriginalUrl`
- `ClaimSourceObservedAtUtc` — nullable; may represent the August 8 report scrape
- `ClaimSourceObservationType` — e.g. `ReportScrape`, `ManualVerification`, `ArchiveCapture`
- `ClaimSourceArchiveId` — nullable until a durable archive exists
- `ClaimSourceArchivedUrl` — nullable until a durable archive exists
- `ClaimSourceCapturedAtUtc` — nullable and reserved for actual archive capture
- `ClaimSourceVerifiedAtUtc` — nullable for live-page verification

Do not overload one `ClaimCapturedDate` field to represent all of these events.

## August 8 report treatment

The August 8 report remains legitimate research provenance and should be retained. It can support statements such as:

> The fact-check analysis was generated from claimant-page content scraped on August 8, 2026.

It must not support statements such as:

> View the claimant page as archived on August 8, 2026.

unless an actual August 8 archived-site artifact is independently located.

If the claimant page changed after August 8, the report may preserve some extracted wording, but SaveNEIN is otherwise **out of luck for reconstructing the full historical website state** unless another archive captured it at the time.

## ArchiveBox first-capture requirements

When the ArchiveBox Docker service and backend client from the parent plan are implemented:

1. Immediately archive `https://yesforallen.org/facts/`.
2. Immediately archive `https://steubenfunwins.com/myths-and-facts/`.
3. Preserve at minimum original response material, rendered DOM, SingleFile, screenshot, and WARC when available.
4. Persist ArchiveBox snapshot ID, original URL, exact capture timestamp, retrieval status, artifact hashes, and extracted normalized text in SaveNEIN's archive manifest.
5. Verify each fact-check quote/framing against the captured normalized text before changing the frontend claimant-source link from the live page to the archived source.
6. Keep the original live URL available only as secondary/current context.
7. Never backdate ArchiveBox to August 8, 2026.
8. Preserve the August 8 report scrape separately as research provenance; do not merge its observation date into ArchiveBox metadata.

## Acceptable evidence for a genuine August 8 archived-site claim

An August 8 archive date may be used only if SaveNEIN later obtains a genuine artifact tied to that date, such as:

- a Wayback or other third-party web archive capture;
- WARC/WACZ generated at the time;
- an original saved HTTP response/resource set with trustworthy timestamp/provenance;
- a browser-preserved HTML/SingleFile/DOM capture with sufficient provenance and integrity evidence.

The derivative fact-check report by itself does not satisfy this requirement.

## Acceptance decision from this pass

- **August 8, 2026 report scrape/source observation:** KNOWN and usable as research provenance.
- **August 8, 2026 immutable Yes for Allen website capture:** NOT VERIFIED.
- **August 21, 2026 Yes for Allen live-page verification:** VERIFIED.
- **August 21, 2026 Steuben live-page verification:** VERIFIED.
- **Earliest SaveNEIN self-hosted immutable capture:** the actual timestamp when ArchiveBox first captures each page.
- **Backdating a newly created archive snapshot:** PROHIBITED.
