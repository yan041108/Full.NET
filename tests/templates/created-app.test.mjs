import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { validateOwnerKey, VALID_PRESETS, resolvePresetModules } from '../../scripts/templates/preset-modules.mjs';
import { verifyCreatedApp } from '../../scripts/templates/verify-created-app.mjs';

const TEMPLATE_ROOT = resolve('templates/fullnet-app');

test('fullnet-app template config exists', () => {
  assert.ok(existsSync(join(TEMPLATE_ROOT, '.template.config/template.json')));
  assert.ok(existsSync(join(TEMPLATE_ROOT, 'framework-manifest.schema.json')));
  assert.ok(existsSync(join(TEMPLATE_ROOT, 'src/App.Host.Api/App.Host.Api.csproj')));
});

test('preset-modules validates owner-key negatives', () => {
  assert.throws(() => validateOwnerKey('fn'), /Reserved owner-key/);
  assert.throws(() => validateOwnerKey('sys'), /Reserved owner-key/);
  assert.throws(() => validateOwnerKey('1bad'), /Invalid owner-key/);
  assert.throws(() => validateOwnerKey('a'), /Invalid owner-key/);
});

test('preset-modules resolves known presets', () => {
  for (const preset of VALID_PRESETS) {
    const modules = resolvePresetModules(preset);
    assert.ok(modules.length > 0, preset);
    assert.ok(modules.includes('Identity'), preset + ' must include Identity');
  }
  assert.throws(() => resolvePresetModules('unknown'), /Unknown preset/);
});

test('verify-created-app accepts minimal fixture layout', () => {
  const fixtureRoot = resolve('tests/templates/fixtures/minimal-created-app');
  const result = verifyCreatedApp(fixtureRoot);
  assert.equal(result.ok, true, result.errors.join('; '));
});