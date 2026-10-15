// Compare the historical target circle with the server's origin-based field.
// This checks input selection only; it does not estimate revenue or accuracy.
import fs from 'node:fs';
import crypto from 'node:crypto';

const [protocolPath, bundlePath, originsPath, outputPath] = process.argv.slice(2);
if (!protocolPath || !bundlePath || !originsPath || !outputPath)
  throw new Error('Expected protocol, captured gaming bundle, captured origin CSV, and new output paths.');
if (fs.existsSync(outputPath)) throw new Error('Audit output already exists.');
const digest = path => crypto.createHash('sha256').update(fs.readFileSync(path)).digest('hex');
const protocol = JSON.parse(fs.readFileSync(protocolPath, 'utf8'));
const bundle = JSON.parse(fs.readFileSync(bundlePath, 'utf8'));
if (digest(bundlePath) !== protocol.sourceBundleSha256) throw new Error('Gaming input checksum differs from protocol.');
const [header, ...lines] = fs.readFileSync(originsPath, 'utf8').trim().split(/\r?\n/);
if (header !== 'stable_origin_id,latitude,longitude') throw new Error('Unexpected captured-origin CSV columns.');
const origins = lines.map(line => {
  const [key, latitude, longitude] = line.split(',');
  const result = { key, latitude: Number(latitude), longitude: Number(longitude) };
  if (!key || !Number.isFinite(result.latitude) || !Number.isFinite(result.longitude)) throw new Error('Invalid captured origin.');
  return result;
});
if (new Set(origins.map(row => row.key)).size !== origins.length) throw new Error('Duplicate captured origin.');
const radians = degrees => degrees * Math.PI / 180;
function within(a, b, miles) {
  const dlat = radians(b.latitude - a.latitude), dlon = radians(b.longitude - a.longitude);
  const h = Math.sin(dlat / 2) ** 2 + Math.cos(radians(a.latitude)) * Math.cos(radians(b.latitude)) * Math.sin(dlon / 2) ** 2;
  return 3958.7613 * 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h)) <= miles;
}
const facilities = bundle.Facilities.Rows.map(row => ({ ...row, key: row.StableVenueId, latitude: row.Latitude, longitude: row.Longitude }));
const effectiveOn = `${protocol.observedYear}-12-31`;
const substitutes = facilities.filter(row => row.IsActive && (!row.OpenedOn || row.OpenedOn <= effectiveOn) &&
  (!row.ClosedOn || row.ClosedOn > effectiveOn) && (row.HasSlots || row.HasTableGames || row.SlotOrVltPositions > 0 || row.TableGameCount > 0 ||
  /casino/i.test(row.VenueType ?? '') || /^racino$/i.test(row.VenueType ?? '') || /tribal/i.test(row.FacilityRegime ?? '')));
const keys = [...protocol.training, ...protocol.selection];
if (new Set(keys).size !== keys.length) throw new Error('Overlapping development partitions.');
const cases = keys.map(key => {
  const target = facilities.find(row => row.key === key);
  if (!target) throw new Error(`Missing target ${key}`);
  const selectedOrigins = origins.filter(row => within(row, target, protocol.originRadiusMiles));
  if (!selectedOrigins.length) throw new Error(`Empty origin field for ${key}`);
  const oldPeers = facilities.filter(row => row.key !== key && within(target, row, protocol.competitorRadiusMiles));
  const oldKeys = new Set(oldPeers.map(row => row.key));
  const newPeers = substitutes.filter(row => row.key !== key && selectedOrigins.some(origin => within(origin, row, protocol.competitorRadiusMiles)));
  const newKeys = new Set(newPeers.map(row => row.key));
  const added = newPeers.filter(row => !oldKeys.has(row.key));
  return { key, partition: protocol.training.includes(key) ? 'training' : 'selection', originCount: selectedOrigins.length,
    oldCompetitorCount: oldPeers.length, correctedCompetitorCount: newPeers.length,
    addedCompetitorKeys: added.map(row => row.key), removedCompetitorKeys: oldPeers.filter(row => !newKeys.has(row.key)).map(row => row.key),
    originsWithOmittedNearbyAlternative: selectedOrigins.filter(origin => added.some(row => within(origin, row, protocol.competitorRadiusMiles))).length };
});
const result = {
  version: 'calibration-choice-field-audit-v1',
  scope: 'Input-choice-field audit against captured inventory and origins, using the server broad inclusion equation. No routes, revenue predictions, parameter fitting, final holdout, or empirical accuracy claim.',
  protocolHash: digest(protocolPath), sourceBundleSha256: digest(bundlePath), sourceCsvSha256: digest(originsPath),
  demographicInputBundleSha256: protocol.demographicInputBundleSha256,
  originSnapshotId: protocol.originSnapshotId, capturedOriginCount: origins.length,
  affectedCaseCount: cases.filter(row => row.addedCompetitorKeys.length || row.removedCompetitorKeys.length).length,
  casesWithOmittedNearbyAlternatives: cases.filter(row => row.originsWithOmittedNearbyAlternative > 0).length,
  addedCaseCompetitorPairs: cases.reduce((sum, row) => sum + row.addedCompetitorKeys.length, 0), cases
};
fs.writeFileSync(outputPath, JSON.stringify(result, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ affectedCaseCount: result.affectedCaseCount, casesWithOmittedNearbyAlternatives: result.casesWithOmittedNearbyAlternatives,
  addedCaseCompetitorPairs: result.addedCaseCompetitorPairs }));
