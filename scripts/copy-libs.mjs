import { cpSync, mkdirSync, existsSync } from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const clientWwwroot = path.join(root, 'SaveNEIN.Client', 'wwwroot');

const copies = [
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'public-sans'),
    to: path.join(clientWwwroot, 'lib', 'public-sans')
  },
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'material-symbols-outlined'),
    to: path.join(clientWwwroot, 'lib', 'material-symbols-outlined')
  }
];

for (const { from, to } of copies) {
  if (existsSync(from)) {
    mkdirSync(path.dirname(to), { recursive: true });
    cpSync(from, to, { recursive: true, force: true, dereference: true });
  }
}
