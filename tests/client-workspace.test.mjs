import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [workspace, rootPackage, tokens, notices] = await Promise.all([
  readFile('pnpm-workspace.yaml', 'utf8'),
  readFile('package.json', 'utf8'),
  readFile('packages/design-tokens/src/tokens.css', 'utf8'),
  readFile('THIRD-PARTY-NOTICES', 'utf8')
]);

assert.match(workspace, /packages\/\*/);
assert.match(workspace, /ui\/\*/);
assert.match(workspace, /clients\/\*/);

const packageDefinition = JSON.parse(rootPackage);
assert.equal(packageDefinition.private, true);
assert.match(packageDefinition.packageManager, /^pnpm@10\./);
assert.equal(packageDefinition.engines.node, '>=24 <25');

assert.match(tokens, /--fullnet-color-accent:/);
assert.match(tokens, /--fullnet-shell-sidebar-width:/);
assert.match(tokens, /--fullnet-font-sans:/);

assert.match(notices, /Layui/i);
assert.match(notices, /MIT/i);
