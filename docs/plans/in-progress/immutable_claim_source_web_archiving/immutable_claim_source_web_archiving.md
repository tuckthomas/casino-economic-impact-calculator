# Immutable Claim-Source Web Archiving for Fact Checks

## Governing AI Agent Implementation Plan

> **Status:** Implemented and validated in the working branch on 2026-08-21. Durable production captures were sealed and entered into the archive manifest at their truthful August 22 UTC capture times. Branch merge/deployment remains a separate release action.
>
> **Primary objective:** Make every mutable claimant-page citation in the SaveNEIN fact-check section independently reproducible and resistant to later edits or deletion by the claimant. SaveNEIN must preserve the exact web evidence it relied on, record when that evidence was captured, and link fact-check readers to the preserved snapshot rather than presenting a live campaign page as if it were immutable evidence.
>
> **Initial scope:** The Yes for Allen claimant page (`https://yesforallen.org/facts/`) and Steuben — Where Fun Wins claimant page (`https://steubenfunwins.com/myths-and-facts/`) currently referenced by `SaveNEIN.Client/Pages/ProponentsCritique.razor`. The architecture must be reusable for additional mutable web sources later.
>
> **Archival platform decision:** Use **ArchiveBox** as the primary self-hosted archival service. ArchiveBox is open source, supports Docker deployment and a REST API, and preserves redundant forms of a page including original HTML/CSS/JavaScript, Chromium-rendered DOM output, SingleFile HTML, screenshots/PDF, response metadata, and WARC output. Do not build a bespoke downloader and do not treat a screenshot alone as sufficient evidence.

---

## 1. Problem Statement

The current fact-check implementation has a provenance gap.

`SaveNEIN.Client/Pages/ProponentsCritique.razor` currently hardcodes:

- `ClaimSourceUrl = "https://yesforallen.org/facts/"`
- `SteubenClaimSourceUrl = "https://steubenfunwins.com/myths-and-facts/"`
- `CapturedDate = "August 8, 2026"`
- `SteubenCapturedDate = "August 21, 2026"`

The UI then sends readers directly to those live claimant URLs through `FactCheckTimelineItem.razor`.

That means the displayed capture date and the linked evidence are not actually bound together. A campaign webmaster can edit or remove a sentence after SaveNEIN publishes a fact check. The SaveNEIN page could continue displaying a historical quote and historical capture date while its citation opens a materially different current page. That creates an avoidable evidentiary and reputational weakness.

The August 8 Yes for Allen counter-analysis report was generated from content scraped from the campaign site. That scrape is valid **research provenance**, but it is not an archived website snapshot. It does not preserve or prove the complete historical HTML/DOM/JavaScript/resource state and cannot establish that the claimant page remained unchanged between August 8 and any later verification date. Even if the same language is present now, the page could have changed in the interim and later changed back. If no genuine archive captured the relevant historical state, that state cannot be reconstructed from the derivative report alone.

The implementation must create an immutable evidence chain:

**fact-check claim -> SaveNEIN archive record -> ArchiveBox snapshot -> preserved artifacts + capture timestamp + hashes -> original URL**

The live claimant page may remain available as secondary context, but it must not be the primary evidentiary hyperlink for a claim that depends on what the page said at a specific time.

---

## 2. Non-Negotiable Evidence Rules

1. **Never represent a live URL as a dated snapshot.** A `ClaimCapturedDate` is valid only if there is a corresponding preserved artifact captured at that time.
2. **A scraped research report is not a web archive.** A report may document text extracted from a site on a particular date, but it must not be presented as an archived copy of the claimant website or used to backdate a later snapshot.
3. **Never rewrite or replace a snapshot already referenced by a published fact check.** A later recapture creates a new snapshot and a new archive record.
4. **The capture timestamp comes from the backend archive record.** Do not maintain a hand-entered date string in the Razor fact-check data once this feature is implemented.
5. **The original URL must always be retained as provenance.** Archiving changes the primary evidence link; it does not erase the source URL.
6. **The exact quoted/framed claim must be validated against the captured page before a snapshot is marked usable for publication.** For direct quotes, normalized captured text must contain the quoted wording. For paraphrased framing, the record must retain the supporting excerpt or verification note.
7. **Use multiple archival representations.** WARC plus rendered HTML/DOM plus screenshot provides substantially stronger evidence than any one representation by itself.
8. **Capture at publication/review time, not later when convenient.** The preferred workflow is to archive the claimant page before or as part of publishing/reviewing a fact check.
9. **Do not falsify historical capture dates during backfill.** A snapshot created on August 21 or later cannot be labeled as an August 8 capture merely because a report scraped the page on August 8.
10. **Track observation and capture separately.** An August 8 report-scrape date may be retained as `ObservedAt`/research provenance while `CapturedAt` remains null until an actual archive exists.

---

## 3. Evidence Taxonomy

The implementation must distinguish four evidentiary events:

### Original URL
The current claimant-controlled URL. Mutable and unsuitable by itself as historical evidence.

### Report scrape / source observation
Text or data extracted by a research process at a stated time. The August 8 Yes for Allen report belongs here. It can establish the provenance of the analytical input, but it may be incomplete and does not reproduce the historical website.

### Live-page verification
A later review confirming what the claimant page says at that later moment. This does not retroactively prove the page was unchanged at an earlier time or continuously contained the same language.

### Immutable archive capture
Preserved website evidence carrying its own capture timestamp and durable artifacts such as original response material, WARC/WACZ, rendered DOM/HTML, SingleFile, screenshot, resource files, metadata, and hashes. Only this category may be linked/labeled as an archived-site snapshot.

Suggested fields:

- `ClaimSourceOriginalUrl`
- `ClaimSourceObservedAtUtc`
- `ClaimSourceObservationType`
- `ClaimSourceArchiveId`
- `ClaimSourceArchivedUrl`
- `ClaimSourceCapturedAtUtc`
- `ClaimSourceVerifiedAtUtc`

`ClaimSourceCapturedAtUtc` and archive identifiers must be nullable until a real capture exists.

---

## 4. Why ArchiveBox

### Selected platform: ArchiveBox

Use the official ArchiveBox Docker image and pin an explicit tested release or image digest. Do not use an unpinned `latest` tag for the evidence service in production.

ArchiveBox is preferred here because it provides all of the following in one self-hostable component:

- persistent Docker service
- web UI for manual inspection
- REST API for the SaveNEIN backend
- original HTML/CSS/JavaScript preservation
- Chromium-rendered DOM capture
- SingleFile HTML
- screenshot and PDF capture
- WARC output
- response headers and metadata
- filesystem-backed artifacts that remain readable outside ArchiveBox

Reference documentation during implementation:

- `https://github.com/ArchiveBox/ArchiveBox`
- `https://github.com/ArchiveBox/ArchiveBox/wiki/Docker`
- `https://github.com/ArchiveBox/ArchiveBox/wiki/Setting-up-Authentication`
- ArchiveBox API docs exposed by the deployed instance at `/api/v1/docs`

### Browsertrix/Webrecorder

Browsertrix Crawler is a valid alternative for specialized browser-based high-fidelity WACZ capture and QA replay, and may be added later if a site defeats ArchiveBox or exact interactive replay becomes a requirement. It is not the first implementation because its core crawler is job-oriented rather than the persistent service/API integration needed here.

Do not introduce Browsertrix in Phase 1 unless an actual target page cannot be preserved adequately by ArchiveBox.

---

## 5. Docker Architecture

### Existing state

Production uses `deploy/compose.production.yml`, which provides the app,
PostGIS, Valhalla, Nginx, and ArchiveBox on an isolated Docker network.
`compose.development.yml` only runs the app and reaches the VPS development
database and Valhalla instance through private SSH tunnels. It must not start a
second local archive, routing, or database stack.

### Add `savenein-archivebox`

ArchiveBox is operated by the production Compose stack; the development app
uses the VPS-hosted archive service when it is enabled. Its persistent volume,
private API configuration, and reverse-proxy access are defined only in
`deploy/compose.production.yml`.

### Port choice

Use the internal Docker service address for the application-to-ArchiveBox API;
do not publish ArchiveBox directly from the development stack.

### Initialization

Document the one-time initialization/admin workflow in the repository README or an operations document. Use the official ArchiveBox initialization flow for the pinned version, e.g. conceptually:

```bash
docker compose run --rm savenein-archivebox init
```

If the pinned image requires a different exact command, follow its version-specific documentation.

### Credentials and API token

Do not commit ArchiveBox credentials or API tokens.

The backend must authenticate to ArchiveBox with a bearer API token stored in deployment configuration/secrets. ArchiveBox's anonymous add/import capability must remain disabled.

### Public read-only access

Separate the internal service address from the URL shown to readers:

- **Internal API URL:** e.g. `http://127.0.0.1:8001`
- **Public archive URL:** e.g. `https://archive.savenein.com`

Do not emit `127.0.0.1`, Docker-only hostnames, or internal ports into browser-facing links.

Production deployment should expose the ArchiveBox read-only interface through the existing reverse proxy or a dedicated archive subdomain. Admin/API mutation endpoints must not become anonymously writable merely because snapshots are public.

### Optional read-only app mount

If artifact-level checksum validation cannot be obtained cleanly through the ArchiveBox API, mount `archivebox_data` into `savenein-app` read-only at a dedicated path such as:

```yaml
- archivebox_data:/var/lib/savenein/archivebox:ro
```

Use this only for verification/hashing. The SaveNEIN application must never mutate ArchiveBox's SQLite database or archive files directly.

---

## 6. Server Configuration

Add strongly typed configuration, for example:

`SaveNEIN.Server/Configuration/ArchiveBoxOptions.cs`

Recommended fields:

```csharp
public sealed class ArchiveBoxOptions
{
    public string InternalBaseUrl { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string ApiToken { get; init; } = string.Empty;
    public string? DataPath { get; init; }
    public string[] AllowedSourceHosts { get; init; } = Array.Empty<string>();
    public TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
```

Expected environment bindings should include equivalents of:

- `ArchiveBox__InternalBaseUrl=http://127.0.0.1:8001`
- `ArchiveBox__PublicBaseUrl=https://archive.savenein.com`
- `ArchiveBox__ApiToken=<secret>`
- `ArchiveBox__AllowedSourceHosts__0=yesforallen.org`
- `ArchiveBox__AllowedSourceHosts__1=steubenfunwins.com`

Allow `www.` aliases only when explicitly needed.

Register a named or typed `HttpClient` in `SaveNEIN.Server/Program.cs` with:

- base URI from configuration
- bearer token authentication
- explicit request timeout
- no browser-supplied target URL pass-through

---

## 7. Backend Domain Model

Create an application-owned archive manifest in PostgreSQL. ArchiveBox remains the artifact store; SaveNEIN stores the evidentiary relationship and audit metadata.

Suggested entity: `ArchivedWebSource` or `WebArchiveCapture`.

Minimum fields:

- `Id` — SaveNEIN UUID
- `SourceKey` — stable logical key such as `yes-for-allen-facts-2026-08-21`
- `OriginalUrl`
- `ObservedAtUtc` — nullable source-observation/report-scrape timestamp
- `ObservationType` — nullable enum/string such as `ReportScrape` or `ManualVerification`
- `ArchiveBoxSnapshotId`
- `CapturedAtUtc` — exact real capture time; never inherited from `ObservedAtUtc`
- `PublicArchivedUrl`
- `HttpStatus`
- `CaptureStatus`
- `NormalizedText`
- `NormalizedTextSha256`
- artifact inventory and SHA-256 hashes
- `CreatedAtUtc`

Do not accept a caller-supplied `CapturedAtUtc` when creating an ArchiveBox record. Derive it from the completed archive capture.

---

## 8. Backend Capture Workflow

Implement a service such as `ArchiveBoxCaptureService`.

For an approved claimant URL:

1. Validate the URL against the configured host allow-list.
2. Submit the URL to ArchiveBox.
3. Poll/query capture state until completion or failure.
4. Retrieve ArchiveBox snapshot metadata.
5. Obtain normalized captured text from the preserved representation.
6. Inventory the produced artifacts.
7. Compute/store SHA-256 hashes for the evidence artifacts where accessible.
8. Persist an immutable SaveNEIN archive manifest record.
9. Verify required quote/framing text against the captured normalized text.
10. Mark the capture eligible for publication only after verification succeeds.

The August 8 report observation must be imported separately as provenance metadata if desired. It must never be passed into this workflow as a requested archive timestamp.

---

## 9. Frontend / Fact-Check Migration

Update `SaveNEIN.Client/Models/FactCheckClaim.cs` so a fact check no longer conflates original URL, research observation, verification, and archive capture.

The primary claimant evidence link should become something equivalent to:

**View archived claimant source — captured August 21, 2026**

but only after a real archive exists.

Before archival backfill, use truthful wording such as:

**Claimant source — observed in research scrape August 8, 2026**

and separately provide the current live claimant URL if desired. Do not call that research scrape an archived source.

After ArchiveBox exists:

- primary historical evidence link -> immutable archived snapshot
- secondary link -> current/live claimant page
- optional provenance text -> research observation/report scrape date

The UI must not synthesize archive dates from the report date.

---

## 10. Initial Backfill

Immediately after ArchiveBox is operational:

1. Capture the current Yes for Allen facts page.
2. Capture the current Steuben Myths and Facts page.
3. Record exact ArchiveBox timestamps.
4. Preserve all required artifacts and hashes.
5. Verify every claim tied to each page against normalized captured text.
6. Update fact-check links to the immutable snapshots.
7. Retain the August 8 Yes for Allen report scrape as separate provenance metadata.
8. Do not label the first ArchiveBox snapshot as August 8 unless ArchiveBox actually captured it then—which it did not.

If an independent historical August 8 archive is later found, ingest it as a separate historical record only after validating its provenance and relevant claim text.

---

## 11. Tests

Add tests covering at minimum:

- source host allow-listing and SSRF rejection
- archive client success/failure/timeout behavior
- immutable manifest persistence
- capture timestamp comes from archive result, not request/report metadata
- `ObservedAtUtc` and `CapturedAtUtc` remain semantically distinct
- report scrape cannot create an `ArchiveCapture` record
- quote verification against captured normalized text
- direct quote mismatch blocks publication
- archived URL used as primary link when available
- live URL remains secondary provenance
- null archive capture renders truthful observation/verification wording rather than fake archive wording

---

## 12. Acceptance Criteria

The work is complete only when:

- ArchiveBox runs persistently in Docker with durable storage.
- SaveNEIN can request and verify captures through the backend.
- Yes for Allen and Steuben have real immutable snapshots captured at truthful timestamps.
- Archive manifests preserve original URLs, timestamps, artifacts, and integrity hashes.
- August 8 remains a report-scrape/source-observation date unless an independent August 8 archive is actually located.
- No UI path labels the August 8 report itself as an archived website.
- Every mutable claimant citation in the fact-check section resolves primarily to a preserved snapshot once one exists.
- The current live page is secondary and clearly identified as current/mutable.
- A later claimant edit cannot silently change the evidence readers see for a previously published fact check.

---

## 13. Core Principle

SaveNEIN should be able to prove **what it archived**, **when it archived it**, and **what research source it observed earlier** without conflating those events.

A scraped report can preserve analytical input. It cannot retroactively become a web archive.
