import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { validateOwnerKey, VALID_PRESETS, resolvePresetModules } from '../../scripts/templates/preset-modules.mjs';

const TEMPLATE_ROOT = resolve('templates/fullnet-app');

test('fullnet-app template config exists', () => {
  assert.ok(existsSync(join(TEMPLATE_ROOT, '.template.config/template.json')));
  assert.ok(existsSync(join(TEMPLATE_ROOT, 'framework-manifest.schema.json')));
  assert.ok(existsSync(join(TEMPLATE_ROOT, 'src/FullNetAppNameToken.Host.Api/FullNetAppNameToken.Host.Api.csproj')));
});

test('preset-modules validates owner-key negatives', () => {
  assert.throws(() => validateOwnerKey('fn'), /Reserved owner-key/);
  assert.throws(() => validateOwnerKey('sys'), /Reserved owner-key/);
  assert.throws(() => validateOwnerKey('1bad'), /Invalid owner-key/);
  assert.throws(() => validateOwnerKey('a'), /Invalid owner-key/);
  assert.throws(() => validateOwnerKey('acme-team'), /Invalid owner-key/);
  assert.throws(() => validateOwnerKey('abcdefghijklmn'), /Invalid owner-key/);
});

test('preset-modules resolves known presets', () => {
  for (const preset of VALID_PRESETS) {
    const modules = resolvePresetModules(preset);
    assert.ok(modules.length > 0, preset);
    assert.ok(modules.includes('Identity'), preset + ' must include Identity');
  }
  assert.throws(() => resolvePresetModules('unknown'), /Unknown preset/);
});

test('application-owned host starts the Full.NET API pipeline', () => {
  const program = readFileSync(join(TEMPLATE_ROOT, 'src/FullNetAppNameToken.Host.Api/Program.cs'), 'utf8');
  const project = readFileSync(join(TEMPLATE_ROOT, 'src/FullNetAppNameToken.Host.Api/FullNetAppNameToken.Host.Api.csproj'), 'utf8');

  assert.match(program, /WebApplication\.CreateBuilder\(args\)/);
  assert.match(program, /app\.MapFullNetModules\(\)/);
  assert.match(program, /app\.Run\(\)/);
  assert.match(project, /Full\.NET\.Composition\.csproj/);
  assert.doesNotMatch(project, /ProjectReference[^\n]*Full\.NET\.Host\.Api\.csproj/);
});

test('fullnet-app template exposes code-generation diagnose scripts', () => {
  const packageJson = JSON.parse(readFileSync(join(TEMPLATE_ROOT, 'package.json'), 'utf8'));
  assert.match(packageJson.scripts['diagnose:development'], /diagnose --workspace \. --profile development/u);
  assert.match(packageJson.scripts['diagnose:production'], /diagnose --workspace \. --profile production/u);
  assert.match(
    packageJson.scripts['diagnose:development'],
    /Full\.NET\.CodeGeneration\.Cli/u);
});
