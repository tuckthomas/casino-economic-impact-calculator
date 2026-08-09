# Production Asset Fingerprinting, Cache Policy, and Automated Deployment Pipeline

Status: Pipeline / Not Started

## Goal

Implement a production delivery pipeline in which static assets are versioned by their own contents, the HTML application shell is always revalidated, Cloudflare and browsers can cache immutable static assets aggressively, Nginx remains a non-caching reverse proxy, and a successful change to `main` can be built, deployed, verified, and rolled back through GitHub Actions without manual cache-buster edits.

The target design must eliminate manually maintained asset versions such as `?v=20260808-whatever` after content-based fingerprinting is proven in production.

---

## 0. Non-negotiable design decisions

- [ ] Use **content-derived fingerprints** for browser-facing static assets.
  - [ ] Do not use a Git commit SHA as the cache-busting mechanism for CSS, JavaScript, fonts, images, or Blazor framework resources.
  - [ ] A Git commit SHA may still identify a deployment or Docker image because deployment identity and browser asset identity are separate concerns.
- [ ] Prefer the native .NET 10 static-web-asset pipeline before introducing a custom Node fingerprinting/bundling system.
- [ ] Keep local development behavior fast and cache-resistant.
  - [ ] Preserve the current Debug-only fingerprinting overrides unless testing proves they are no longer needed.
  - [ ] Do not force immutable production caching behavior into `dotnet watch` or normal Debug builds.
- [ ] Revalidate the HTML/app shell instead of caching it as immutable content.
- [ ] Cache only fingerprint-safe static content aggressively.
- [ ] Keep dynamic APIs non-cacheable by default unless an endpoint is deliberately classified as safe to cache.
- [ ] Keep Nginx out of application-content caching.
- [ ] Use GitHub Actions for CI/CD and deployment orchestration.
- [ ] Keep Git hooks limited to optional local developer checks. Do not make production deployment depend on a Git hook.
- [ ] Do not purge the entire Cloudflare cache on every deployment.
- [ ] Do not remove any current manual cache-buster until the replacement has been validated from published production output and through Cloudflare.

---

## 1. Verified current repository state

These items describe the existing starting point and should be revalidated immediately before implementation.

- [x] Client project targets .NET 10 and uses `Microsoft.NET.Sdk.BlazorWebAssembly`.
- [x] Server project targets .NET 10 and hosts the Blazor WebAssembly client.
- [x] `SaveNEIN.Client.csproj` explicitly disables static-web-asset fingerprinting behavior for Debug builds.
- [x] `SaveNEIN.Server/Pages/Index.cshtml` already uses `asp-append-version="true"` on many local CSS, JS, font, icon, and framework references.
  - [x] This is already content-derived query-string versioning for those Razor Page references.
  - [ ] It is not yet the final desired static-asset delivery design because the server still relies on `UseStaticFiles()` rather than the .NET 10 `MapStaticAssets()` optimized endpoint pipeline.
- [x] `Index.cshtml` currently adds a client assembly MVID query parameter to Blazor boot resources as a deployment-wide stale-resource defense.
- [x] `SaveNEIN.Client/wwwroot/index.html` still contains manually maintained `?v=...` asset versions.
- [x] `SaveNEIN.Server/Program.cs` currently calls `UseBlazorFrameworkFiles()` and `UseStaticFiles()`.
- [x] `Program.cs` disables caching for HTML, framework assets, JS, CSS, WASM, and PDB files only in the Development environment.
- [x] Current production Nginx configuration contains no `proxy_cache`, `fastcgi_cache`, or equivalent application-content cache.
- [x] Current Nginx `ssl_session_cache` is TLS session caching only and must remain conceptually separate from HTTP content caching.
- [x] Current production Compose configuration builds/uses `savenein-app:latest` locally rather than pulling an immutable application image from a registry.
- [x] Current repository has a GitHub Actions UI text-size guard but no production deployment workflow.
- [x] Current repository has a local `.githooks/pre-commit` hook that runs the UI text-size check.

---

## 2. Establish one authoritative production HTML shell

The repository currently contains both a server Razor Page shell and a client `wwwroot/index.html`. Before changing fingerprint behavior, determine exactly which shell owns production references.

- [ ] Confirm with a Release publish and the live request pipeline that `SaveNEIN.Server/Pages/Index.cshtml` is the authoritative production document served for `/` and SPA fallback routes.
- [ ] Confirm whether `SaveNEIN.Client/wwwroot/index.html` is required for any current local-development, standalone publish, test, or tooling path.
- [ ] Inventory every local `<link>`, `<script>`, favicon, font, CSS, and JS reference in both shell files.
- [ ] Classify each shell reference as:
  - [ ] production authoritative;
  - [ ] development-only;
  - [ ] standalone-client-only;
  - [ ] obsolete/duplicate.
- [ ] Prevent the two shells from independently accumulating different cache-busting schemes.
- [ ] If the client `index.html` must remain:
  - [ ] generate or synchronize its production asset references from one source where practical; or
  - [ ] clearly constrain it to a development/standalone role.
- [ ] If it is not required:
  - [ ] remove it from the production path only after proving hosted Blazor startup is unaffected.
- [ ] Add a CI check that catches divergent production-local asset references between the active shell and any required secondary shell.

---

## 3. Native .NET 10 static-asset fingerprinting technical spike

Do this before writing custom fingerprint tooling.

- [ ] Create a temporary implementation branch for the static-asset technical spike.
- [ ] Run a clean Release publish of the current application and record:
  - [ ] final static-web-asset manifest(s);
  - [ ] generated Blazor framework asset names;
  - [ ] generated scoped CSS asset names;
  - [ ] current cache headers when served from a Release container;
  - [ ] whether custom JS/CSS assets receive fingerprint aliases/endpoints.
- [ ] Replace or supplement `UseStaticFiles()` with `.NET 10` `MapStaticAssets()` in a controlled test build.
- [ ] Verify compatibility with:
  - [ ] `UseBlazorFrameworkFiles()`;
  - [ ] hosted Blazor WebAssembly;
  - [ ] Razor Pages fallback through `MapFallbackToPage("/Index")`;
  - [ ] CSS isolation (`SaveNEIN.Client.styles.css`);
  - [ ] Tailwind-generated `css/app.css`;
  - [ ] copied third-party libraries under `wwwroot/lib` and `wwwroot/js/lib`;
  - [ ] developer-authored JS under `wwwroot/js`;
  - [ ] static images/SVGs under `wwwroot/assets`.
- [ ] Verify that `MapStaticAssets()` produces content-based fingerprints from the final built file contents and immutable cache behavior in Production.
- [ ] Verify that the fingerprint is based on the built asset after Tailwind minification/copy steps, not the pre-build source file.
- [ ] Verify that unchanged files retain the same content fingerprint across two independent clean Release builds.
- [ ] Verify that changing one file changes only that file's content fingerprint and dependent manifest/reference data.
- [ ] Determine whether additional `<StaticWebAssetFingerprintPattern>` entries are required for project-specific assets.
- [ ] Do not add custom fingerprint patterns merely to duplicate framework defaults.
- [ ] Confirm whether `OverrideHtmlAssetPlaceholders` must be enabled for the hosted production path.
  - [ ] Preserve the current Debug behavior that disables placeholder/fingerprint churn during development unless the native .NET 10 development behavior proves sufficient without it.
- [ ] Audit the unconditional `builder.WebHost.UseStaticWebAssets()` call.
  - [ ] Determine whether it is actually required in Production.
  - [ ] If it is only needed for non-Production local/staging behavior, scope it to the appropriate environment instead of enabling it globally.

### Decision gate

- [ ] If native .NET 10 static-web-asset fingerprinting cleanly supports the hosted architecture, adopt it as the production mechanism.
- [ ] If a specific asset class cannot be referenced through the native fingerprint system from the Razor Page shell:
  - [ ] document the limitation;
  - [ ] prefer a content-hash manifest/rewrite for that asset class;
  - [ ] use a hash of that file's contents, never a Git SHA, timestamp, or manually incremented string;
  - [ ] do not introduce a whole-site custom bundler for one unsupported edge case.

---

## 4. Implement production static-asset delivery

- [ ] Update `SaveNEIN.Server/Program.cs` to use the .NET 10 static-asset endpoint pipeline where supported.
- [ ] Keep `UseStaticFiles()` only for asset classes or locations that genuinely require middleware behavior not provided by `MapStaticAssets()`.
- [ ] If both are required:
  - [ ] explicitly document which paths are served by each mechanism;
  - [ ] ensure critical CSS/JS/framework assets remain on the fingerprinted `MapStaticAssets()` path.
- [ ] Configure fingerprint coverage for at least:
  - [ ] `css/app.css`;
  - [ ] generated `SaveNEIN.Client.styles.css`;
  - [ ] custom component JS;
  - [ ] economics JS;
  - [ ] library JS/CSS copied into `wwwroot` when safe;
  - [ ] local fonts;
  - [ ] logos/icons/SVGs and other static images when native mapping supports their references cleanly;
  - [ ] Blazor framework boot/runtime resources.
- [ ] Confirm CSS `url(...)` references and nested asset references resolve correctly after fingerprinting.
- [ ] Confirm JS imports and dynamic resource loads resolve the current fingerprinted resource rather than a hard-coded unhashed path.
- [ ] Confirm no page references an old fingerprint after a clean rebuild.
- [ ] Confirm stale fingerprints never alias silently to new content.

---

## 5. HTML/app-shell cache policy

Target behavior: the browser and Cloudflare may store the shell, but they must validate it before reuse so a deployment can immediately advertise the new fingerprinted asset references.

- [ ] Add an explicit Production cache policy for HTML/Razor shell responses.
- [ ] Preferred starting header:

```text
Cache-Control: public, no-cache, must-revalidate
```

- [ ] Apply the policy to:
  - [ ] `/`;
  - [ ] Razor/SPA fallback HTML responses;
  - [ ] any other route that returns the application shell.
- [ ] Do not mark the HTML shell `immutable`.
- [ ] Do not assign a long browser `max-age` to the HTML shell.
- [ ] Do not use `no-store` for the public shell unless testing identifies a concrete reason to prevent storage entirely.
- [ ] Verify Cloudflare is configured to respect the origin's HTML revalidation policy.
- [ ] Verify browser refresh/navigation receives the latest shell after a deployment without requiring a hard refresh.
- [ ] Optional optimization after correctness is proven:
  - [ ] evaluate a stable validator such as an ETag for the generated shell so unchanged HTML can return `304 Not Modified` efficiently.

---

## 6. Fingerprinted static-asset cache policy

Target behavior: once a URL contains a content fingerprint, it is safe to cache aggressively because changed content receives a different fingerprinted URL.

- [ ] Confirm the .NET 10 static-asset endpoint emits appropriate immutable caching headers for fingerprinted endpoints.
- [ ] Target browser-facing behavior equivalent to:

```text
Cache-Control: public, max-age=31536000, immutable
```

- [ ] Do not manually override a stronger/saner framework-generated immutable policy without a specific need.
- [ ] Ensure Cloudflare can cache fingerprinted static assets at the edge.
- [ ] Ensure Browser Cache TTL is configured to respect origin headers for these resources.
- [ ] Verify assets are not accidentally marked `private` or `no-store` in Production.
- [ ] Verify unchanged assets produce Cloudflare `HIT` responses after warm-up.
- [ ] Verify a changed asset has a new URL and therefore produces a normal first-request `MISS` without requiring a cache purge.
- [ ] Keep old fingerprinted objects purge-free during normal deployments so old browser tabs can still use already-cached immutable resources where possible.

---

## 7. Transitional policy for non-fingerprinted static assets

Until every important browser asset is content-versioned:

- [ ] Identify each intentionally non-fingerprinted asset.
- [ ] Apply a conservative revalidation policy instead of a one-year immutable policy.
- [ ] Preferred transitional behavior:

```text
Cache-Control: public, max-age=0, must-revalidate
```

- [ ] Do not accidentally apply immutable caching by extension alone to files that still use stable URLs.
- [ ] Track remaining non-fingerprinted files in this plan until the list reaches zero or each exception is documented.

---

## 8. API cache policy

- [ ] Treat dynamic model/calculation APIs as non-cacheable by default.
- [ ] Apply `Cache-Control: no-store` to personalized, scenario-specific, or rapidly changing calculation endpoints unless there is a deliberate cache design.
- [ ] Audit public reference-data APIs separately.
  - [ ] Stable public geographic/reference responses may receive explicit short/medium caching later if safe.
  - [ ] Any such cache must be endpoint-specific, documented, and invalidated by data-version changes.
- [ ] Create a Cloudflare cache bypass rule for `/api/*` as the safe baseline unless specific endpoints are later opted in.
- [ ] Verify Cloudflare does not cache POST responses or model-run results unintentionally.

---

## 9. Cloudflare cache configuration

- [ ] Inventory all current Cloudflare Cache Rules/Page Rules affecting `savenein.com`.
- [ ] Identify and remove or narrow any broad `Cache Everything` behavior that could make HTML or APIs stale.
- [ ] Set Browser Cache TTL to **Respect Existing Headers** unless a narrower rule has a documented reason to override origin headers.
- [ ] Configure static fingerprinted paths to be eligible for Cloudflare edge caching while respecting origin cache headers.
- [ ] Configure HTML/application-shell routes to respect the origin revalidation policy or bypass edge cache if revalidation cannot be made reliable.
- [ ] Configure `/api/*` to bypass cache by default.
- [ ] Do not use a Cloudflare rule that overrides a fingerprinted asset's immutable browser policy with a shorter arbitrary Browser TTL unless intentionally desired.
- [ ] Confirm query-string cache-key behavior during the transition period while `asp-append-version` or manual `?v=` references still exist.
- [ ] After filename/content-fingerprint routing is fully active, verify Cloudflare uses each fingerprinted URL as a distinct cache object.
- [ ] Document the final Cloudflare rules in `deploy/README.md` so the CDN configuration is not tribal knowledge.

---

## 10. Nginx policy

- [ ] Keep Nginx as TLS termination/reverse proxy/compression infrastructure only.
- [ ] Do **not** add:
  - [ ] `proxy_cache_path`;
  - [ ] `proxy_cache`;
  - [ ] `fastcgi_cache`;
  - [ ] long-lived `expires` rules that conflict with application/CDN cache policy.
- [ ] Do not override correct `Cache-Control`, `ETag`, `Last-Modified`, or content-encoding headers coming from ASP.NET unless testing identifies a concrete problem.
- [ ] Preserve current TLS session caching because it does not cache application responses.
- [ ] Verify Nginx passes conditional requests (`If-None-Match`, `If-Modified-Since`) to the application as expected when applicable.
- [ ] Verify Nginx compression does not double-compress assets already served precompressed by ASP.NET.

---

## 11. Replace ad hoc CI with an explicit CI workflow

- [ ] Create or consolidate into `.github/workflows/ci.yml`.
- [ ] Trigger CI on:
  - [ ] pull requests targeting `main`;
  - [ ] pushes to `main`.
- [ ] Move/reuse the existing UI text-size guard inside CI or keep it as a required separate check, but ensure production deployment cannot run before required checks succeed.
- [ ] CI steps should include:
  - [ ] checkout;
  - [ ] install the repository's pinned .NET SDK from `global.json`;
  - [ ] set up Node 20;
  - [ ] `npm ci`;
  - [ ] `npm run check:ui-text`;
  - [ ] `dotnet restore`;
  - [ ] run existing automated tests;
  - [ ] perform a clean Release publish;
  - [ ] fail on compilation/analyzer errors;
  - [ ] validate generated static-asset/fingerprint output.
- [ ] Avoid building Tailwind twice unnecessarily.
  - [ ] Account for the current MSBuild `BuildFrontendAssets` target, which already runs `copy-libs` and `build:css` before builds.
  - [ ] Make the CI sequence deterministic rather than running parallel asset builds against the same output files.
- [ ] Add a CI check that rejects new manually maintained local asset cache-busters matching patterns such as `?v=YYYY...` after the fingerprint migration is complete.
- [ ] Add a CI check that detects missing fingerprint/manifest coverage for critical production CSS/JS assets.

---

## 12. Build one immutable production image in GitHub Actions

The production server should run the exact artifact CI built, not rebuild a potentially different artifact on the VPS.

- [ ] Configure GitHub Container Registry (GHCR) for the application image.
- [ ] Grant the workflow minimum required permissions:
  - [ ] `contents: read`;
  - [ ] `packages: write`;
  - [ ] optional artifact-attestation permissions if implemented.
- [ ] Build the Docker image in GitHub Actions after CI succeeds.
- [ ] Push an immutable image tag tied to the deployment revision, for example `sha-<commit>`.
  - [ ] This SHA is only a container/deployment identifier.
  - [ ] It must not be injected into browser asset URLs.
- [ ] Record the pushed image digest.
- [ ] Prefer deploying by immutable image digest or immutable SHA tag rather than `latest`.
- [ ] Optionally also publish a convenience `main`/`latest` tag, but never use that mutable tag as the rollback source of truth.
- [ ] Pin third-party GitHub Actions by a reviewed immutable commit SHA where practical for production/supply-chain safety.

---

## 13. Convert production Compose from local build to immutable image pull

- [ ] Change the production `app` service so it can consume a registry image variable, for example:

```text
APP_IMAGE=ghcr.io/tuckthomas/casino-economic-impact-calculator:<immutable-tag-or-digest>
```

- [ ] Remove `build:` from the production-only deployment path once registry deployment is verified.
- [ ] Keep a separate local-development/maintenance override if local Docker builds remain useful.
- [ ] Do not rebuild PostgreSQL, Valhalla, or Nginx merely because application code changed.
- [ ] Ensure database data and Valhalla data remain persistent and independent of application image replacement.
- [ ] Keep production secrets such as the PostgreSQL password outside the repository.
- [ ] Do not copy the database password into GitHub Actions unless a workflow step genuinely requires it.

---

## 14. Create the production deployment workflow

- [ ] Create `.github/workflows/deploy-production.yml`.
- [ ] Deployment should occur only after the required CI workflow for the exact `main` commit succeeds.
- [ ] Support `workflow_dispatch` for controlled manual redeploy/rollback operations.
- [ ] Add `concurrency` protection so two production deployments cannot run simultaneously.
- [ ] Use a GitHub `production` environment for deployment secrets and environment-level controls.
- [ ] Required deployment secrets should be narrowly scoped, for example:
  - [ ] VPS host;
  - [ ] dedicated deploy username;
  - [ ] dedicated SSH private key;
  - [ ] pinned SSH host key/known-host fingerprint;
  - [ ] Cloudflare Zone ID only if the workflow performs a targeted purge;
  - [ ] Cloudflare API token with Cache Purge permission only if purge is enabled.
- [ ] Create a dedicated non-root VPS deploy user.
- [ ] Restrict that user's privileges to the minimum needed to pull the app image and update the production Compose stack.
- [ ] On deployment:
  - [ ] record currently deployed application image/tag as rollback candidate;
  - [ ] authenticate to GHCR from the VPS with read-only package credentials where required;
  - [ ] pull the exact new image;
  - [ ] update the application image variable/tag;
  - [ ] run `docker compose up -d` for the application service without rebuilding unrelated services;
  - [ ] wait for Docker health status;
  - [ ] verify Nginx can reach the application;
  - [ ] verify the public HTTPS site through Cloudflare.
- [ ] If deployment verification fails:
  - [ ] automatically restore the previous app image where safe; or
  - [ ] stop and expose an explicit one-command/manual rollback path.
- [ ] Do not declare deployment successful only because `docker compose up` returned exit code zero.

---

## 15. Database migration safety in automated deployment

The application currently applies pending EF Core migrations during startup, so automatic deployment can implicitly change the database schema.

- [ ] Inventory current migrations and startup migration behavior before enabling unattended deployment.
- [ ] Define a policy for schema changes:
  - [ ] additive/backward-compatible migrations may deploy automatically;
  - [ ] destructive or non-backward-compatible migrations require an explicit deployment note/gate.
- [ ] Ensure a rollback to the previous application image is still compatible with any schema migration performed by the new version.
- [ ] If not, require a forward-fix strategy or explicit migration rollback plan before deployment.
- [ ] Surface migration failures in GitHub Actions deployment output rather than allowing a container to look healthy while startup initialization silently failed.

---

## 16. Production health verification

- [ ] Keep the existing container health check.
- [ ] Add GitHub Actions post-deploy checks against the public site, not only the internal container.
- [ ] Verify at minimum:
  - [ ] `https://savenein.com/` returns `200`;
  - [ ] page body contains a known application marker/title;
  - [ ] one fingerprinted CSS asset returns `200`;
  - [ ] one fingerprinted JS asset returns `200`;
  - [ ] a representative public API endpoint responds as expected;
  - [ ] TLS redirect/canonical-host behavior remains correct.
- [ ] Capture response headers during verification:
  - [ ] `Cache-Control`;
  - [ ] `ETag` where applicable;
  - [ ] `Last-Modified` where applicable;
  - [ ] `CF-Cache-Status`;
  - [ ] `Age` where applicable;
  - [ ] `Content-Encoding`.
- [ ] Fail the deployment if the HTML shell is accidentally served with a long immutable cache policy.
- [ ] Fail the deployment if a critical fingerprinted static asset is missing.

---

## 17. Cloudflare purge policy after fingerprinting

Normal deployments should not need a broad purge because changed assets have new content-derived URLs.

- [ ] Do **not** run `purge_everything` as a normal deployment step.
- [ ] First attempt to operate with no purge at all:
  - [ ] HTML always revalidates;
  - [ ] new fingerprinted asset URLs naturally miss and fill the CDN cache;
  - [ ] unchanged fingerprinted assets remain hot.
- [ ] If testing proves Cloudflare can still retain the old shell under an existing rule, add a targeted post-health-check purge for only the application-shell URL(s).
- [ ] Perform any targeted purge **after** the new application is healthy so Cloudflare never fetches the new shell while the old application is still active.
- [ ] Keep old fingerprinted static assets out of the purge list.
- [ ] Use the least-privileged Cloudflare API token possible.

---

## 18. Remove manual and deployment-wide browser cache-busters

Only perform this phase after the new fingerprint system has passed production tests.

- [ ] Search the repository for all manually maintained local asset query versions, including:
  - [ ] `?v=` in `SaveNEIN.Client/wwwroot/index.html`;
  - [ ] any `?v=` in Razor Pages/components;
  - [ ] manual version strings in CSS imports;
  - [ ] manual version strings in dynamic JS loaders.
- [ ] Remove manually maintained date/label versions such as `?v=20260808-...`.
- [ ] Evaluate the current `asp-append-version="true"` references.
  - [ ] Keep them only where they remain the chosen content-hash mechanism for a Razor Page asset that cannot cleanly use native fingerprinted routing.
  - [ ] Remove redundant `asp-append-version` where the referenced URL is already a content-fingerprinted immutable asset.
- [ ] Evaluate and remove the custom client-MVID `build=` parameter from Blazor boot resources once native framework fingerprinting proves it is redundant.
- [ ] Remove any old cache-busting comments/documentation that tells developers to manually increment query strings.
- [ ] Add a CI guard preventing reintroduction of manual timestamp/date cache-busters.

---

## 19. Development behavior

Production cache hardening must not make normal development painful.

- [ ] Preserve Development no-cache behavior for HTML, CSS, JS, WASM, and PDB resources as needed.
- [ ] Preserve `dotnet watch` behavior without forcing developers to regenerate immutable production URLs manually.
- [ ] Verify Debug builds do not produce stale browser resources after a code/CSS/JS change.
- [ ] Verify Release builds do produce deterministic content fingerprints.
- [ ] Document the difference:
  - [ ] Debug/Development prioritizes rapid change visibility;
  - [ ] Release/Production prioritizes immutable content-addressed static caching.
- [ ] Keep `.githooks/pre-commit` optional/local.
- [ ] Do not put Docker deploy, VPS SSH, Cloudflare purge, or production mutation logic in Git hooks.

---

## 20. Rollback design

- [ ] Every successful deployment must record the immutable application image it replaced.
- [ ] Provide a manual rollback through `workflow_dispatch` or a documented deployment command.
- [ ] Rollback must:
  - [ ] select the prior immutable image tag/digest;
  - [ ] pull it if absent;
  - [ ] update only the app service where possible;
  - [ ] wait for health checks;
  - [ ] verify public HTTPS;
  - [ ] avoid purging fingerprinted assets.
- [ ] Verify that rolling back the app also restores the corresponding HTML references to that release's fingerprinted assets.
- [ ] Test rollback before treating the deployment pipeline as complete.
- [ ] Document database-migration limitations that could prevent binary rollback.

---

## 21. Required cache/fingerprint test matrix

### Release build determinism

- [ ] Build the same commit twice from clean workspaces.
- [ ] Confirm unchanged CSS/JS content receives the same fingerprint.
- [ ] Confirm no timestamp contaminates the content hash.

### Single CSS change

- [ ] Change only one CSS asset.
- [ ] Confirm its fingerprint changes.
- [ ] Confirm unrelated JS/image fingerprints remain unchanged.
- [ ] Confirm new HTML references the new CSS fingerprint.

### Single JS change

- [ ] Change only one JS asset.
- [ ] Confirm its fingerprint changes.
- [ ] Confirm unrelated CSS/image fingerprints remain unchanged.
- [ ] Confirm no manual `?v=` update is required.

### Cloudflare behavior

- [ ] First request for a new fingerprinted asset is a normal MISS/DYNAMIC-to-cache transition as expected.
- [ ] Subsequent request becomes a HIT when eligible.
- [ ] Old and new fingerprints remain distinct cache objects.
- [ ] HTML never remains fresh for a long TTL across deployments.
- [ ] `/api/*` remains uncached unless explicitly opted in.

### Browser behavior

- [ ] Open the site before deployment.
- [ ] Deploy a visible CSS/JS change.
- [ ] Navigate/reload normally without a hard refresh.
- [ ] Confirm the new shell loads the new fingerprinted asset automatically.
- [ ] Confirm the developer does not need to clear browser cache.

### Nginx behavior

- [ ] Confirm no Nginx response cache is introduced.
- [ ] Confirm Nginx forwards cache validators/headers correctly.
- [ ] Confirm Nginx restart is not required for ordinary application deployments.

### Rollback behavior

- [ ] Deploy release A.
- [ ] Deploy release B with changed fingerprints.
- [ ] Roll back to A.
- [ ] Confirm A's shell and assets load without manual Cloudflare purge or browser-cache clearing.

---

## 22. Observability and troubleshooting

- [ ] Add deployment output that records:
  - [ ] Git commit/deployment revision;
  - [ ] Docker image tag and digest;
  - [ ] deployment start/end time;
  - [ ] health-check results;
  - [ ] public response cache headers;
  - [ ] targeted Cloudflare purge result if one was required.
- [ ] Add a lightweight deployment/version endpoint or response header if useful for confirming which application image is active.
  - [ ] This deployment identifier may use a Git SHA because it is diagnostic metadata, not an asset cache key.
- [ ] Document a stale-content troubleshooting order:
  - [ ] confirm deployed image/version;
  - [ ] inspect current HTML source and asset URLs;
  - [ ] inspect browser Network cache status;
  - [ ] inspect `CF-Cache-Status`/`Age`;
  - [ ] inspect origin `Cache-Control`;
  - [ ] confirm Nginx is not caching;
  - [ ] only then consider targeted Cloudflare purge.

---

## 23. Documentation updates

- [ ] Update `deploy/README.md` with:
  - [ ] production deployment architecture;
  - [ ] GHCR image naming/tagging;
  - [ ] GitHub Actions workflow behavior;
  - [ ] required GitHub environment/secrets;
  - [ ] Cloudflare Cache Rules;
  - [ ] cache header policy;
  - [ ] rollback procedure;
  - [ ] troubleshooting procedure.
- [ ] Document that content fingerprints, not Git SHAs or timestamps, invalidate browser static assets.
- [ ] Document that Git SHA image tags identify deployments only.
- [ ] Document that Nginx performs no HTTP response caching.
- [ ] Document which HTML shell is production-authoritative.
- [ ] Document whether `SaveNEIN.Client/wwwroot/index.html` remains necessary and for what purpose.

---

## 24. Recommended implementation sequence

- [ ] Step 1: establish the authoritative production HTML shell.
- [ ] Step 2: perform the .NET 10 `MapStaticAssets()` Release technical spike.
- [ ] Step 3: adopt native content fingerprinting and immutable static asset delivery.
- [ ] Step 4: add explicit HTML and API cache policies.
- [ ] Step 5: configure Cloudflare to respect the origin policy and bypass dynamic APIs.
- [ ] Step 6: create consolidated CI and fingerprint-validation checks.
- [ ] Step 7: publish immutable application images to GHCR.
- [ ] Step 8: convert production Compose to pull the exact built application image.
- [ ] Step 9: create automated deployment and public health verification.
- [ ] Step 10: test targeted/no-purge Cloudflare behavior.
- [ ] Step 11: remove manual `?v=` strings and redundant MVID/query cache-busters.
- [ ] Step 12: test rollback and stale-cache failure scenarios.
- [ ] Step 13: update deployment documentation.
- [ ] Step 14: move this plan from `pipeline` to `in-progress` only when implementation actually begins.
- [ ] Step 15: move this plan to `completed` only after all production acceptance criteria pass.

---

## 25. Production acceptance criteria

Do not call this work complete until all of the following are true:

- [ ] A normal merge/push to `main` can proceed through CI and production deployment without manually editing asset version strings.
- [ ] Browser-facing static cache invalidation is based on each asset's contents.
- [ ] Changing one static asset does not invalidate every unrelated static asset.
- [ ] The production HTML shell always revalidates or otherwise cannot remain stale across deployment.
- [ ] Fingerprinted assets are cached aggressively by browsers and Cloudflare.
- [ ] Dynamic APIs are not accidentally cached.
- [ ] Nginx performs no application-content caching.
- [ ] The exact Docker image built by CI is the image deployed to the VPS.
- [ ] Production deployment does not rebuild application source on the VPS.
- [ ] Public post-deploy health checks validate both application response and representative fingerprinted assets.
- [ ] A failed deployment has a documented/tested rollback path.
- [ ] Cloudflare `purge_everything` is not part of the normal deployment path.
- [ ] Manual date/timestamp cache-busters are absent from production local-asset references.
- [ ] Debug/local development still receives fresh CSS/JS changes without production-style cache friction.

---

## 26. Primary technical references

- Microsoft Learn, ASP.NET Core static files (.NET 10):
  - https://learn.microsoft.com/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0
- Microsoft Learn, ASP.NET Core Blazor static files (.NET 10):
  - https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0
- GitHub Docs, publishing Docker images/GHCR through Actions:
  - https://docs.github.com/actions/tutorials/publish-packages/publish-docker-images
- Cloudflare, Origin Cache Control:
  - https://developers.cloudflare.com/cache/concepts/cache-control/
- Cloudflare, Edge and Browser Cache TTL:
  - https://developers.cloudflare.com/cache/how-to/edge-browser-cache-ttl/
- Cloudflare, Cache Rules:
  - https://developers.cloudflare.com/cache/how-to/cache-rules/
- Cloudflare, cache purge behavior:
  - https://developers.cloudflare.com/cache/how-to/purge-cache/
