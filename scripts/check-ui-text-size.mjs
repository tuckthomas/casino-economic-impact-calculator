import { readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('SaveFW.Client');
const fix = process.argv.includes('--fix');
const allowedExtensions = new Set(['.razor', '.js', '.css', '.html']);
const ignoredSegments = [
  `${path.sep}bin${path.sep}`,
  `${path.sep}obj${path.sep}`,
  `${path.sep}wwwroot${path.sep}css${path.sep}`,
  `${path.sep}wwwroot${path.sep}js${path.sep}lib${path.sep}`,
  `${path.sep}wwwroot${path.sep}lib${path.sep}`
];
const forbiddenTextClass = /\btext-xs\b|\btext-\[(?:[0-9]|1[0-3])px\]/g;

async function collectFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (ignoredSegments.some(segment => fullPath.includes(segment))) continue;

    if (entry.isDirectory()) {
      files.push(...await collectFiles(fullPath));
    } else if (allowedExtensions.has(path.extname(entry.name))) {
      files.push(fullPath);
    }
  }

  return files;
}

const files = await collectFiles(root);
const violations = [];

for (const file of files) {
  const source = await readFile(file, 'utf8');
  const matches = [...source.matchAll(forbiddenTextClass)];
  if (!matches.length) continue;

  if (fix) {
    const updated = source.replace(forbiddenTextClass, 'text-sm');
    await writeFile(file, updated, 'utf8');
    continue;
  }

  for (const match of matches) {
    const line = source.slice(0, match.index).split(/\r?\n/).length;
    violations.push(`${path.relative(process.cwd(), file)}:${line}: ${match[0]} -> use text-sm or larger`);
  }
}

if (fix) {
  console.log('Replaced forbidden tiny-text utilities with text-sm.');
  process.exit(0);
}

if (violations.length) {
  console.error('Tiny text is not allowed in SaveNEIN UI source:');
  violations.forEach(violation => console.error(`  ${violation}`));
  process.exit(1);
}

console.log('UI text-size guard passed.');
