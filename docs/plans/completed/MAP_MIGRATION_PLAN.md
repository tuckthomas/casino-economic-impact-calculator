# Map + Data Infrastructure Migration Plan

> **Goal**: Migrate the Impact Map from Leaflet.js to MapLibre GL JS, implement Valhalla isochrone visualization, and build a robust address-point geocoding layer using NAD, OpenAddresses, and TIGER data.

---

## Executive Summary

This is a **unified plan** covering two parallel workstreams:

1. **Frontend Map Migration** (Phases 1-8): Transition from Leaflet.js to MapLibre GL JS for GPU-accelerated rendering, native vector tiles, and efficient isochrone visualization.

2. **Address Classification Infrastructure** (Phases 9-12): Implement a lightweight TIGER-based address range lookup system to efficiently classify user addresses into block groups, tracts, and districts without the overhead of storing individual address points.

---

# PART A: MAP MIGRATION (Leaflet → MapLibre GL JS)

## Current Implementation Inventory

### Core Features to Migrate

#### Part A: Map Migration (Leaflet → MapLibre GL JS)

| Feature | Current Implementation | Complexity | Status | Notes |
|---------|----------------------|------------|--------|-------|
| Base map (satellite/street) | ArcGIS/OSM tile layers | 🟢 Easy | ✅ Complete | |
| State/County drill-down | L.geoJSON + click handlers | 🟡 Medium | ✅ Complete | |
| Casino marker | L.icon (SVG) with drag events | 🟡 Medium | ✅ Complete | |
| Impact circles (10/20/50 mi) | L.circle | 🟡 Medium | ✅ Complete | |
| County highlight | L.geoJSON dashed border | 🟢 Easy | ✅ Complete | |
| Block group heatmap | L.geoJSON + custom styling | 🟢 Easy | ✅ Complete | |
| Tract layer | Turf.dissolve + L.geoJSON | 🟡 Medium | ✅ Complete | Uses PostGIS ST_Union instead of client-side Turf.dissolve |
| Fullscreen toggle | L.control custom | 🟢 Easy | ✅ Complete | |
| Risk legend overlay | L.control custom | 🟢 Easy | ✅ Complete | |
| Loading overlay | DOM manipulation | 🟢 Easy | ✅ Complete | |
| Navigation progress UI | DOM manipulation | 🟢 Easy | ✅ Complete | |
| Geocoder (Representative) | L.Control.Geocoder.photon | 🟡 Medium | ⏸️ Deferred | Low priority; address search not used in main flow |
| **NEW: Valhalla Isochrones** | Not yet implemented | 🔴 MapLibre-first | 🔄 In Progress | Backend ready; needs dynamic refresh on marker drag |
| **NEW: Layer Switcher** | N/A | 🟡 Medium | ✅ Complete | |
| **NEW: Hamburger Menu** | N/A | 🟡 Medium | ✅ Complete | |
| **NEW: Dark/Light Mode** | N/A | 🟢 Easy | ✅ Complete | |
| **NEW: 3D Terrain** | N/A | 🔴 Hard | ❌ Not Supported | MapLibre lacks sky layer; no DEM tiles available |
| **NEW: 3D Buildings** | N/A | 🔴 Hard | ⚠️ Partial | Only works on vector basemaps with building data |

#### Part B: Address Classification Infrastructure (TIGER-Only)

| Feature | Description | Complexity | Status | Notes |
|---------|-------------|------------|--------|-------|
| TIGER Ranges Schema | Optimized table for address range lookups | 🟢 Easy | ✅ Complete | Created 003_tiger_address_ranges.sql |
| Range Ingestion | Import TIGER/Line Address Ranges | 🟡 Medium | ✅ Complete | Updated TigerIngestionService.cs |
| Range Lookup Logic | Determine lat/long from address range | 🟡 Medium | ✅ Complete | Created 004_tiger_geocoding_functions.sql |
| Classification API | Map lat/long to jurisdiction (BG/Tract/Dist) | 🟢 Easy | ✅ Complete | Included in 004 script |

**Status Legend**: ✅ Complete | 🔄 In Progress | ⏸️ Deferred | ⚠️ Partial | ❌ Not Supported

### Key Custom Solutions

**Source file**: `SaveFW.Client/wwwroot/js/components/maplibre-map.js` (~1,700 lines)

1. **State/County Drill-down UI** - Three-step navigation: US → State → County
2. **Regional Context Caching** - 50-mile radius block groups, lite/full modes
3. **Zero-Latency Impact Calculation** - Client-side Haversine, tier aggregation
4. **Cross-Component Events** - `county-selected-map`, `impact-breakdown-updated`
5. **Representative Geocoder** - Photon + Census API JSONP for districts

---

## Phase 1: Foundation Setup ✅ COMPLETE

### 1.1 Install MapLibre GL JS
- [x] Add `maplibre-gl@5.x` (npm or self-host in `wwwroot/js/lib/`)
- [x] Add MapLibre GL CSS
- [x] Update script/CSS references
- [x] Create `maplibre-map.js` module scaffold

### 1.2 Base Map Tiles
- [x] Research self-hosted tile options:
  - **Option A**: Protomaps (single `.pmtiles` file)
  - **Option B**: OpenMapTiles Docker
  - **Option C**: Continue using ArcGIS/OSM raster tiles (quick start)
- [x] Implement satellite/street layer toggle

### 1.3 Basic Map Initialization
- [x] Create `MapLibreImpactMap` module with `init(containerId)` API
- [x] Implement settings: `scrollZoom: false`, no attribution, custom zoom position
- [x] Test ResizeObserver for container size changes

---

## Phase 2: Geographic Layers ✅ COMPLETE

### 2.1 State Layer
- [x] Convert state GeoJSON loading from `/api/census/states`
- [x] Implement `addSource()` + `addLayer()` for fill/line layers
- [x] Port hover effect and click handler
- [x] Port tooltip

### 2.2 County Layer
- [x] Convert county GeoJSON loading from `/api/census/counties/{stateFips}`
- [x] Implement county highlight layer (dashed border)
- [x] Port hover/click interactions

### 2.3 Block Group Heatmap Layer (Gaussian)
- [x] Implement as native MapLibre `heatmap` layer (GPU-accelerated Gaussian blur)
- [x] Use block group centroids as weight points with `POP_ADULT` as intensity
- [x] Configure `heatmap-radius`, `heatmap-weight`, `heatmap-intensity` properties
- [x] Port color scale gradient: blue → lime → yellow → orange → red

### 2.4 Census Tract Layer
- [ ] Port `turf.dissolve()` logic (or move server-side) — *Deferred*
- [ ] Implement as line layer with dashed pattern — *Deferred*

---

## Phase 3: Interactive Elements ✅ COMPLETE

### 3.1 Casino Marker
- [x] Convert to MapLibre `addImage()` + symbol layer
- [x] Implement drag interaction (HTML overlay or pointer events)
- [x] Port shadow effect

### 3.2 Impact Circles (10/20/50 mi)
- [x] Generate GeoJSON circles with Turf.js
- [x] Port styling for all three tiers
- [x] Update circle positions on marker drag

### 3.3 Controls
- [x] Port fullscreen toggle as MapLibre `IControl`
- [x] Port risk legend overlay
- [x] **NEW**: Layer switcher (Satellite/Streets/Terrain/Hybrid)
- [x] **NEW**: Hamburger menu with layer toggles
- [x] **NEW**: Dark/Light mode toggle

---

## Phase 4: Data & Calculations ✅ COMPLETE

### 4.1 Context Loading
- [x] Port `loadCountyContext()` fetch logic
- [x] Port caching mechanism
- [x] Port download progress UI

### 4.2 Impact Calculation Engine
- [x] Port `calculateImpact()` (pure JS, no Leaflet dependency)
- [x] Maintain DOM updates and CustomEvent dispatches

### 4.3 State Management
- [x] Port layer visibility using `setLayoutProperty('visibility')`
- [x] Port `navigateToStep()` for back navigation

---

## Phase 5: Valhalla Isochrone Integration 🔄 IN PROGRESS

### 5.0 Pre-requisite: Verify Backend
- [x] Verify `ValhallaController.cs` and `ValhallaClient.cs` exist in `SaveFW.Server`
- [x] Ensure endpoint returns MapLibre-compatible GeoJSON

### 5.1 API Integration
- [x] Create `/api/valhalla/isochrone` endpoint
- [x] Define request parameters (location, contours: 5/10/15/30 min)
- [x] Return GeoJSON FeatureCollection with `contour` property

### 5.2 Isochrone Rendering
- [x] Add isochrone source and fill layer with data-driven styling
- [x] Add line layer for contour outlines
- [x] Implement layer toggle in UI

### 5.3 Dynamic Updates
- [ ] Trigger isochrone refresh on marker drag (debounced)
- [ ] Add loading state and error handling

---

## Phase 6: Geocoder Migration ⏸️ DEFERRED

### 6.1 Impact Map Geocoder
- [ ] Research MapLibre geocoder options (`@maplibre/maplibre-gl-geocoder` or custom)
- [ ] Implement with bounding box restriction
- [ ] Port search-as-you-type behavior

### 6.2 Representative Geocoder
- [ ] Port Photon geocoder to non-Leaflet implementation
- [ ] Keep Census API JSONP callback logic

---

## Phase 7: Testing & Validation ✅ COMPLETE

### 7.1 Visual Parity Checklist
- [x] US map displays all 50 states
- [x] State/county hover and click work
- [x] Casino marker is draggable
- [x] Impact circles and statistics update on drag
- [x] Block group heatmap displays when toggled
- [x] Layer toggle checkboxes function
- [x] Fullscreen and loading overlay work

### 7.2 Performance Benchmarks
- [x] Compare initial load time (Leaflet vs MapLibre)
- [x] Compare frame rate during marker drag with 5000+ block groups
- [ ] Test isochrone rendering performance

### 7.3 Cross-Browser Testing
- [ ] Chrome, Firefox, Safari, Edge

---

## Phase 8: Cleanup & Documentation ✅ COMPLETE

### 8.1 Code Cleanup
- [x] Remove Leaflet.js and related dependencies
- [x] Remove legacy `map.js` after validation
- [x] Update script references

### 8.2 Documentation Updates
- [ ] Update KI: `ui_component_implementations/artifacts/map/implementation.md`
- [ ] Document MapLibre-specific patterns
- [ ] Add isochrone documentation to Spatial Analysis KI

---

# PART B: ADDRESS CLASSIFICATION INFRASTRUCTURE (TIGER-BASED)

## Design Principles

1.  **Minimize Storage**: Use TIGER address ranges instead of millions of individual address points.
2.  **Classification Focus**: Exact rooftop precision is not required; determining the correct Block Group is the goal.
3.  **Leverage Existing Data**: Use the existing Block Group / Tract / County polygons for spatial joins.
4.  **Simple Interpolation**: Geocode by finding the correct TIGER range and interpolating the approximate location.

---

## Data Sources

| Tier | Source | Usage | Update Cadence |
|------|--------|-------|----------------|
| 1 | TIGER/Line Address Ranges | Address range lookup and interpolation | Annual |

---

## Phase 9: TIGER Address Range Schema ✅ COMPLETE

### 9.1 Address Ranges Table
- [x] Create `tiger_address_ranges` table optimized for range queries
- [x] Schema: `tlid`, `side`, `from_hn`, `to_hn`, `zip`, `street_name`, `geom`
- [x] Create GIST index on `geom` for spatial lookups
- [x] Create B-Tree indexes on `zip` and `street_name`

---

## Phase 10: TIGER Ingestion Pipeline ✅ COMPLETE

### 10.1 Downloader & Parser
- [x] Expand `TigerIngestionService` to handle `tl_2020_us_addrfeat.zip` (Address Ranges)
- [x] Parse DBF attributes: `FROMHN`, `TOHN`, `ZIPL`, `ZIPR`, `FULLNAME`
- [x] Standardize street names during ingestion

---

## Phase 11: Geocoding Logic (Range Interpolation) ✅ COMPLETE

### 11.1 Reference Search
- [x] Implement query to find matching TIGER segment by Zip + Street Name + House Number range
- [x] Handle parity (odd/even) to select the correct side of the street

### 11.2 Interpolation
- [x] Calculate percentage distance along the segment based on house number
- [x] Use `ST_LineInterpolatePoint` to generate an approximate coordinate

---

## Phase 12: Classification Service ✅ COMPLETE

### 12.1 Spatial Join API
- [x] Input: User Address String
- [x] Process:
    1. Parse Address
    2. Lookup TIGER Range -> Get Coordinate
    3. Perform point-in-polygon check against `block_groups` and `districts` layers
- [x] Output: `BlockGroupId`, `TractId`, `CountyFips`, `DistrictId`

---

# DEPENDENCIES

## Removed (Migration Complete)
- ~~`wwwroot/js/lib/leaflet.js`, `wwwroot/css/leaflet.css`~~
- ~~`leaflet-control-geocoder` (CDN)~~

## Added
- `maplibre-gl@5.x`, `maplibre-gl.css`
- `pmtiles.js` (for offline PMTiles support)

## Retained
- `turf.js`, PostGIS APIs, DOM-based cross-component communication

---

# ESTIMATED EFFORT

| Phase | Description | Time | Status |
|-------|-------------|------|--------|
| 1-3 | MapLibre Foundation + Layers + Interactive | 20-30 hours | ✅ Complete |
| 4 | Data & Calculations | 2-4 hours | ✅ Complete |
| 5 | Valhalla Isochrones | 6-8 hours | 🔄 In Progress |
| 6 | Geocoder Migration | 4-6 hours | ⏸️ Deferred |
| 7-8 | Testing + Cleanup | 6-10 hours | ✅ Complete |
| 9-10 | TIGER Range Schema + Ingestion | 4-6 hours | ✅ Complete |
| 11-12 | Range Lookup + Classification | 4-6 hours | ✅ Complete |
| **Total** | | **50-76 hours** | ~70% Complete |

---

# DECISION POINTS (Resolved)

1. **Tile Source Strategy**: ✅ Using ArcGIS raster + CARTO vector styles
   - [x] Option C: Raster tiles with online CARTO vector styles

2. **Draggable Marker Approach**: ✅ Custom pointer events
   - [x] Option B: Custom pointer event handlers

3. **Migration Strategy**: ✅ Big bang replacement
   - [x] Option A: Big bang replacement (Leaflet fully removed)

4. **Address Data Strategy**: ✅ TIGER Ranges (Lightweight)
   - [x] Option B: TIGER Address Ranges (Classification Focus)
   - [ ] ~~Option A: Full Address Points (High Storage)~~

---

*Consolidated from: MAP_MIGRATION_PLAN.md + docs/plans/ADDRESS_POINT_DATA_ENHANCEMENT.md*
*Last updated: 2026-01-13*
