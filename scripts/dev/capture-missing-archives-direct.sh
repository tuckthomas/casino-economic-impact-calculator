#!/usr/bin/env bash
set -uo pipefail

compose=(docker compose --env-file /opt/save-nein/deploy/.env -f /opt/save-nein/app/deploy/compose.production.yml)
source_keys=(
  yes-for-allen-facts
  steuben-myths-facts
  cbre-greater-fort-wayne-casino-analysis
  bea-rims-ii-user-guide
  indiana-gaming-commission-cy2001-annual-report
  spectrum-relocation-study
  indian-gaming-regulatory-act
  ecfr-25-cfr-part-292
  interior-gaming-land-decisions
  indiana-gaming-commission-fy2021-annual-report
  indiana-2026-legislative-synopsis
  indiana-northeast-casino-guidance
  wiley-walker-detroit-case
  siegel-anders-displacement-effects
  st-louis-fed-casinos-economic-development
)

if [ "$#" -gt 0 ]; then
  source_keys=("$@")
fi

for key in "${source_keys[@]}"; do
  status=$("${compose[@]}" exec -T -e SOURCE_KEY="$key" app sh -lc 'curl -sS -o /dev/null -w "%{http_code}" http://localhost:8080/api/web-archives/$SOURCE_KEY/latest')
  if [ "$status" = "200" ]; then
    echo "$key EXISTS"
    continue
  fi

  echo "$key CAPTURING"
  "${compose[@]}" exec -T -e SOURCE_KEY="$key" app sh -lc 'curl -sS -o /tmp/archive-capture-response -w "%{http_code}" -X POST -H "X-Archive-Capture-Token: $ArchiveBox__CaptureAdminToken" http://localhost:8080/api/web-archives/capture/$SOURCE_KEY'
  echo
done
