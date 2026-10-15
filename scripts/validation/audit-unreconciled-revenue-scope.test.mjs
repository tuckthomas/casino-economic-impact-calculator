import test from 'node:test';
import assert from 'node:assert/strict';
import { errorBounds } from './audit-unreconciled-revenue-scope.mjs';

test('missing visitor revenue cannot repair an overprediction', () => {
  const bounds = errorBounds([{ key: 'over', observed: 100, residentPrediction: 160 },
    { key: 'under', observed: 100, residentPrediction: 40 }]);
  assert.equal(bounds.minimumPossibleTotalMape, 30);
  assert(Math.abs(bounds.minimumPossibleTotalWape - 30) < 1e-12);
  assert.equal(bounds.maximumUnavoidablePropertyApe, 60);
  assert.deepEqual(bounds.propertiesDefinitelyExceeding40Percent, ['over']);
});

test('underprediction is unidentified, not proof of a point error or a visitor forecast', () => {
  const bounds = errorBounds([{ key: 'under', observed: 100, residentPrediction: 10 }]);
  assert.equal(bounds.minimumPossibleTotalWape, 0);
  assert.deepEqual(bounds.propertiesDefinitelyExceeding40Percent, []);
  assert.equal(bounds.minimumPossibleSignedBias, -90);
});

test('invalid observations cannot manufacture a zero error bound', () => {
  for (const rows of [[], [{ key: 'invalid', observed: 0, residentPrediction: 10 }],
    [{ key: 'invalid', observed: 10, residentPrediction: NaN }], [{ key: 'invalid', observed: 10, residentPrediction: -1 }]])
    assert.throws(() => errorBounds(rows));
});
