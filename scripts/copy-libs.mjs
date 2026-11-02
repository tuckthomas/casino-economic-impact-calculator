import { cpSync, mkdirSync, existsSync, readdirSync } from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const clientWwwroot = path.join(root, 'SaveNEIN.Client', 'wwwroot');

const copies = [
  { from: path.join(root, 'node_modules', 'web-haptics', 'LICENSE'),
    to: path.join(clientWwwroot, 'lib', 'web-haptics', 'LICENSE') },
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'public-sans'),
    to: path.join(clientWwwroot, 'lib', 'public-sans')
  },
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'material-symbols-outlined'),
    to: path.join(clientWwwroot, 'lib', 'material-symbols-outlined')
  }
];

// Copy the ESM entry and its generated chunks together; no runtime CDN dependency.
const hapticsDist = path.join(root, 'node_modules', 'web-haptics', 'dist');
for (const file of readdirSync(hapticsDist).filter(file => file.endsWith('.mjs'))) {
  copies.push({ from: path.join(hapticsDist, file), to: path.join(clientWwwroot, 'lib', 'web-haptics', file) });
}

for (const { from, to } of copies) {
  if (existsSync(from)) {
    mkdirSync(path.dirname(to), { recursive: true });
    cpSync(from, to, { recursive: true, force: true, dereference: true });
  }
}
