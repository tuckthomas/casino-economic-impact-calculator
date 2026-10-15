// Reconcile the fixed-coefficient competitive-field correction, without fitting.
import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';

const [oldResultPath, protocolPath, resultPath, outputPath] = process.argv.slice(2);
if (!oldResultPath || !protocolPath || !resultPath || !outputPath)
  throw new Error('Expected previous result, corrected protocol, corrected result, and new audit output paths.');
if (fs.existsSync(outputPath)) throw new Error('Audit output already exists.');
const read = path => JSON.parse(fs.readFileSync(path, 'utf8'));
const hash = path => crypto.createHash('sha256').update(fs.readFileSync(path)).digest('hex');
const previous = read(oldResultPath), protocol = read(protocolPath), result = read(resultPath);
assert.equal(result.protocolHash, hash(protocolPath));
assert.equal(result.graphHash, previous.graphHash, 'Routing graph changed.');
assert.equal(result.candidateCount, 1, 'Correction must not perform another coefficient search.');
assert.equal(result.competitorSelectionVersion, 'origin-union-v2');
assert.equal(result.finalHoldoutScored, false);
assert.equal(result.releaseAccepted, false);
assert.deepEqual(result.inputSnapshots, previous.inputSnapshots, 'Frozen source snapshots changed.');
assert.deepEqual(result.observedGgrExcludedVenueIds, previous.observedGgrExcludedVenueIds);
const before = previous.selected ?? previous.bestDiagnosticCandidate;
const after = result.selected ?? result.bestDiagnosticCandidate;
assert(before && after, 'Both saved diagnostics are required.');
for (const [field, candidateField] of Object.entries({ beta: 'beta', alpha: 'alpha', positionsCoefficient: 'positionsCoefficient',
  gamingPositionsOffset: 'positionsOffset', gamingFloorCoefficient: 'floorCoefficient', tableGameCoefficient: 'tableCoefficient',
  travelTimeRegularizationMinutes: 'travelRegularizationMinutes', incomeElasticity: 'incomeElasticity', comparableScale: 'scale',
  outsideWeight: 'outsideWeight', exponentialLambda: 'exponentialLambda' })) {
  assert.deepEqual(protocol[field], [before[candidateField]], `Changed coefficient ${field}.`);
  assert.equal(after[candidateField], before[candidateField], `Executed coefficient differs: ${field}.`);
}
assert.equal(result.referenceIncome, previous.referenceIncome);
const keys = [...protocol.training, ...protocol.selection].sort();
assert.deepEqual(before.observations.map(row => row.caseKey).sort(), keys);
assert.deepEqual(after.observations.map(row => row.caseKey).sort(), keys);
const oldByKey = new Map(before.observations.map(row => [row.caseKey, row]));
const marketByKey = new Map(Object.entries(protocol.developmentMarkets).flatMap(([market, values]) => values.map(key => [key, market])));
const rows = after.observations.map(row => {
  const old = oldByKey.get(row.caseKey);
  assert.equal(row.observed, old.observed, `Changed observed revenue: ${row.caseKey}`);
  for (const value of [row.observed, row.predicted, old.predicted]) assert(Number.isFinite(value) && value > 0);
  return { key: row.caseKey, partition: protocol.training.includes(row.caseKey) ? 'training' : 'selection',
    jurisdiction: row.caseKey.split('-')[1], market: marketByKey.get(row.caseKey),
    observed: row.observed, previousPrediction: old.predicted, correctedPrediction: row.predicted,
    previousApe: 100 * Math.abs(old.predicted - row.observed) / row.observed,
    correctedApe: 100 * Math.abs(row.predicted - row.observed) / row.observed };
});
function metrics(values, predictionKey) {
  const total = values.reduce((sum, row) => sum + row.observed, 0);
  const errors = values.map(row => row[predictionKey] - row.observed);
  const apes = values.map((row, index) => 100 * Math.abs(errors[index]) / row.observed);
  return { properties: values.length, mape: apes.reduce((sum, value) => sum + value, 0) / values.length,
    wape: 100 * errors.reduce((sum, value) => sum + Math.abs(value), 0) / total,
    normalizedBias: 100 * errors.reduce((sum, value) => sum + value, 0) / total,
    maximumPropertyApe: Math.max(...apes), fractionWithin30Percent: apes.filter(value => value <= 30).length / values.length };
}
const partitions = ['training', 'selection'].map(partition => {
  const values = rows.filter(row => row.partition === partition);
  const oldMetrics = metrics(values, 'previousPrediction'), newMetrics = metrics(values, 'correctedPrediction');
  assert(Math.abs(newMetrics.mape - after[`${partition}Metrics`].meanAbsolutePercentageError) <= 1e-10);
  assert(Math.abs(oldMetrics.mape - before[`${partition}Metrics`].meanAbsolutePercentageError) <= 1e-10);
  if (partition === 'selection') {
    assert(Math.abs(newMetrics.wape - after.selectionWape) <= 1e-10);
    assert(Math.abs(oldMetrics.wape - before.selectionWape) <= 1e-10);
  }
  return { partition, previous: oldMetrics, corrected: newMetrics,
    groups: ['market', 'jurisdiction'].flatMap(kind => [...new Set(values.map(row => row[kind]))].sort().map(key => {
      const group = values.filter(row => row[kind] === key);
      return { kind, key, previous: metrics(group, 'previousPrediction'), corrected: metrics(group, 'correctedPrediction') };
    })) };
});
const output = { version: 'origin-universe-correction-audit-v1',
  scope: 'Fixed-coefficient comparison on the same inspected development observations. Only training expenditure refits. No fresh holdout or empirical Northeast Indiana accuracy claim.',
  previousResultHash: hash(oldResultPath), protocolHash: hash(protocolPath), resultHash: hash(resultPath),
  graphHash: result.graphHash, candidateCount: result.candidateCount, admittedCandidateCount: result.admittedCandidateCount,
  previousTrainingExpenditure: before.fittedDemandScale, correctedTrainingExpenditure: after.fittedDemandScale,
  finalHoldoutScored: false, releaseAccepted: false, partitions, rows, failures: after.admission?.failures ?? [] };
fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ partitions: partitions.map(({ partition, previous, corrected }) => ({ partition, previous, corrected })), failures: output.failures }));
