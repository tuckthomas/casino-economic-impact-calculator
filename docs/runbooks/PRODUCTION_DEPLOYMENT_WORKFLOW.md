# Production deployment workflow

This document defines how code moves from the Windows development checkout to
the production VPS. GitHub `main` is the source of truth. The VPS is a deployment
target and must not be used as the primary development copy.

## Release pipeline

This is a CI/CD workflow. Normal development uses native `dotnet watch`; Docker
is used for production-like validation in GitHub Actions and for the production
release, not for every local edit.

```text
M:\SaveNEIN -> GitHub pull request/main -> CI validates Docker image -> VPS deploys verified main
 development        source of truth          GitHub Actions             production
```

Production details:

- Canonical public site: `https://savenein.com`
- Redirect aliases: `https://www.savenein.com`, `https://savefw.com`, and `https://www.savefw.com`
- Windows checkout: `M:\SaveNEIN`
- SSH host alias: `savefw-vps`
- VPS checkout: `/opt/save-nein/app`
- Compose file: `/opt/save-nein/app/deploy/compose.production.yml`
- Environment file: `/opt/save-nein/deploy/.env`
- CI workflow: `.github/workflows/continuous-integration.yml`
- CD workflow: `.github/workflows/continuous-deployment.yml`

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

For application changes, use native hot reload against the VPS development
database and Valhalla tunnel:

```powershell
npm run dev
```

Use the container only when validating a Compose/Dockerfile change locally:

```powershell
npm run validate:container
```

GitHub Actions runs the production Dockerfile build on every pull request and
every push to `main`.

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

## 4. Automatic deployment to the VPS

A successful CI run for `main` triggers the deployment workflow. The workflow
uses a dedicated SSH key that is forced to run only the VPS deployment script.
That script refuses to deploy a dirty production checkout, fast-forwards it to
the verified `main` commit, rebuilds the production Compose app, and waits for
`https://savenein.com/` to return HTTP 200.

The repository must have these GitHub Actions secrets configured:

- `VPS_DEPLOY_HOST`
- `VPS_DEPLOY_USER`
- `VPS_DEPLOY_SSH_KEY`

The private production `.env`, database credentials, and application secrets
remain on the VPS and are never copied into GitHub Actions.

## 5. Verify production

On the VPS, verify container state and the origin response:

```bash
docker compose \
  --env-file /opt/save-nein/deploy/.env \
  -f /opt/save-nein/app/deploy/compose.production.yml \
  ps

curl -fsS -o /dev/null \
  -w 'origin HTTP %{http_code} in %{time_total}s\n' \
  https://savenein.com/
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

Do not reuse a personal SSH key for automation. The pipeline uses a dedicated
deployment identity with only the permissions required to update this
application. GitHub Actions secrets contain deployment-specific credentials,
never the production `.env` file or database contents.
