# Production deployment

The production stack runs the ASP.NET application, PostgreSQL/PostGIS,
Valhalla, and Nginx as separate containers. Only Nginx publishes ports 80 and
443. Keep the real `.env` file outside the repository.

See [the production deployment workflow](../docs/runbooks/PRODUCTION_DEPLOYMENT_WORKFLOW.md)
for the complete local-to-GitHub-to-VPS release process, validation, rollback,
and planned CI/CD design.

```bash
docker compose \
  --env-file /opt/save-nein/deploy/.env \
  -f /opt/save-nein/app/deploy/compose.production.yml \
  up -d --build
```

TLS certificates are stored in `/etc/letsencrypt` and renewed by the included
systemd timer. Never commit `.env`, private keys, certificates, generated map
tiles, or database data.
