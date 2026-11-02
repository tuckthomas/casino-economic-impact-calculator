import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

function loadCalculator() {
  const containers = new Map();
  for (const id of ['net-impact-host-table', 'net-impact-regional-table', 'net-impact-consolidated-table',
    'net-impact-note', 'net-impact-host-subtitle', 'net-impact-regional-expand-all']) {
    containers.set(id, { innerHTML: '', textContent: '', classList: { toggle() {} }, setAttribute() {} });
  }
  const context = { window: {}, document: { getElementById: id => containers.get(id) ?? null } };
  vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/calculator.js', import.meta.url), 'utf8'), context);
  return { calculator: context.window.EconomicCalculator, render: context.window.renderNetEconomicImpactTable, containers };
}

test('research default yields $24.3M sales displacement on $100M AGR', () => {
  const { calculator } = loadCalculator();
  const result = calculator.calculateDisplacementScenario(100_000_000, undefined, -20_000_000);
  assert.equal(result.coefficientPerThousand, 243);
  assert.equal(result.displacedSales, 24_300_000);
  assert.equal(result.baseBalance, -20_000_000);
  assert.equal(result.adjustedBalance, -44_300_000);
});

test('zero excludes sales displacement and overrides use dollars per thousand', () => {
  const { calculator } = loadCalculator();
  assert.equal(calculator.calculateDisplacementScenario(100_000_000, 0, 77).adjustedBalance, 77);
  assert.equal(calculator.calculateDisplacementScenario(100_000_000, 500).displacedSales, 50_000_000);
  assert.equal(calculator.calculateDisplacementScenario(0, 243, 77).adjustedBalance, 77);
  assert.equal(calculator.calculateDisplacementScenario(100_000_000, 1000).displacedSales, 100_000_000);
  for (const coefficient of [-1, 1001, NaN, Infinity]) {
    assert.throws(() => calculator.calculateDisplacementScenario(100, coefficient), { name: 'RangeError' });
  }
  for (const revenue of [-1, NaN, Infinity]) {
    assert.throws(() => calculator.calculateDisplacementScenario(revenue), { name: 'RangeError' });
  }
});

test('sensitivity changes displaced sales linearly without changing nonlinear social costs', () => {
  const { calculator } = loadCalculator();
  const options = { revenueShare: 1, baseCost: 20_000_000, anchorAgrM: 100 };
  const before = calculator.createSensitivityNetEvaluator(options);
  const after = calculator.createSensitivityNetEvaluator({ ...options, displacementCoefficient: 243 });
  for (const agrM of [0, 25, 100, 200, 500]) {
    assert.ok(Math.abs((before(agrM) - after(agrM)) - agrM * 1_000_000 * 0.243) < 1e-6);
  }
});

test('host allocation is adjustable and the consolidated total is counted once', () => {
  const { calculator, render, containers } = loadCalculator();
  const model = {
    subjectCountyName: 'Allen', subjectCountyFips: '18003', subjectStateName: 'Indiana', baselineRate: 2.3,
    displacement: calculator.calculateDisplacementScenario(100_000_000, 243, 77_000_000),
    rows: [
      { key: 'gen_sub', countyCost: 10_000_000 }, { key: 'private_sub', countyCost: 10_000_000 },
      { key: 'county_revenue', revenue: 5_000_000 },
      { key: 'host_local_revenue_sub', revenue: 5_000_000 },
      { key: 'host_local_total', countyBalance: -15_000_000 },
      { key: 'total_revenue', revenue: 100_000_000 },
      { key: 'total', countyBalance: 80_000_000, otherCost: 3_000_000 }
    ],
    otherCounties: [{ fips: '18033', name: 'DeKalb', costs: { total: 3_000_000, public: 1_000_000, private: 2_000_000 } }]
  };
  render(model);
  const consolidated = containers.get('net-impact-consolidated-table').innerHTML;
  assert.match(consolidated, /77\.0MM/);
  assert.match(consolidated, /24\.3MM/);
  assert.match(consolidated, /52\.7MM/);
  assert.equal((consolidated.match(/Displaced business sales/g) ?? []).length, 1);
  const host = containers.get('net-impact-host-table').innerHTML;
  assert.match(host, /100% assumed host-area share/);
  assert.match(host, /24\.3MM/);
  assert.match(host, /39\.3MM/);
  assert.match(host, /text-red-400[^>]*>\$24\.3MM/);
  assert.match(consolidated, /Business-Sales Displacement/);
  assert.doesNotMatch(consolidated, /Separate Business-Sales Scenario|text-amber-300/);
  assert.doesNotMatch(containers.get('net-impact-regional-table').innerHTML, /Displaced business sales|24\.3MM/);
  model.displacement = calculator.calculateDisplacementScenario(100_000_000, 243, 77_000_000, 75);
  render(model);
  assert.equal(model.displacement.hostDisplacedSales, 18_225_000);
  assert.equal(model.displacement.unallocatedDisplacedSales, 6_075_000);
  assert.match(containers.get('net-impact-host-table').innerHTML, /75% assumed host-area share/);
  assert.match(containers.get('net-impact-host-table').innerHTML, /33\.2MM/);
  assert.match(containers.get('net-impact-consolidated-table').innerHTML, /52\.7MM/);
  model.displacement = calculator.calculateDisplacementScenario(100_000_000, 243, 77_000_000, 0);
  render(model);
  assert.match(containers.get('net-impact-host-table').innerHTML, /0% assumed host-area share/);
  assert.match(containers.get('net-impact-host-table').innerHTML, /15\.0MM/);
  assert.match(containers.get('net-impact-consolidated-table').innerHTML, /52\.7MM/);
  model.displacement = calculator.calculateDisplacementScenario(100_000_000, 0, 77_000_000);
  render(model);
  assert.match(containers.get('net-impact-consolidated-table').innerHTML, /\$0 per \$1,000 AGR/);
});

test('host share rejects invalid allocations and scales host sensitivity only', () => {
  const { calculator } = loadCalculator();
  for (const share of [-1, 101, NaN, Infinity]) {
    assert.throws(() => calculator.calculateDisplacementScenario(100_000_000, 243, 0, share), { name: 'RangeError' });
  }
  const options = { revenueShare: 0.2, baseCost: 20_000_000, anchorAgrM: 100 };
  const before = calculator.createSensitivityNetEvaluator(options);
  const after = calculator.createSensitivityNetEvaluator({ ...options, displacementCoefficient: 243 * 0.75 });
  for (const agrM of [0, 25, 100, 200]) {
    assert.ok(Math.abs(before(agrM) - after(agrM) - agrM * 1_000_000 * 0.243 * 0.75) < 1e-6);
  }
});

test('methodology opens on a direct route and closing restores focus and scrolling', () => {
  let restoredFocus = false;
  const trigger = { isConnected: true, focus() { restoredFocus = true; } };
  const dialog = {
    open: false, scrollTop: 99,
    showModal() { this.open = true; },
    close() { this.open = false; this.onclose(); }
  };
  const context = { window: {}, document: {
    activeElement: trigger, body: { style: { overflow: 'auto' } },
    getElementById: id => id === 'methodology-modal' ? dialog : null
  } };
  vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/components/modals.js', import.meta.url), 'utf8'), context);
  context.window.openMethodologyModal();
  assert.equal(dialog.open, true);
  assert.equal(dialog.scrollTop, 0);
  assert.equal(context.document.body.style.overflow, 'hidden');
  context.window.closeMethodologyModal();
  assert.equal(dialog.open, false);
  assert.equal(context.document.body.style.overflow, 'auto');
  assert.equal(restoredFocus, true);
});
