# Production deployment workflow

This document defines how code moves from the Windows development checkout to
the production VPS. GitHub `main` is the source of truth. The VPS is a deployment
target and must not be used as the primary development copy.

## Current state

The current release process is a **manual deployment workflow**, not a complete
CI/CD pipeline. Changes are developed locally, pushed to GitHub, and then pulled
and rebuilt on the VPS. The desired future state is described under
[Planned CI/CD pipeline](#planned-cicd-pipeline).

```text
M:\SaveNEIN  ->  GitHub main  ->  /opt/save-nein/app on the VPS
 development       source             production
```

Production details:

- Public site: `https://savefw.com`
- Windows checkout: `M:\SaveNEIN`
- SSH host alias: `savefw-vps`
- VPS checkout: `/opt/save-nein/app`
- Compose file: `/opt/save-nein/app/deploy/compose.production.yml`
- Environment file: `/opt/save-nein/deploy/.env`

The real environment file, database data, TLS private keys, certificates,
generated Valhalla files, and generated geographic data must never be committed.

## 1. Start local work from current `main`

Run these commands from PowerShell:

```powershell
Set-Location M:\SaveNEIN
git switch main
git pull --ff-only origin main
git status --short --branch
```

The working tree should be clean before starting unrelated work. Make and test
changes only in the local checkout. Do not edit production files directly on the
VPS.

## 2. Validate local changes

Choose checks that cover the files being changed. At minimum:

```powershell
git diff --check
docker compose --env-file deploy/.env.example `
  -f deploy/compose.production.yml config --quiet
```

For application changes, validate the production image when Docker Desktop is
available:

```powershell
docker build --tag savenein-validation:local .
```

The repository currently uses Unix `cp` commands in `npm run copy-libs`, so a
native Windows `dotnet build` is not a reliable validation path until that
cross-platform issue is corrected. The Docker build uses the same Linux build
environment as production.

## 3. Commit and publish to GitHub `main`

Review the exact files before staging them:

```powershell
git status --short
git diff
git add <files-that-belong-to-the-change>
git diff --cached
git commit -m "Describe the change"
git push origin main
```

For a larger or riskier change, a feature branch and pull request can be used,
but the change is not ready for production until it has been merged into GitHub
`main`.

## 4. Deploy GitHub `main` to the VPS

Connect from Windows:

```powershell
ssh savefw-vps
```

Then run on the VPS:

```bash
cd /opt/save-nein/app

# Stop if this reports local modifications. Production must remain pull-only.
git status --short --branch

git fetch origin main
git merge --ff-only origin/main

docker compose \
  --env-file /opt/save-nein/deploy/.env \
  -f deploy/compose.production.yml \
  up -d --build --remove-orphans
```

Never use `git reset --hard` to hide unexplained production changes. Determine
why the checkout is dirty before deploying.

## 5. Verify production

On the VPS, verify container state and the origin response:

```bash
docker compose \
  --env-file /opt/save-nein/deploy/.env \
  -f /opt/save-nein/app/deploy/compose.production.yml \
  ps

curl -fsS -o /dev/null \
  -w 'origin HTTP %{http_code} in %{time_total}s\n' \
  https://savefw.com/
```

The application, database, and Nginx containers must report healthy. Valhalla
must report running, and the public request must return HTTP 200. Application or
geospatial changes should also be checked through the affected browser workflow.

## Failed deployment and rollback

A failed image build normally leaves the previously running containers in
place. If a newly started release is unhealthy:

1. Record the failing commit and container logs.
2. Identify the last known-good commit with `git log --oneline`.
3. Revert the failing commit on GitHub `main` and push the revert.
4. Pull the new revert commit on the VPS and rebuild with the production Compose
   command above.

Do not roll application code backward across an incompatible database migration
without a tested database recovery plan. Take a logical backup of non-reseedable
visitor data before deploying schema-changing releases.

## Planned CI/CD pipeline

The recommended automated pipeline is:

1. A push to GitHub `main` triggers GitHub Actions.
2. Actions restores dependencies and runs validation.
3. Actions builds a production container image tagged with the Git commit SHA.
4. The image is pushed to GitHub Container Registry.
5. A dedicated, restricted deployment credential tells the VPS to pull that
   exact image.
6. The VPS starts the release and runs health checks.
7. A failed health check retains or restores the previous known-good image.

This is the point at which the process becomes a CI/CD pipeline: **continuous
integration** validates and builds every change, while **continuous deployment**
promotes a successful `main` build to the VPS.

Do not reuse a personal SSH key for automation. The pipeline should use a
dedicated deployment identity with only the permissions required to update this
application. GitHub Actions secrets must contain only deployment-specific
credentials, never the production `.env` file or database contents.

