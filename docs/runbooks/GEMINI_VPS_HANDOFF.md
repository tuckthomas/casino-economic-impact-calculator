# Gemini production handoff

Give Gemini the instructions below before asking it to modify or deploy this
project.

```text
You are working on the SaveNEIN production application.

Repository and production topology:
- Windows development checkout: M:\SaveNEIN
- GitHub repository: tuckthomas/casino-economic-impact-calculator
- GitHub main is the source of truth.
- SSH alias from Windows: savefw-vps
- VPS checkout: /opt/save-nein/app
- Compose file: /opt/save-nein/app/deploy/compose.production.yml
- Production environment file: /opt/save-nein/deploy/.env
- Canonical site: https://savenein.com
- www.savenein.com, savefw.com, and www.savefw.com redirect to the canonical site.

Development rules:
1. Make changes only in M:\SaveNEIN. Do not develop directly on the VPS.
2. Before editing, run:
   Set-Location M:\SaveNEIN
   git switch main
   git pull --ff-only origin main
   git status --short --branch
3. Preserve unrelated or untracked user files. Never use git reset --hard or
   discard unexplained changes.
4. Read .agents/AGENTS.md. Never introduce text-xs or an arbitrary UI text size below
   14px. The pre-commit hook and GitHub workflow enforce this.
5. Validate relevant JavaScript with node --check, run npm run check:ui-text,
   and run git diff --check. For a full application build, use the Linux Docker
   build because the current npm copy-libs script is not Windows-native.
6. Review and stage only files belonging to the task, commit to main, and push:
   git add <explicit files>
   git diff --cached
   git commit -m "Clear description"
   git push origin main

After local changes are pushed, deploy them from Windows with:
ssh savefw-vps "cd /opt/save-nein/app && git status --short --branch && git pull --ff-only origin main && docker compose --env-file /opt/save-nein/deploy/.env -f deploy/compose.production.yml up -d --build --remove-orphans"

If the VPS git status reports local modifications, stop and investigate. Do
not overwrite them. After deployment, verify:
ssh savefw-vps "cd /opt/save-nein/app && git rev-parse HEAD && docker compose --env-file /opt/save-nein/deploy/.env -f deploy/compose.production.yml ps"

Confirm local HEAD, origin/main, and VPS HEAD are identical. Then test
https://savenein.com in a real browser, check the affected desktop and mobile
workflow, and inspect browser errors. Verify that www and legacy SaveFW hosts
return a permanent redirect to https://savenein.com while preserving the path
and query string.

Never print, copy, rotate, or commit /opt/save-nein/deploy/.env, TLS private
keys, database credentials, database data, or generated Valhalla data. Before
schema migrations, back up non-reseedable visitor/contact tables. A failed
release should be reverted on GitHub main and redeployed; do not conceal it with
a destructive reset on production.
```

The full workflow, including rollback guidance, is in
`docs/runbooks/PRODUCTION_DEPLOYMENT_WORKFLOW.md`.
