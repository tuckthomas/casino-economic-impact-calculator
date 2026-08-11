#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SaveNEIN Advanced Economic Modeling Subsystem
# Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
# Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

set -euo pipefail

validation_db="${1:?validation database name is required}"
validation_dir="${2:?validation publish directory is required}"
bundle_file="${3:?Michigan provider bundle path is required}"
validation_created=0

case "$validation_db" in
  savenein_provider_validation_[a-z0-9]*) ;;
  *) echo "Unsafe validation database name" >&2; exit 64 ;;
esac
case "$validation_dir" in
  /tmp/savenein-provider-validation.*) ;;
  *) echo "Unsafe validation temp path" >&2; exit 64 ;;
esac
case "$bundle_file" in
  "$validation_dir"/michigan-provider-bundle.json) ;;
  *) echo "Unsafe Michigan provider bundle path" >&2; exit 64 ;;
esac

validation_compose=(
  docker compose
  --env-file /opt/save-nein/deploy/.env
  -f /opt/save-nein/app/deploy/compose.production.yml
)

cleanup_validation() {
  validation_status=$?
  trap - EXIT
  set +e
  if [ "$validation_created" -eq 1 ]; then
    if "${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
      sh -lc 'dropdb --if-exists -U "$POSTGRES_USER" "$VALIDATION_DB"' >/dev/null
    then
      echo "validation_database_removed=$validation_db"
    else
      echo "Failed to remove validation database $validation_db" >&2
      if [ "$validation_status" -eq 0 ]; then validation_status=1; fi
    fi
  fi
  if rm -rf -- "$validation_dir"; then
    echo "validation_temp_directory_removed=$validation_dir"
  elif [ "$validation_status" -eq 0 ]; then
    validation_status=1
  fi
  exit "$validation_status"
}
trap cleanup_validation EXIT

"${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
  sh -lc 'createdb -U "$POSTGRES_USER" "$VALIDATION_DB"'
validation_created=1
"${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
  sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$VALIDATION_DB" -c "CREATE EXTENSION IF NOT EXISTS postgis;"'

"${validation_compose[@]}" run --rm --no-deps \
  --entrypoint dotnet \
  -v "$validation_dir/publish:/validation:ro" \
  -v "$bundle_file:/input/michigan-provider-bundle.json:ro" \
  app \
  /validation/GravityModelIntegrationHarness.dll \
  --validate-michigan-provider-bundle \
  "$validation_db" \
  /input/michigan-provider-bundle.json

echo "Remote Michigan provider-bundle PostGIS ingestion validation passed."
