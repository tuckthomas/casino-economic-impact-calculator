import fs from 'node:fs';
import path from 'node:path';

const roots = ['SaveNEIN.Client'];
const ignored = [
  `${path.sep}bin${path.sep}`,
  `${path.sep}obj${path.sep}`,
  `${path.sep}node_modules${path.sep}`,
  `${path.sep}wwwroot${path.sep}js${path.sep}lib${path.sep}`,
  `${path.sep}wwwroot${path.sep}lib${path.sep}`
];
const allowedDirectPortalFile = path.normalize('SaveNEIN.Client/wwwroot/js/components/tooltip-portal.js');
const allowedCursorHelpFile = path.normalize('SaveNEIN.Client/Components/AppTooltip.razor');
const extensions = new Set(['.razor', '.html', '.cshtml', '.js', '.mjs', '.ts']);
const violations = [];

function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (ignored.some(fragment => full.includes(fragment))) continue;
    if (entry.isDirectory()) walk(full);
    else if (extensions.has(path.extname(full).toLowerCase())) inspect(full);
  }
}

function lineNumber(text, index) {
  return text.slice(0, index).split('\n').length;
}

function add(file, text, match, message) {
  violations.push(`${file}:${lineNumber(text, match.index)} ${message}`);
}

function inspect(file) {
  const normalized = path.normalize(file);
  const text = fs.readFileSync(file, 'utf8');

  if (/\.razor$|\.html$|\.cshtml$/i.test(file)) {
    const nativeInteractiveTitle = /<(?:button|a|input|select|textarea)\b[^>]*\btitle\s*=/gis;
    for (const match of text.matchAll(nativeInteractiveTitle)) {
      add(file, text, match, 'Interactive native title tooltip is prohibited; use AppTooltip.');
    }
  }

  if (/\.js$|\.mjs$|\.ts$/i.test(file) && normalized !== allowedDirectPortalFile) {
    for (const pattern of [/\bTooltipPortal\s*\./g, /\.title\s*=/g, /setAttribute\(\s*['"]title['"]/g]) {
      for (const match of text.matchAll(pattern)) {
        add(file, text, match, 'Ad hoc/native tooltip API is prohibited; use AppTooltip.');
      }
    }
    for (const pattern of [/economic-calculator-global-tooltip/g, /\bglobalTooltip\b/g, /\bshowTooltip\s*\(/g, /\bmoveTooltip\s*\(/g]) {
      for (const match of text.matchAll(pattern)) {
        add(file, text, match, 'Custom tooltip implementation is prohibited; use the canonical AppTooltip portal.');
      }
    }
  }

  if (normalized !== allowedCursorHelpFile) {
    for (const match of text.matchAll(/cursor-help/g)) {
      add(file, text, match, 'Tooltip trigger styling belongs in AppTooltip.');
    }
  }
}

for (const root of roots) {
  if (fs.existsSync(root)) walk(root);
}

const component = 'SaveNEIN.Client/Components/AppTooltip.razor';
if (!fs.existsSync(component)) violations.push(`${component}: missing canonical tooltip component`);

if (violations.length) {
  console.error('Tooltip standard violations found:\n' + violations.map(v => `- ${v}`).join('\n'));
  process.exit(1);
}

console.log('Tooltip guard passed. App-owned interactive tooltips use the canonical AppTooltip implementation.');
