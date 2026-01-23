/**
 * MapLibre GL JS Impact Map - Complete Implementation
 * 
 * Full replacement for Leaflet-based map.js, providing GPU-accelerated rendering,
 * native vector tile support, Valhalla isochrone visualization, and complete
 * impact calculation engine.
 *
 * @module MapLibreImpactMap
 * @version 2.0.0 - Full Migration
 */

window.MapLibreImpactMap = (function ()
{
    'use strict';

    // === CONSTANTS ===
    const MILE_TO_METERS = 1609.34;
    const EARTH_RADIUS_MILES = 3958.7613;
    const TO_RAD = Math.PI / 180;

    const CIRCLE_RADII = {
        tier1: 10 * MILE_TO_METERS,
        tier2: 20 * MILE_TO_METERS,
        tier3: 50 * MILE_TO_METERS
    };

    const TIER_COLORS = {
        tier1: '#3b82f6',
        tier2: '#ef4444',
        tier3: '#f59e0b'
    };

    const ISOCHRONE_COLORS = {
        5: '#22c55e',
        10: '#84cc16',
        15: '#eab308',
        30: '#ef4444'
    };

    const DEFAULT_CENTER = [-98.35, 39.5];
    const DEFAULT_ZOOM = 3.5; // Zoomed out enough to see continental US

    // === STATE ===
    let map = null;
    let draw = null;
    let marker = null;
    let currentStateFips = null;
    let currentCountyFips = null;
    let markerPosition = null;
    let stateData = null;
    let countyData = null;

    // Context data for calculations
    let currentContextGeoJSON = null;
    let currentCalcFeatures = null;
    let currentCountyTotals = null;
    let contextIsLite = false;
    let contextCache = {};
    let activePrefetches = []; // Track background prefetch controllers
    // Population counts (Global for sharing between grid/radius modes)
    let t1PopRegional = 0, t2PopRegional = 0, t3PopRegional = 0;
    let t1PopCounty = 0, t2PopCounty = 0, t3PopCounty = 0;

    // Per-county breakdown storage
    let byCounty = {};

    // Name Resolution Cache
    const countyNamesCache = {};
    const stateCountiesLoaded = new Set();

    // Cache references
    const cache = {
        states: null,
        counties: {},
        context: {}
    };

    // Layer visibility state
    const layersVisible = {
        zones: true,
        boundary: true,
        overlay: true,
        blocks: false,
        tracts: false,
        heatmap: false,
        streets: false,
        terrain3d: false,
        buildings3d: false
    };

    // Risk Zone Mode: 'radius' or 'isochrone'
    let riskZoneMode = 'radius';

    // Current basemap
    let currentBasemap = 'offline';
    let mapDarkMode = true; // Default to dark mode for streets/terrain

    // Basemap configurations
    const BASEMAPS = {
        offline: {
            name: 'Offline',
            icon: 'public_off',
            style: {
                version: 8,
                sources: {},
                layers: [{
                    id: 'background',
                    type: 'background',
                    paint: { 'background-color': '#020617' } // slate-950
                }]
            }
        },
        satellite: {
            name: 'Satellite',
            icon: 'satellite_alt',
            style: {
                version: 8,
                sources: {
                    'satellite-tiles': {
                        type: 'raster',
                        tiles: ['https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}'],
                        tileSize: 256
                    }
                },
                layers: [{ id: 'satellite-layer', type: 'raster', source: 'satellite-tiles' }]
            }
        },
        streets: {
            name: 'Streets',
            icon: 'map',
            darkStyle: 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json',
            lightStyle: 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json',
            get style() { return mapDarkMode ? this.darkStyle : this.lightStyle; }
        },
        terrain: {
            name: 'Terrain',
            icon: 'terrain',
            darkStyle: 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json',
            lightStyle: 'https://basemaps.cartocdn.com/gl/voyager-gl-style/style.json',
            get style() { return mapDarkMode ? this.darkStyle : this.lightStyle; }
        },
        hybrid: {
            name: 'Hybrid',
            icon: 'layers',
            style: {
                version: 8,
                sources: {
                    'satellite-tiles': {
                        type: 'raster',
                        tiles: ['https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}'],
                        tileSize: 256
                    },
                    'labels': {
                        type: 'vector',
                        url: 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'
                    }
                },
                layers: [{ id: 'satellite-layer', type: 'raster', source: 'satellite-tiles' }]
            }
        }
    };

    // DOM Elements cache
    let els = {};

    // Tile loading state (module scope for access from drillToState)
    let tileLoadingTimeout = null;
    let initialStateDrill = false; // Only show loading during initial drill, not on zoom

    // === UTILITY FUNCTIONS ===

    function normalizeCountyFips(value)
    {
        const s = String(value == null ? "" : value).trim();
        if (!s) return "";
        return /^\d+$/.test(s) ? s.padStart(5, '0') : s;
    }

    function distanceMiles(lng1, lat1, lng2, lat2)
    {
        const dLat = (lat2 - lat1) * TO_RAD;
        const dLng = (lng2 - lng1) * TO_RAD;
        const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(lat1 * TO_RAD) * Math.cos(lat2 * TO_RAD) *
            Math.sin(dLng / 2) * Math.sin(dLng / 2);
        const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        return EARTH_RADIUS_MILES * c;
    }

    function formatBytes(bytes)
    {
        if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
        const units = ["B", "KB", "MB", "GB"];
        const idx = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
        const scaled = bytes / Math.pow(1024, idx);
        return `${scaled.toFixed(idx === 0 ? 0 : 1)} ${units[idx]}`;
    }

    function setNum(id, val)
    {
        const el = document.getElementById(id);
        if (el) el.textContent = Math.round(val).toLocaleString();
    }

    function animateValue(el, val)
    {
        if (!el) return;
        el.textContent = Math.round(val).toLocaleString();
    }

    // === CIRCLE GENERATION ===

    function createCircleGeoJSON(center, radiusMeters, steps = 64)
    {
        if (typeof turf !== 'undefined' && turf.circle)
        {
            return turf.circle(center, radiusMeters / 1000, { steps, units: 'kilometers' });
        }
        const coords = [];
        for (let i = 0; i <= steps; i++)
        {
            const angle = (i / steps) * 2 * Math.PI;
            const dx = radiusMeters * Math.cos(angle);
            const dy = radiusMeters * Math.sin(angle);
            const lat = center[1] + (dy / 111320);
            const lng = center[0] + (dx / (111320 * Math.cos(center[1] * Math.PI / 180)));
            coords.push([lng, lat]);
        }
        return { type: 'Feature', geometry: { type: 'Polygon', coordinates: [coords] } };
    }

    function updateCircles(lngLat)
    {
        if (!map) return;

        // Mode check
        if (riskZoneMode === 'grid') return;

        // Ensure source exists
        if (!map.getSource('impact-circles')) setupCircleLayers();

        const circles = {
            type: 'FeatureCollection',
            features: [
                { ...createCircleGeoJSON([lngLat.lng, lngLat.lat], CIRCLE_RADII.tier3), properties: { tier: 3 } },
                { ...createCircleGeoJSON([lngLat.lng, lngLat.lat], CIRCLE_RADII.tier2), properties: { tier: 2 } },
                { ...createCircleGeoJSON([lngLat.lng, lngLat.lat], CIRCLE_RADII.tier1), properties: { tier: 1 } }
            ]
        };
        const source = map.getSource('impact-circles');
        if (source) source.setData(circles);
    }

    // === DATA LOADING ===

    async function loadStates()
    {
        if (cache.states) return cache.states;

        // Only show loading indicator after 200ms delay (prevents flash for cached responses)
        let showingLoading = false;
        const loadingTimeout = setTimeout(() =>
        {
            showingLoading = true;
            toggleLoading(true, "Loading State Map...");
        }, 200);

        try 
        {
            const res = await fetch('/api/census/states');
            const data = await res.json();
            if (data && Array.isArray(data.features))
            {
                data.features.forEach((f, i) =>
                {
                    if (!f.properties) f.properties = {};
                    f.properties.GEOID = String(f.properties.geoid || f.properties.GEOID || '').padStart(2, '0');
                    f.properties.NAME = f.properties.NAME || f.properties.name || '';
                    f.properties.POP_TOTAL = f.properties.POP_TOTAL || f.properties.pop_total || 0;
                    f.id = i;
                });
            }
            cache.states = data;
            stateData = data;
            return data;
        } finally 
        {
            clearTimeout(loadingTimeout);
            if (showingLoading) toggleLoading(false);
        }
    }

    async function loadCounties(stateFips)
    {
        if (cache.counties[stateFips]) return cache.counties[stateFips];
        const res = await fetch(`/api/census/counties/${stateFips}`);
        const data = await res.json();
        if (data && Array.isArray(data.features))
        {
            data.features.forEach((f, i) =>
            {
                if (!f.properties) f.properties = {};
                f.properties.GEOID = normalizeCountyFips(f.properties.geoid || f.properties.GEOID);
                f.properties.NAME = f.properties.NAME || f.properties.name || '';
                f.properties.POP_TOTAL = f.properties.POP_TOTAL || f.properties.pop_total || 0;
                f.id = i;
            });
        }
        cache.counties[stateFips] = data;
        return data;
    }

    // === CONTEXT LOADING (Block Group Data) ===

    let activeContextLoad = null;
    let contextLoadSeq = 0;

    async function loadCountyContext(fips, lite = false, loadingText = "Loading Data...", manageLoading = true, isPrefetch = false)
    {
        fips = normalizeCountyFips(fips);
        if (!fips) return false;

        if (contextCache[fips])
        {
            const cached = contextCache[fips];
            if (!(!lite && cached.isLite))
            {
                currentContextGeoJSON = cached.geojson;
                currentCalcFeatures = cached.calcFeatures;
                currentCountyTotals = cached.totals;
                contextIsLite = cached.isLite;
                return true;
            }
        }

        // Prefetch loads shouldn't abort primary loads
        if (activeContextLoad && !isPrefetch)
        {
            if (activeContextLoad.fips === fips && (activeContextLoad.lite === false || activeContextLoad.lite === lite))
            {
                return activeContextLoad.promise;
            }
            try { activeContextLoad.controller.abort(); } catch { }
        } else if (activeContextLoad && isPrefetch && activeContextLoad.fips === fips)
        {
            // Prefetch for same FIPS, reuse existing load
            return activeContextLoad.promise;
        }

        const loadId = ++contextLoadSeq;
        const controller = new AbortController();
        const timeoutMs = lite ? 45000 : 90000;
        const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

        if (manageLoading) toggleLoading(true, loadingText);

        const promise = (async () =>
        {
            try
            {
                const ts = Date.now();
                const res = await fetch(`/api/Impact/county-context/${fips}?lite=${lite}&_ts=${ts}`, {
                    signal: controller.signal,
                    cache: 'no-store'
                });
                if (!res.ok) throw new Error(`Fetch failed: ${res.status}`);

                const text = await res.text();
                const data = JSON.parse(text);

                let geojson = null;
                let calcFeatures = [];
                let countyAdults = 0;
                let countyTotal = 0;

                if (data && Array.isArray(data.points))
                {
                    countyAdults = Number(data.county_adults || 0);
                    countyTotal = Number(data.county_total || 0);
                    for (const p of data.points)
                    {
                        if (!p || p.length < 3) continue;
                        const lng = Number(p[0]);
                        const lat = Number(p[1]);
                        const popAdult = Number(p[2] || 0);
                        const pCountyFips = (p.length >= 4 && p[3] != null) ? String(p[3]) : "";
                        if (!Number.isFinite(lng) || !Number.isFinite(lat) || popAdult <= 0) continue;
                        calcFeatures.push({ lng, lat, popAdult, countyFips: pCountyFips || fips });
                    }
                } else
                {
                    geojson = data;
                    const features = (data && Array.isArray(data.features)) ? data.features : [];
                    for (const f of features)
                    {
                        if (!f) continue;
                        const props = f.properties || (f.properties = {});
                        const geoid = String(props.GEOID || props.geoid || "");
                        const popAdult = Number(props.POP_ADULT || 0);
                        const popTotal = Number(props.POPULATION || 0);
                        if (geoid.startsWith(fips))
                        {
                            countyAdults += popAdult;
                            countyTotal += popTotal;
                        }
                        const cx = Number(props.CX || 0);
                        const cy = Number(props.CY || 0);
                        if (Number.isFinite(cx) && Number.isFinite(cy) && (cx !== 0 || cy !== 0))
                        {
                            calcFeatures.push({ lng: cx, lat: cy, popAdult, countyFips: fips });
                        } else if (f.geometry && typeof turf !== 'undefined')
                        {
                            try
                            {
                                const coords = turf.centroid(f).geometry.coordinates;
                                if (coords && coords.length === 2)
                                {
                                    calcFeatures.push({ lng: coords[0], lat: coords[1], popAdult, countyFips: fips });
                                }
                            } catch { }
                        }
                    }
                }

                contextCache[fips] = { geojson, calcFeatures, totals: { adults: countyAdults, total: countyTotal }, isLite: lite };

                if (!activeContextLoad || activeContextLoad.id === loadId)
                {
                    contextIsLite = lite;
                    currentContextGeoJSON = geojson;
                    currentCalcFeatures = calcFeatures;
                    currentCountyTotals = { adults: countyAdults, total: countyTotal };
                }
                return true;
            } catch (e)
            {
                // Don't log AbortError - it's expected when a new request supersedes
                if (e.name !== 'AbortError')
                {
                    console.error("Context Load Error", e);
                }
                // If aborted but cache exists from another load, use it
                if (contextCache[fips])
                {
                    const cached = contextCache[fips];
                    currentContextGeoJSON = cached.geojson;
                    currentCalcFeatures = cached.calcFeatures;
                    currentCountyTotals = cached.totals;
                    contextIsLite = cached.isLite;
                    return true;
                }
                return false;
            } finally
            {
                clearTimeout(timeoutId);
                if (activeContextLoad && activeContextLoad.id === loadId)
                {
                    activeContextLoad = null;
                    if (manageLoading) toggleLoading(false);
                }
            }
        })();

        // Only track primary loads (not prefetch) as activeContextLoad
        if (!isPrefetch)
        {
            activeContextLoad = { id: loadId, fips, lite, controller, promise };
        }
        else
        {
            // Track prefetch for cancellation
            const prefetchItem = { id: loadId, controller };
            activePrefetches.push(prefetchItem);
            // Remove from list when done (in promise chain)
            promise.finally(() =>
            {
                activePrefetches = activePrefetches.filter(p => p.id !== loadId);
            });
        }
        return promise;
    }

    // === IMPACT CALCULATION ENGINE ===

    function calculateImpact()
    {
        // Debug mode status
        console.log('[Impact] calculateImpact called. Mode:', riskZoneMode);

        if (!currentCalcFeatures || !currentCountyTotals || !markerPosition || !currentCountyFips) return;

        const baselineRate = parseFloat(els.inputRate ? els.inputRate.value : 2.3);
        const centerLat = markerPosition.lat;
        const centerLng = markerPosition.lng;

        const countyAdults = currentCountyTotals.adults || 0;
        const countyTotal = currentCountyTotals.total || 0;
        const stateFips = String(currentCountyFips || "").substring(0, 2);

        // Reset population counts ONLY if in radius mode (Grid mode sets them via updateIsochrones)
        // Or if in grid mode, we should NOT reset them if they were just set?
        // Actually, updateIsochrones calls calculateImpact immediately after setting them.
        // So we should only zero them out if we are about to recalculate them (Radius mode).

        if (riskZoneMode !== 'grid')
        {
            t1PopRegional = 0; t2PopRegional = 0; t3PopRegional = 0;
            t1PopCounty = 0; t2PopCounty = 0; t3PopCounty = 0;
        }

        const byCounty = {}; // still needed for table later?
        // Note: Grid mode doesn't populate byCounty logic correctly yet for table, 
        // but let's fix the main numbers first.

        // Only run radius summation if NOT in grid mode
        if (riskZoneMode !== 'grid')
        {
            for (const entry of currentCalcFeatures)
            {
                if (!entry) continue;
                const popAdult = Number(entry.popAdult || 0);
                if (!Number.isFinite(popAdult) || popAdult <= 0) continue;

                const entryCountyFips = String(entry.countyFips || "");
                const effectiveCountyFips = entryCountyFips || currentCountyFips;
                const isSameCounty = effectiveCountyFips === currentCountyFips;
                const isSameState = !stateFips || effectiveCountyFips.substring(0, 2) === stateFips;
                if (!isSameState) continue;

                const dist = distanceMiles(centerLng, centerLat, entry.lng, entry.lat);
                const bucket = byCounty[effectiveCountyFips] || (byCounty[effectiveCountyFips] = { fips: effectiveCountyFips, t1Pop: 0, t2Pop: 0, t3Pop: 0 });

                if (dist <= 10)
                {
                    t1PopRegional += popAdult;
                    if (isSameCounty) t1PopCounty += popAdult;
                    bucket.t1Pop += popAdult;
                } else if (dist <= 20)
                {
                    t2PopRegional += popAdult;
                    if (isSameCounty) t2PopCounty += popAdult;
                    bucket.t2Pop += popAdult;
                } else if (dist <= 50)
                {
                    t3PopRegional += popAdult;
                    if (isSameCounty) t3PopCounty += popAdult;
                    bucket.t3Pop += popAdult;
                }
            }
        } else
        {
            // Grid Mode: We don't have per-county breakdown from the API, only Total vs Subject County.
            // We populate 'byCounty' with a single aggregate "Other Counties" entry so the calculator can use it.
            const t1Other = Math.max(0, t1PopRegional - t1PopCounty);
            const t2Other = Math.max(0, t2PopRegional - t2PopCounty);
            const t3Other = Math.max(0, t3PopRegional - t3PopCounty);

            if (t1Other + t2Other + t3Other > 0)
            {
                const otherFips = '99000'; // Dummy FIPS for aggregate
                byCounty[otherFips] = {
                    fips: otherFips,
                    t1Pop: t1Other,
                    t2Pop: t2Other,
                    t3Pop: t3Other
                };
                countyNamesCache[otherFips] = 'Other Counties (Aggregate)';
            }
        }

        const totalWithin50 = t1PopRegional + t2PopRegional + t3PopRegional;

        const preRate = baselineRate;
        const baselineIncrease = parseFloat(els.inputBaselineIncrease ? els.inputBaselineIncrease.value : 0);
        const r1 = preRate * 2.0, r2 = preRate * 1.5, r3 = preRate * 1.0;
        // Delta rates
        const d1 = Math.max(0, r1 - preRate) + baselineIncrease;
        const d2 = Math.max(0, r2 - preRate) + baselineIncrease;
        const d3 = baselineIncrease;

        const t1PopOther = Math.max(0, t1PopRegional - t1PopCounty);
        const t2PopOther = Math.max(0, t2PopRegional - t2PopCounty);
        const t3PopOther = Math.max(0, t3PopRegional - t3PopCounty);

        const v1Total = t1PopRegional * (r1 / 100);
        const v2Total = t2PopRegional * (r2 / 100);
        const v3Total = t3PopRegional * (r3 / 100);
        const v1County = t1PopCounty * (r1 / 100);
        const v2County = t2PopCounty * (r2 / 100);
        const v3County = t3PopCounty * (r3 / 100);
        const totalEstimatedCounty = v1County + v2County + v3County;
        const totalEstimatedRegional = v1Total + v2Total + v3Total;

        const n1Total = t1PopRegional * (d1 / 100);
        const n2Total = t2PopRegional * (d2 / 100);
        const n3Total = t3PopRegional * (d3 / 100);
        const n1County = t1PopCounty * (d1 / 100);
        const n2County = t2PopCounty * (d2 / 100);
        const n3County = t3PopCounty * (d3 / 100);
        const totalNetNewCounty = n1County + n2County + n3County;
        const totalNetNewRegional = n1Total + n2Total + n3Total;

        // Update UI
        animateValue(els.t1, t1PopRegional);
        animateValue(els.t2, t2PopRegional);
        animateValue(els.t3, t3PopRegional);

        setNum('val-t1-county', t1PopCounty);
        setNum('val-t1-other', t1PopOther);
        setNum('val-t2-county', t2PopCounty);
        setNum('val-t2-other', t2PopOther);
        setNum('val-t3-county', t3PopCounty);
        setNum('val-t3-other', t3PopOther);

        if (els.rateT1) els.rateT1.textContent = r1.toFixed(1) + '%';
        if (els.rateT2) els.rateT2.textContent = r2.toFixed(1) + '%';
        if (els.rateT3) els.rateT3.textContent = r3.toFixed(1) + '%';

        if (els.vicT1) els.vicT1.textContent = Math.round(v1Total).toLocaleString();
        if (els.vicT2) els.vicT2.textContent = Math.round(v2Total).toLocaleString();
        if (els.vicT3) els.vicT3.textContent = Math.round(v3Total).toLocaleString();

        setNum('victims-t1-county', v1County);
        setNum('victims-t1-other', t1PopOther * (r1 / 100));
        setNum('victims-t2-county', v2County);
        setNum('victims-t2-other', t2PopOther * (r2 / 100));
        setNum('victims-t3-county', v3County);
        setNum('victims-t3-other', t3PopOther * (r3 / 100));

        setNum('net-new-t1', n1Total);
        setNum('net-new-t2', n2Total);
        setNum('net-new-t3', n3Total);
        setNum('net-new-t1-county', n1County);
        setNum('net-new-t1-other', t1PopOther * (d1 / 100));
        setNum('net-new-t2-county', n2County);
        setNum('net-new-t2-other', t2PopOther * (d2 / 100));
        setNum('net-new-t3-county', n3County);
        setNum('net-new-t3-other', t3PopOther * (d3 / 100));

        if (els.totalVictims) els.totalVictims.textContent = Math.round(totalNetNewCounty).toLocaleString();

        // Update fullscreen overlay labels
        const lblHighVal = document.getElementById('label-high-val');
        const lblElevatedVal = document.getElementById('label-elevated-val');
        const lblBaselineVal = document.getElementById('label-baseline-val');

        console.log('[Impact] Updating fullscreen labels:', {
            highRisk: Math.round(t1PopRegional),
            elevated: Math.round(t2PopRegional),
            baseline: Math.round(t3PopRegional),
            labelsFound: { high: !!lblHighVal, elevated: !!lblElevatedVal, baseline: !!lblBaselineVal }
        });

        if (lblHighVal) lblHighVal.textContent = Math.round(t1PopRegional).toLocaleString();
        if (lblElevatedVal) lblElevatedVal.textContent = Math.round(t2PopRegional).toLocaleString();
        if (lblBaselineVal) lblBaselineVal.textContent = Math.round(t3PopRegional).toLocaleString();

        setNum('calc-result', totalNetNewCounty);
        setNum('calc-gamblers', totalNetNewCounty);
        setNum('disp-pop-impact-zones', countyTotal);
        setNum('disp-pop-adults', countyAdults);
        setNum('disp-pop-regional-50', totalWithin50);
        setNum('disp-victims-regional-50', totalNetNewRegional);
        setNum('disp-victims-regional-other', Math.max(0, totalNetNewRegional - totalNetNewCounty));
        // Effective Rates
        const dispRateAdult = document.getElementById('disp-rate-adult');
        const dispRateTotal = document.getElementById('disp-rate-total');
        if (dispRateAdult)
        {
            const effectiveRate = countyAdults > 0 ? (totalEstimatedCounty / countyAdults) * 100 : 0;
            dispRateAdult.textContent = effectiveRate.toFixed(2) + '%';
        }
        if (dispRateTotal)
        {
            const effectiveRate = countyTotal > 0 ? (totalEstimatedCounty / countyTotal) * 100 : 0;
            dispRateTotal.textContent = effectiveRate.toFixed(2) + '%';
        }

        // Impacted Counties
        const impactedCounties = Object.values(byCounty).filter(c => c && (c.t1Pop + c.t2Pop + c.t3Pop) > 0);
        setNum('disp-regional-counties', impactedCounties.length);
        const impactedCounties20 = impactedCounties.filter(c => c && (c.t1Pop + c.t2Pop) > 0);
        const dispRegionalCounties20 = document.getElementById('disp-regional-counties-20');
        if (dispRegionalCounties20) dispRegionalCounties20.textContent = `≤20 mi: ${impactedCounties20.length.toLocaleString()}`;

        // Get state name for event
        let stateName = '';
        if (stateData && stateData.features)
        {
            const stateFeature = stateData.features.find(f =>
            {
                const geoid = f?.properties?.GEOID || f?.properties?.geoid || '';
                return String(geoid).padStart(2, '0') === stateFips;
            });
            if (stateFeature) stateName = stateFeature.properties?.NAME || stateFeature.properties?.name || '';
        }

        // Build a FIPS->Name lookup from MVT counties layer AND cache
        if (map && map.getLayer('counties-fill'))
        {
            try
            {
                const renderedCounties = map.querySourceFeatures('census-vector', { sourceLayer: 'counties' });
                for (const f of renderedCounties)
                {
                    const fips = f.properties?.geoid;
                    const name = f.properties?.name;
                    if (fips && name) countyNamesCache[fips] = name;
                }
            } catch (e) { /* ignore */ }
        }

        // Check for missing names and fetch if needed
        const missingFips = Object.keys(byCounty).filter(f => !countyNamesCache[f]);
        if (missingFips.length > 0)
        {
            ensureCountyNames(missingFips);
        }

        // Build byCounty array for calculator with names
        const byCountyArray = Object.entries(byCounty).map(([fips, data]) => ({
            fips,
            geoid: fips,
            name: countyNamesCache[fips] || fips, // Use cache (populated by MVT or API)
            t1Pop: data.t1Pop,
            t2Pop: data.t2Pop,
            t3Pop: data.t3Pop
        })).filter(c => c.t1Pop + c.t2Pop + c.t3Pop > 0);

        // Dispatch events
        window.dispatchEvent(new CustomEvent('impact-breakdown-updated', {
            detail: {
                countyFips: currentCountyFips,
                stateFips,
                stateName,
                countyName: countyNamesCache[currentCountyFips] || '',
                baselineRate,
                county: {
                    adults: countyAdults,
                    total: countyTotal,
                    t1Adults: t1PopCounty,
                    t2Adults: t2PopCounty,
                    t3Adults: t3PopCounty,
                    adultsWithin50: t1PopCounty + t2PopCounty + t3PopCounty,
                    victims: { t1: n1County, t2: n2County, t3: n3County, total: totalNetNewCounty },
                    totalEstimated: { t1: v1County, t2: v2County, t3: v3County, total: totalEstimatedCounty }
                },
                regional: {
                    adultsWithin50: t1PopRegional + t2PopRegional + t3PopRegional,
                    t1Adults: t1PopRegional,
                    t2Adults: t2PopRegional,
                    t3Adults: t3PopRegional,
                    victimsWithin50: totalNetNewRegional
                },
                byCounty: byCountyArray
            }
        }));

        const triggerInput = document.getElementById('input-revenue');
        if (triggerInput) triggerInput.dispatchEvent(new Event('input'));
    }

    // === ISOCHRONE INTEGRATION ===

    async function fetchIsochrones(lat, lon, minutes = [5, 15, 30])
    {
        try
        {
            const results = await Promise.all(
                minutes.map(m => fetch(`/api/valhalla/isochrone?lat=${lat}&lon=${lon}&minutes=${m}`).then(r => r.ok ? r.json() : null))
            );
            const validResults = results.filter(r => r);
            return {
                type: 'FeatureCollection',
                features: results.map((r, i) => ({
                    type: 'Feature',
                    properties: { contour: minutes[i] },
                    geometry: r.features?.[0]?.geometry || r.geometry
                })).filter(f => f.geometry)
            };
        } catch (e)
        {
            console.error('Isochrone fetch failed:', e);
            return null;
        }
    }

    let isochroneTimeout = null;
    async function updateIsochrones(lngLat)
    {
        if (!layersVisible.zones) return;

        // If in grid mode, we fetch from our cache instead of Valhalla live
        if (riskZoneMode === 'grid')
        {
            try
            {
                const res = await fetch(`/api/Impact/cached-isochrone?lat=${lngLat.lat}&lon=${lngLat.lng}`);
                if (res.ok)
                {
                    const data = await res.json();
                    // API returns { geoJson: {...}, stats: {...} }
                    const geoJson = data.geoJson || data;
                    if (map.getSource('impact-grid-isochrones'))
                    {
                        map.getSource('impact-grid-isochrones').setData(geoJson);
                    }

                    // Also update population stats if available
                    if (data.stats)
                    {
                        // stats format: { "15": {total: X, by_county: {fips: Y...}}, ... }

                        function getStat(min)
                        {
                            const val = data.stats[min];
                            if (!val) return { total: 0, by_county: {} };
                            // Handle old format vs new
                            if (typeof val === 'number') return { total: val, by_county: {} };
                            return {
                                total: val.total || 0,
                                by_county: val.by_county || (val.county ? { [currentCountyFips]: val.county } : {})
                            };
                        }

                        const s15 = getStat('15');
                        const s30 = getStat('30');
                        const s60 = getStat('60');

                        // Reset byCounty global
                        byCounty = {};

                        // Collect all involved FIPS
                        const allFips = new Set([
                            ...Object.keys(s15.by_county || {}),
                            ...Object.keys(s30.by_county || {}),
                            ...Object.keys(s60.by_county || {})
                        ]);

                        // Populate byCounty with exclusive tier counts
                        allFips.forEach(fips =>
                        {
                            const c15 = (s15.by_county && s15.by_county[fips]) || 0;
                            const c30 = (s30.by_county && s30.by_county[fips]) || 0;
                            const c60 = (s60.by_county && s60.by_county[fips]) || 0;

                            // Cumulative to Exclusive
                            const t1 = c15;
                            const t2 = Math.max(0, c30 - c15);
                            const t3 = Math.max(0, c60 - c30);

                            if (t1 + t2 + t3 > 0)
                            {
                                byCounty[fips] = { t1Pop: t1, t2Pop: t2, t3Pop: t3 };
                            }
                        });

                        // Fallback: If no counties found (e.g. old API), populate current county from totals?
                        // The getStat fallback tries to handle 'val.county', but let's ensure:
                        if (allFips.size === 0 && (s15.total > 0 || s30.total > 0 || s60.total > 0))
                        {
                            // Fallback logic for safety
                        }

                        // Update global Regional Totals variables used by calculateImpact
                        t1PopRegional = s15.total;
                        t2PopRegional = Math.max(0, s30.total - s15.total);
                        t3PopRegional = Math.max(0, s60.total - s30.total);

                        // Update Subject County specific Globals (t1PopCounty is used for "Subject County" row)
                        // If we have data for the subject county in byCounty, use it.
                        // Otherwise fallback to existing logic (though sXX.county isn't directly available in new schema unless we look it up)

                        if (currentCountyFips && byCounty[currentCountyFips])
                        {
                            const cData = byCounty[currentCountyFips];
                            t1PopCounty = cData.t1Pop;
                            t2PopCounty = cData.t2Pop;
                            t3PopCounty = cData.t3Pop;
                        } else
                        {
                            // Zero out if not found (or should we trust the total-aggregate logic?)
                            // Current API returns 'by_county' so we should trust it.
                            t1PopCounty = 0;
                            t2PopCounty = 0;
                            t3PopCounty = 0;
                        }

                        // Update global var references for radius mode if they were used? 
                        // Actually logic above (lines 857-863 in original) updated t1PopRegional etc.

                        calculateImpact();
                    }
                } else
                {
                    console.warn("No cached isochrone found for this point");
                    // clear
                    if (map.getSource('impact-grid-isochrones'))
                    {
                        map.getSource('impact-grid-isochrones').setData({ type: 'FeatureCollection', features: [] });
                    }
                }
            } catch (e) { console.error(e); }
            return;
        }

        /* 
           Original live Valhalla fetch logic (currently disabled/unused effectively unless mode is isochrone 
           but distinct from 'grid' mode which uses pre-calculated db cache).
           If we ever want live valhalla again, we can re-enable this path.
        */
    }

    // === GRID MODE ===

    let gridPointsLoaded = false;

    async function loadGridPoints() 
    {
        if (gridPointsLoaded) return true;
        try
        {
            const res = await fetch('/api/Impact/grid-points'); // defaults to Allen County
            if (!res.ok) return false;

            const points = await res.json();

            if (!Array.isArray(points) || points.length === 0) return false;

            const features = points.map((p, i) => ({
                type: 'Feature',
                id: i,
                geometry: { type: 'Point', coordinates: [p.lon, p.lat] },
                properties: { lat: p.lat, lon: p.lon }
            }));

            const geojson = { type: 'FeatureCollection', features };

            if (!map.getSource('impact-grid-points'))
            {
                map.addSource('impact-grid-points', { type: 'geojson', data: geojson });

                // Visible points
                map.addLayer({
                    id: 'impact-grid-points-layer',
                    type: 'circle',
                    source: 'impact-grid-points',
                    paint: {
                        'circle-radius': 5,
                        'circle-color': '#3b82f6',
                        'circle-stroke-width': 1,
                        'circle-stroke-color': '#ffffff',
                        'circle-opacity': [
                            'case',
                            ['boolean', ['feature-state', 'selected'], false],
                            1,
                            0.6
                        ]
                    },
                    layout: { visibility: 'none' }
                });

                // Hover effect (optional, or just use click)
                map.on('click', 'impact-grid-points-layer', (e) =>
                {
                    // Stop event from bubbling to other layers (states/counties)
                    e.originalEvent.stopPropagation();
                    e.originalEvent.preventDefault();

                    const f = e.features[0];
                    if (!f) return;

                    // Move marker to clicked grid point
                    const lat = f.properties.lat;
                    const lon = f.properties.lon;
                    const newPos = { lng: lon, lat: lat };

                    // Update marker position
                    markerPosition = newPos;
                    if (marker) marker.setLngLat([lon, lat]);

                    // Update isochrones for this point
                    updateIsochrones(newPos);

                    // Recalculate population impact for the new position
                    calculateImpact();
                });

                map.on('mouseenter', 'impact-grid-points-layer', () => map.getCanvas().style.cursor = 'pointer');
                map.on('mouseleave', 'impact-grid-points-layer', () => map.getCanvas().style.cursor = '');
            }

            // Add isochrone layer for grid mode
            if (!map.getSource('impact-grid-isochrones'))
            {
                map.addSource('impact-grid-isochrones', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });

                map.addLayer({
                    id: 'impact-grid-isochrones-fill',
                    type: 'fill',
                    source: 'impact-grid-isochrones',
                    filter: ['in', 'contour', 15, 30, 60],
                    layout: {
                        'fill-sort-key': ['-', 0, ['get', 'contour']] // Ensure largest (60) is drawn first (bottom)
                    },
                    paint: {
                        'fill-color': [
                            'match',
                            ['get', 'contour'],
                            15, TIER_COLORS.tier1,
                            30, TIER_COLORS.tier2,
                            60, TIER_COLORS.tier3,
                            TIER_COLORS.tier3 // Fallback
                        ],
                        'fill-opacity': 0.3
                    }
                }, map.getLayer('circle-tier1-fill') ? 'circle-tier1-fill' : undefined);

                map.addLayer({
                    id: 'impact-grid-isochrones-line',
                    type: 'line',
                    source: 'impact-grid-isochrones',
                    filter: ['in', 'contour', 15, 30, 60],
                    layout: {
                        'line-sort-key': ['-', 0, ['get', 'contour']]
                    },
                    paint: {
                        'line-color': [
                            'match',
                            ['get', 'contour'],
                            15, TIER_COLORS.tier1,
                            30, TIER_COLORS.tier2,
                            60, TIER_COLORS.tier3,
                            TIER_COLORS.tier3
                        ],
                        'line-width': 2
                    }
                }, map.getLayer('circle-tier1-line') ? 'circle-tier1-line' : undefined);
            }
            gridPointsLoaded = true;
            return true;
        } catch (e)
        {
            console.error("Error loading grid points", e);
            return false;
        }
    }

    async function snapMarkerToNearestGridPoint()
    {
        if (!markerPosition || !map) return;

        const source = map.getSource('impact-grid-points');
        if (!source) return;

        const data = source._data;
        if (!data || !data.features || data.features.length === 0) return;

        // Find nearest grid point to current marker position
        let nearestPoint = null;
        let minDist = Infinity;

        data.features.forEach(f =>
        {
            const coords = f.geometry.coordinates;
            const dx = coords[0] - markerPosition.lng;
            const dy = coords[1] - markerPosition.lat;
            const dist = dx * dx + dy * dy;

            if (dist < minDist)
            {
                minDist = dist;
                nearestPoint = { lng: coords[0], lat: coords[1] };
            }
        });

        if (nearestPoint)
        {
            markerPosition = nearestPoint;
            if (marker) marker.setLngLat([nearestPoint.lng, nearestPoint.lat]);

            toggleLoading(true, "Loading drivetime isochromes...");
            try 
            {
                await updateIsochrones(nearestPoint);
                // calculateImpact(); // Called internally by updateIsochrones
            } finally 
            {
                toggleLoading(false);
            }
        }
    }

    function showMapNotification(message)
    {
        let el = document.getElementById('map-notification-toast');
        if (!el)
        {
            el = document.createElement('div');
            el.id = 'map-notification-toast';
            el.className = 'absolute top-1/2 left-1/2 transform -translate-x-1/2 -translate-y-1/2 bg-slate-900/90 text-white px-6 py-4 rounded-xl shadow-2xl border border-slate-700 z-[1000] flex flex-col items-center gap-3 backdrop-blur-md opacity-0 transition-all duration-300 scale-90 pointer-events-none';
            el.innerHTML = `
                <div class="p-3 bg-blue-500/20 rounded-full text-blue-400 mb-1">
                    <span class="material-symbols-outlined text-3xl">info</span>
                </div>
                <div class="text-center">
                    <h4 class="font-bold text-lg mb-1">Notice</h4>
                    <p id="map-notif-msg" class="text-slate-300 text-sm"></p>
                </div>
            `;
            const container = document.getElementById('impact-map').parentElement;
            if (container) container.appendChild(el);
        }

        document.getElementById('map-notif-msg').textContent = message;

        // Show
        requestAnimationFrame(() =>
        {
            el.style.opacity = '1';
            el.style.transform = 'translate(-50%, -50%) scale(1)';
        });

        // Hide after 3s
        setTimeout(() =>
        {
            el.style.opacity = '0';
            el.style.transform = 'translate(-50%, -50%) scale(0.90)';
        }, 3500);
    }

    async function setRiskZoneMode(mode)
    {
        // mode: 'radius' | 'grid'

        if (!map) return;

        // Helper to sync UI elements
        function syncModeUI(activeMode)
        {
            // Sync sidebar buttons
            const radiusBtn = document.getElementById('mode-btn-radius');
            const gridBtn = document.getElementById('mode-btn-grid');
            if (radiusBtn && gridBtn)
            {
                if (activeMode === 'radius')
                {
                    radiusBtn.style.background = '#3b82f6';
                    radiusBtn.style.color = 'white';
                    gridBtn.style.background = 'rgba(255,255,255,0.1)';
                    gridBtn.style.color = 'rgba(255,255,255,0.5)';
                } else
                {
                    gridBtn.style.background = '#3b82f6';
                    gridBtn.style.color = 'white';
                    radiusBtn.style.background = 'rgba(255,255,255,0.1)';
                    radiusBtn.style.color = 'rgba(255,255,255,0.5)';
                }
            }
        }

        if (mode === 'grid')
        {
            // Require county selection before enabling grid mode
            if (!currentCountyFips || !markerPosition)
            {
                syncModeUI('radius');
                showMapNotification("Please select a county first before switching to Grid mode.");
                return;
            }

            // Show loading...
            toggleLoading(true, "Loading Grid Data...");

            const success = await loadGridPoints();

            if (!success)
            {
                toggleLoading(false);
                // No data or error - revert UI to radius
                syncModeUI('radius');
                showMapNotification("No isochrone grid data is available for this area yet.");
                return;
            }

            riskZoneMode = 'grid';
            syncModeUI('grid');

            // Disable marker dragging in grid mode
            if (marker) marker.setDraggable(false);

            // Hide circles (correct layer names)
            if (map.getLayer('circle-tier1-fill')) map.setLayoutProperty('circle-tier1-fill', 'visibility', 'none');
            if (map.getLayer('circle-tier2-fill')) map.setLayoutProperty('circle-tier2-fill', 'visibility', 'none');
            if (map.getLayer('circle-tier3-fill')) map.setLayoutProperty('circle-tier3-fill', 'visibility', 'none');
            if (map.getLayer('circle-tier1-line')) map.setLayoutProperty('circle-tier1-line', 'visibility', 'none');
            if (map.getLayer('circle-tier2-line')) map.setLayoutProperty('circle-tier2-line', 'visibility', 'none');
            if (map.getLayer('circle-tier3-line')) map.setLayoutProperty('circle-tier3-line', 'visibility', 'none');

            // Show Grid Points
            if (map.getLayer('impact-grid-points-layer')) map.setLayoutProperty('impact-grid-points-layer', 'visibility', 'visible');
            // Ensure isochrones visible
            if (map.getLayer('impact-grid-isochrones-fill')) map.setLayoutProperty('impact-grid-isochrones-fill', 'visibility', 'visible');
            if (map.getLayer('impact-grid-isochrones-line')) map.setLayoutProperty('impact-grid-isochrones-line', 'visibility', 'visible');

            // Auto-snap marker to nearest grid point
            // This function handles its own loading toggles (updating the message), so we don't turn it off here.
            await snapMarkerToNearestGridPoint();
            // snap... will turn off loading when done.

        } else
        {
            riskZoneMode = 'radius';
            syncModeUI('radius');

            // Re-enable marker dragging in radius mode
            if (marker) marker.setDraggable(true);

            // Radius Mode
            // Show circles (correct layer names)
            if (map.getLayer('circle-tier1-fill')) map.setLayoutProperty('circle-tier1-fill', 'visibility', 'visible');
            if (map.getLayer('circle-tier2-fill')) map.setLayoutProperty('circle-tier2-fill', 'visibility', 'visible');
            if (map.getLayer('circle-tier3-fill')) map.setLayoutProperty('circle-tier3-fill', 'visibility', 'visible');
            if (map.getLayer('circle-tier1-line')) map.setLayoutProperty('circle-tier1-line', 'visibility', 'visible');
            if (map.getLayer('circle-tier2-line')) map.setLayoutProperty('circle-tier2-line', 'visibility', 'visible');
            if (map.getLayer('circle-tier3-line')) map.setLayoutProperty('circle-tier3-line', 'visibility', 'visible');

            // Hide Grid Pts
            if (map.getLayer('impact-grid-points-layer')) map.setLayoutProperty('impact-grid-points-layer', 'visibility', 'none');
            if (map.getLayer('impact-grid-isochrones-fill')) map.setLayoutProperty('impact-grid-isochrones-fill', 'visibility', 'none');
            if (map.getLayer('impact-grid-isochrones-line')) map.setLayoutProperty('impact-grid-isochrones-line', 'visibility', 'none');

            // Refresh circles for current marker pos
            if (markerPosition) updateCircles(markerPosition);
        }
    }

    // === UI FUNCTIONS ===

    function toggleLoading(show, text = "Loading...")
    {
        const mapEl = document.getElementById('impact-map');
        if (!mapEl) return;
        let overlay = document.getElementById('map-loading-overlay');

        if (!overlay && show)
        {
            overlay = document.createElement('div');
            overlay.id = 'map-loading-overlay';
            overlay.className = 'absolute inset-0 z-[500] bg-slate-900/50 backdrop-blur-sm flex items-center justify-center transition-opacity duration-300';
            overlay.innerHTML = `<div class="flex flex-col items-center gap-4"><div class="w-12 h-12 border-4 border-blue-500/30 border-t-blue-500 rounded-full animate-spin"></div><div class="text-white font-bold" id="map-loading-text">${text}</div></div>`;
            mapEl.parentElement.appendChild(overlay);
        }
        else if (overlay && show) 
        {
            // Update text if overlay exists
            const textEl = document.getElementById('map-loading-text');
            if (textEl) textEl.textContent = text;
        }

        if (overlay)
        {
            overlay.style.opacity = show ? '1' : '0';
            overlay.style.pointerEvents = show ? 'auto' : 'none';
            if (!show) 
            {
                setTimeout(() => { if (overlay.parentNode) overlay.parentNode.removeChild(overlay); }, 300);
            }
        }
    }

    function updateMapNavUI(step)
    {
        const colors = {
            1: { bar: 'bg-blue-500', shadow: 'shadow-[0_0_10px_rgba(59,130,246,0.5)]', text: 'text-blue-400' },
            2: { bar: 'bg-emerald-500', shadow: 'shadow-[0_0_10px_rgba(16,185,129,0.5)]', text: 'text-emerald-400' },
            3: { bar: 'bg-purple-500', shadow: 'shadow-[0_0_10px_rgba(168,85,247,0.5)]', text: 'text-purple-400' }
        };
        for (let i = 1; i <= 3; i++)
        {
            const bar = document.getElementById(`map-nav-bar-${i}`);
            const label = document.getElementById(`map-nav-label-${i}`);
            if (!bar || !label) continue;
            bar.className = bar.className.replace(/bg-\w+-\d+|shadow-\[.*?\]/g, '').trim();
            label.className = label.className.replace(/text-\w+-\d+/g, '').trim();
            if (i <= step)
            {
                bar.classList.add(colors[i].bar, colors[i].shadow);
                label.classList.add(colors[i].text);
            } else
            {
                bar.classList.add('bg-slate-700');
                label.classList.add('text-slate-600');
            }
        }
    }

    function resetImpactStats()
    {
        currentContextGeoJSON = null;
        currentCalcFeatures = null;
        currentCountyTotals = null;
        const idsToZero = ['val-t1', 'val-t2', 'val-t3', 'total-gamblers', 'calc-result', 'calc-gamblers', 'disp-pop-impact-zones', 'disp-pop-adults'];
        idsToZero.forEach(id => { const el = document.getElementById(id); if (el) el.textContent = "0"; });
        window.dispatchEvent(new Event('map-state-reset'));
    }

    /**
     * Setup overlay controls (fullscreen button + risk zone legend)
     */
    function setupOverlayControls(container)
    {
        // Fullscreen toggle button - TOP RIGHT corner of map container's parent (which has position: relative)
        const fsBtn = document.createElement('button');
        fsBtn.id = 'fs-toggle-btn';
        fsBtn.title = 'Toggle Fullscreen';
        fsBtn.style.cssText = 'position: absolute; top: 12px; right: 12px; z-index: 70;';
        fsBtn.className = 'bg-slate-950/40 backdrop-blur-sm w-[30px] h-[30px] flex items-center justify-center rounded-lg shadow-lg border border-white/5 text-white hover:bg-slate-900/60 transition-colors cursor-pointer';
        fsBtn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">fullscreen</span>';
        fsBtn.onclick = function ()
        {
            const mapEl = container.parentElement;
            if (!document.fullscreenElement)
            {
                mapEl.requestFullscreen().catch(err => console.log(`Fullscreen error: ${err.message}`));
            } else
            {
                document.exitFullscreen();
            }
        };
        container.parentElement.appendChild(fsBtn);

        // Legend labels - positioned on RIGHT side, two-column layout: label left, value right
        const labelStack = document.createElement('div');
        labelStack.id = 'map-overlay-topright';
        labelStack.style.cssText = 'position: absolute; top: 80px; right: 12px; z-index: 60; display: flex; flex-direction: column; gap: 8px;';
        labelStack.innerHTML = `
            <div style="min-width: 160px; display: flex; justify-content: space-between; align-items: center;" class="bg-blue-600/40 text-white text-xs font-bold px-3 py-1.5 rounded-lg shadow-xl border border-white/20 backdrop-blur-sm transition-all duration-300 transform hover:scale-105 cursor-pointer"><span>High Risk:</span><span id="label-high-val">-</span></div>
            <div style="min-width: 160px; display: flex; justify-content: space-between; align-items: center;" class="bg-red-600/40 text-white text-xs font-bold px-3 py-1.5 rounded-lg shadow-xl border border-white/20 backdrop-blur-sm transition-all duration-300 transform hover:scale-105 cursor-pointer"><span>Elevated:</span><span id="label-elevated-val">-</span></div>
            <div style="min-width: 160px; display: flex; justify-content: space-between; align-items: center;" class="bg-orange-600/40 text-white text-xs font-bold px-3 py-1.5 rounded-lg shadow-xl border border-white/20 backdrop-blur-sm transition-all duration-300 transform hover:scale-105 cursor-pointer"><span>Baseline:</span><span id="label-baseline-val">-</span></div>
        `;
        container.parentElement.appendChild(labelStack);

        // Fullscreen change handler
        document.addEventListener('fullscreenchange', () =>
        {
            const btn = document.getElementById('fs-toggle-btn');
            if (btn)
            {
                if (document.fullscreenElement)
                {
                    btn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">fullscreen_exit</span>';
                    if (map)
                    {
                        map.scrollZoom.enable();
                        // Resize map to fit new fullscreen container dimensions
                        setTimeout(() => map.resize(), 100);
                    }
                    console.log('[Map] Entered fullscreen mode');
                } else
                {
                    btn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">fullscreen</span>';
                    if (map)
                    {
                        map.scrollZoom.disable();
                        // Resize map to fit original container dimensions
                        setTimeout(() => map.resize(), 100);
                    }
                    console.log('[Map] Exited fullscreen mode');
                }
            }
        });
    }

    /**
     * Setup layer switcher control (Google Maps style thumbnails - collapsible)
     */
    function setupLayerSwitcher(container)
    {
        // Container for layer button + cards (positioned left of zoom controls)
        const wrapper = document.createElement('div');
        wrapper.id = 'layer-switcher-wrapper';
        wrapper.style.cssText = 'position: absolute; bottom: 12px; right: 90px; z-index: 60; display: flex; align-items: center; gap: 8px;';

        // Layer toggle button
        const layerBtn = document.createElement('button');
        layerBtn.id = 'layer-toggle-btn';
        layerBtn.title = 'Change Map Style';
        layerBtn.className = 'bg-slate-950/40 backdrop-blur-sm w-[30px] h-[30px] flex items-center justify-center rounded-lg shadow-lg border border-white/5 text-white hover:bg-slate-900/60 transition-colors cursor-pointer';
        layerBtn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">layers</span>';

        // Cards container (hidden by default)
        const cardsContainer = document.createElement('div');
        cardsContainer.id = 'layer-cards';
        cardsContainer.style.cssText = 'display: none; flex-direction: row; gap: 6px; align-items: center;';

        Object.entries(BASEMAPS).forEach(([key, config]) =>
        {
            const card = document.createElement('button');
            card.className = `layer-card ${key === currentBasemap ? 'active' : ''}`;
            card.dataset.basemap = key;
            card.title = config.name;
            card.innerHTML = `
                <span class="material-symbols-outlined">${config.icon}</span>
                <span class="layer-card-label">${config.name}</span>
            `;
            card.onclick = (e) =>
            {
                e.stopPropagation();
                switchBasemap(key);
            };
            cardsContainer.appendChild(card);
        });

        wrapper.appendChild(cardsContainer);
        wrapper.appendChild(layerBtn);
        container.parentElement.appendChild(wrapper);

        // Toggle cards visibility
        let cardsVisible = false;
        layerBtn.onclick = (e) =>
        {
            e.stopPropagation();
            cardsVisible = !cardsVisible;
            cardsContainer.style.display = cardsVisible ? 'flex' : 'none';
            layerBtn.classList.toggle('active', cardsVisible);
        };

        // Close on click outside
        document.addEventListener('click', (e) =>
        {
            if (!wrapper.contains(e.target) && cardsVisible)
            {
                cardsVisible = false;
                cardsContainer.style.display = 'none';
                layerBtn.classList.remove('active');
            }
        });

        // Add CSS if not exists
        if (!document.getElementById('layer-switcher-styles'))
        {
            const style = document.createElement('style');
            style.id = 'layer-switcher-styles';
            style.textContent = `
                .layer-card {
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    justify-content: center;
                    width: 52px;
                    height: 52px;
                    background: rgba(15, 23, 42, 0.85);
                    backdrop-filter: blur(8px);
                    border: 1px solid rgba(255, 255, 255, 0.1);
                    border-radius: 8px;
                    color: rgba(255, 255, 255, 0.7);
                    cursor: pointer;
                    transition: all 0.2s ease;
                    font-size: 9px;
                    gap: 2px;
                }
                .layer-card:hover {
                    background: rgba(30, 41, 59, 0.95);
                    border-color: rgba(255, 255, 255, 0.2);
                    color: white;
                    transform: scale(1.05);
                }
                .layer-card.active {
                    background: rgba(59, 130, 246, 0.5);
                    border-color: rgba(59, 130, 246, 0.7);
                    color: white;
                }
                .layer-card .material-symbols-outlined {
                    font-size: 18px;
                }
                .layer-card-label {
                    font-weight: 500;
                    white-space: nowrap;
                }
                #layer-toggle-btn.active {
                    background: rgba(59, 130, 246, 0.4) !important;
                    border-color: rgba(59, 130, 246, 0.5) !important;
                }
                /* Horizontal zoom controls */
                .maplibregl-ctrl-group.maplibregl-ctrl {
                    display: flex !important;
                    flex-direction: row !important;
                }
                .maplibregl-ctrl-group button {
                    border: none !important;
                }
                .maplibregl-ctrl-group button:not(:first-child) {
                    border-left: 1px solid rgba(255,255,255,0.1) !important;
                }
            `;
            document.head.appendChild(style);
        }
    }

    /**
     * Setup hamburger menu control (slide-out drawer)
     */
    function setupHamburgerMenu(container)
    {
        // Hamburger button
        const menuBtn = document.createElement('button');
        menuBtn.id = 'map-menu-btn';
        menuBtn.title = 'Layer Options';
        menuBtn.style.cssText = 'position: absolute; top: 12px; left: 12px; z-index: 70;';
        menuBtn.className = 'bg-slate-950/40 backdrop-blur-sm w-[30px] h-[30px] flex items-center justify-center rounded-lg shadow-lg border border-white/5 text-white hover:bg-slate-900/60 transition-colors cursor-pointer';
        menuBtn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">menu</span>';
        container.parentElement.appendChild(menuBtn);

        // Slide-out panel
        const panel = document.createElement('div');
        panel.id = 'map-options-panel';
        panel.className = 'map-options-panel';
        panel.innerHTML = `
            <div class="panel-header">
                <span class="panel-title">Map Options</span>
                <button id="close-panel-btn" class="close-btn">
                    <span class="material-symbols-outlined">close</span>
                </button>
            </div>
            <div class="panel-section">
            <div class="panel-section">
                <span class="section-label">Overlays</span>
                <label class="toggle-row">
                    <span>Show Risk Zones</span>
                    <input type="checkbox" id="toggle-zones" checked />
                    <span class="toggle-slider"></span>
                </label>
                <label class="toggle-row" style="padding-left: 12px; border-left: 2px solid rgba(255,255,255,0.1); margin-left: 4px;">
                    <span class="text-xs text-slate-400">Mode:</span>
                    <div id="mode-toggle-wrapper" style="display: flex; gap: 4px; margin-left: auto;">
                        <button id="mode-btn-radius" class="mode-btn active" style="padding: 4px 10px; font-size: 10px; font-weight: 700; text-transform: uppercase; border: none; cursor: pointer; border-radius: 4px; background: #3b82f6; color: white;">Radius</button>
                        <button id="mode-btn-grid" class="mode-btn" style="padding: 4px 10px; font-size: 10px; font-weight: 700; text-transform: uppercase; border: none; cursor: pointer; border-radius: 4px; background: rgba(255,255,255,0.1); color: rgba(255,255,255,0.5);">Grid</button>
                    </div>
                </label>
                <label class="toggle-row">
                    <span>Risk Zone Labels</span>
                    <input type="checkbox" id="toggle-risklabels" checked />
                    <span class="toggle-slider"></span>
                </label>
                <label class="toggle-row">
                    <span>County Boundaries</span>
                    <input type="checkbox" id="toggle-boundary" checked />
                    <span class="toggle-slider"></span>
                </label>
                <label class="toggle-row">
                    <span>Heatmap</span>
                    <input type="checkbox" id="toggle-heatmap" />
                    <span class="toggle-slider"></span>
                </label>
                <label class="toggle-row">
                    <span>Census Tracts</span>
                    <input type="checkbox" id="toggle-tracts" />
                    <span class="toggle-slider"></span>
                </label>
            </div>
            <div class="panel-section">
                <span class="section-label">Map Theme</span>
                <label class="toggle-row">
                    <span>Dark Mode</span>
                    <input type="checkbox" id="toggle-darkmode" checked />
                    <span class="toggle-slider"></span>
                </label>
            </div>
            <div class="panel-section">
                <span class="section-label">3D Features</span>
                <label class="toggle-row">
                    <span>3D Terrain</span>
                    <input type="checkbox" id="toggle-terrain3d" />
                    <span class="toggle-slider"></span>
                </label>
                <label class="toggle-row">
                    <span>3D Buildings</span>
                    <input type="checkbox" id="toggle-buildings3d" />
                    <span class="toggle-slider"></span>
                </label>
            </div>
        `;
        container.parentElement.appendChild(panel);

        // Panel toggle logic
        menuBtn.onclick = () => panel.classList.add('open');
        document.getElementById('close-panel-btn').onclick = () => panel.classList.remove('open');

        // Close on click outside
        document.addEventListener('click', (e) =>
        {
            if (!panel.contains(e.target) && !menuBtn.contains(e.target))
            {
                panel.classList.remove('open');
            }
        });

        // Toggle handlers
        const toggles = {
            'toggle-zones': 'zones',
            'toggle-boundary': 'boundary',
            'toggle-heatmap': 'heatmap',
            'toggle-tracts': 'tracts',
            'toggle-terrain3d': 'terrain3d',
            'toggle-buildings3d': 'buildings3d'
        };

        Object.entries(toggles).forEach(([id, layer]) =>
        {
            const checkbox = document.getElementById(id);
            if (checkbox)
            {
                checkbox.checked = layersVisible[layer];
                checkbox.onchange = () => toggleLayerVisibility(layer, checkbox.checked);
            }
        });

        // Mode toggle button handlers (Radius/Grid)
        const radiusBtn = document.getElementById('mode-btn-radius');
        const gridBtn = document.getElementById('mode-btn-grid');

        if (radiusBtn && gridBtn)
        {
            radiusBtn.onclick = () =>
            {
                setRiskZoneMode('radius');
            };
            gridBtn.onclick = () =>
            {
                setRiskZoneMode('grid');
            };
        }

        // Dark mode toggle handler
        const darkModeToggle = document.getElementById('toggle-darkmode');
        if (darkModeToggle)
        {
            darkModeToggle.checked = mapDarkMode;
            darkModeToggle.onchange = () =>
            {
                mapDarkMode = darkModeToggle.checked;
                // Re-apply current basemap if it's streets or terrain
                if (currentBasemap === 'streets' || currentBasemap === 'terrain')
                {
                    switchBasemap(currentBasemap);
                }
            };
        }

        // Risk Labels toggle handler
        const riskLabelToggle = document.getElementById('toggle-risklabels');
        if (riskLabelToggle)
        {
            riskLabelToggle.onchange = () =>
            {
                const el = document.getElementById('map-overlay-topright');
                if (el) el.style.display = riskLabelToggle.checked ? 'flex' : 'none';
            };
        }

        // Add panel CSS if not exists
        if (!document.getElementById('hamburger-menu-styles'))
        {
            const style = document.createElement('style');
            style.id = 'hamburger-menu-styles';
            style.textContent = `
                .map-options-panel {
                    position: absolute;
                    top: 0;
                    left: 0;
                    width: 220px;
                    height: 100%;
                    background: rgba(15, 23, 42, 0.95);
                    backdrop-filter: blur(12px);
                    border-right: 1px solid rgba(255, 255, 255, 0.1);
                    z-index: 80;
                    transform: translateX(-100%);
                    transition: transform 0.3s ease;
                    display: flex;
                    flex-direction: column;
                    color: white;
                }
                .map-options-panel.open {
                    transform: translateX(0);
                }
                .panel-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding: 12px 16px;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.1);
                }
                .panel-title {
                    font-weight: 600;
                    font-size: 14px;
                }
                .close-btn {
                    background: none;
                    border: none;
                    color: rgba(255, 255, 255, 0.6);
                    cursor: pointer;
                    padding: 4px;
                    display: flex;
                }
                .close-btn:hover { color: white; }
                .panel-section {
                    padding: 12px 16px;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.05);
                }
                .section-label {
                    font-size: 10px;
                    text-transform: uppercase;
                    letter-spacing: 0.5px;
                    color: rgba(255, 255, 255, 0.4);
                    margin-bottom: 8px;
                    display: block;
                }
                .toggle-row {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding: 8px 0;
                    font-size: 13px;
                    cursor: pointer;
                    position: relative;
                }
                .toggle-row input[type="checkbox"] {
                    appearance: none;
                    width: 36px;
                    height: 20px;
                    background: rgba(100, 116, 139, 0.5);
                    border-radius: 10px;
                    position: relative;
                    cursor: pointer;
                    transition: background 0.2s;
                }
                .toggle-row input[type="checkbox"]:checked {
                    background: rgba(59, 130, 246, 0.7);
                }
                .toggle-row input[type="checkbox"]::before {
                    content: '';
                    position: absolute;
                    top: 2px;
                    left: 2px;
                    width: 16px;
                    height: 16px;
                    background: white;
                    border-radius: 50%;
                    transition: transform 0.2s;
                }
                .toggle-row input[type="checkbox"]:checked::before {
                    transform: translateX(16px);
                }
            `;
            document.head.appendChild(style);
        }
    }

    /**
     * Switch basemap style
     */
    async function switchBasemap(basemapKey)
    {
        if (basemapKey === currentBasemap) return;

        const config = BASEMAPS[basemapKey];
        if (!config) return;

        // Store current state
        const center = map.getCenter();
        const zoom = map.getZoom();
        const pitch = map.getPitch();
        const bearing = map.getBearing();

        currentBasemap = basemapKey;

        // Update active card
        document.querySelectorAll('.layer-card').forEach(card =>
        {
            card.classList.toggle('active', card.dataset.basemap === basemapKey);
        });

        // Switch style
        map.setStyle(config.style);

        // Restore state after style loads
        map.once('style.load', async () =>
        {
            try
            {
                // Restore view
                map.setCenter(center);
                map.setZoom(zoom);
                map.setPitch(pitch);
                map.setBearing(bearing);

                // Re-add Vector Tile layers (boundaries)
                setupVectorLayers();

                // Re-add analysis layers
                setupCircleLayers();
                setupHeatmapLayer();
                setupIsochroneLayers();
                setupTractLayer();

                // Re-add terrain if enabled
                if (layersVisible.terrain3d) enableTerrain3d(true);
                if (layersVisible.buildings3d) enable3dBuildings(true);

                // IMPORTANT: Restore state/county selection state
                if (currentStateFips)
                {
                    // Re-apply county filter for the selected state
                    setCountyFilter(currentStateFips);

                    // Hide state hover layers since we're in county mode
                    if (map.getLayer('states-hover'))
                    {
                        map.setLayoutProperty('states-hover', 'visibility', 'none');
                    }
                    if (map.getLayer('states-line-hover'))
                    {
                        map.setLayoutProperty('states-line-hover', 'visibility', 'none');
                    }
                }

                // Re-apply visibility based on current toggles
                Object.keys(layersVisible).forEach(key => toggleLayerVisibility(key, layersVisible[key]));

                // Restore marker
                if (markerPosition)
                {
                    marker = null; // Force recreation
                    updateMarker(markerPosition);
                    updateCircles(markerPosition);
                }

                // Restore county highlight if a county was selected
                if (currentCountyFips && map.getLayer('county-highlight-line'))
                {
                    map.setFilter('county-highlight-line', ['==', 'geoid', currentCountyFips]);
                }

                // Re-init drawing tools
                setupDrawingTools();

            } catch (err)
            {
                console.error("Error restoring map state after style switch:", err);
            }
        });
    }

    /**
     * Toggle layer visibility with proper handling
     */
    function toggleLayerVisibility(layerType, visible)
    {
        layersVisible[layerType] = visible;

        switch (layerType)
        {
            case 'zones':
                // Logic respects riskZoneMode: only show the active mode's layers
                const showCircles = visible && riskZoneMode === 'radius';
                const showIso = visible && riskZoneMode === 'isochrone';

                // update circles
                ['circle-tier1-fill', 'circle-tier1-line', 'circle-tier2-fill', 'circle-tier2-line', 'circle-tier3-fill', 'circle-tier3-line']
                    .forEach(id => setLayerVisibility(id, showCircles));

                // update isochrones
                setLayerVisibility('isochrone-fill', showIso);
                setLayerVisibility('isochrone-line', showIso);

                if (showIso && markerPosition) updateIsochrones(markerPosition);
                // Circles can always be updated or just on drag, updateMarker calls updateCircles anyway.
                break;
            case 'valhalla':
                riskZoneMode = visible ? 'isochrone' : 'radius';
                // Trigger refresh of zones visibility to apply mode change
                toggleLayerVisibility('zones', layersVisible.zones);
                break;
            case 'boundary':
                ['counties-fill', 'counties-line', 'county-highlight-line'].forEach(id => setLayerVisibility(id, visible));
                break;
            case 'heatmap':
                setLayerVisibility('block-groups-heat', visible);
                break;
            case 'tracts':
                setLayerVisibility('tract-lines', visible);
                if (visible && currentCountyFips) loadTracts(currentCountyFips);
                break;
            case 'terrain3d':
                enableTerrain3d(visible);
                break;
            case 'buildings3d':
                enable3dBuildings(visible);
                break;
        }
    }

    /**
     * Enable/disable 3D terrain
     * NOTE: 3D terrain requires a proper terrain tile source and is not available
     * in the current configuration. The MapLibre demo tiles are no longer available.
     */
    function enableTerrain3d(enable)
    {
        if (!map) return;

        if (enable)
        {
            console.warn('3D terrain is not available in this configuration. Requires a DEM tile source.');
            // Uncheck the toggle since feature is not available
            const checkbox = document.getElementById('toggle-terrain3d');
            if (checkbox) checkbox.checked = false;
            layersVisible.terrain3d = false;
        }
    }

    /**
     * Enable/disable 3D buildings (vector basemaps only)
     * NOTE: Requires a vector basemap with building data (e.g., CARTO styles)
     */
    function enable3dBuildings(enable)
    {
        if (!map) return;

        const uncheckToggle = () =>
        {
            const checkbox = document.getElementById('toggle-buildings3d');
            if (checkbox) checkbox.checked = false;
            layersVisible.buildings3d = false;
        };

        if (enable)
        {
            // Only works with vector basemaps
            if (currentBasemap === 'satellite' || currentBasemap === 'hybrid')
            {
                console.warn('3D buildings require Streets or Terrain basemap');
                uncheckToggle();
                return;
            }

            if (!map.getLayer('buildings-3d'))
            {
                // Try to find building layer in current style
                const layers = map.getStyle().layers || [];
                const buildingLayer = layers.find(l => l['source-layer'] === 'building');

                if (buildingLayer)
                {
                    try
                    {
                        map.addLayer({
                            id: 'buildings-3d',
                            type: 'fill-extrusion',
                            source: buildingLayer.source,
                            'source-layer': 'building',
                            filter: ['==', 'extrude', 'true'],
                            paint: {
                                'fill-extrusion-color': '#444',
                                'fill-extrusion-height': ['interpolate', ['linear'], ['zoom'], 15, 0, 16, ['get', 'height']],
                                'fill-extrusion-base': ['get', 'min_height'],
                                'fill-extrusion-opacity': 0.7
                            }
                        });
                        // Tilt for 3D view
                        map.easeTo({ pitch: 45, duration: 500 });
                    } catch (e)
                    {
                        console.warn('Failed to add 3D buildings layer:', e.message);
                        uncheckToggle();
                    }
                } else
                {
                    console.warn('No building data available in current basemap');
                    uncheckToggle();
                }
            } else
            {
                map.setLayoutProperty('buildings-3d', 'visibility', 'visible');
                map.easeTo({ pitch: 45, duration: 500 });
            }
        } else
        {
            if (map.getLayer('buildings-3d'))
            {
                map.setLayoutProperty('buildings-3d', 'visibility', 'none');
            }
        }
    }

    // === LAYER SETUP ===

    function setupCircleLayers()
    {
        if (!map) return;

        try
        {
            map.addSource('impact-circles', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
        } catch (e) { }

        [{ tier: 3, color: TIER_COLORS.tier3, opacity: 0.12, dash: [2, 6] },
        { tier: 2, color: TIER_COLORS.tier2, opacity: 0.35, dash: [5, 5] },
        { tier: 1, color: TIER_COLORS.tier1, opacity: 0.25, dash: null }].forEach(cfg =>
        {
            try
            {
                if (!map.getLayer(`circle-tier${cfg.tier}-fill`))
                {
                    map.addLayer({
                        id: `circle-tier${cfg.tier}-fill`, type: 'fill', source: 'impact-circles',
                        filter: ['==', ['get', 'tier'], cfg.tier],
                        paint: { 'fill-color': cfg.color, 'fill-opacity': cfg.opacity }
                    });
                }
            } catch (e) { }

            try
            {
                if (!map.getLayer(`circle-tier${cfg.tier}-line`))
                {
                    const linePaint = { 'line-color': cfg.color, 'line-width': 2 };
                    if (cfg.dash) linePaint['line-dasharray'] = cfg.dash;
                    map.addLayer({
                        id: `circle-tier${cfg.tier}-line`, type: 'line', source: 'impact-circles',
                        filter: ['==', ['get', 'tier'], cfg.tier], paint: linePaint
                    });
                }
            } catch (e) { }
        });
    }

    function setupHeatmapLayer()
    {
        if (!map) return;

        try
        {
            map.addSource('block-groups', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
        } catch (e) { }

        try
        {
            if (!map.getLayer('block-groups-heat'))
            {
                const firstLayer = map.getStyle().layers.find(l => l.type === 'symbol');
                const beforeId = firstLayer ? firstLayer.id : undefined;

                map.addLayer({
                    id: 'block-groups-heat', type: 'heatmap', source: 'block-groups',
                    paint: {
                        'heatmap-weight': ['interpolate', ['linear'], ['get', 'POP_ADULT'], 0, 0, 5000, 1],
                        'heatmap-intensity': ['interpolate', ['linear'], ['zoom'], 5, 1, 15, 3],
                        'heatmap-radius': ['interpolate', ['linear'], ['zoom'], 5, 15, 15, 30],
                        'heatmap-color': ['interpolate', ['linear'], ['heatmap-density'], 0, 'rgba(178,226,226,0)', 0.2, '#ADD8E6', 0.4, '#FEB24C', 0.6, '#FC4E2A', 0.8, '#E31A1C', 1, '#800026'],
                        'heatmap-opacity': 0.6
                    }
                }, beforeId);
            }
        } catch (e) { }
    }

    function setupIsochroneLayers()
    {
        if (!map) return;
        try
        {
            map.addSource('isochrones', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
        } catch (e) { }

        try
        {
            if (!map.getLayer('isochrone-fill'))
            {
                map.addLayer({
                    id: 'isochrone-fill', type: 'fill', source: 'isochrones',
                    paint: { 'fill-color': ['interpolate', ['linear'], ['get', 'contour'], 5, ISOCHRONE_COLORS[5], 15, ISOCHRONE_COLORS[15], 30, ISOCHRONE_COLORS[30]], 'fill-opacity': 0.3 }
                });
            }
        } catch (e) { }

        try
        {
            if (!map.getLayer('isochrone-line'))
            {
                map.addLayer({
                    id: 'isochrone-line', type: 'line', source: 'isochrones',
                    paint: { 'line-color': ['interpolate', ['linear'], ['get', 'contour'], 5, ISOCHRONE_COLORS[5], 15, ISOCHRONE_COLORS[15], 30, ISOCHRONE_COLORS[30]], 'line-width': 2 }
                });
            }
        } catch (e) { }
    }

    /**
     * Setup census tract layer with dashed line pattern
     */
    function setupTractLayer()
    {
        if (!map) return;

        try
        {
            map.addSource('tracts', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
        } catch (e) { }

        try
        {
            if (!map.getLayer('tract-lines'))
            {
                map.addLayer({
                    id: 'tract-lines',
                    type: 'line',
                    source: 'tracts',
                    paint: {
                        'line-color': '#94a3b8',
                        'line-width': 1,
                        'line-dasharray': [2, 2],
                        'line-opacity': 0.6
                    },
                    layout: {
                        visibility: layersVisible.tracts ? 'visible' : 'none'
                    }
                });
            }
        } catch (e) { }
    }

    /**
     * Load census tract boundaries for a county
     */
    async function loadTracts(countyFips)
    {
        if (!countyFips) return;

        try
        {
            const resp = await fetch(`/api/census/tracts/${countyFips}`);
            if (!resp.ok)
            {
                console.warn(`Failed to load tracts for county ${countyFips}`);
                return;
            }
            const data = await resp.json();

            if (map.getSource('tracts'))
            {
                map.getSource('tracts').setData(data);
            } else
            {
                setupTractLayer();
                map.getSource('tracts').setData(data);
            }
        } catch (e)
        {
            console.warn('Error loading tracts:', e.message);
        }
    }


    // Note: loadStates/loadCounties are still used for UI (dropdown population)
    // Map rendering now uses setupVectorLayers() with MVT tiles

    // Flag to track if vector layer events are registered
    let vectorLayerEventsRegistered = false;

    // Named event handler functions (for removal)
    function onStatesMouseMove(e)
    {
        if (e.features.length > 0)
        {
            const geoid = e.features[0].properties.geoid;
            map.setFilter('states-hover', ['==', 'geoid', geoid]);
            map.setFilter('states-line-hover', ['==', 'geoid', geoid]);
            map.getCanvas().style.cursor = 'pointer';
        }
    }

    function onStatesMouseLeave()
    {
        map.setFilter('states-hover', ['==', 'geoid', '']);
        map.setFilter('states-line-hover', ['==', 'geoid', '']);
        map.getCanvas().style.cursor = '';
    }

    function onStatesClick(e)
    {
        // Ignore state clicks when in grid mode
        if (riskZoneMode === 'grid') return;

        if (e.features.length > 0)
        {
            const props = e.features[0].properties;
            drillToState(props);
        }
    }

    let lastHoveredCounty = null;
    let markerDragging = false; // Suppress hover effects while dragging marker

    function onCountiesMouseMove(e)
    {
        if (markerDragging || initialStateDrill || !map) return;

        if (!e.features || e.features.length === 0) return;

        const f = e.features[0];
        const props = f.properties;
        const geoid = props.geoid;

        if (!geoid) return;

        // Helper to clear hover
        const clearHover = () =>
        {
            if (lastHoveredCounty !== null)
            {
                lastHoveredCounty = null;
                if (map.getLayer('counties-hover')) map.setFilter('counties-hover', ['==', 'geoid', '']);
                if (map.getLayer('counties-line-hover')) map.setFilter('counties-line-hover', ['==', 'geoid', '']);
            }
            map.getCanvas().style.cursor = '';
        };

        // 1. [REMOVED] Strict State Check - likely caused issues with missing properties.
        // We rely on the filter opacity of non-selected states (which is low) to make hover less distracting anyway.
        // And the 'counties-fill' filter handles the visual "only show this state" aspect.

        // 2. Active County Check: Do not highlight the currently selected county
        if (currentCountyFips && String(geoid) === String(currentCountyFips))
        {
            clearHover();
            return;
        }

        // 3. Apply Hover
        if (lastHoveredCounty !== geoid)
        {
            lastHoveredCounty = geoid;
            map.getCanvas().style.cursor = 'pointer';

            if (map.getLayer('counties-hover')) map.setFilter('counties-hover', ['==', 'geoid', geoid]);
            if (map.getLayer('counties-line-hover')) map.setFilter('counties-line-hover', ['==', 'geoid', geoid]);
        }
    }

    function onCountiesMouseLeave()
    {
        lastHoveredCounty = null;
        if (map && map.getLayer('counties-hover')) map.setFilter('counties-hover', ['==', 'geoid', '']);
        if (map && map.getLayer('counties-line-hover')) map.setFilter('counties-line-hover', ['==', 'geoid', '']);
        if (map) map.getCanvas().style.cursor = '';
    }

    function onCountiesClick(e)
    {
        // Ignore county clicks when in grid mode - let grid points handle it
        if (riskZoneMode === 'grid') return;

        if (e.features.length > 0)
        {
            const props = e.features[0].properties;
            if (currentStateFips && props.state_fp === currentStateFips)
            {
                selectCountyFromMVT(props);
            }
        }
    }

    // Helper to remove vector layers before re-adding (needed for style switches)
    function removeVectorLayers()
    {
        if (!map) return;

        // Remove event listeners first
        if (vectorLayerEventsRegistered)
        {
            try
            {
                map.off('mousemove', 'states-fill', onStatesMouseMove);
                map.off('mouseleave', 'states-fill', onStatesMouseLeave);
                map.off('click', 'states-fill', onStatesClick);
                map.off('mousemove', 'counties-fill', onCountiesMouseMove);
                map.off('mouseleave', 'counties-fill', onCountiesMouseLeave);
                map.off('click', 'counties-fill', onCountiesClick);
            } catch (e) { /* ignore */ }
            vectorLayerEventsRegistered = false;
        }

        const layersToRemove = [
            'county-highlight-line', 'counties-line-hover', 'counties-line',
            'counties-hover', 'counties-fill',
            'states-line-hover', 'states-line', 'states-hover', 'states-fill'
        ];

        for (const id of layersToRemove)
        {
            if (map.getLayer(id))
            {
                try { map.removeLayer(id); } catch (e) { /* ignore */ }
            }
        }

        if (map.getSource('census-vector'))
        {
            try { map.removeSource('census-vector'); } catch (e) { /* ignore */ }
        }
    }

    // --- Vector Tile Setup ---
    // This function can be called after style changes - it fully rebuilds layers
    function setupVectorLayers()
    {
        if (!map) return;

        console.log('[MVT] setupVectorLayers called');

        // Remove any existing vector layers first (needed for style switches)
        removeVectorLayers();

        // Add the vector tile source
        map.addSource('census-vector', {
            type: 'vector',
            tiles: [window.location.origin + '/api/census/tiles/{z}/{x}/{y}'],
            minzoom: 0,
            maxzoom: 14
        });

        console.log('[MVT] Added census-vector source');

        // State Fill (transparent base for click detection)
        map.addLayer({
            'id': 'states-fill',
            'type': 'fill',
            'source': 'census-vector',
            'source-layer': 'states',
            'paint': {
                'fill-color': '#94a3b8',
                'fill-opacity': 0.05
            }
        });

        // State Hover Highlight Layer (filtered to hovered state only)
        map.addLayer({
            'id': 'states-hover',
            'type': 'fill',
            'source': 'census-vector',
            'source-layer': 'states',
            'filter': ['==', 'geoid', ''], // Initially no state highlighted
            'paint': {
                'fill-color': '#60a5fa',
                'fill-opacity': 0.3
            }
        });

        // State Line
        map.addLayer({
            'id': 'states-line',
            'type': 'line',
            'source': 'census-vector',
            'source-layer': 'states',
            'paint': {
                'line-color': '#94a3b8',
                'line-width': 1
            }
        });

        // State Line Hover (thicker on hover)
        map.addLayer({
            'id': 'states-line-hover',
            'type': 'line',
            'source': 'census-vector',
            'source-layer': 'states',
            'filter': ['==', 'geoid', ''],
            'paint': {
                'line-color': '#60a5fa',
                'line-width': 2.5
            }
        });

        // County Fill (for hover detection) - hidden by default, shown when state selected
        map.addLayer({
            'id': 'counties-fill',
            'type': 'fill',
            'source': 'census-vector',
            'source-layer': 'counties',
            'minzoom': 4,
            'filter': ['==', 'state_fp', ''], // Initially hidden
            'paint': {
                'fill-color': '#94a3b8',
                'fill-opacity': 0.08
            }
        });

        // County Hover Highlight Layer
        map.addLayer({
            'id': 'counties-hover',
            'type': 'fill',
            'source': 'census-vector',
            'source-layer': 'counties',
            'minzoom': 4,
            'filter': ['==', 'geoid', ''], // Initially no county highlighted
            'paint': {
                'fill-color': '#60a5fa',
                'fill-opacity': 0.35
            }
        });

        // County Line - hidden by default, shown when state selected
        map.addLayer({
            'id': 'counties-line',
            'type': 'line',
            'source': 'census-vector',
            'source-layer': 'counties',
            'minzoom': 4,
            'filter': ['==', 'state_fp', ''], // Initially hidden
            'paint': {
                'line-color': '#64748b',
                'line-width': 0.5
            }
        });

        // County Line Hover
        map.addLayer({
            'id': 'counties-line-hover',
            'type': 'line',
            'source': 'census-vector',
            'source-layer': 'counties',
            'minzoom': 4,
            'filter': ['==', 'geoid', ''],
            'paint': {
                'line-color': '#60a5fa',
                'line-width': 2
            }
        });

        // County highlight line (selected county)
        map.addLayer({
            'id': 'county-highlight-line',
            'type': 'line',
            'source': 'census-vector',
            'source-layer': 'counties',
            'minzoom': 4,
            'filter': ['==', 'geoid', ''],
            'paint': {
                'line-color': '#22c55e',
                'line-width': 3
            }
        });

        // === EVENT LISTENERS ===
        // Register named handlers (allows removal via map.off)
        console.log('[MVT] Registering event handlers');

        map.on('mousemove', 'states-fill', onStatesMouseMove);
        map.on('mouseleave', 'states-fill', onStatesMouseLeave);
        map.on('click', 'states-fill', onStatesClick);
        map.on('mousemove', 'counties-fill', onCountiesMouseMove);
        map.on('mouseleave', 'counties-fill', onCountiesMouseLeave);
        map.on('click', 'counties-fill', onCountiesClick);

        vectorLayerEventsRegistered = true;
        console.log('[MVT] setupVectorLayers complete');
    }

    // Neighboring states lookup (contiguous US only, for filtering county display)
    const STATE_NEIGHBORS = {
        '01': ['12', '13', '28', '47'], // AL
        '04': ['06', '32', '35', '49'], // AZ
        '05': ['22', '28', '29', '40', '47', '48'], // AR
        '06': ['04', '32', '41'], // CA
        '08': ['20', '31', '35', '40', '49', '56'], // CO
        '09': ['25', '36', '44'], // CT
        '10': ['24', '34', '42'], // DE
        '12': ['01', '13'], // FL
        '13': ['01', '12', '37', '45', '47'], // GA
        '16': ['30', '32', '41', '49', '53', '56'], // ID
        '17': ['18', '19', '21', '29', '55'], // IL
        '18': ['17', '21', '26', '39'], // IN
        '19': ['17', '27', '29', '31', '46', '55'], // IA
        '20': ['08', '29', '31', '40'], // KS
        '21': ['17', '18', '29', '39', '47', '51', '54'], // KY
        '22': ['05', '28', '48'], // LA
        '23': ['33'], // ME
        '24': ['10', '42', '51', '54'], // MD
        '25': ['09', '33', '36', '44'], // MA
        '26': ['18', '39', '55'], // MI
        '27': ['19', '38', '46', '55'], // MN
        '28': ['01', '05', '22', '47'], // MS
        '29': ['05', '17', '19', '20', '21', '31', '40', '47'], // MO
        '30': ['16', '38', '46', '56'], // MT
        '31': ['08', '19', '20', '29', '46', '56'], // NE
        '32': ['04', '06', '16', '41', '49'], // NV
        '33': ['23', '25', '50'], // NH
        '34': ['10', '36', '42'], // NJ
        '35': ['04', '08', '40', '48', '49'], // NM
        '36': ['09', '25', '34', '42', '50'], // NY
        '37': ['13', '45', '47', '51'], // NC
        '38': ['27', '30', '46'], // ND
        '39': ['18', '21', '26', '42', '54'], // OH
        '40': ['05', '08', '20', '29', '35', '48'], // OK
        '41': ['06', '16', '32', '53'], // OR
        '42': ['10', '24', '34', '36', '39', '54'], // PA
        '44': ['09', '25'], // RI
        '45': ['13', '37'], // SC
        '46': ['19', '27', '30', '31', '38', '56'], // SD
        '47': ['01', '05', '13', '21', '28', '29', '37', '51'], // TN
        '48': ['05', '22', '35', '40'], // TX
        '49': ['04', '08', '16', '32', '35', '56'], // UT
        '50': ['25', '33', '36'], // VT
        '51': ['21', '24', '37', '47', '54'], // VA
        '53': ['16', '41'], // WA
        '54': ['21', '24', '39', '42', '51'], // WV
        '55': ['17', '19', '26', '27'], // WI
        '56': ['08', '16', '30', '31', '46', '49'], // WY
        '11': ['24', '51'], // DC
    };

    // Set county filter to show only selected state + neighbors
    function setCountyFilter(stateFips)
    {
        if (!map) return;

        const neighbors = STATE_NEIGHBORS[stateFips] || [];
        const allStates = [stateFips, ...neighbors];

        // Use 'in' expression: ['in', 'state_fp', 'XX', 'YY', ...]
        const filter = ['in', 'state_fp', ...allStates];

        if (map.getLayer('counties-fill'))
        {
            map.setFilter('counties-fill', filter);
        }
        if (map.getLayer('counties-line'))
        {
            map.setFilter('counties-line', filter);
        }
    }

    // Handle county selection from MVT click - delegate to selectCounty
    async function selectCountyFromMVT(props)
    {
        const countyFips = props.geoid;
        if (!countyFips) return;

        // selectCounty handles zoom, marker placement, and all the other logic
        await selectCounty(countyFips);
    }

    // State click handler - zoom to state using cached data
    async function drillToState(props)
    {
        if (!props) return;

        const stateFips = props.geoid || props.GEOID;
        if (!stateFips) return;

        currentStateFips = stateFips;

        // Show loading indicator immediately - tiles will be requested after filter change
        // Set initialStateDrill flag so tile loading handler knows to show overlay
        initialStateDrill = true;
        clearTimeout(tileLoadingTimeout);
        toggleLoading(true, "Loading County Boundaries...");

        // Show counties only for this state and its neighbors
        setCountyFilter(stateFips);

        // Hide state hover layers - we're now in county selection mode
        if (map.getLayer('states-hover'))
        {
            map.setLayoutProperty('states-hover', 'visibility', 'none');
        }
        if (map.getLayer('states-line-hover'))
        {
            map.setLayoutProperty('states-line-hover', 'visibility', 'none');
        }

        // Use cached state data to get bounds
        if (stateData && stateData.features && typeof turf !== 'undefined')
        {
            const stateFeature = stateData.features.find(f =>
                (f.properties.GEOID === stateFips || f.properties.geoid === stateFips)
            );
            if (stateFeature)
            {
                const bounds = turf.bbox(stateFeature);
                map.fitBounds(bounds, { padding: 40 });
                if (els.stateDisplay) els.stateDisplay.textContent = stateFeature.properties.NAME || stateFeature.properties.name;

                // Notify Blazor to sync dropdown
                if (window.notifyBlazorStateSelected)
                {
                    window.notifyBlazorStateSelected(stateFips, stateFeature.properties.NAME || stateFeature.properties.name);
                }
            }
        }
        else
        {
            // Fallback: just update UI
            if (els.stateDisplay) els.stateDisplay.textContent = props.name || props.NAME;

            // Notify Blazor to sync dropdown
            if (window.notifyBlazorStateSelected)
            {
                window.notifyBlazorStateSelected(stateFips, props.name || props.NAME || '');
            }
        }

        updateMapNavUI(2);

        // Pre-fetch county context and names in background
        prefetchTopCounties(stateFips);
        ensureCountyNames([stateFips + '000']); // Trigger state load effectively by passing a dummy fips for this state? 
        // Actually ensureCountyNames extracts state part. Passing any FIPS from that state works.
        // We can just pass the stateFips if we modify ensureCountyNames slightly or pass a fake county.
        // Let's rely on prefetchTopCounties for now as it loads the county list anyway.
        // Wait, prefetchTopCounties loads the list but doesn't populate countyNamesCache.
        // Let's explicitly load the cache:
        loadStateCountyNames(stateFips);
    }

    // Explicitly load names for a state into cache
    async function loadStateCountyNames(stateFips)
    {
        if (stateCountiesLoaded.has(stateFips)) return;
        stateCountiesLoaded.add(stateFips);
        try
        {
            const res = await fetch(`/api/census/counties/${stateFips}`);
            if (res.ok)
            {
                const data = await res.json();
                if (data && data.features)
                {
                    data.features.forEach(f =>
                    {
                        if (f.properties && f.properties.geoid && f.properties.name)
                        {
                            countyNamesCache[f.properties.geoid] = f.properties.name;
                        }
                    });
                }
            }
        } catch (e) { console.warn('Name load failed', e); }
    }

    async function ensureCountyNames(fipsList)
    {
        const missing = fipsList.filter(f => !countyNamesCache[f]);
        if (missing.length === 0) return;

        // Group by state
        const neededStates = new Set(missing.map(f => f.substring(0, 2)));

        const promises = [];
        let scheduledFetch = false;

        for (const stateFips of neededStates)
        {
            if (stateCountiesLoaded.has(stateFips) || !stateFips || stateFips.length !== 2) continue;

            scheduledFetch = true;
            promises.push(loadStateCountyNames(stateFips));
        }

        if (scheduledFetch)
        {
            Promise.all(promises).then(() =>
            {
                // Re-run impact calculation to update UI with new names
                calculateImpact();
            });
        }
    }

    // Pre-fetch county context for top counties by population (runs in background)
    async function prefetchTopCounties(stateFips)
    {
        try
        {
            // Fetch county list with population from census API (already server-cached)
            const res = await fetch(`/api/census/counties/${stateFips}`);
            if (!res.ok) return;

            const data = await res.json();
            if (!data?.features) return;

            // Sort by adult population descending, take top 15
            const sorted = [...data.features].sort((a, b) =>
                (b.properties?.pop_adult || 0) - (a.properties?.pop_adult || 0)
            );
            const topCounties = sorted.slice(0, 15);

            console.log(`[Prefetch] Pre-warming cache for ${topCounties.length} counties in state ${stateFips}`);

            // Prefetch context for each county with idle scheduling to avoid blocking
            for (const county of topCounties)
            {
                const fips = county.properties?.geoid;
                if (!fips || contextCache[fips]) continue; // Skip if already cached

                // Use requestIdleCallback if available, else setTimeout
                const scheduleIdle = window.requestIdleCallback || ((cb) => setTimeout(cb, 50));
                scheduleIdle(() =>
                {
                    // isPrefetch=true so this won't abort primary loads
                    loadCountyContext(fips, true, "", false, true).catch(() => { });
                });
            }
        }
        catch (e)
        {
            console.warn('[Prefetch] Error pre-fetching county contexts:', e);
        }
    }

    // --- End Vector Tile Setup ---


    async function selectCounty(countyFips)
    {
        currentCountyFips = countyFips;

        // CRITICAL: Prevent 'idle' event from previous state drill from hiding our overlay
        initialStateDrill = false;
        clearTimeout(tileLoadingTimeout);

        // Cancel any pending prefetches to free up network slots
        if (activePrefetches.length > 0)
        {
            console.log(`[SelectCounty] Aborting ${activePrefetches.length} background prefetches`);
            activePrefetches.forEach(p =>
            {
                try { p.controller.abort(); } catch (e) { }
            });
            activePrefetches = [];
        }

        // Show loading immediately when county is clicked
        toggleLoading(true, "Loading County...");

        try
        {
            // Try cached county data first
            let countyFeature = countyData?.features?.find(f => f.properties.GEOID === countyFips || f.properties.geoid === countyFips);

            // If not in cache, fetch from API
            if (!countyFeature)
            {
                toggleLoading(true, "Fetching County Geometry...");
                try
                {
                    const res = await fetch(`/api/census/county/${countyFips}`);
                    if (res.ok)
                    {
                        countyFeature = await res.json();
                    }
                } catch (e)
                {
                    console.warn('Failed to fetch county geometry:', e);
                }
            }

            toggleLoading(true, "Loading Population Data...");
            const contextLoaded = await loadCountyContext(countyFips, true, "Loading Population Data...", false);

            console.log('[SelectCounty] Context load result:', {
                success: contextLoaded,
                hasCalcFeatures: !!currentCalcFeatures,
                calcFeaturesCount: currentCalcFeatures?.length || 0,
                hasCountyTotals: !!currentCountyTotals,
                countyTotals: currentCountyTotals
            });

            if (countyFeature && typeof turf !== 'undefined')
            {
                // Use centroid from API if available, otherwise calculate
                let center;
                if (countyFeature.properties?.centroid)
                {
                    center = countyFeature.properties.centroid;
                } else
                {
                    center = turf.center(countyFeature).geometry.coordinates;
                }

                markerPosition = { lng: center[0], lat: center[1] };
                updateMarker(markerPosition);
                updateMarker(markerPosition);
                // updateCircles(markerPosition); // Handled by setRiskZoneMode below

                // FEATURE: Auto-switch to Grid Mode if available for this county
                await loadGridPoints();
                let useGrid = false;
                const gridSource = map.getSource('impact-grid-points');
                if (gridSource && gridSource._data && gridSource._data.features)
                {
                    // Check if any grid point is close to the county center (e.g. within 0.1 degree ~ 7 miles)
                    // This creates a "hit test" to see if we have data for this region
                    const isClose = gridSource._data.features.some(f =>
                    {
                        const dx = f.properties.lon - markerPosition.lng;
                        const dy = f.properties.lat - markerPosition.lat;
                        return (dx * dx + dy * dy) < 0.01;
                    });
                    if (isClose) useGrid = true;
                }

                if (useGrid)
                {
                    console.log('[SelectCounty] Grid points available, switching to Grid Mode');
                    await setRiskZoneMode('grid');
                }
                else
                {
                    console.log('[SelectCounty] No grid points found, defaulting to Radius Mode');
                    setRiskZoneMode('radius');
                }

                // Fit to 50-mile circle around center
                const circle50 = createCircleGeoJSON([center[0], center[1]], CIRCLE_RADII.tier3);

                // Wait for zoom animation to complete before hiding loading
                // Use a race with timeout in case move doesn't fire
                await new Promise(resolve =>
                {
                    const timer = setTimeout(resolve, 2000);
                    map.once('moveend', () => { clearTimeout(timer); resolve(); });
                    map.fitBounds(turf.bbox(circle50), { padding: 20 });
                });

                highlightCounty(countyFeature);

                if (els.displayCounty) els.displayCounty.textContent = countyFeature.properties.name || countyFeature.properties.NAME;
            }

            calculateImpact();

            // Load tract boundaries if tracts layer is enabled
            if (layersVisible.tracts) loadTracts(countyFips);

            const countyName = countyFeature?.properties?.name || countyFeature?.properties?.NAME || '';
            window.dispatchEvent(new CustomEvent('county-selected-map', { detail: { geoid: countyFips, name: countyName } }));

            // Notify Blazor to sync dropdown
            if (window.notifyBlazorCountySelected)
            {
                window.notifyBlazorCountySelected(countyFips, countyName);
            }

            updateMapNavUI(3);
        }
        catch (err)
        {
            console.error('Error selecting county:', err);
        }
        finally
        {
            // Always hide loading when done
            toggleLoading(false);
        }
    }

    function updateMarker(lngLat)
    {
        if (!map) return;
        if (marker)
        {
            marker.setLngLat([lngLat.lng, lngLat.lat]);
            return;
        }
        const el = document.createElement('div');
        el.style.cssText = 'width:50px;height:88px;cursor:grab;background:url(assets/Casino_Map_Marker.svg) no-repeat bottom center/contain;position:relative;';



        // Click to zoom handler
        el.addEventListener('click', (e) =>
        {
            if (!e.defaultPrevented && markerPosition)
            {
                map.flyTo({ center: [markerPosition.lng, markerPosition.lat], zoom: 10, duration: 800 });
            }
        });

        marker = new maplibregl.Marker({ element: el, anchor: 'bottom', draggable: true, scale: 1 })
            .setLngLat([lngLat.lng, lngLat.lat])
            .addTo(map);
        marker.on('drag', () =>
        {
            const pos = marker.getLngLat();
            markerPosition = pos;
            updateCircles(pos);

            // Check which state the marker is in using stateData
            let markerInCorrectState = true;
            let markerStateName = null;

            if (stateData && stateData.features && currentStateFips)
            {
                const pt = turf.point([pos.lng, pos.lat]);
                const matchedState = stateData.features.find(f => turf.booleanPointInPolygon(pt, f));
                if (matchedState)
                {
                    const stateFips = matchedState.properties.GEOID || matchedState.properties.geoid;
                    markerStateName = matchedState.properties.NAME || matchedState.properties.name;
                    markerInCorrectState = (stateFips === currentStateFips);
                }
            }

            // If marker is in wrong state, hide circles and show warning
            if (!markerInCorrectState)
            {
                // Hide impact circles
                if (map.getSource('impact-circles'))
                {
                    map.getSource('impact-circles').setData({ type: 'FeatureCollection', features: [] });
                }

                // Show warning toast ONCE (track with a flag on the marker element)
                if (!el._wrongStateToastShown && window.AdaptiveToast)
                {
                    el._wrongStateToastShown = true;
                    const mapContainer = document.getElementById('maplibre-map-container')?.parentElement;
                    AdaptiveToast.show(
                        'Invalid Location',
                        `Casino marker must be within the selected state. Currently in: ${markerStateName || 'unknown'}`,
                        { container: mapContainer, duration: 4000 }
                    );
                }
                return; // Don't calculate impact for wrong state
            }

            // Reset toast flag when back in correct state
            el._wrongStateToastShown = false;

            // Check if marker moved to a new county within the correct state
            // Use queryRenderedFeatures for counties from MVT
            const countyFeatures = map.queryRenderedFeatures(
                map.project([pos.lng, pos.lat]),
                { layers: ['counties-fill'] }
            );

            if (countyFeatures && countyFeatures.length > 0)
            {
                const matchedCounty = countyFeatures[0];
                const newCountyFips = matchedCounty.properties.geoid;
                const countyStateFips = matchedCounty.properties.state_fp;

                // Only process if county is in selected state
                if (countyStateFips === currentStateFips && newCountyFips && newCountyFips !== currentCountyFips)
                {
                    currentCountyFips = newCountyFips;
                    // Use lite=true to get all block groups within 50-mile radius
                    loadCountyContext(newCountyFips, true, "Loading Impact Analysis...").then(() =>
                    {
                        calculateImpact();
                    });

                    // Fetch full county geometry for highlight
                    fetch(`/api/census/county/${newCountyFips}`)
                        .then(r => r.ok ? r.json() : null)
                        .then(feature =>
                        {
                            if (feature) highlightCounty(feature);
                        });

                    const countyName = matchedCounty.properties.name;
                    window.dispatchEvent(new CustomEvent('county-selected-map', {
                        detail: { name: countyName, geoid: newCountyFips }
                    }));
                    const displayEl = document.getElementById('county-display');
                    if (displayEl) displayEl.textContent = countyName;

                    // Notify Blazor to sync the Assessed County dropdown
                    if (window.notifyBlazorCountySelected)
                    {
                        window.notifyBlazorCountySelected(newCountyFips, countyName);
                    }

                    if (layersVisible.zones && riskZoneMode === 'isochrone') updateIsochrones(pos);
                    return; // Exit early, calculateImpact will be called after context loads
                }
            }

            calculateImpact();
            if (layersVisible.zones && riskZoneMode === 'isochrone') updateIsochrones(pos);
        });
        marker.on('dragstart', () => { el.style.cursor = 'grabbing'; markerDragging = true; });
        marker.on('dragend', () => { el.style.cursor = 'grab'; markerDragging = false; });
    }

    function highlightCounty(feature)
    {
        if (!map) return;
        const src = map.getSource('county-highlight');
        if (src) 
        {
            src.setData(feature);
        }
        else
        {
            map.addSource('county-highlight', { type: 'geojson', data: feature });
        }

        // Ensure layer exists (check regardless of source existence)
        if (!map.getLayer('county-highlight-line'))
        {
            map.addLayer({ id: 'county-highlight-line', type: 'line', source: 'county-highlight', paint: { 'line-color': '#fff', 'line-width': 3, 'line-dasharray': [1, 2] } });
        }
    }

    // === DRAWING TOOLS ===

    async function setupDrawingTools()
    {
        if (!map) return;

        // Dynamic CDN fallback for online mode
        if (!window.TerraDraw) 
        {
            if (navigator.onLine)
            {
                console.log("TerraDraw not found locally, attempting CDN load...");
                try 
                {
                    await new Promise((resolve, reject) =>
                    {
                        const script = document.createElement('script');
                        script.src = 'https://unpkg.com/terra-draw@0.0.1-alpha.49/dist/terra-draw.umd.js'; // Revert to alpha.49
                        script.onload = resolve;
                        script.onerror = reject;
                        document.head.appendChild(script);
                    });
                    console.log("TerraDraw loaded from CDN");
                } catch (e)
                {
                    console.warn("Failed to load TerraDraw from CDN:", e);
                    return;
                }
            }
            else
            {
                console.warn("TerraDraw not loaded and offline");
                return;
            }
        }

        try 
        {
            // Check for various possible global names
            const TD = window.TerraDraw || window.terraDraw;

            if (!TD)
            {
                console.warn("TerraDraw not available even after load attempt. Globals found:", Object.keys(window).filter(k => k.toLowerCase().includes('terra')));
                return;
            }

            console.log("TerraDraw init. Keys:", Object.keys(TD));

            draw = new TD.TerraDraw({
                adapter: new TD.TerraDrawMapLibreGLAdapter({
                    map: map
                }),
                modes: [
                    new TD.TerraDrawSelectMode({
                        flags: {
                            polygon: {
                                feature: {
                                    draggable: true,
                                    rotateable: true,
                                    scaleable: true,
                                    coordinates: {
                                        midpoints: true,
                                        draggable: true,
                                        deletable: true
                                    }
                                }
                            }
                        }
                    }),
                    new TD.TerraDrawPointMode(),
                    new TD.TerraDrawLineStringMode(),
                    new TD.TerraDrawPolygonMode(),
                    new TD.TerraDrawRectangleMode(),
                    new TD.TerraDrawCircleMode(),
                    new TD.TerraDrawFreehandMode()
                ]
            });

            draw.start();
            setupDrawingUI();
        } catch (e) 
        {
            console.warn("Failed to init TerraDraw:", e);
        }
    }

    function setupDrawingUI()
    {
        const container = map.getContainer();
        const wrapper = document.createElement('div');
        wrapper.id = 'drawing-tools-wrapper';
        wrapper.style.cssText = 'position: absolute; top: 12px; left: 50px; z-index: 60; display: flex; flex-direction: column; gap: 4px;';

        // Toggle Button
        const toggleBtn = document.createElement('button');
        toggleBtn.id = 'draw-toggle-btn';
        toggleBtn.title = 'Drawing Tools';
        toggleBtn.className = 'bg-slate-950/40 backdrop-blur-sm w-[30px] h-[30px] flex items-center justify-center rounded-lg shadow-lg border border-white/5 text-white hover:bg-slate-900/60 transition-colors cursor-pointer';
        toggleBtn.innerHTML = '<span class="material-symbols-outlined text-xl leading-none">edit</span>';

        // Tools Panel
        const panel = document.createElement('div');
        panel.id = 'drawing-panel';
        panel.style.cssText = 'display: none; flex-direction: column; gap: 4px; margin-top: 4px; background: rgba(15, 23, 42, 0.9); padding: 4px; border-radius: 12px; backdrop-filter: blur(4px); border: 1px solid rgba(255,255,255,0.1);';

        const modes = [
            { mode: 'select', icon: 'near_me_disabled', title: 'Select/Edit' },
            { mode: 'point', icon: 'location_on', title: 'Point' },
            { mode: 'linestring', icon: 'timeline', title: 'Line' },
            { mode: 'polygon', icon: 'hexagon', title: 'Polygon' },
            { mode: 'rectangle', icon: 'check_box_outline_blank', title: 'Rectangle' },
            { mode: 'circle', icon: 'radio_button_unchecked', title: 'Circle' },
            { mode: 'freehand', icon: 'gesture', title: 'Freehand' },
            { mode: 'clear', icon: 'delete', title: 'Clear All' }
        ];

        let currentMode = 'static';

        modes.forEach(m =>
        {
            const btn = document.createElement('button');
            btn.className = 'draw-tool-btn w-8 h-8 flex items-center justify-center rounded hover:bg-slate-700/50 text-slate-300 transition-colors';
            btn.title = m.title;
            btn.innerHTML = `<span class="material-symbols-outlined text-sm">${m.icon}</span>`;

            btn.onclick = () =>
            {
                if (m.mode === 'clear')
                {
                    if (draw)
                    {
                        draw.clear();
                    }
                    return;
                }

                // Toggle logic
                if (currentMode === m.mode)
                {
                    draw.setMode('static');
                    currentMode = 'static';
                    panel.querySelectorAll('.draw-tool-btn').forEach(b => b.classList.remove('bg-blue-600', 'text-white'));
                } else
                {
                    draw.setMode(m.mode);
                    currentMode = m.mode;
                    panel.querySelectorAll('.draw-tool-btn').forEach(b => b.classList.remove('bg-blue-600', 'text-white'));
                    btn.classList.add('bg-blue-600', 'text-white');
                }
            };
            panel.appendChild(btn);
        });

        // Toggle visibility
        toggleBtn.onclick = () =>
        {
            const isVisible = panel.style.display !== 'none';
            panel.style.display = isVisible ? 'none' : 'flex';
            toggleBtn.classList.toggle('bg-blue-600', !isVisible);
        };

        wrapper.appendChild(toggleBtn);
        wrapper.appendChild(panel);
        container.parentElement.appendChild(wrapper);
    }

    function setLayerVisibility(id, visible)
    {
        if (!map || !map.getLayer(id)) return;
        map.setLayoutProperty(id, 'visibility', visible ? 'visible' : 'none');
    }

    // === STYLE RESTORATION & OFFLINE PREP ===

    function restoreAppLayers()
    {
        if (!map) return;

        // Check if a critical layer exists. If 'states-fill' is missing, likely the style changed.
        if (map.getLayer('states-fill')) return;

        console.log('[Map] Restoring app layers after style change...');

        // Re-run setup functions
        setupVectorLayers();
        setupCircleLayers();
        setupHeatmapLayer();
        setupIsochroneLayers();

        // Re-apply filters based on current state
        if (typeof currentStateFips !== 'undefined' && currentStateFips)
        {
            if (currentCountyFips)
            {
                setCountyFilter(currentStateFips);
                map.setFilter('counties-fill', ['==', 'state_fp', currentStateFips]);
                if (map.getSource('county-highlight') && map.getSource('county-highlight')._data)
                {
                    highlightCounty(map.getSource('county-highlight')._data);
                }
            } else
            {
                setCountyFilter(currentStateFips);
            }
        }

        // Restore grid points if in grid mode
        if (typeof riskZoneMode !== 'undefined' && riskZoneMode === 'grid')
        {
            loadGridPoints();
            if (markerPosition && typeof updateIsochrones === 'function') updateIsochrones(markerPosition);
        }
    }

    async function checkOfflineSatellite()
    {
        // Removed auto-detection to prevent 404 errors
    }

    // === PUBLIC API ===

    return {
        init: async function (containerId, options = {})
        {
            const container = document.getElementById(containerId);
            if (!container) { console.error('MapLibre: Container not found'); return; }

            els = {
                t1: document.getElementById('val-t1'),
                t2: document.getElementById('val-t2'),
                t3: document.getElementById('val-t3'),
                rateT1: document.getElementById('rate-t1'),
                rateT2: document.getElementById('rate-t2'),
                rateT3: document.getElementById('rate-t3'),
                vicT1: document.getElementById('victims-t1'),
                vicT2: document.getElementById('victims-t2'),
                vicT3: document.getElementById('victims-t3'),
                totalVictims: document.getElementById('total-gamblers'),
                inputRate: document.getElementById('input-rate'),
                inputBaselineIncrease: document.getElementById('input-baseline-increase'),
                stateDisplay: document.getElementById('state-display'),
                displayCounty: document.getElementById('display-impact-county')
            };

            if (typeof pmtiles !== 'undefined')
            {
                const protocol = new pmtiles.Protocol();
                maplibregl.addProtocol('pmtiles', protocol.tile);
            }

            // Determine default basemap based on connectivity
            const isOnline = navigator.onLine;
            currentBasemap = isOnline ? 'hybrid' : 'offline';
            const initialStyle = isOnline ? BASEMAPS.hybrid.style : BASEMAPS.offline.style;

            map = new maplibregl.Map({
                container: containerId,
                style: options.style || initialStyle,
                center: options.center || DEFAULT_CENTER,
                zoom: options.zoom || DEFAULT_ZOOM,
                scrollZoom: false,
                attributionControl: false
            });

            map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'bottom-right');

            map.on('load', async () =>
            {
                // Initial setup
                setupVectorLayers(); // Initial call

                // Analysis layers (GeoJSON-based, dynamic)
                setupCircleLayers();
                setupHeatmapLayer();
                setupIsochroneLayers();

                // Load state data for dropdown/UI (not for map rendering)
                await loadStates();

                updateMapNavUI(1);
                setupDrawingTools();
                console.log('MapLibreImpactMap v2.0 initialized');

                // Check for offline satellite support
                checkOfflineSatellite();
            });

            // Persist app layers when style changes (e.g. Offline -> Satellite)
            map.on('styledata', () =>
            {
                restoreAppLayers();
            });

            // Note: Tile loading indicator is handled by drillToState setting initialStateDrill=true,
            // and the idle handler below clearing it. This prevents loading overlay on every zoom.

            map.on('idle', () =>
            {
                // Map is idle = all tiles are loaded
                // Only hide loading if we were doing initial state drill
                if (initialStateDrill)
                {
                    initialStateDrill = false;
                    clearTimeout(tileLoadingTimeout);
                    toggleLoading(false);
                }
            });

            // Add overlay controls (fullscreen button + legend)
            setupOverlayControls(container);

            // Add layer switcher and hamburger menu
            setupLayerSwitcher(container);
            setupHamburgerMenu(container);

            // CTRL + Scroll zoom handling - use native scroll zoom when CTRL is held
            let ctrlPressed = false;

            // Enable/disable scroll zoom based on CTRL key
            document.addEventListener('keydown', (e) =>
            {
                if (e.key === 'Control' && !ctrlPressed && !document.fullscreenElement)
                {
                    ctrlPressed = true;
                    map.scrollZoom.enable();
                }
            });

            document.addEventListener('keyup', (e) =>
            {
                if (e.key === 'Control' && ctrlPressed && !document.fullscreenElement)
                {
                    ctrlPressed = false;
                    map.scrollZoom.disable();
                }
            });

            // Show hint when scrolling without CTRL
            container.addEventListener('wheel', (e) =>
            {
                if (document.fullscreenElement) return;
                if (ctrlPressed) return; // Let native zoom handle it

                const hint = document.getElementById('map-zoom-hint');
                if (hint)
                {
                    hint.style.opacity = '1';
                    clearTimeout(hint._hideTimeout);
                    hint._hideTimeout = setTimeout(() => { hint.style.opacity = '0'; }, 1500);
                }
            }, { passive: true });

            // CTRL + Plus/Minus keyboard zoom
            container.setAttribute('tabindex', '0');
            container.addEventListener('keydown', (e) =>
            {
                if (document.fullscreenElement) return;
                if (!e.ctrlKey) return;

                if (e.key === '+' || e.key === '=')
                {
                    e.preventDefault();
                    map.zoomIn({ duration: 300 });
                } else if (e.key === '-' || e.key === '_')
                {
                    e.preventDefault();
                    map.zoomOut({ duration: 300 });
                }
            });

            new ResizeObserver(() => map?.resize()).observe(container);

            // Set up listeners for rate sliders
            // Use event delegation as backup in case elements load after map init
            if (els.inputRate) els.inputRate.addEventListener('input', () => calculateImpact());
            if (els.inputBaselineIncrease) els.inputBaselineIncrease.addEventListener('input', () => calculateImpact());

            // Fallback: global listener for baseline increase in case element wasn't ready at init
            document.addEventListener('input', (e) =>
            {
                if (e.target && e.target.id === 'input-baseline-increase')
                {
                    // Update els reference if it was null before
                    if (!els.inputBaselineIncrease)
                    {
                        els.inputBaselineIncrease = e.target;
                    }
                    calculateImpact();
                }
            });

            return map;
        },

        toggleLayer: function (type)
        {
            if (type === 'valhalla')
            {
                const newVal = riskZoneMode !== 'isochrone';
                toggleLayerVisibility('valhalla', newVal);
                // Sync UI if needed
                const cb = document.getElementById('toggle-valhalla');
                if (cb) cb.checked = newVal;
                return;
            }

            // For other layers, toggle visibility
            const newVal = !layersVisible[type];
            toggleLayerVisibility(type, newVal);

            // Sync UI
            const cb = document.getElementById('toggle-' + type);
            if (cb) cb.checked = newVal;
        },

        navigateToStep: function (step)
        {
            if (step === 1)
            {
                // Reset state
                currentStateFips = null;
                currentCountyFips = null;
                markerPosition = null;
                resetImpactStats();

                // Remove marker if exists
                if (marker)
                {
                    marker.remove();
                    marker = null;
                }

                // Clear circles
                if (map.getSource('impact-circles'))
                {
                    map.getSource('impact-circles').setData({ type: 'FeatureCollection', features: [] });
                }

                // Show state layers
                setLayerVisibility('states-fill', true);
                setLayerVisibility('states-line', true);

                // Re-enable state hover layers
                if (map.getLayer('states-hover'))
                {
                    map.setLayoutProperty('states-hover', 'visibility', 'visible');
                }
                if (map.getLayer('states-line-hover'))
                {
                    map.setLayoutProperty('states-line-hover', 'visibility', 'visible');
                }

                // Hide county layers (reset filter to hide all)
                if (map.getLayer('counties-fill'))
                {
                    map.setFilter('counties-fill', ['==', 'state_fp', '']);
                }
                if (map.getLayer('counties-line'))
                {
                    map.setFilter('counties-line', ['==', 'state_fp', '']);
                }
                setLayerVisibility('county-highlight-line', false);

                // Fly to nationwide view
                map.flyTo({ center: DEFAULT_CENTER, zoom: DEFAULT_ZOOM });
                updateMapNavUI(1);
            }
            else if (step === 2 && currentStateFips) 
            {
                // Go back to state view - remove marker and circles, keep state selected
                currentCountyFips = null;
                markerPosition = null;

                // Remove marker if exists
                if (marker)
                {
                    marker.remove();
                    marker = null;
                }

                // Clear circles
                if (map.getSource('impact-circles'))
                {
                    map.getSource('impact-circles').setData({ type: 'FeatureCollection', features: [] });
                }

                // Clear county highlight
                setLayerVisibility('county-highlight-line', false);

                // Re-show county lines for state selection
                setCountyFilter(currentStateFips);

                // Hide state hover since we're in county selection mode
                if (map.getLayer('states-hover'))
                {
                    map.setLayoutProperty('states-hover', 'visibility', 'none');
                }
                if (map.getLayer('states-line-hover'))
                {
                    map.setLayoutProperty('states-line-hover', 'visibility', 'none');
                }

                // Zoom back to state bounds
                if (stateData && stateData.features && typeof turf !== 'undefined')
                {
                    const stateFeature = stateData.features.find(f =>
                        f.properties.GEOID === currentStateFips || f.properties.geoid === currentStateFips
                    );
                    if (stateFeature)
                    {
                        map.fitBounds(turf.bbox(stateFeature), { padding: 40 });
                    }
                }

                resetImpactStats();
                updateMapNavUI(2);
            }
        },

        getMarkerPosition: () => markerPosition,
        getMap: () => map,
        loadState: (fips) => drillToState(fips),
        loadCounty: (fips) => selectCounty(fips),
        setIsochroneVisibility: (v) => { toggleLayerVisibility('valhalla', v); const cb = document.getElementById('toggle-valhalla'); if (cb) cb.checked = v; },
        setRiskZoneMode // expose
    };
})();

// === GLOBAL HELPERS (for HTML onclick handlers) ===

window.toggleLayer = function (id)
{
    const cb = document.getElementById('layer-' + id);
    if (cb && window.MapLibreImpactMap)
    {
        window.MapLibreImpactMap.toggleLayer(id);
    }
};

// Alias for backward compatibility
window.ImpactMap = window.MapLibreImpactMap;

// JS→Blazor interop for dropdown synchronization
window._mapDropdownBlazorRef = null;

window.registerMapDropdownSync = function (blazorRef)
{
    window._mapDropdownBlazorRef = blazorRef;
    console.log('Map dropdown sync registered');
};

// Call this when map state is selected
window.notifyBlazorStateSelected = function (stateFips, stateName)
{
    if (window._mapDropdownBlazorRef)
    {
        window._mapDropdownBlazorRef.invokeMethodAsync('OnMapStateSelected', stateFips, stateName || '');
    }
};

// Call this when map county is selected
window.notifyBlazorCountySelected = function (countyFips, countyName)
{
    if (window._mapDropdownBlazorRef)
    {
        window._mapDropdownBlazorRef.invokeMethodAsync('OnMapCountySelected', countyFips, countyName || '');
    }
};
