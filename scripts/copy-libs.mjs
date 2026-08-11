import { cpSync, mkdirSync, existsSync } from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const clientWwwroot = path.join(root, 'SaveNEIN.Client', 'wwwroot');

const copies = [
  {
    from: path.join(root, 'node_modules', '@turf', 'turf', 'turf.min.js'),
    to: path.join(clientWwwroot, 'js', 'lib', 'turf.min.js')
  },
  {
    from: path.join(root, 'node_modules', 'chart.js', 'dist', 'chart.umd.min.js'),
    to: path.join(clientWwwroot, 'js', 'lib', 'chart.js')
  },
  {
    from: path.join(root, 'node_modules', 'html2canvas', 'dist', 'html2canvas.min.js'),
    to: path.join(clientWwwroot, 'js', 'lib', 'html2canvas.min.js')
  },
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'public-sans'),
    to: path.join(clientWwwroot, 'lib', 'public-sans')
  },
  {
    from: path.join(root, 'node_modules', '@fontsource-variable', 'material-symbols-outlined'),
    to: path.join(clientWwwroot, 'lib', 'material-symbols-outlined')
  },
  {
    from: path.join(root, 'node_modules', 'terra-draw', 'dist', 'terra-draw.umd.js'),
    to: path.join(clientWwwroot, 'js', 'lib', 'terra-draw.umd.js')
  }
];

for (const { from, to } of copies) {
  if (existsSync(from)) {
    mkdirSync(path.dirname(to), { recursive: true });
    cpSync(from, to, { recursive: true, force: true, dereference: true });
  }
}
