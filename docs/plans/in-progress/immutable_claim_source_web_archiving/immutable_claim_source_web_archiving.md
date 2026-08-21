# Immutable Claim-Source Web Archiving for Fact Checks

## Governing AI Agent Implementation Plan

> **Status:** In Progress — implementation plan created 2026-08-21; implementation has not started.
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

The implementation must create an immutable evidence chain:

**fact-check claim -> SaveNEIN archive record -> ArchiveBox snapshot -> preserved artifacts + capture timestamp + hashes -> original URL**

The live claimant page may remain available as secondary context, but it must not be the primary evidentiary hyperlink for a claim that depends on what the page said at a specific time.

---

## 2. Non-Negotiable Evidence Rules

1. **Never represent a live URL as a dated snapshot.** A `ClaimCapturedDate` is valid only if there is a corresponding preserved artifact captured at that time.
2. **Never rewrite or replace a snapshot already referenced by a published fact check.** A later recapture creates a new snapshot and a new archive record.
3. **The capture timestamp comes from the backend archive record.** Do not maintain a hand-entered date string in the Razor fact-check data once this feature is implemented.
4. **The original URL must always be retained as provenance.** Archiving changes the primary evidence link; it does not erase the source URL.
5. **The exact quoted/framed claim must be validated against the captured page before a snapshot is marked usable for publication.** For direct quotes, normalized captured text must contain the quoted wording. For paraphrased framing, the record must retain the supporting excerpt or verification note.
6. **Use multiple archival representations.** WARC plus rendered HTML/DOM plus screenshot provides substantially stronger evidence than any one representation by itself.
7. **Capture at publication/review time, not later when convenient.** The preferred workflow is to archive the claimant page before or as part of publishing/reviewing a fact check.
8. **Do not falsify historical capture dates during backfill.** A snapshot created on August 21 cannot be labeled as an August 8 capture merely because the fact check says the page was reviewed on August 8.

---

## 3. Why ArchiveBox

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

## 4. Docker Architecture

### Existing state

The repository's `docker-compose.yml` currently defines:

- `savenein-db`
- `savenein-db-gui`
- `valhalla`
- `savenein-app`

All currently use host networking where applicable.

### Add `savenein-archivebox`

Add an ArchiveBox service to `docker-compose.yml` using the existing host-network convention unless the broader Docker networking strategy is deliberately refactored in the same change.

Target shape:

```yaml
savenein-archivebox:
  image: archivebox/archivebox:<PINNED_VERSION_OR_DIGEST>
  container_name: savenein-archivebox
  network_mode: "host"
  command: server 0.0.0.0:8001
  volumes:
    - archivebox_data:/data
  environment:
    # Use ArchiveBox-supported configuration only; verify exact names
    # against the pinned version before committing.
    - PUBLIC_INDEX=True
    - PUBLIC_ADD_VIEW=False
    - PERMISSIONS=public
  restart: unless-stopped
```

Add:

```yaml
volumes:
  archivebox_data:
```

### Port choice

Use an ArchiveBox internal host port that does not collide with the ASP.NET application or other services. `8001` is the recommended initial value, but verify the deployment environment before finalizing it.

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

## 5. Server Configuration

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

## 6. Backend Domain Model

Create an application-owned archive manifest in PostgreSQL. ArchiveBox remains the artifact store; SaveNEIN stores the evidentiary relationship and audit metadata.

Suggested entity: `ArchivedWebSource` or `WebArchiveCapture`.

Minimum fields:

- `Id` — SaveNEIN UUID
- `SourceKey` — stable logical key such as `yes-for-allen-facts-2026-08-21`
- `OriginalUrl`
- `OriginalHost`
- `ArchiveProvider` — initially `ArchiveBox`
- `ArchiveBoxSnapshotId`
- `ArchivePublicUrl`
- `RequestedAtUtc`
- `CapturedAtUtc`
- `CaptureStatus` — `Pending`, `Succeeded`, `Verified`, `Failed`
- `HttpStatusCode` if available
- `PageTitle` if available
- `FinalUrlAfterRedirects`
- `VerificationStatus`
- `VerificationNote`
- `ExpectedEvidenceText` or an associated evidence-verification record
- `RenderedTextSha256` if available
- `RenderedDomSha256` if available
- `SingleFileSha256` if available
- `WarcSha256` if available
- `ScreenshotSha256` if available
- `ArchiveMetadataJson` — provider response/metadata needed for audit/replay without creating schema churn
- `CreatedAtUtc`
- `CreatedBy` / capture reason if the repository has an existing audit convention

Do not duplicate ArchiveBox's full internal database. Save only the metadata needed to bind a SaveNEIN claim to an immutable ArchiveBox snapshot and verify it later.

### Immutability

After a record reaches `Verified` and is referenced by a published fact check:

- do not change `ArchiveBoxSnapshotId`
- do not change `CapturedAtUtc`
- do not change hashes
- do not silently repoint `ArchivePublicUrl`

A new capture requires a new row.

Add a migration under the repository's existing migration mechanism and tests for the schema.

---

## 7. ArchiveBox Service Layer

Add a service under `SaveNEIN.Server/Services`, for example:

- `IWebArchiveService`
- `ArchiveBoxWebArchiveService`

Responsibilities:

1. validate a requested source against the allowlist
2. submit the URL to ArchiveBox through the authenticated API
3. request a zero-depth/single-page capture for claimant evidence pages unless a broader crawl is deliberately requested
4. use an explicit reproducible extractor/plugin set
5. obtain the ArchiveBox snapshot identifier and capture metadata
6. wait/poll for capture completion with bounded timeout and cancellation
7. verify expected artifacts exist
8. compute/store artifact hashes when available
9. extract normalized rendered page text for evidence verification
10. persist the SaveNEIN manifest record
11. return the canonical public snapshot URL

### Capture profile

For these claimant pages, prefer a capture profile that includes at least:

- `wget`/original-resource capture and WARC
- Chromium/Chrome rendered capture
- DOM output
- SingleFile
- screenshot

Verify the exact ArchiveBox plugin names against the pinned version before coding. Do not cargo-cult names from old ArchiveBox releases.

Use depth `0` for the initial claimant pages. The objective is preservation of the cited page and its required assets, not indiscriminate crawling of the entire campaign website.

### Full HTML/JavaScript requirement

The archival job must preserve more than server-returned HTML. A page that renders claim text through JavaScript must still produce a usable rendered DOM/SingleFile artifact and WARC/resource capture.

Treat the WARC and original resources as the archival record and the rendered DOM/SingleFile/screenshot as reader-friendly corroborating representations.

### Redirects

Record both the requested original URL and the final URL after redirects.

If a redirect leaves the configured claimant host/domain family, fail verification unless explicitly approved. This prevents a compromised or changed source URL from turning the archive service into a general-purpose SSRF/request proxy.

---

## 8. Capture API / Maintenance Workflow

The SaveNEIN public site must not expose an unauthenticated arbitrary-URL archiving endpoint.

Implement one of the following, in order of preference based on existing repository administration conventions:

1. an authenticated/internal maintenance endpoint that accepts only a `SourceKey` from a server-side source registry; or
2. an authenticated endpoint that accepts a URL but enforces the strict host allowlist; or
3. a server-side maintenance command if the repository already has a clean command/job pattern.

Recommended API shape if an internal controller is used:

```text
POST /api/admin/web-archives/capture/{sourceKey}
GET  /api/admin/web-archives/{id}
POST /api/admin/web-archives/{id}/verify
```

The caller should not be able to submit `http://localhost`, RFC1918 addresses, metadata-service IPs, `file://`, or arbitrary third-party hosts.

### Source registry

Add a small server-side registry/configuration for known mutable claimant sources:

```text
yes-for-allen-facts
  https://yesforallen.org/facts/

steuben-fun-wins-myths-facts
  https://steubenfunwins.com/myths-and-facts/
```

This makes normal captures key-driven and keeps URL policy out of the browser.

---

## 9. Evidence Verification

Archiving a URL is not enough. The backend must verify that the captured content actually supports the fact check before the snapshot becomes the cited source.

### Direct quotes

For `IsDirectQuote == true`:

- normalize HTML entities, Unicode punctuation, whitespace, and line wrapping
- extract rendered text from the captured page
- require the normalized direct quote to occur in captured text
- store success/failure plus the matched excerpt location/context if practical

If the exact quoted text is missing, the capture remains stored but cannot be promoted to `Verified` for that claim without manual resolution.

### Paraphrased/framing claims

For `IsDirectQuote == false`, exact-string matching may not be appropriate. Store one or more expected supporting excerpts or a manual verification note tied to the archive record.

### Hashes

At minimum, hash the primary preserved evidence artifacts that are available in the selected ArchiveBox release. SHA-256 is preferred.

The purpose is not to create a blockchain-style system. The hashes simply make accidental or unauthorized post-capture modification detectable.

---

## 10. Fact-Check Model Refactor

Current `FactCheckSource` contains only:

```csharp
string Label,
string Citation,
string? Url
```

Current `FactCheckClaim` contains live-source fields:

```csharp
string ClaimSourceUrl,
string ClaimCapturedDate
```

Refactor these so the model distinguishes:

- the original/live source URL
- the immutable archived evidence URL
- the actual capture timestamp/date
- optionally the SaveNEIN archive record identifier

One acceptable design:

```csharp
public sealed record FactCheckSource(
    string Label,
    string Citation,
    string? OriginalUrl = null,
    string? ArchivedUrl = null,
    DateTimeOffset? CapturedAt = null);
```

and a dedicated claimant source reference on `FactCheckClaim`, for example:

```csharp
public sealed record FactCheckClaimSource(
    Guid? ArchiveRecordId,
    string OriginalUrl,
    string ArchivedUrl,
    DateTimeOffset CapturedAt);
```

Do not keep a free-form `ClaimCapturedDate` string as the source of truth.

If the project intentionally keeps the fact-check claims compile-time/static, the initial archive IDs/URLs may be checked into the fact-check data after capture. The backend/database remains authoritative for how those immutable URLs were produced and verified.

If the fact checks are later moved into persistence/CMS data, preserve the same model semantics.

---

## 11. Frontend Changes

Update `SaveNEIN.Client/Components/FactCheckTimelineItem.razor` and `SaveNEIN.Client/Pages/ProponentsCritique.razor`.

### Primary claimant hyperlink

The primary claimant-source action must open the archived snapshot, not the current campaign page.

Replace generic copy such as:

```text
View claimant source
```

with explicit provenance copy such as:

```text
View archived claimant source — captured August 21, 2026
```

or, if space is constrained:

```text
Archived claimant source
Captured August 21, 2026
```

The displayed date must be formatted from the actual archive metadata.

### Source list

When a `FactCheckSource` is the mutable claimant page:

- link its source label to `ArchivedUrl`
- show `Captured <date>` in the citation/detail text
- retain the original URL as provenance metadata

Government PDFs and other currently stable direct documents do not need to be migrated in the first tranche unless they are also mutable HTML pages used for a quote.

### Optional live-page link

The live claimant page may be retained as a clearly secondary link:

```text
Current live page (may have changed)
```

Do not give the live page equal visual weight to the archived evidence, and do not label it as the source captured on the historical date.

### Failure behavior

If an archive record is missing or unverified:

- do not silently fall back to the live page while continuing to display a historical capture date
- display a clear evidence-unavailable state during development/admin validation
- preferably prevent release/publication of the affected claim through tests or validation

---

## 12. Initial Backfill: Yes for Allen and Steuben

### Target sources

1. `yes-for-allen-facts`
   - Original URL: `https://yesforallen.org/facts/`
   - Current code claims capture date: August 8, 2026

2. `steuben-fun-wins-myths-facts`
   - Original URL: `https://steubenfunwins.com/myths-and-facts/`
   - Current code claims capture date: August 21, 2026

### Critical timestamp rule

The implementation agent must **not** create a new ArchiveBox snapshot on August 21 and label it August 8.

For the Yes for Allen August 8 provenance, use this order:

1. determine whether a genuine contemporaneous August 8 artifact already exists in repository data, prior capture files, or another reliable dated archive;
2. if an external historical snapshot exists and exactly contains the cited language, retain/register it as historical evidence and separately create a new self-hosted capture for current preservation if useful;
3. if no genuine August 8 snapshot exists, capture the live page on the actual implementation date, verify every affected claim against it, and update the displayed capture date to the truthful new capture date;
4. if the page has changed and the cited wording is no longer present, do not pretend the new capture proves the old quote. Locate a real historical archive or mark the citation as requiring historical-evidence recovery.

For the Steuben source, because the current stated capture date is August 21, 2026, capture it on August 21 if implementation occurs that day and verify all Steuben-dependent claims immediately.

### Claim mapping

Do not assume one snapshot per fact-check card. Multiple fact checks can reference the same immutable page snapshot when they were all verified against that same captured page.

Create a clear mapping in the fact-check data from affected claims to the archive record/snapshot they use.

---

## 13. Source-Change Detection

After the immutable citation system works, add an optional follow-on comparison capability.

A later recapture of the same claimant URL may be compared with the snapshot currently cited by SaveNEIN.

Useful outputs:

- rendered-text SHA change
- exact quoted sentence removed/changed
- title change
- redirect change
- major DOM/text diff

This feature is informational only. A newly captured version must never replace the historical cited snapshot automatically.

A future scheduled monitor may notify maintainers that a claimant edited a page after SaveNEIN's capture, but that monitoring is not required for Phase 1 acceptance.

---

## 14. Security Requirements

Because the backend will cause a browser/downloader service to fetch URLs, SSRF controls are mandatory.

At minimum:

- normal capture calls accept a server-side `SourceKey`, not an arbitrary public URL
- explicit hostname allowlist
- only `https` unless a known source requires otherwise
- reject localhost/loopback/link-local/private-network targets after DNS resolution
- revalidate redirect targets
- block `file:`, `ftp:`, `data:`, `javascript:`, and other unsupported schemes
- keep ArchiveBox add/admin endpoints private/authenticated
- never expose the ArchiveBox API token to Blazor/client code
- bound maximum capture time/size
- log capture attempts and failures
- treat archived HTML/JS as untrusted content

The public archive interface should be read-only. Do not embed archived third-party JavaScript directly into the SaveNEIN application's DOM. Open the archive in its own page/origin.

---

## 15. Testing

### Unit tests

Add tests for:

- source-key resolution
- hostname allowlist
- redirect-domain validation
- private/loopback IP rejection
- archive public URL construction/normalization
- timestamp formatting
- direct-quote normalization/matching
- immutable record update rules

### Backend integration tests

Mock or fake the ArchiveBox API and verify:

- successful submit -> completed snapshot -> persisted manifest
- provider timeout
- provider 4xx/5xx
- missing snapshot ID
- missing required artifacts
- quote not found
- redirected target leaves allowlist
- duplicate capture request does not overwrite an existing verified record

### Docker/high-fidelity test fixture

Add a small local fixture page for an integration/manual test that:

1. returns minimal server HTML;
2. loads JavaScript;
3. injects a known evidence sentence after page load.

Run a real ArchiveBox capture against the fixture in the Docker validation workflow and confirm:

- rendered DOM or SingleFile contains the injected sentence
- screenshot is produced
- WARC/original-resource capture is produced
- backend verification succeeds

This test proves the implementation preserves client-rendered evidence rather than merely downloading raw HTML.

### Frontend tests

Verify:

- claimant source links use archived URLs
- actual capture date is rendered
- live source is secondary and labeled as mutable if retained
- no claim can display `Captured <date>` while linking only to the live mutable claimant page

---

## 16. Observability and Operations

Add structured logging around:

- source key
- original URL host
- SaveNEIN archive record ID
- ArchiveBox snapshot ID
- requested/captured timestamps
- capture duration
- final status
- verification status
- artifact hashes/checksum prefixes where useful

Do not log secrets or bearer tokens.

Expose a lightweight health/readiness check for ArchiveBox connectivity if the repository already has health-check infrastructure. The public application itself should remain able to render existing archived links if the ArchiveBox mutation API is temporarily unavailable.

Back up `archivebox_data` with the same seriousness as the application database. The database contains the evidentiary index; the ArchiveBox volume contains the actual preserved evidence.

---

## 17. Documentation

Update repository documentation with:

- how to initialize ArchiveBox
- how to create/store the API token
- how to run a claimant-page capture
- how to inspect the result manually
- how to verify a quote
- how to promote a snapshot to a fact-check citation
- how to recapture without overwriting historical evidence
- backup/restore expectations for `archivebox_data`
- production public archive URL/reverse-proxy requirement

Add a short fact-check editorial rule:

> Any mutable claimant webpage quoted or materially paraphrased by a published fact check must have a verified immutable archive snapshot. The archived snapshot is the primary citation; the live page is optional secondary context.

---

## 18. Implementation Sequence

### Phase 0 — Baseline and platform validation

- [ ] Pin a tested ArchiveBox version/digest.
- [ ] Confirm its current REST API endpoints/authentication from the pinned instance's `/api/v1/docs`.
- [ ] Confirm extractor/plugin names for original resources/WARC, Chromium DOM, SingleFile, and screenshot.
- [ ] Manually capture both claimant pages in a disposable ArchiveBox volume and inspect quality before changing application code.
- [ ] Confirm public snapshot URL behavior for the pinned version.

### Phase 1 — Docker/operations

- [ ] Add `savenein-archivebox` to `docker-compose.yml`.
- [ ] Add persistent `archivebox_data` volume.
- [ ] Add health/restart behavior appropriate to the existing stack.
- [ ] Add secrets/configuration placeholders without committing credentials.
- [ ] Document one-time initialization and public reverse-proxy/subdomain requirements.

### Phase 2 — Backend integration

- [ ] Add strongly typed ArchiveBox options.
- [ ] Add authenticated ArchiveBox `HttpClient`.
- [ ] Add source registry/allowlist.
- [ ] Add archive manifest entity + migration.
- [ ] Implement `IWebArchiveService` / `ArchiveBoxWebArchiveService`.
- [ ] Implement capture completion polling and artifact verification.
- [ ] Implement SHA-256 artifact hashing where practical.
- [ ] Implement rendered-text extraction and direct-quote verification.
- [ ] Add internal/authenticated maintenance capture workflow.

### Phase 3 — Initial evidence captures

- [ ] Resolve the truthful historical-evidence status of the Yes for Allen August 8 claim source.
- [ ] Capture/verify the current Yes for Allen page without falsifying the capture timestamp.
- [ ] Capture/verify the Steuben Myths and Facts page.
- [ ] Verify every affected direct quote against its archived snapshot.
- [ ] Record snapshot IDs, actual timestamps, hashes, and canonical archive URLs.

### Phase 4 — Fact-check/frontend migration

- [ ] Refactor `FactCheckSource` to distinguish original and archived URLs.
- [ ] Replace `ClaimSourceUrl` + free-form `ClaimCapturedDate` with an archive-aware source reference.
- [ ] Update every Yes for Allen claimant-source citation to the verified archive snapshot.
- [ ] Update every Steuben claimant-source citation to the verified archive snapshot.
- [ ] Change UI copy to explicitly say `Archived claimant source` and display actual capture date.
- [ ] If retained, label live links `Current live page (may have changed)`.
- [ ] Ensure missing/unverified archive evidence never silently degrades to a historical-date/live-link mismatch.

### Phase 5 — Tests and release verification

- [ ] Add security/allowlist unit tests.
- [ ] Add ArchiveBox service integration tests.
- [ ] Add JS-rendered fixture capture test.
- [ ] Add frontend archived-link tests.
- [ ] Run the full `SaveNEIN.Server.Tests` suite.
- [ ] Build the entire solution with zero new warnings/errors.
- [ ] Run the Docker stack from a clean volume and verify ArchiveBox initialization/startup.
- [ ] Open every migrated fact-check claimant link in a browser and confirm it displays the preserved page associated with the shown capture date.

---

## 19. Acceptance Criteria

This plan is complete only when all of the following are true:

1. `docker compose up` includes a persistent self-hosted ArchiveBox service.
2. ArchiveBox is version-pinned and not dependent on an unpinned production `latest` tag.
3. SaveNEIN backend can request a capture for a configured claimant source without exposing an arbitrary public fetch endpoint.
4. The capture preserves original web resources/WARC plus a Chromium-rendered representation and screenshot.
5. SaveNEIN records an immutable application-owned manifest with the actual capture timestamp and ArchiveBox snapshot ID.
6. At least the primary archival artifacts are SHA-256 verifiable.
7. Direct quotes used in fact checks are validated against captured rendered text before publication.
8. Yes for Allen and Steuben claimant citations no longer use live campaign pages as their primary historical evidence links.
9. The fact-check UI explicitly labels the evidence as archived and displays the real capture date.
10. No August 21-created snapshot is mislabeled as an August 8 capture.
11. Previously cited snapshots are never overwritten by later recaptures.
12. ArchiveBox mutation/admin functionality is not anonymously writable from the public internet.
13. Full test suite/build remains green.
14. Repository documentation explains capture, verification, publication, recapture, and backup procedures.

---

## 20. Explicit Non-Goals for Phase 1

Do not expand this work into an uncontrolled archival platform project.

Phase 1 does **not** require:

- crawling every page on Yes for Allen or Steuben Fun Wins
- archiving every external source in the application
- replacing authoritative government PDF URLs with ArchiveBox copies
- scheduled daily captures of every source
- public user-submitted archiving
- an ArchiveBox administration UI inside SaveNEIN
- Browsertrix/WACZ infrastructure unless ArchiveBox fails a demonstrated target-page fidelity requirement
- automatic semantic determination that a campaign claim is true or false

The first implementation exists to make the **evidence underlying SaveNEIN's published fact checks immutable, dated, inspectable, and defensible**.
