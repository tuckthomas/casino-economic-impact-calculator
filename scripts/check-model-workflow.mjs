import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

const plan = path.resolve('docs/plans/in-progress/sector_weighted_casino_displacement_model_and_revenue_projections_model');
const readJson = file => JSON.parse(readFileSync(file, 'utf8'));
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const index = readJson(path.join(plan, 'workflow/requirements.json'));
const state = readJson(path.join(plan, 'workflow/state.json'));
// Markdown checkouts may use CRLF on Windows; the index hashes Git's LF text.
// Captured validation evidence uses its separate raw-byte integrity guard.
const canonical = readFileSync(path.join(plan, index.canonicalFile), 'utf8').replace(/\r\n/g, '\n');
const lines = canonical.split('\n');
assert.equal(index.canonicalHash, digest(canonical), 'Canonical file hash is stale');
const requirements = new Map();
for (const requirement of index.requirements) {
    assert(!requirements.has(requirement.id), `Duplicate requirement ${requirement.id}`);
    requirements.set(requirement.id, requirement);
    assert(['completed', 'pending'].includes(requirement.state), `Invalid requirement state: ${requirement.id}`);
    assert.equal(requirement.sourceFile, index.canonicalFile, `Unexpected source: ${requirement.id}`);
    assert(/^\d+-\d+$/.test(requirement.lines), `Invalid source range: ${requirement.id}`);
    const [start, end] = requirement.lines.split('-').map(Number);
    assert(start >= 1 && end >= start && end <= lines.length, `Invalid source range: ${requirement.id}`);
    assert.equal(lines.slice(start - 1, end).join('\n'), requirement.exactText, `Source text drift: ${requirement.id}`);
    assert.equal(digest(requirement.exactText), requirement.hash, `Text hash drift: ${requirement.id}`);
    const checkbox = requirement.exactText.match(/^\s*- \[([xX ])\]/);
    if (checkbox) assert.equal(requirement.state, checkbox[1].toLowerCase() === 'x' ? 'completed' : 'pending', `Checkbox state drift: ${requirement.id}`);
}
const completed = index.requirements.filter(requirement => requirement.state === 'completed').length;
assert.equal(state.completedRequirements, completed, 'Completed requirement counter is stale');
assert.equal(state.pendingRequirements, requirements.size - completed, 'Pending requirement counter is stale');
const assigned = new Set();
const sectionCounts = { completed: 0, 'in-progress': 0, pending: 0 };
for (const file of readdirSync(path.join(plan, 'workflow/tasks')).filter(file => file.endsWith('.json'))) {
    const task = readJson(path.join(plan, 'workflow/tasks', file));
    assert(task.requirements.length > 0, `Empty section: ${file}`);
    let count = 0;
    for (const id of task.requirements) {
        assert(requirements.has(id), `Unknown requirement ${id} in ${file}`);
        assert(!assigned.has(id), `Requirement assigned twice: ${id}`);
        assigned.add(id);
        if (requirements.get(id).state === 'completed') count++;
    }
    const expected = count === task.requirements.length ? 'completed' : count === 0 ? 'pending' : 'in-progress';
    assert.equal(task.state, expected, `Section state drift: ${file}`);
    sectionCounts[expected]++;
}
assert.equal(assigned.size, requirements.size, 'Some requirements have no section');
assert.equal(state.completedSections, sectionCounts.completed, 'Completed section counter is stale');
assert.equal(state.inProgressSections, sectionCounts['in-progress'], 'In-progress section counter is stale');
assert.equal(state.pendingSections, sectionCounts.pending, 'Pending section counter is stale');
console.log(`Model workflow guard passed: ${requirements.size} indexed entries (${completed} completed, ${requirements.size - completed} pending), ${assigned.size} unique section assignments.`);
