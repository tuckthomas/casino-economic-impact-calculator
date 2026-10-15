import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const context = vm.createContext({ window: {} });
vm.runInContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/zones.js', import.meta.url), 'utf8'), context);
const zones = context.window.EconomicZones;
const radii = { tier1: 5 * 1609.34, tier2: 12 * 1609.34, tier3: 40 * 1609.34 };

test('custom map radii and saved thresholds agree at boundaries', () => {
    const configuration = zones.snapshot('radius', 3, radii);
    assert.equal(JSON.stringify(configuration), JSON.stringify({ count: 3, tiers: [5, 12, 40], unit: 'miles', mode: 'radius' }));
    for (const [distance, expected] of [[0, 1], [5, 1], [5.01, 2], [12, 2], [12.01, 3], [40, 3], [40.01, null]])
        assert.equal(zones.tierForDistance(distance, configuration), expected);
});

test('disabled outer zones do not capture population or appear in the snapshot', () => {
    for (const count of [1, 2]) {
        const configuration = zones.snapshot('radius', count, radii);
        assert.equal(configuration.tiers.length, count);
        assert.equal(zones.tierForDistance(configuration.tiers.at(-1) + 0.01, configuration), null);
    }
});

test('routed modes preserve minutes instead of treating them as miles', () => {
    for (const mode of ['isochrone', 'grid']) {
        const configuration = zones.snapshot(mode, 1, radii);
        assert.equal(configuration.unit, 'minutes-drive-time');
        assert.equal(configuration.count, 3);
        assert.equal(JSON.stringify(configuration.tiers), '[15,30,60]');
    }
});

test('unsafe zone and distance values fail explicitly', () => {
    for (const count of [0, 4, 1.5]) assert.throws(() => zones.radiusConfiguration(count, radii));
    assert.throws(() => zones.radiusConfiguration(3, { ...radii, tier2: radii.tier1 }));
    assert.throws(() => zones.radiusConfiguration(1, { ...radii, tier1: NaN }));
    assert.throws(() => zones.snapshot('unknown', 3, radii));
    for (const distance of [-1, NaN, Infinity])
        assert.throws(() => zones.tierForDistance(distance, zones.snapshot('radius', 3, radii)));
});
