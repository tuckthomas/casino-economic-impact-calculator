# Isochrone Seeding Runbook

This doc captures the current Allen County isochrone seeding flow, parameters,
and how to reproduce or extend the run.

## Current Allen County configuration

Source of truth: `SaveNEIN.Server/appsettings.json`

- County: Allen County, IN (`state_fips=18`, `name=Allen`)
- Grid spacing: 8,000 meters
- Contours: 5..120 minutes in 5-minute increments
- Valhalla contour batch size: 4 per request (Valhalla max contours = 4)
- Cache table: `isochrone_cache`
- Run log table: `isochrone_runs`

## How to run

```bash
cd /opt/save-nein/app/SaveNEIN.Server
/root/.dotnet/dotnet run -- --run-allen-isochrones
```

## What it does

1) Builds grid points inside the county boundary (PostGIS `ST_SquareGrid`).
2) Batches contour minutes into groups of 4 and calls Valhalla `/isochrone`.
3) Inserts polygons into `isochrone_cache` (one row per contour per point).
4) Writes a run summary to `isochrone_runs` with timing and hardware stats.

## Run metadata

Each run writes a summary record to `isochrone_runs`:

- Timings: start/end, request counts, min/avg/max request time.
- Coverage: grid spacing, points, intervals.
- Hardware: CPU model/cores, RAM (or fallback note).
- County area in square miles (computed from geometry).

Example query:

```sql
SELECT id, started_at, completed_at, grid_meters, point_count,
       request_count, inserted_isochrones, avg_request_ms
FROM isochrone_runs
ORDER BY id DESC
LIMIT 1;
```

## Verification queries

How many cached isochrones exist and how many points were covered:

```sql
SELECT COUNT(*) AS total_rows,
       COUNT(DISTINCT (lat, lon)) AS point_count
FROM isochrone_cache;
```

Check that a specific contour exists:

```sql
SELECT COUNT(*) AS contour_rows
FROM isochrone_cache
WHERE minutes = 120;
```

## Notes

- Valhalla rejects requests with more than 4 contours.
- The run log is intended for future cost/quote estimates.
