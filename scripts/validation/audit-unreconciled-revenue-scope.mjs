// Error bounds for an incomplete resident prediction, not point accuracy.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function errorBounds(rows) {
  assert(rows.length > 0, 'Empty comparisons have no error bound.');
  for (const row of rows) {
    assert(Number.isFinite(row.observed) && row.observed > 0);
    assert(Number.isFinite(row.residentPrediction) && row.residentPrediction >= 0);
  }
  const scale = Math.max(...rows.map(row => row.observed));
  const denominator = rows.reduce((sum, row) => sum + row.observed / scale, 0);
  const floor = rows.map(row => 100 * Math.max(row.residentPrediction - row.observed, 0) / row.observed);
  assert(floor.every(Number.isFinite), 'Non-finite error bounds cannot be serialized as missing or zero errors.');
  return { properties: rows.length,
    minimumPossibleTotalMape: floor.reduce((sum, value) => sum + value, 0) / rows.length,
    minimumPossibleTotalWape: 100 * rows.reduce((sum, row) => sum + Math.max(row.residentPrediction / scale - row.observed / scale, 0), 0) / denominator,
    minimumPossibleSignedBias: 100 * rows.reduce((sum, row) => sum + row.residentPrediction / scale - row.observed / scale, 0) / denominator,
    maximumUnavoidablePropertyApe: Math.max(...floor),
    propertiesDefinitelyExceeding40Percent: rows.filter((_, index) => floor[index] > 40).map(row => row.key),
    propertiesDefinitelyExceeding30Percent: rows.filter((_, index) => floor[index] > 30).map(row => row.key) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [protocolPath, resultPath, outputPath] = process.argv.slice(2);
  assert(protocolPath && resultPath && outputPath, 'Expected frozen protocol, historical component result and new audit path.');
  assert(!fs.existsSync(outputPath), 'Audit output already exists.');
  const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const protocol = JSON.parse(fs.readFileSync(protocolPath, 'utf8'));
  const result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
  assert.equal(result.protocolHash, hash(protocolPath));
  assert.equal(result.finalHoldoutScored, false);
  assert.equal(result.releaseAccepted, false);
  assert(!result.revenueScopeEvidenceSha256, 'This audit only accepts historical unreconciled resident-component results.');
  assert(!result.scopeReconciliation?.some(row => row.revenueScope.fixedNonresidentGgr > 0), 'Prediction already contains visitor additions.');
  const candidate = result.selected ?? result.bestDiagnosticCandidate;
  assert(candidate, 'Historical component observations are required.');
  const keys = [...protocol.training, ...protocol.selection];
  assert.deepEqual(candidate.observations.map(row => row.caseKey).sort(), [...keys].sort());
  assert.equal(new Set(keys).size, keys.length);
  const markets = new Map(Object.entries(protocol.developmentMarkets).flatMap(([market, values]) => values.map(key => [key, market])));
  const rows = candidate.observations.map(row => ({ key: row.caseKey,
    partition: protocol.training.includes(row.caseKey) ? 'training' : 'selection',
    market: markets.get(row.caseKey), jurisdiction: row.caseKey.split('-')[1],
    observed: row.observed, residentPrediction: row.predicted,
    unavoidableOverprediction: Math.max(row.predicted - row.observed, 0),
    fullPredictionAndObservedResidentRevenue: 'not identified by these inputs' }));
  const partitions = ['training', 'selection'].map(partition => {
    const selected = rows.filter(row => row.partition === partition);
    return { partition, ...errorBounds(selected), groups: ['market', 'jurisdiction'].flatMap(kind =>
      [...new Set(selected.map(row => row[kind]))].sort().map(key => ({ kind, key, ...errorBounds(selected.filter(row => row[kind] === key)) }))) };
  });
  const output = { version: 'unreconciled-revenue-scope-bounds-v1', protocolHash: hash(protocolPath), resultHash: hash(resultPath),
    predictionScope: 'modeled resident origins only', observationScope: 'all-patron property land-based revenue',
    pointAccuracyAvailable: false, residentSpendingIdentified: false, releaseAccepted: false,
    method: 'For R >= 0 and omitted demand N >= 0, min_N |R+N-Y| = max(R-Y,0). These are optimistic lower bounds under the existing nonnegative added-demand equations, not MAPE/WAPE estimates or a passing accuracy result. No revenue gaps become predicted visitor inputs.',
    partitions, rows };
  fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + '\n', { flag: 'wx' });
  console.log(JSON.stringify(partitions.map(({ partition, groups, ...bounds }) => ({ partition, ...bounds }))));
}
