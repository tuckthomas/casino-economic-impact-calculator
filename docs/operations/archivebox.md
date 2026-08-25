# ArchiveBox operations

SaveNEIN uses a digest-pinned ArchiveBox container as an internal evidence service. ArchiveBox's control plane is not exposed through nginx. Readers receive only a verified SingleFile artifact through `GET /api/web-archives/captures/{id}/singlefile`, served with a restrictive sandbox CSP.

## Private configuration

Copy the blank `ARCHIVEBOX_*` entries from `deploy/.env.example` into the private VPS environment file. Never commit their values.

- `ARCHIVEBOX_ADMIN_PASSWORD`: a long random password used for the internal ArchiveBox administrator.
- `ARCHIVEBOX_API_TOKEN`: the ArchiveBox bearer token used by the SaveNEIN backend.
- `ARCHIVEBOX_CAPTURE_ADMIN_TOKEN`: an independent long random token protecting SaveNEIN's capture endpoint.
- `ARCHIVEBOX_ENABLED`: leave `false` until ArchiveBox is initialized and the API token is present; then set `true`.
- `ARCHIVEBOX_CRAWL_MAX_URLS`, `ARCHIVEBOX_CRAWL_MAX_SIZE`, and `ARCHIVEBOX_CRAWL_TIMEOUT`: hard safety ceilings for recursive source-site captures. Production defaults are 250 pages, 512 MB, and one hour per source.

## First initialization

Start only ArchiveBox, then wait for its server to become ready:

```bash
docker compose --env-file deploy/.env -f deploy/compose.production.yml up -d archivebox
```

The pinned image creates the configured administrator on first initialization. Obtain an API token from the internal container network without printing it into shell history or logs. The ArchiveBox endpoint is:

```text
POST /api/v1/auth/get_api_token
Content-Type: application/json

{"username":"savenein","password":"<private admin password>"}
```

Store the returned token as `ARCHIVEBOX_API_TOKEN` in the private VPS environment file. Generate a separate random `ARCHIVEBOX_CAPTURE_ADMIN_TOKEN`, enable the integration, and recreate the app container.

## Capturing approved sources

The public browser cannot provide an arbitrary target URL. Capture requests accept only a configured source key and require the private capture token:

```bash
curl --fail --request POST \
  --header "X-Archive-Capture-Token: ${ARCHIVEBOX_CAPTURE_ADMIN_TOKEN}" \
  https://savenein.com/api/web-archives/capture/yes-for-allen-facts

curl --fail --request POST \
  --header "X-Archive-Capture-Token: ${ARCHIVEBOX_CAPTURE_ADMIN_TOKEN}" \
  https://savenein.com/api/web-archives/capture/steuben-myths-facts
```

The backend submits configured sources as bounded recursive crawls and publishes a manifest only after ArchiveBox reports the crawl and seed snapshot sealed, all required DOM/SingleFile/screenshot/WARC artifacts exist, their SHA-256 hashes are recorded, and the configured claimant wording is present in normalized captured text. Public archived HTML rewrites web links through SaveNEIN's archive resolver; navigation never falls through to a live website.

## Provenance rule

August 8, 2026 is a Yes for Allen report-scrape observation date, not an archive date. `ObservedAtUtc`, `VerifiedAtUtc`, and `CapturedAtUtc` are separate events. Never copy an observation date into a capture timestamp or backdate an ArchiveBox record.

## Backup

Back up both the `archivebox_data` Docker volume and PostgreSQL. The volume contains the preserved artifacts; PostgreSQL contains the immutable evidentiary relationship, normalized text, capture timestamp, and hashes. Neither is a complete backup by itself.
