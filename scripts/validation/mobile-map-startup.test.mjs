import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

function setup() {
  const events = new Map();
  const layers = new Map();
  const calls = [];
  const mapEvents = new Map();
  const element = () => ({ style: {}, children: [], append(...items) { this.children.push(...items); }, setAttribute() {}, remove() {} });
  const container = element();
  const map = {
    getSource: () => ({}), getLayer: id => layers.get(id),
    addLayer: layer => { calls.push(['layer', layer]); layers.set(layer.id, { ...layer, layout: {} }); },
    getLayoutProperty: (id, property) => layers.get(id)?.layout[property],
    setLayoutProperty: (id, property, value) => { layers.get(id).layout[property] = value; },
    on: (name, handler) => mapEvents.set(name, handler), off() {}, getContainer: () => container,
    fitBounds: (...args) => calls.push(['bounds', ...args])
  };
  const api = { getMap: () => map, loadState: async fips => calls.push(['state', fips]) };
  class Marker {
    constructor({ element }) { this.element = element; }
    setLngLat(coordinates) { this.coordinates = coordinates; return this; }
    addTo(map) { map.getContainer().append(this.element); calls.push(['label', this]); return this; }
    remove() {}
  }
  const context = { maplibregl: { Marker }, window: { addEventListener: (name, handler) => events.set(name, handler) }, console,
    document: { createElement: element, createTextNode: text => text } };
  vm.runInNewContext(readFileSync(new URL('../../SaveNEIN.Client/wwwroot/js/components/northeast-indiana-startup.js', import.meta.url), 'utf8'), context);
  return { startup: context.window.NortheastIndianaMapStartup, api, calls, events, layers, container, mapEvents };
}

test('optional startup selects Indiana without selecting a county and frames all three highlighted counties', async () => {
  const { startup, api, calls, layers, container } = setup();
  await startup.apply(api);
  assert.deepEqual(calls[0], ['state', '18']);
  const layer = layers.get('nein-featured-counties');
  assert.deepEqual(Array.from(layer.filter[2][1]), ['18151', '18033', '18003']);
  assert.equal(layer.layout.visibility, 'visible');
  assert.equal(layer.paint['line-width'], 3);
  assert.ok(calls.some(call => call[0] === 'bounds'));
  assert.equal(layer.paint['line-color'], '#b08d57');
  const fill = layers.get('nein-featured-counties-fill');
  assert.equal(fill.paint['fill-color'], '#b08d57');
  assert.equal(fill.paint['fill-opacity'], 0.16);
  const names = container.children.filter(element => element.className === 'nein-featured-county-name');
  assert.equal(names.length, 3);
  assert.deepEqual(names.map(element => element.textContent), ['Steuben County', 'DeKalb County', 'Allen County']);
  assert.ok(names.every(element => element.style.pointerEvents === 'none'));
  assert.ok(names.every(element => element.style.color === '#0f172a' && element.style.backgroundColor === '#efe3cc'));
});

test('back to national hides regional highlights and startup cannot force the user back to Indiana', async () => {
  const { startup, api, calls, events, layers, container } = setup();
  await startup.apply(api);
  events.get('impact-map-navigation')({ detail: { step: 3, stateFips: '18' } });
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'none');
  assert.equal(layers.get('nein-featured-counties-fill').layout.visibility, 'none');
  assert.ok(container.children.filter(element => element.className === 'nein-featured-county-name').every(element => element.hidden));
  events.get('impact-map-navigation')({ detail: { step: 2, stateFips: '18' } });
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'visible');
  assert.ok(container.children.every(element => !element.hidden));
  events.get('impact-map-navigation')({ detail: { step: 1, stateFips: null } });
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'none');
  assert.ok(container.children.filter(element => element.className === 'nein-featured-county-name').every(element => element.hidden && element.style.display === 'none'));
  assert.equal(layers.get('nein-featured-counties-fill').layout.visibility, 'none');
  await startup.apply(api);
  assert.equal(calls.filter(call => call[0] === 'state').length, 1);
  assert.equal(calls.filter(call => call[0] === 'bounds').length, 1);
  events.get('impact-map-navigation')({ detail: { step: 2, stateFips: '26' } });
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'none');
  events.get('impact-map-navigation')({ detail: { step: 2, stateFips: '18' } });
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'visible');
  assert.equal(layers.get('nein-featured-counties-fill').layout.visibility, 'visible');
  assert.ok(container.children.every(element => !element.hidden));
});

test('style restoration keeps the county outlines without resetting the view or selecting Indiana again', async () => {
  const { startup, api, calls, layers, mapEvents } = setup();
  await startup.apply(api);
  layers.clear();
  mapEvents.get('styledata')();
  assert.equal(layers.get('nein-featured-counties').layout.visibility, 'visible');
  assert.equal(calls.filter(call => call[0] === 'state').length, 1);
  assert.equal(calls.filter(call => call[0] === 'bounds').length, 1);
});
