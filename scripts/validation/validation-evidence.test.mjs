import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, writeFileSync, unlinkSync, rmdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { verifyValidationEvidence } from '../check-validation-evidence.mjs';

function fixture(run) {
    const directory = mkdtempSync(path.join(tmpdir(), 'savenein-evidence-'));
    const bundles = path.join(directory, 'bundles');
    mkdirSync(bundles);
    const source = Buffer.from('{\r\n  "observed": 123\r\n}\r\n');
    const sourceFile = path.join(bundles, 'capture.json');
    const protocolFile = path.join(directory, 'protocol.json');
    writeFileSync(sourceFile, source);
    writeFileSync(protocolFile, JSON.stringify({ sourceBundleSha256: createHash('sha256').update(source).digest('hex') }));
    try { run({ directory, sourceFile, protocolFile }); }
    finally {
        unlinkSync(sourceFile);
        unlinkSync(protocolFile);
        rmdirSync(bundles);
        rmdirSync(directory);
    }
}

test('resolves recorded checksums from exact captured bytes', () => fixture(({ directory }) => {
    assert.deepEqual(verifyValidationEvidence(directory), { files: 2, references: 1 });
}));

test('rejects line-ending conversion even when JSON data is identical', () => fixture(({ directory, sourceFile }) => {
    writeFileSync(sourceFile, '{\n  "observed": 123\n}\n');
    assert.throws(() => verifyValidationEvidence(directory), /sourceBundleSha256 does not match/);
}));

test('rejects unresolved frozen protocol references', () => fixture(({ directory, protocolFile }) => {
    writeFileSync(protocolFile, JSON.stringify({ protocolHash: 'a'.repeat(64) }));
    assert.throws(() => verifyValidationEvidence(directory), /protocolHash does not match/);
}));

test('checks separately frozen demographic inputs and replay results', () => fixture(({ directory, sourceFile, protocolFile }) => {
    for (const key of ['demographicInputBundleSha256', 'replayResultHash']) {
        const bytes = Buffer.from('{\r\n  "observed": 123\r\n}\r\n');
        writeFileSync(sourceFile, bytes);
        writeFileSync(protocolFile, JSON.stringify({ [key]: createHash('sha256').update(bytes).digest('hex') }));
        assert.deepEqual(verifyValidationEvidence(directory), { files: 2, references: 1 });
        writeFileSync(sourceFile, '{}');
        assert.throws(() => verifyValidationEvidence(directory), new RegExp(`${key} does not match`));
    }
}));

test('verifies raw source CSV references without parsing CSV as JSON', () => fixture(({ directory, protocolFile }) => {
    const csvFile = path.join(directory, 'bundles', 'source.csv');
    const bytes = Buffer.from('license,income\r\n123,100.30\r\n');
    writeFileSync(csvFile, bytes);
    writeFileSync(protocolFile, JSON.stringify({ sourceCsvSha256: createHash('sha256').update(bytes).digest('hex') }));
    try {
        assert.deepEqual(verifyValidationEvidence(directory), { files: 3, references: 1 });
        writeFileSync(csvFile, bytes.toString().replaceAll('\r\n', '\n'));
        assert.throws(() => verifyValidationEvidence(directory), /sourceCsvSha256 does not match/);
    } finally { unlinkSync(csvFile); }
}));
