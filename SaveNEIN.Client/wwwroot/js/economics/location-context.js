/*
 * Shared location display context for economics rendering.
 *
 * calculator.js has rendering paths that run independently of the map event handler.
 * Keep the currently selected county's display label available at global scope so
 * those renderers never depend on a block-local variable from the narrative builder.
 */
window.formattedCountyName = window.formattedCountyName || 'Subject County';

window.addEventListener('impact-breakdown-updated', function (event)
{
    const detail = event && event.detail ? event.detail : {};
    let rawCountyName = String(detail.countyName || '').trim();

    if (!rawCountyName && detail.countyFips && Array.isArray(window.CurrentCountyList))
    {
        const countyFips = String(detail.countyFips);
        const match = window.CurrentCountyList.find(function (county)
        {
            return String(county && (county.geoid || county.id) || '') === countyFips;
        });
        rawCountyName = String(match && match.name || '').trim();
    }

    const cleanCountyName = rawCountyName.replace(/\s+County$/i, '').trim();
    window.formattedCountyName = `${cleanCountyName || 'Subject'} County`;
});
