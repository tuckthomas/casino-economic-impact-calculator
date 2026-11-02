/** Optional SaveNEIN campaign default. Remove this script from both HTML hosts to disable it.
 * The reusable map only publishes ready/navigation events; it contains no regional defaults.
 */
window.NortheastIndianaMapStartup = (() => {
    const counties = ['18151', '18033', '18003']; // Steuben, DeKalb, Allen
    const colors = ['match', ['get', 'geoid'], '18151', '#fb923c', '18033', '#38bdf8', '18003', '#c4b5fd', '#ffffff'];
    const initialized = new WeakSet();
    let activeMap = null;
    let visible = true;
    let restoring = false;
    let legend = null;

    function restoreHighlights() {
        if (!activeMap?.getSource('census-vector') || restoring) return;
        restoring = true;
        try {
            if (!activeMap.getLayer('nein-featured-counties')) activeMap.addLayer({
                id: 'nein-featured-counties', type: 'line', source: 'census-vector', 'source-layer': 'counties',
                filter: ['in', ['get', 'geoid'], ['literal', counties]],
                paint: { 'line-color': colors, 'line-width': 3, 'line-opacity': 0.95 }
            });
            const visibility = visible ? 'visible' : 'none';
            if (activeMap.getLayoutProperty('nein-featured-counties', 'visibility') !== visibility)
                activeMap.setLayoutProperty('nein-featured-counties', 'visibility', visibility);
        } finally { restoring = false; }
    }

    async function apply(api) {
        const map = api?.getMap();
        if (!map || initialized.has(map)) return;
        initialized.add(map);
        if (activeMap) activeMap.off('styledata', restoreHighlights);
        legend?.remove();
        activeMap = map;
        visible = true;
        await api.loadState('18');
        restoreHighlights();
        map.on('styledata', restoreHighlights);
        legend = document.createElement('div');
        legend.className = 'sfw-map-focus-legend';
        legend.setAttribute('aria-label', 'Highlighted northeast Indiana counties');
        for (const [name, color] of [['Steuben', '#fb923c'], ['DeKalb', '#38bdf8'], ['Allen', '#c4b5fd']]) {
            const label = document.createElement('span');
            const swatch = document.createElement('i');
            swatch.style.backgroundColor = color;
            swatch.setAttribute('aria-hidden', 'true');
            label.append(swatch, document.createTextNode(name));
            legend.append(label);
        }
        map.getContainer().append(legend);
        map.fitBounds([[-85.72, 40.88], [-84.72, 41.83]], {
            padding: { top: 55, bottom: 55, left: 30, right: 30 }, duration: 0
        });
    }

    window.addEventListener('impact-map-ready', event => {
        apply(event.detail.api).catch(error => console.warn('Optional northeast Indiana map startup:', error));
    });
    window.addEventListener('impact-map-navigation', event => {
        visible = event.detail.step !== 1 && event.detail.stateFips === '18';
        if (legend) legend.hidden = !visible;
        restoreHighlights();
    });
    return { apply };
})();
