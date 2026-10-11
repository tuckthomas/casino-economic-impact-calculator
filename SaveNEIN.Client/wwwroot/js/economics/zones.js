// Shared by map drawing, population allocation, and saved scenario metadata.
window.EconomicZones = (() => {
    const metersPerMile = 1609.34;

    function radiusConfiguration(count, radii) {
        if (!Number.isInteger(count) || count < 1 || count > 3)
            throw new Error('Select between one and three impact zones.');
        const tiers = [radii.tier1, radii.tier2, radii.tier3].slice(0, count)
            .map(radius => radius / metersPerMile);
        if (tiers.some((radius, index) => !Number.isFinite(radius) || radius <= 0 ||
            (index > 0 && radius <= tiers[index - 1])))
            throw new Error('Impact zone radii must be positive and increasing.');
        return { count, tiers, unit: 'miles', mode: 'radius' };
    }

    function tierForDistance(distanceMiles, configuration) {
        if (!Number.isFinite(distanceMiles) || distanceMiles < 0)
            throw new Error('Distance must be finite and nonnegative.');
        const index = configuration.tiers.findIndex(radius => distanceMiles <= radius);
        return index < 0 ? null : index + 1;
    }

    function snapshot(mode, count, radii) {
        if (mode === 'radius') return radiusConfiguration(count, radii);
        if (mode !== 'isochrone' && mode !== 'grid') throw new Error('Unsupported impact zone mode.');
        return { count: 3, tiers: [15, 30, 60], unit: 'minutes-drive-time', mode };
    }

    return { radiusConfiguration, tierForDistance, snapshot };
})();
