import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const context = { window: {}, document: { getElementById: () => null } };
vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/calculator.js', import.meta.url), 'utf8'), context);
const { renderSocialCostAssumptions, describeExposure, describeHostDisplacement, calculateDisplacementScenario } = context.window.EconomicCalculator;
const defaults = [
  ['Crime', 1625], ['Lost Employment', 4051], ['Civil Legal', 431],
  ['Public Health', 1328], ['Social Services', 713], ['Abused Dollars', 5055]
].map(([label, reference]) => ({ label, reference, selected: reference }));

test('unchanged research defaults have no override notice and restoring a value removes it', () => {
  const initial = renderSocialCostAssumptions(defaults, 10);
  assert.match(initial, /Research defaults in use/);
  assert.match(initial, /\$13,203/);
  assert.doesNotMatch(initial, /User-adjusted social costs/);
  const adjusted = defaults.map(entry => ({ ...entry }));
  adjusted[0].selected = 1000;
  assert.match(renderSocialCostAssumptions(adjusted, 10), /User-adjusted social costs/);
  adjusted[0].selected = 1625;
  assert.equal(renderSocialCostAssumptions(adjusted, 10), initial);
});

test('lower, higher and zero assumptions disclose exact research deviation and supporting-evidence responsibility', () => {
  const adjusted = defaults.map(entry => ({ ...entry }));
  adjusted[0].selected = 1000;
  adjusted[1].selected = 5000;
  adjusted[2].selected = 0;
  const html = renderSocialCostAssumptions(adjusted, 10);
  assert.match(html, /Selected \$1,000 versus research-derived default \$1,625.*\$625 lower; 38.5% lower/);
  assert.match(html, /Selected \$5,000 versus research-derived default \$4,051.*\$949 higher; 23.4% higher/);
  assert.match(html, /Selected \$0 versus research-derived default \$431/);
  assert.match(html, /supply supporting research or data/);
  assert.match(html, /entering zero assumes no cost/);
  assert.match(html, /reduce.*by \$1,070/);
  assert.doesNotMatch(html, /<strong>Public Health:/);
});

test('offsetting category changes remain disclosed even when totals are unchanged', () => {
  const adjusted = defaults.map(entry => ({ ...entry }));
  adjusted[0].selected -= 100;
  adjusted[1].selected += 100;
  const html = renderSocialCostAssumptions(adjusted, 123.4);
  assert.match(html, /User-adjusted social costs/);
  assert.match(html, /leave unchanged/);
});

test('geographic descriptions distinguish routing contours from radius bands and unavailable metadata', () => {
  const drive = describeExposure({ mode: 'isochrone' });
  assert.equal(drive.footprint, '60-minute drive-time footprint');
  assert.equal(drive.bands[1], '15–30 minutes');
  assert.match(drive.methodology, /Valhalla automobile-routing/);
  assert.match(drive.methodology, /fraction of each block group’s area/);
  assert.match(drive.methodology, /cumulative 30-minute minus 15-minute/);
  assert.match(drive.methodology, /does not request live traffic/);
  const radius = describeExposure({ mode: 'radius' });
  assert.equal(radius.footprint, '50-mile radius footprint');
  assert.equal(radius.bands[1], '10–20 miles');
  assert.match(radius.methodology, /representative point/);
  assert.doesNotMatch(radius.methodology, /Valhalla/);
  assert.match(describeExposure().methodology, /not yet available/);
});

test('host sales explanation describes business losses and financial consequences at full, partial and zero shares', () => {
  const full = describeHostDisplacement(calculateDisplacementScenario(100e6), 'Steuben County', 10e6);
  assert.match(full, /all \$24.3MM.*Steuben County/);
  assert.match(full, /from \$10.0MM.*-\$14.3MM/);
  assert.match(full, /not a \$24.3MM expense to county government/);
  assert.doesNotMatch(full, /remainder|unallocated|\$0.0MM/);
  for (const share of [75, 0]) {
    const description = describeHostDisplacement(calculateDisplacementScenario(100e6, 243, 0, share), 'Steuben County', 10e6);
    assert.match(description, new RegExp(`assigns ${share}%`));
    assert.match(description, /not the total sales loss/);
    assert.match(description, /without being assigned to individual other counties/);
  }
});

// Expose closure functions only in the VM so this exercises the real loader without a WebGL map.
test('county-context points are not added again to authoritative county population totals', async () => {
  const source = readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/components/maplibre-map.js', import.meta.url), 'utf8');
  const marker = '    // === PUBLIC API ===\n\n    return {';
  assert.ok(source.includes(marker));
  const instrumented = source.replace(marker, marker + '\n        testLoadContext: loadCountyContext, testCachedContext: fips => contextCache[fips],');
  const payload = { county_adults: 27504, county_total: 34435, points: [
    [-85, 41.7, 14000, '18151'], [-85.1, 41.6, 13504, '18151'], [-85.2, 41.4, 5000, '18033']
  ] };
  const mapContext = {
    window: {}, document: { getElementById: () => null }, AbortController, setTimeout, clearTimeout, console,
    fetch: async () => ({ ok: true, text: async () => JSON.stringify(payload) })
  };
  vm.runInNewContext(instrumented, mapContext);
  const map = mapContext.window.MapLibreImpactMap;
  assert.equal(await map.testLoadContext('18151', true, '', false, true), true);
  const cached = map.testCachedContext('18151');
  assert.equal(cached.totals.adults, 27504);
  assert.equal(cached.totals.total, 34435);
  assert.equal(cached.calcFeatures.length, 3);
  assert.equal(cached.calcFeatures.reduce((sum, point) => sum + point.popAdult, 0), 32504);
});
