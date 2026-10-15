import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const context = vm.createContext({ window: {} });
vm.runInContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/scenario-visualization.js', import.meta.url), 'utf8'), context);
const visualization = context.window.EconomicScenarioVisualization;

test('zone descriptions use custom radii, drive-time units, and only active bands', () => {
    const radius = visualization.describeZones({ count: 2, tiers: [5, 12.25], unit: 'miles', mode: 'radius' });
    assert.equal(JSON.stringify(radius.bands), '["0–5 miles","5–12.25 miles"]');
    assert.equal(radius.footprint, '12.25-mile radius');
    for (const mode of ['grid', 'isochrone']) {
        const routed = visualization.describeZones({ count: 3, tiers: [15, 30, 60], unit: 'minutes-drive-time', mode });
        assert.equal(routed.bands[2], '30–60 minutes drive time');
        assert.equal(routed.footprint, '60-minute drive-time footprint');
    }
});

test('malformed zone settings are rejected before describing a saved result', () => {
    for (const configuration of [null,
        { count: 2, tiers: [5], unit: 'miles', mode: 'radius' },
        { count: 2, tiers: [5, 4], unit: 'miles', mode: 'radius' },
        { count: 1, tiers: [Infinity], unit: 'miles', mode: 'radius' },
        { count: 1, tiers: [5], unit: 'minutes-drive-time', mode: 'radius' }])
        assert.throws(() => visualization.describeZones(configuration));
});

function scenario() {
    const zoneConfiguration = { count: 3, tiers: [5, 12, 40], unit: 'miles', mode: 'radius' };
    const populationProjection = { baseYear: 2020, targetYear: 2026,
        annualGrowthRate: (Math.pow(1.2, 1 / 6) - 1) * 100, yearsFromBase: 6, multiplier: 1.2, usesProjectedAdults: true };
    return {
        version: 'economic-impact-client-v1', stateFips: '18', countyFips: '18003',
        markerLatitude: 41.0793, markerLongitude: -85.1394, zoneConfiguration,
        visualizationContext: {
            version: visualization.version,
            impactBreakdown: {
                stateFips: '18', countyFips: '18003', markerLat: 41.0793, markerLng: -85.1394,
                countyName: 'Allen', baselineRate: 1.5, baselineIncrease: 0.25, zoneConfiguration, populationProjection,
                county: { adults: 1200, baseAdults: 1000, total: 1500, t1Adults: 100, t2Adults: 200, t3Adults: 300,
                    adultsWithin50: 600, victims: { t1: 1.75, t2: 2, t3: 0.75, total: 4.5 } },
                regional: { t1Adults: 120, t2Adults: 230, t3Adults: 350, adultsWithin50: 700, baseAdultsWithin50: 700 / 1.2, victimsWithin50: 5.275 },
                byCounty: [
                    { fips: '18003', t1Pop: 100, t2Pop: 200, t3Pop: 300 },
                    { fips: '18033', t1Pop: 20, t2Pop: 30, t3Pop: 50 }
                ]
            }
        }
    };
}

test('restore preserves projected population, zone totals, and county attribution without a live map', () => {
    const snapshot = scenario();
    const restored = visualization.restore(snapshot);
    assert.equal(JSON.stringify(restored), JSON.stringify(snapshot.visualizationContext.impactBreakdown));
    restored.county.adults = 999;
    assert.equal(snapshot.visualizationContext.impactBreakdown.county.adults, 1200);
});

test('zero population and victims stay zero rather than becoming defaults', () => {
    const snapshot = scenario();
    const detail = snapshot.visualizationContext.impactBreakdown;
    for (const scope of [detail.county, detail.regional])
        for (const key of Object.keys(scope)) if (typeof scope[key] === 'number') scope[key] = 0;
    detail.county.victims = { t1: 0, t2: 0, t3: 0, total: 0 };
    detail.byCounty = [];
    assert.equal(visualization.restore(snapshot).county.adults, 0);
});

test('old or incomplete saved contexts require a new selection instead of an empty visualization', () => {
    const snapshot = scenario();
    delete snapshot.visualizationContext;
    assert.throws(() => visualization.restore(snapshot), /no restorable visualization/);
    snapshot.visualizationContext = { version: 'unsupported', impactBreakdown: {} };
    assert.throws(() => visualization.restore(snapshot));
});

test('mismatched locations and changed zones are rejected', () => {
    for (const [key, value] of [['stateFips', '39'], ['countyFips', '18033'], ['markerLatitude', 40], ['markerLongitude', -84]]) {
        const snapshot = scenario();
        snapshot[key] = value;
        assert.throws(() => visualization.restore(snapshot), /selected location/);
    }
    const snapshot = scenario();
    snapshot.zoneConfiguration = { ...snapshot.zoneConfiguration, tiers: [10, 20, 50] };
    assert.throws(() => visualization.restore(snapshot), /selected impact zones/);
});

test('population reconciliation, finite values, and duplicate counties are enforced', () => {
    const mutations = [
        detail => { detail.county.adults = -1; },
        detail => { detail.county.t1Adults = NaN; },
        detail => { detail.regional.adultsWithin50 = 701; },
        detail => { detail.county.victims.total = 7; },
        detail => { detail.byCounty[1].t1Pop = 21; },
        detail => { detail.byCounty.push({ ...detail.byCounty[0] }); }
    ];
    for (const mutate of mutations) {
        const snapshot = scenario();
        mutate(snapshot.visualizationContext.impactBreakdown);
        assert.throws(() => visualization.restore(snapshot));
    }
});

test('disabled zones cannot retain population in a saved visualization', () => {
    const snapshot = scenario();
    snapshot.zoneConfiguration.count = 1;
    snapshot.zoneConfiguration.tiers = [5];
    assert.throws(() => visualization.restore(snapshot), /Disabled impact zones/);
});

test('disabled zones cannot retain victims when their populations are zero', () => {
    const snapshot = scenario();
    snapshot.zoneConfiguration.count = 2;
    snapshot.zoneConfiguration.tiers = [5, 12];
    const detail = snapshot.visualizationContext.impactBreakdown;
    detail.county.t3Adults = 0;
    detail.county.adultsWithin50 = 300;
    detail.regional.t3Adults = 0;
    detail.regional.adultsWithin50 = 350;
    detail.regional.baseAdultsWithin50 = 350 / 1.2;
    detail.byCounty.forEach(county => { county.t3Pop = 0; });
    assert.throws(() => visualization.restore(snapshot), /Disabled impact zones contain saved victims/);
    detail.county.victims.t3 = 0;
    detail.county.victims.total = 3.75;
    detail.regional.victimsWithin50 = 4.4;
    assert.equal(visualization.restore(snapshot).county.victims.total, 3.75);
});

test('equivalent zone JSON restores regardless of property order', () => {
    const snapshot = scenario();
    snapshot.zoneConfiguration = { unit: 'miles', tiers: [5, 12, 40], mode: 'radius', count: 3 };
    assert.equal(visualization.restore(snapshot).county.adults, 1200);
});

test('nonzero baseline increase survives restoration and county cases conserve the regional result', () => {
    const restored = visualization.restore(scenario());
    const countyCases = restored.byCounty.map(county => visualization.calculateExposure(
        [county.t1Pop, county.t2Pop, county.t3Pop], restored.baselineRate, restored.baselineIncrease).totalIncrementalCases);
    assert.equal(restored.baselineIncrease, 0.25);
    assert.equal(countyCases[0], 4.5);
    assert.ok(Math.abs(countyCases[1] - 0.775) < 1e-12);
    assert.ok(Math.abs(countyCases.reduce((sum, value) => sum + value, 0) - restored.regional.victimsWithin50) < 1e-12);
    // Expected cases remain fractional for costing; rounding is only presentation.
    assert.equal(countyCases[0] * 13203, 59413.5);
    assert.notEqual(Math.round(countyCases[0]) * 13203, 59413.5);
});

test('explicit zero prevalence and increase produce no invented cases', () => {
    const result = visualization.calculateExposure([100, 200, 300], 0, 0);
    assert.equal(result.totalIncrementalCases, 0);
    assert.equal(result.totalEstimatedCases, 0);
});

test('total prevalence includes baseline increase and differs from incremental cases by baseline cases', () => {
    const result = visualization.calculateExposure([100, 200, 300], 1.5, 0.25);
    assert.equal(result.totalEstimatedCases, 13.5);
    assert.equal(result.totalEstimatedCases - result.totalIncrementalCases, 600 * 0.015);
});

test('missing assumptions, tampered cases, host attribution and out-of-state counties fail restoration', () => {
    for (const mutate of [
        detail => { delete detail.baselineIncrease; },
        detail => { detail.baselineIncrease = 0; },
        detail => { detail.county.victims.t1 += 1; detail.county.victims.total += 1; },
        detail => { detail.regional.victimsWithin50 += 1; },
        detail => { detail.byCounty[0].t1Pop -= 1; detail.byCounty[1].t1Pop += 1; },
        detail => { detail.byCounty[1].fips = '39001'; }
    ]) {
        const snapshot = scenario();
        mutate(snapshot.visualizationContext.impactBreakdown);
        assert.throws(() => visualization.restore(snapshot));
    }
});

test('invalid prevalence, increase, and population values are rejected', () => {
    for (const [population, baseline, increase] of [
        [[1, 2], 1.5, 0], [[1, -2, 3], 1.5, 0], [[1, 2, Infinity], 1.5, 0],
        [[1, 2, 3], 1.5, -1], [[1, 2, 3], NaN, 0], [[1, 2, 3], 51, 0]
    ]) assert.throws(() => visualization.calculateExposure(population, baseline, increase));
});

test('projected population is conserved across small origin groups without per-point rounding', () => {
    const origins = [1, 1, 1, 1, 1];
    const total = origins.map(value => visualization.scalePopulation(value, 1.2)).reduce((sum, value) => sum + value, 0);
    assert.equal(total, visualization.scalePopulation(5, 1.2));
    assert.equal(total, 6);
    assert.equal(visualization.scalePopulation(0, 1.2), 0);
});

test('saved projection assumptions and county/regional controls must reconcile', () => {
    for (const mutate of [
        detail => { delete detail.populationProjection; },
        detail => { detail.populationProjection.multiplier = 1.1; },
        detail => { detail.populationProjection.annualGrowthRate = -100; },
        detail => { detail.populationProjection.targetYear = 2025.5; },
        detail => { detail.populationProjection.yearsFromBase = 7; },
        detail => { detail.populationProjection.usesProjectedAdults = false; },
        detail => { detail.county.adults = 1201; },
        detail => { detail.regional.baseAdultsWithin50 += 1; }
    ]) {
        const snapshot = scenario();
        mutate(snapshot.visualizationContext.impactBreakdown);
        assert.throws(() => visualization.restore(snapshot));
    }
});
