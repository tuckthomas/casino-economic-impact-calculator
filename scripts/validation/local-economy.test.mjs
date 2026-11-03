import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const context = { window: {}, document: { getElementById: () => null } };
vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/local-economy.js', import.meta.url), 'utf8'), context);
const { hiringModel, evidenceNarrative } = context.window.LocalEconomy;
const settings = { positions: 1000, ftePerPosition: .8, previouslyUnemployedPercent: 25, existingEmployersPercent: 25, inCommutersPercent: 25, newResidentsPercent: 25, supportingInformation: '' };
const profile = { countyFips: '18151', industryYear: 2025, industries: [
  { code: '10', name: 'All private', jobs: 10000, establishments: 500, annualPay: 50000, changeFiveYear: 10, changeTenYear: 20 },
  { code: '72', name: 'Accommodation and food services', jobs: 2000, share: 20, annualPay: 25000, changeFiveYear: 15, changeTenYear: 25 },
  { code: '31-33', name: 'Manufacturing', jobs: 3000, share: 30, annualPay: 65000 }
], workforce: { civilianLaborForce: { value: 10000, marginOfError: 500 }, unemployed: { value: 200, marginOfError: 30 } }, occupations: [], notices: [] };

test('hiring source assignments sum to people; FTE scales independently and existing-employer transfers are not new employed residents', () => {
  const model = hiringModel(settings, profile);
  assert.equal(model.valid, true);
  assert.equal(model.fte, 800);
  assert.equal(model.unemployedHires + model.transfers + model.commuters + model.newResidents, 1000);
  assert.equal(model.newlyEmployedResidents, 500);
  assert.equal(model.shareOfUnemployed, 125);
});

test('invalid shares, missing values, negative jobs and invalid FTE never produce a hiring result', () => {
  for (const override of [{ existingEmployersPercent: 26 }, { positions: NaN }, { positions: -1 }, { positions: 1.5 }, { ftePerPosition: 0 }, { ftePerPosition: 1.1 }, { inCommutersPercent: Infinity }]) {
    assert.equal(hiringModel({ ...settings, ...override }, profile).valid, false);
  }
  assert.equal(hiringModel({ ...settings, positions: 0 }, profile).valid, true);
});

test('automated industry and recruitment narrative preserves periods, geography, evidence and conditional interpretation', () => {
  const text = evidenceNarrative(profile, 'Steuben County', settings);
  assert.match(text.industry.join(' '), /10,000 private UI-covered workplace jobs/);
  assert.match(text.industry.join(' '), /2020.*2025.*2015.*2025/);
  assert.match(text.industry.join(' '), /not allocated into drive-time polygons/);
  const workforce = text.workforce.join(' ');
  assert.match(workforce, /10,000 ± 500/);
  assert.match(workforce, /exceed the baseline unemployment estimate/);
  assert.match(workforce, /not a measured net jobs benefit/);
  assert.match(workforce, /No supporting information/);
  assert.match(workforce, /do not measure incoming commuters/);
});

test('unavailable statistics stay unavailable and supporting text is escaped', () => {
  const text = evidenceNarrative({ ...profile, industries: [], workforce: null }, '<County>', { ...settings, supportingInformation: '<script>bad</script>' });
  assert.match(text.industry.join(' '), /Not available/);
  assert.match(text.workforce.join(' '), /&lt;script&gt;/);
  assert.doesNotMatch(text.workforce.join(' '), /<script>/);
  assert.doesNotMatch(text.workforce.join(' '), /NaN|Infinity/);
});

test('out-of-order county requests cannot mix old evidence into the selected county or its PDF', async () => {
  const resolvers = [];
  const ctx = { window: {}, document: { getElementById: () => null }, AbortController, fetch: () => new Promise(resolve => resolvers.push(resolve)) };
  vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/local-economy.js', import.meta.url), 'utf8'), ctx);
  ctx.window.LocalEconomy.update('18151', 'Steuben County');
  ctx.window.LocalEconomy.update('18003', 'Allen County');
  resolvers[1]({ ok: true, json: async () => ({ ...profile, countyFips: '18003' }) });
  await ctx.window.LocalEconomy.ensureReady();
  resolvers[0]({ ok: true, json: async () => profile });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(ctx.window.LocalEconomy.getReport().profile.countyFips, '18003');
});

test('monthly LAUS evidence takes precedence for staffing ratios and remains separate from ACS', () => {
  const data = { ...profile, currentLaborMarket: { period: '2026-09', laborForce: 12000, unemployed: 400, employed: 11600, unemploymentRate: 3.3, preliminary: true } };
  const model = hiringModel(settings, data);
  assert.equal(model.shareOfUnemployed, 62.5);
  assert.equal(model.baselineLabel, 'BLS LAUS 2026-09');
  const text = evidenceNarrative(data, 'Test County', settings).workforce.join(' ');
  assert.match(text, /not seasonally adjusted; preliminary/);
  assert.match(text, /LAUCN181510000000003/);
  assert.match(text, /ACS 2020–2024/);
});
