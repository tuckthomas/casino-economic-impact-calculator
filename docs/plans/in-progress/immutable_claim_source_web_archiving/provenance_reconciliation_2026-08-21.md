# Claimant Source Provenance Reconciliation — 2026-08-21

## Status

Completed source-verification pass for the mutable claimant pages currently used by the SaveNEIN fact-check timeline. This document supplements `immutable_claim_source_web_archiving.md` and is part of the governing implementation handoff for issue #35 on branch `codex/consolidated-active-work`.

## Why this reconciliation was required

`SaveNEIN.Client/Pages/ProponentsCritique.razor` currently assigns:

- Yes for Allen claimant URL: `https://yesforallen.org/facts/`
- Yes for Allen `CapturedDate`: `August 8, 2026`
- Steuben claimant URL: `https://steubenfunwins.com/myths-and-facts/`
- Steuben `CapturedDate`: `August 21, 2026`

The existing implementation does not bind either date to a stored immutable snapshot. A historical date must not be represented as a capture date merely because that is when the fact-check content was drafted or reviewed.

## Historical archive search result

A public-web search was performed on August 21, 2026 for historical/Wayback-style captures of the Yes for Allen facts page around August 8, 2026 and of the Steuben Myths and Facts page around August 21, 2026.

### Result

No independently retrievable public historical snapshot of the Yes for Allen facts page dated August 8, 2026 was located during this pass.

This is **not** evidence that no such capture exists anywhere. It means SaveNEIN does not currently possess or have a verified URL for an August 8 snapshot and therefore must not claim that an immutable August 8 capture exists.

## Current-page verification performed August 21, 2026

Both claimant pages were independently fetched and reviewed on August 21, 2026. The challenged language remains present on the live pages as of that verification.

### Yes for Allen — live page verified 2026-08-21

URL: `https://yesforallen.org/facts/`

The page currently includes, among other language used by the fact-check section:

- `2,000 New Permanent Jobs`
- a statement that a recent study projects `over 2,000 new permanent jobs` and `approximately 5,500 construction jobs`
- the assertion that these are `jobs for Allen County residents`
- `Tax Relief for Residents`
- a statement that casino development would help `reduce the tax burden on residents over time`
- `more than $100 million in new revenue annually`
- `more than $41 million in new local revenue`
- `nearly $550 million in economic output annually by year three of operations`
- `Keep Dollars Local`
- language that a YES vote would allow the community to `bring that revenue back and put it to work here for Allen County residents`

Accordingly, **August 21, 2026 is the earliest independently re-verified date established by this reconciliation pass for the current Yes for Allen page content.**

### Steuben — Where Fun Wins — live page verified 2026-08-21

URL: `https://steubenfunwins.com/myths-and-facts/`

The page currently includes, among other language used by the fact-check section:

- `Casinos provide net decreases to tax payer liabilities`
- a statement that a casino is `mandated to pay portions of their profits directly back into the community`
- a statement that funds are directed to government entities and `in grants to local non-profits through Local Distribution Agreements`
- the page also states that a third-party impact study is `already in the works`

The existing August 21, 2026 verification date is therefore consistent with the date on which this content was independently reviewed. It still must not be described as an immutable archive capture until ArchiveBox stores the page and returns a durable snapshot identifier.

## Required correction to current semantics

### Yes for Allen

The existing `CapturedDate = "August 8, 2026"` is unsupported as an immutable web-capture date and must not survive the archive migration as though it were one.

Until a genuine August 8 historical snapshot is located and verified, implementation must use one of these two truthful states:

1. **Preferred after ArchiveBox capture:** archive the current page and bind the resulting archive record to a capture timestamp of August 21, 2026 (or the exact later timestamp when ArchiveBox actually performs the first capture).
2. **Before ArchiveBox is operational:** treat August 21, 2026 as a `verified/reviewed` date, not an archive `captured` date.

If a genuine historical snapshot is later found, it may be added as an earlier archive record only after verifying that the exact relevant claim text appears in that stored representation.

### Steuben

The August 21, 2026 date may remain as a verification date. Once ArchiveBox is operational, replace the live claimant-source evidence link with the ArchiveBox snapshot captured on the actual archive timestamp.

## Implementation rule for issue #35

The fact-check UI must not display a date labeled or semantically treated as `captured` unless the corresponding fact-check record contains a durable archive snapshot identifier and the application can resolve that identifier to preserved artifacts.

Recommended model split:

- `ClaimSourceOriginalUrl`
- `ClaimSourceArchiveId` / immutable SaveNEIN archive record ID
- `ClaimSourceArchivedUrl`
- `ClaimSourceCapturedAtUtc`
- `ClaimSourceVerifiedAtUtc`

`ClaimSourceCapturedAtUtc` must be nullable. The UI may display `Verified August 21, 2026` while archival backfill is pending. It may display `Archived [timestamp]` only after a durable archive exists.

## ArchiveBox first-capture requirements

When the ArchiveBox Docker service and backend client from the parent plan are implemented:

1. Immediately archive `https://yesforallen.org/facts/`.
2. Immediately archive `https://steubenfunwins.com/myths-and-facts/`.
3. Preserve at minimum original response material, rendered DOM, SingleFile, screenshot, and WARC when available.
4. Persist ArchiveBox snapshot ID, original URL, exact capture timestamp, retrieval status, artifact hashes, and extracted normalized text in SaveNEIN's archive manifest.
5. Verify each fact-check quote/framing against the captured normalized text before changing the frontend link from live source to archived source.
6. Keep the original live URL available only as secondary context, clearly labeled as the current claimant page.
7. Never backdate the ArchiveBox record to August 8, 2026.

## Acceptance decision from this pass

- **August 8, 2026 immutable Yes for Allen capture:** NOT VERIFIED.
- **Yes for Allen challenged content present on August 21, 2026 live page:** VERIFIED.
- **Steuben challenged content present on August 21, 2026 live page:** VERIFIED.
- **Safe provenance baseline for first self-hosted archive capture:** August 21, 2026 or the exact later timestamp at which ArchiveBox actually performs the capture.
- **Backdating any newly created snapshot:** PROHIBITED.
