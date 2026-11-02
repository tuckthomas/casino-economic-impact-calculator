/** Optional SaveNEIN campaign default. Remove this script from both HTML hosts to disable it.
 * The reusable map only publishes ready/navigation events; it contains no regional defaults.
 */
window.NortheastIndianaMapStartup = (() => {
    const featuredCounties = [
        { fips: '18151', name: 'Steuben County', labelPosition: [-85.0024, 41.6435] },
        { fips: '18033', name: 'DeKalb County', labelPosition: [-85.0000, 41.3974] },
        { fips: '18003', name: 'Allen County', labelPosition: [-85.0666, 41.0919] }
    ];
    const counties = featuredCounties.map(county => county.fips);
    const gold = '#b08d57';
    const initialized = new WeakSet();
    let activeMap = null;
    let visible = true;
    let restoring = false;
    let labels = [];
    let overviewStyle = null;
    let overviewContainer = null;

    function restoreHighlights() {
        if (!activeMap?.getSource('census-vector') || restoring) return;
        restoring = true;
        try {
            const filter = ['in', ['get', 'geoid'], ['literal', counties]];
            if (!activeMap.getLayer('nein-featured-counties-fill')) activeMap.addLayer({
                id: 'nein-featured-counties-fill', type: 'fill', source: 'census-vector', 'source-layer': 'counties',
                filter, paint: { 'fill-color': gold, 'fill-opacity': 0.16 }
            });
            if (!activeMap.getLayer('nein-featured-counties')) activeMap.addLayer({
                id: 'nein-featured-counties', type: 'line', source: 'census-vector', 'source-layer': 'counties',
                filter, paint: { 'line-color': gold, 'line-width': 3, 'line-opacity': 1 }
            });
            const visibility = visible ? 'visible' : 'none';
            for (const id of ['nein-featured-counties-fill', 'nein-featured-counties']) {
                if (activeMap.getLayoutProperty(id, 'visibility') !== visibility)
                    activeMap.setLayoutProperty(id, 'visibility', visibility);
            }
        } finally { restoring = false; }
    }

    async function apply(api) {
        const map = api?.getMap();
        if (!map || initialized.has(map)) return;
        initialized.add(map);
        if (activeMap) activeMap.off('styledata', restoreHighlights);
        for (const label of labels) label.marker.remove();
        labels = [];
        overviewStyle?.remove();
        overviewContainer?.removeAttribute('data-nein-county-overview');
        overviewContainer = map.getContainer().parentElement;
        activeMap = map;
        visible = true;
        await api.loadState('18');
        restoreHighlights();
        map.on('styledata', restoreHighlights);
        // Empty risk counters obscure the county names on a narrow overview.
        // Restore them automatically when a county is selected; preserve the user's label toggle.
        overviewStyle = document.createElement('style');
        overviewStyle.textContent = '@media (max-width: 767px) { [data-nein-county-overview] #map-overlay-topright { visibility: hidden; } }';
        map.getContainer().append(overviewStyle);
        // DOM labels avoid a dependency on the selected basemap's glyph service.
        // Their style and placement live entirely in this removable startup function.
        labels = featuredCounties.map(county => {
            const element = document.createElement('div');
            element.textContent = county.name;
            element.setAttribute('aria-label', county.name);
            element.className = 'nein-featured-county-name';
            Object.assign(element.style, {
                color: '#0f172a', backgroundColor: '#efe3cc', border: `1px solid ${gold}`,
                borderRadius: '6px', padding: '5px 8px', whiteSpace: 'nowrap',
                font: '700 15px/1.2 "Public Sans Variable", sans-serif',
                boxShadow: '0 1px 5px rgba(0,0,0,0.45)', pointerEvents: 'none'
            });
            const marker = new maplibregl.Marker({ element, anchor: 'center' })
                .setLngLat(county.labelPosition).addTo(map);
            return { marker, element };
        });
        map.fitBounds([[-85.72, 40.88], [-84.72, 41.83]], {
            padding: { top: 55, bottom: 55, left: 30, right: 30 }, duration: 0
        });
    }

    window.addEventListener('impact-map-ready', event => {
        apply(event.detail.api).catch(error => console.warn('Optional northeast Indiana map startup:', error));
    });
    window.addEventListener('impact-map-navigation', event => {
        visible = event.detail.step !== 1 && event.detail.stateFips === '18';
        overviewContainer?.toggleAttribute('data-nein-county-overview', visible && event.detail.step === 2);
        for (const label of labels) {
            label.element.hidden = !visible;
            label.element.style.display = visible ? 'block' : 'none';
        }
        restoreHighlights();
    });
    return { apply };
})();
