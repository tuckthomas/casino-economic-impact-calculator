import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

function loadBenchmarks(move = async () => true, ready = true) {
  const calls = [];
  const buttons = ['steubenLow', 'steubenBase', 'steubenHigh'].map(key => ({
    dataset: { agrPreset: key }, disabled: false, classList: { toggle() {} }, setAttribute() {}
  }));
  const status = { textContent: '', classList: { toggle() {} } };
  const agr = { value: '112.00', dispatchEvent(event) { calls.push({ event: event.type, agr: this.value }); } };
  const window = { MapLibreImpactMap: {
    getMap: () => ready ? {} : null,
    async setCasinoLocation(...args) { calls.push({ location: args }); return move(...args); }
  } };
  const context = { window, Event, document: {
    getElementById: id => id === 'input-agr' ? agr : id === 'steuben-benchmark-status' ? status : null,
    querySelectorAll: () => buttons
  } };
  vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/economics/calculator.js', import.meta.url), 'utf8'), context);
  return { window, calls, buttons, status, agr };
}

test('each Steuben benchmark places the marker at the study-area interchange before applying its AGR', async () => {
  for (const [key, expected] of [['steubenLow', '188.60'], ['steubenBase', '203.10'], ['steubenHigh', '214.00']]) {
    let finishMove;
    const h = loadBenchmarks(() => new Promise(resolve => { finishMove = resolve; }));
    const pending = h.window.applySteubenAgrPreset(key);
    assert.equal(h.agr.value, '112.00');
    assert.equal(h.calls.length, 1);
    assert.deepEqual(h.calls[0].location, [41.7321, -85.0003, '18151']);
    assert.ok(h.buttons.every(button => button.disabled));
    await h.window.applySteubenAgrPreset('steubenHigh');
    assert.equal(h.calls.length, 1, 'pending location requests must not overlap');
    finishMove(true);
    await pending;
    assert.equal(h.agr.value, expected);
    assert.deepEqual(h.calls.slice(1).map(call => call.event), ['input', 'change']);
    assert.ok(h.buttons.every(button => !button.disabled));
    assert.match(h.status.textContent, /approximate I-69\/I-80\/I-90 study-area point/);
  }
});

test('failed or unavailable map leaves AGR intact and allows another attempt', async () => {
  for (const [move, ready] of [[async () => false, true], [async () => { throw new Error('Network failed'); }, true], [async () => true, false]]) {
    const h = loadBenchmarks(move, ready);
    await h.window.applySteubenAgrPreset('steubenBase');
    assert.equal(h.agr.value, '112.00');
    assert.ok(h.buttons.every(button => !button.disabled));
    assert.ok(h.status.textContent.length > 0);
    assert.equal(h.calls.filter(call => call.event).length, 0);
    if (ready) {
      h.window.MapLibreImpactMap.setCasinoLocation = async () => true;
      await h.window.applySteubenAgrPreset('steubenBase');
      assert.equal(h.agr.value, '203.10');
    }
  }
});

test('ordinary AGR changes do not relocate the marker and unknown benchmark keys are ignored', async () => {
  const h = loadBenchmarks();
  h.window.applyAgrPreset(330, 'Custom');
  assert.equal(h.agr.value, '330.00');
  await h.window.applySteubenAgrPreset('unknown');
  assert.equal(h.agr.value, '330.00');
  assert.equal(h.calls.filter(call => call.location).length, 0);
});
