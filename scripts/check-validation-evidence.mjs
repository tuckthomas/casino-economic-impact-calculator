import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');

export function verifyValidationEvidence(directory) {
    const documents = [];
    for (const folder of [directory, path.join(directory, 'bundles')]) {
        for (const name of readdirSync(folder).filter(name => name.endsWith('.json') || name.endsWith('.csv'))) {
            const file = path.join(folder, name);
            const bytes = readFileSync(file);
            documents.push({ file, folder, hash: digest(bytes), value: name.endsWith('.json') ? JSON.parse(bytes.toString('utf8')) : {} });
        }
    }
    const protocolHashes = new Set(documents.filter(document => document.folder === directory).map(document => document.hash));
    const bundleHashes = new Set(documents.filter(document => document.folder === path.join(directory, 'bundles')).map(document => document.hash));
    let references = 0;
    for (const document of documents) {
        for (const key of ['sourceBundleSha256', 'protocolHash', 'sourceBundleHash', 'resultHash', 'sourceCsvSha256', 'sourceGeocodeSha256']) {
            const reference = document.value[key];
            if (reference === undefined) continue;
            const hashes = key === 'protocolHash' ? protocolHashes : bundleHashes;
            if (typeof reference !== 'string' || !/^[a-f0-9]{64}$/.test(reference) || !hashes.has(reference)) {
                throw new Error(`${path.basename(document.file)}: ${key} does not match any retained evidence bytes.`);
            }
            references++;
        }
    }
    return { files: documents.length, references };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const result = verifyValidationEvidence(path.resolve('docs/validation'));
    console.log(`Validation byte-integrity guard passed: ${result.files} retained artifacts, ${result.references} checksum references.`);
}
