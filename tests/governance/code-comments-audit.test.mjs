import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..',
);

const script = path.join(repositoryRoot, 'scripts/governance/audit-code-comments.mjs');

test('audit-code-comments script runs and scans src', () => {
  const result = spawnSync(process.execPath, [script, '--no-summary'], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
  assert.equal(result.status, 0, result.stderr || result.stdout);
  const payload = JSON.parse(result.stdout);
  assert.ok(payload.scannedFiles > 1000);
  assert.ok(typeof payload.issueCount === 'number');
});

test('english-summary ignores Chinese summaries that start with product names', () => {
  const result = spawnSync(
    process.execPath,
    [script, '--english-summary', '--roots=tests/governance/fixtures'],
    { cwd: repositoryRoot, encoding: 'utf8' },
  );
  assert.equal(result.status, 0, result.stderr || result.stdout);
  const payload = JSON.parse(result.stdout);
  assert.equal(
    payload.issueCount,
    0,
    `expected no english-only false positives: ${JSON.stringify(payload.sample)}`,
  );
});

test('audit recognizes XML doc before attributes on public types', () => {
  const result = spawnSync(
    process.execPath,
    [script, '--roots=tests/governance/fixtures'],
    { cwd: repositoryRoot, encoding: 'utf8' },
  );
  assert.equal(result.status, 0, result.stderr || result.stdout);
  const payload = JSON.parse(result.stdout);
  assert.equal(
    payload.issueCount,
    0,
    `expected no public-type gaps in fixtures: ${JSON.stringify(payload.sample)}`,
  );
});
