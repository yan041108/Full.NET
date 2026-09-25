import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { createApp } from '../../scripts/templates/create-app.mjs';

test('create-app rejects invalid owner key before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot: parent, output, name: 'Demo', ownerKey: 'acme-team' }), /Invalid owner-key/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app preserves an existing output directory', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  const marker = join(parent, 'keep.txt');
  try {
    writeFileSync(marker, 'user content');
    assert.throws(() => createApp({ packageRoot: parent, output: parent, name: 'Demo', ownerKey: 'acme' }), /must not exist/);
    assert.equal(readFileSync(marker, 'utf8'), 'user content');
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app preserves even an existing empty output directory', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const output = join(parent, 'app');
    mkdirSync(output);
    assert.throws(() => createApp({ packageRoot: parent, output, name: 'Demo', ownerKey: 'acme' }), /must not exist/);
    assert.deepEqual(readdirSync(output), []);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects a modified managed framework file before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const manifest = JSON.stringify({ managedFiles: { 'global.json': '0'.repeat(64) } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), '{}');
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /digest mismatch/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects unlisted framework files before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ managedFiles: { 'global.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), content);
    writeFileSync(join(bundleRoot, 'Directory.Build.targets'), '<Project />');
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /Unexpected framework file/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app removes staging after template installation fails', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ managedFiles: { 'global.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), content);
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /dotnet new fullnet-app failed/);
    assert.equal(existsSync(output), false);
    assert.deepEqual(readdirSync(parent).filter((entry) => entry.startsWith('.fullnet-create-')), []);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});
