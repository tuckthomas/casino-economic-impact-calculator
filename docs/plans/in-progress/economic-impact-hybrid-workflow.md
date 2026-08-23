# HIGH PRIORITY: Restore the two-part economic impact workflow

Status: Implemented  
Owner: Codex  
Scope: SaveNEIN client economic-impact experience and its supporting public APIs

## Objective

Restore the useful public, client-side economic-impact experience from `main` while preserving the newer server-side model as the authoritative second-stage analysis. Keep the homepage informational and keep the workflow public with no login requirement.

## User workflow

### Part 1 — Location and impact zones

Route: `/economic-impact/location`

- Restore the map, cached geographic data, location controls, and map interactions from `main`.
- Allow state, county, and candidate-location selection.
- Allow the user to configure the number of risk tiers.
- Allow the user to configure the mile range for each tier.
- Display the resulting zones and geographic context on the map.
- Keep map data server-provided and cached; run interactive zone calculations in the browser.
- Save a complete scenario snapshot without requiring login.
- Navigate to Part 2 using an opaque, session-specific scenario token.

### Part 2 — Variables and analysis

Route: `/economic-impact/variables`

- Load Part 1 location, geographic selections, zone definitions, and risk-tier configuration from the saved scenario token.
- Restore the old social-cost inputs, simulator controls, tables, graphs, and automated analysis from `main`.
- Allow users to edit cost inputs and simulator assumptions before calculating.
- Preserve the newer server-side model as the authoritative calculation/reporting layer where applicable.
- Clearly distinguish client-side preview outputs from saved server-side model results.

## Data and security constraints

- No authentication or login wall.
- Do not persist secrets in the repository or browser payloads.
- Use opaque scenario tokens rather than user identity.
- Apply expiration, payload-size limits, validation, and rate limiting to saved scenarios and server requests.
- Store the exact client-model version, map/data version, zone definitions, inputs, and preview outputs needed for auditability.
- Do not send full map datasets back to the server; persist selections and version identifiers.

## Implementation sequence

1. Compare the current branch with `main` and identify the map, simulator, cost, graph, table, and automated-analysis assets to restore.
2. Create the two routed pages and move the existing server-side model out of the homepage.
3. Restore Part 1 map/location content and its required cached geographic assets.
4. Restore Part 2 variables and analysis content without reintroducing obsolete endpoint dependencies.
5. Add the scenario snapshot/token handoff between the two routes.
6. Update navigation and homepage links.
7. Validate client-side calculations, server handoff, route loading, and build output.

## Acceptance criteria

- Homepage remains informational and does not load the heavy calculator/map workflow.
- `/economic-impact/location` loads the map and supports configurable impact zones.
- `/economic-impact/variables` receives and displays the Part 1 scenario context.
- Cost inputs, simulator controls, tables, graphs, and automated analysis are available in Part 2.
- No login is required.
- Interactive Part 1 changes do not trigger a server calculation for every adjustment.
- Saved scenarios preserve enough metadata to reproduce the client-side result.
- The client project builds successfully; the full solution build is blocked only when the running dev server holds its output DLL open. All restored `/api/Impact/*` calls now have their corresponding server controller restored.

## Implementation notes

- Part 1 was restored from `main` into `EconomicImpactLocation.razor` and defaults to browser-side radius previews so zone adjustments do not trigger server calculations.
- Part 2 was restored from `main` into `EconomicImpactVariables.razor`; it loads the saved Part 1 context through the opaque scenario token.
- `/economic-impact/model` remains the newer server-side model route.
- The restored map/calculator assets retain the original `/api/Impact/*` calls because they are functional map capabilities. `ImpactController` was restored from `main`, including county context, cached isochrones, live Valhalla isochrones, and grid-point loading.
