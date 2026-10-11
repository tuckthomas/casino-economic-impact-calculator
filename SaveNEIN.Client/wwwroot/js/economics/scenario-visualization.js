// Restores the map's calculation context without inventing populations from DOM placeholders.
window.EconomicScenarioVisualization = (() => {
    const version = 'economic-impact-visualization-v3';
    const number = (value, label) => {
        if (!Number.isFinite(value) || value < 0) throw new Error(`Invalid saved ${label}.`);
    };
    const sameTotal = (actual, expected, label) => {
        if (Math.abs(actual - expected) > Math.max(1e-6, Math.abs(expected) * 1e-9))
            throw new Error(`Saved ${label} does not reconcile.`);
    };

    function scalePopulation(population, multiplier) {
        number(population, 'population');
        if (!Number.isFinite(multiplier) || multiplier <= 0) throw new Error('Invalid population projection multiplier.');
        const projected = population * multiplier;
        number(projected, 'projected population');
        return projected;
    }

    function validateProjection(detail) {
        const projection = detail.populationProjection;
        if (!projection || !Number.isInteger(projection.baseYear) || !Number.isInteger(projection.targetYear) ||
            projection.baseYear < 1900 || projection.targetYear > 2200 || projection.targetYear < projection.baseYear ||
            projection.targetYear - projection.baseYear > 100 || !Number.isFinite(projection.annualGrowthRate) ||
            projection.annualGrowthRate <= -100 || projection.annualGrowthRate > 100)
            throw new Error('Invalid saved population projection assumptions.');
        const years = projection.targetYear - projection.baseYear;
        if (projection.yearsFromBase !== years || projection.usesProjectedAdults !== (years > 0 && projection.annualGrowthRate !== 0))
            throw new Error('Saved population projection metadata does not reconcile.');
        number(projection.multiplier, 'projection multiplier');
        sameTotal(projection.multiplier, Math.pow(1 + projection.annualGrowthRate / 100, years), 'projection multiplier');
        sameTotal(detail.county.adults, scalePopulation(detail.county.baseAdults, projection.multiplier), 'projected county population');
        number(detail.regional.baseAdultsWithin50, 'regional baseline population');
        sameTotal(detail.regional.adultsWithin50, scalePopulation(detail.regional.baseAdultsWithin50, projection.multiplier), 'projected regional population');
    }

    // Percentage-point inputs match the location controls; outputs retain fractional expected cases.
    function calculateExposure(populations, baselineRate, baselineIncrease) {
        number(baselineRate, 'baseline rate');
        number(baselineIncrease, 'baseline increase');
        if (!Array.isArray(populations) || populations.length !== 3)
            throw new Error('Three impact-zone populations are required.');
        populations.forEach(value => number(value, 'zone population'));
        const prevalenceRates = [2, 1.5, 1].map(multiplier => baselineRate * multiplier + baselineIncrease);
        if (prevalenceRates.some(rate => rate > 100)) throw new Error('Impact-zone prevalence cannot exceed 100 percent.');
        const incrementalRates = [baselineRate + baselineIncrease, baselineRate * 0.5 + baselineIncrease, baselineIncrease];
        const incrementalCases = populations.map((population, index) => population * incrementalRates[index] / 100);
        const estimatedCases = populations.map((population, index) => population * prevalenceRates[index] / 100);
        return {
            prevalenceRates, incrementalRates, incrementalCases, estimatedCases,
            totalIncrementalCases: incrementalCases.reduce((sum, value) => sum + value, 0),
            totalEstimatedCases: estimatedCases.reduce((sum, value) => sum + value, 0)
        };
    }

    function describeZones(configuration) {
        const { count, tiers, unit, mode } = configuration || {};
        if (!Number.isInteger(count) || count < 1 || count > 3 || !Array.isArray(tiers) || tiers.length !== count ||
            tiers.some((value, index) => !Number.isFinite(value) || value <= 0 || (index > 0 && value <= tiers[index - 1])) ||
            !((mode === 'radius' && unit === 'miles') ||
                ((mode === 'isochrone' || mode === 'grid') && unit === 'minutes-drive-time')))
            throw new Error('Invalid saved impact zone configuration.');
        const format = value => Number(value.toFixed(2)).toString();
        const suffix = unit === 'miles' ? 'miles' : 'minutes drive time';
        return {
            bands: tiers.map((value, index) => `${format(index === 0 ? 0 : tiers[index - 1])}–${format(value)} ${suffix}`),
            footprint: unit === 'miles' ? `${format(tiers[count - 1])}-mile radius` : `${format(tiers[count - 1])}-minute drive-time footprint`
        };
    }

    function restore(snapshot) {
        if (snapshot?.version !== 'economic-impact-client-v1') throw new Error('Unsupported saved scenario version.');
        const context = snapshot.visualizationContext;
        if (context?.version !== version || !context.impactBreakdown)
            throw new Error('This saved location has no restorable visualization. Return to location selection and save it again.');
        const detail = JSON.parse(JSON.stringify(context.impactBreakdown));
        if (detail.stateFips !== snapshot.stateFips || detail.countyFips !== snapshot.countyFips ||
            detail.markerLat !== snapshot.markerLatitude || detail.markerLng !== snapshot.markerLongitude)
            throw new Error('Saved visualization does not match the selected location.');
        describeZones(detail.zoneConfiguration);
        describeZones(snapshot.zoneConfiguration);
        const savedZones = snapshot.zoneConfiguration;
        const detailZones = detail.zoneConfiguration;
        if (detailZones.count !== savedZones.count || detailZones.mode !== savedZones.mode || detailZones.unit !== savedZones.unit ||
            detailZones.tiers.some((value, index) => value !== savedZones.tiers[index]))
            throw new Error('Saved visualization does not match the selected impact zones.');
        for (const scope of ['county', 'regional']) {
            const population = detail[scope];
            if (!population) throw new Error(`Missing saved ${scope} population.`);
            for (const key of ['t1Adults', 't2Adults', 't3Adults', 'adultsWithin50']) number(population[key], `${scope} ${key}`);
            sameTotal(population.adultsWithin50, population.t1Adults + population.t2Adults + population.t3Adults, `${scope} zone population`);
            for (let tier = detail.zoneConfiguration.count + 1; tier <= 3; tier++)
                if (population[`t${tier}Adults`] !== 0) throw new Error('Disabled impact zones contain saved population.');
        }
        for (const key of ['adults', 'baseAdults', 'total']) number(detail.county[key], `county ${key}`);
        validateProjection(detail);
        if (detail.county.adultsWithin50 > detail.county.adults + Math.max(1e-6, detail.county.adults * 1e-9))
            throw new Error('Saved impact-zone population exceeds the host county adult population.');
        number(detail.baselineRate, 'baseline rate');
        number(detail.baselineIncrease, 'baseline increase');
        for (const key of ['t1', 't2', 't3', 'total']) number(detail.county.victims?.[key], `county victims ${key}`);
        sameTotal(detail.county.victims.total, detail.county.victims.t1 + detail.county.victims.t2 + detail.county.victims.t3, 'county victims');
        for (let tier = detailZones.count + 1; tier <= 3; tier++)
            if (detail.county.victims[`t${tier}`] !== 0) throw new Error('Disabled impact zones contain saved victims.');
        const countyExposure = calculateExposure(
            [detail.county.t1Adults, detail.county.t2Adults, detail.county.t3Adults], detail.baselineRate, detail.baselineIncrease);
        ['t1', 't2', 't3'].forEach((key, index) => sameTotal(detail.county.victims[key], countyExposure.incrementalCases[index], `county ${key} cases`));
        number(detail.regional.victimsWithin50, 'regional cases');
        const regionalExposure = calculateExposure(
            [detail.regional.t1Adults, detail.regional.t2Adults, detail.regional.t3Adults], detail.baselineRate, detail.baselineIncrease);
        sameTotal(detail.regional.victimsWithin50, regionalExposure.totalIncrementalCases, 'regional cases');
        if (!Array.isArray(detail.byCounty)) throw new Error('Missing saved county population breakdown.');
        const seen = new Set();
        const totals = [0, 0, 0];
        for (const county of detail.byCounty) {
            if (!/^\d{5}$/.test(county.fips) || seen.has(county.fips)) throw new Error('Invalid or duplicate saved county.');
            if (!county.fips.startsWith(detail.stateFips)) throw new Error('Saved county is outside the selected state.');
            seen.add(county.fips);
            ['t1Pop', 't2Pop', 't3Pop'].forEach((key, index) => {
                number(county[key], `${county.fips} ${key}`);
                totals[index] += county[key];
            });
        }
        ['t1Adults', 't2Adults', 't3Adults'].forEach((key, index) => sameTotal(totals[index], detail.regional[key], `regional ${key}`));
        const hostCounty = detail.byCounty.find(county => county.fips === detail.countyFips);
        ['t1Adults', 't2Adults', 't3Adults'].forEach((key, index) =>
            sameTotal(hostCounty?.[`t${index + 1}Pop`] ?? 0, detail.county[key], `host county ${key}`));
        return detail;
    }

    return { version, restore, describeZones, calculateExposure, scalePopulation };
})();
