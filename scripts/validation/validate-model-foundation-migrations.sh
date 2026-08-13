#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SaveNEIN Advanced Economic Modeling Subsystem
# Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
# Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

set -euo pipefail

validation_db="${1:?validation database name is required}"
validation_dir="${2:?validation SQL directory is required}"
validation_created=0

case "$validation_db" in
  savenein_migration_validation_[a-z0-9]*) ;;
  *) echo "Unsafe validation database name" >&2; exit 64 ;;
esac
case "$validation_dir" in
  /tmp/savenein-migration-validation.*) ;;
  *) echo "Unsafe validation temp path" >&2; exit 64 ;;
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
      if [ "$validation_status" -eq 0 ]; then
        validation_status=1
      fi
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

for validation_pass in 1 2; do
  echo "Applying migrations, pass $validation_pass"
  for validation_file in \
    005_casino_competitors.sql \
    006_gravity_model_foundation.sql \
    007_model_data_foundation.sql \
    008_gravity_engine.sql \
    009_market_expansion.sql \
    010_tourism_traffic_capacity_ramp.sql \
    011_comprehensive_impact_accounting.sql \
    012_validation_and_calibration.sql \
    013_stored_run_reports.sql \
    014_indiana_benchmark_evidence.sql \
    015_sensitivity_analyses.sql \
    016_local_economic_inventory.sql \
    017_nullable_facility_evidence_flags.sql \
    018_candidate_location_travel_cache.sql \
    019_indiana_benchmark_reconciliation_outputs.sql \
    020_coordinate_versioned_incumbent_travel_cache.sql \
    021_component_gaming_fiscal_allocation.sql \
    022_employment_assumption_provenance.sql \
    023_capacity_productivity_benchmark_provenance.sql \
    024_reported_casino_employment.sql \
    025_validation_geographic_residual_patterns.sql
  do
    echo "  $validation_file"
    "${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
      sh -lc 'psql -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$VALIDATION_DB"' \
      < "$validation_dir/$validation_file"
  done
done

"${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
  sh -lc 'psql -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$VALIDATION_DB"' \
  < "$validation_dir/validation/model_foundation_smoke_test.sql"

"${validation_compose[@]}" exec -T -e VALIDATION_DB="$validation_db" db \
  sh -lc 'psql -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d "$VALIDATION_DB" -c "SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal AND tgname LIKE '\''trg_prevent_%'\'';"' \
  | sed 's/^/immutability_trigger_count=/'

echo "Remote model-foundation migration validation passed."
