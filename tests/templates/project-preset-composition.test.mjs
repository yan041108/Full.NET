import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import test from 'node:test';
import { projectCompositionSource } from '../../scripts/templates/project-preset-composition.mjs';
import { resolvePresetModules, VALID_PRESETS } from '../../scripts/templates/preset-modules.mjs';

const root = resolve('src/Composition/Full.NET.Composition');
const sources = {
  catalog: readFileSync(resolve(root, 'FullNetModuleCatalog.cs'), 'utf8'),
  selection: readFileSync(resolve(root, 'FullNetModuleSelection.cs'), 'utf8'),
  project: readFileSync(resolve(root, 'Full.NET.Composition.csproj'), 'utf8'),
};

test('minimal projection excludes unselected module implementation references', () => {
  const result = projectCompositionSource(sources, resolvePresetModules('minimal'));
  for (const module of ['Identity', 'Tenancy', 'Settings', 'Organization']) {
    assert.match(result.catalog, new RegExp(`new ${module}Module\\(\\)`));
    assert.match(result.project, new RegExp(`Full\\.NET\\.Modules\\.${module}\\\\`));
  }
  assert.doesNotMatch(result.catalog, /new PaymentsModule\(\)|AddAiProviderServices/);
  assert.doesNotMatch(result.project, /Full\.NET\.Modules\.Payments\\|Full\.NET\.AI\.Providers/);
  const officialNames = result.selection.split('OfficialModuleNames =')[1].split('];')[0];
  assert.doesNotMatch(officialNames, /"Payments"/);
});

test('enterprise projection retains selected optional modules', () => {
  const result = projectCompositionSource(sources, resolvePresetModules('enterprise'));
  assert.match(result.catalog, /new EnterpriseRequestModule\(\)/);
  assert.match(result.project, /Full\.NET\.Modules\.EnterpriseRequest\.csproj/);
  assert.doesNotMatch(result.catalog, /new PaymentsModule\(\)/);
});

test('packaged preset lists match the Composition source presets', () => {
  for (const preset of VALID_PRESETS) {
    const name = preset[0].toUpperCase() + preset.slice(1);
    const block = sources.selection.split(`${name}PresetModuleNames =`)[1]?.split('];')[0];
    assert.ok(block, `missing Composition preset: ${preset}`);
    const moduleNames = [...block.matchAll(/"([A-Za-z0-9]+)"/g)].map((match) => match[1]);
    assert.deepEqual(resolvePresetModules(preset), moduleNames, `preset drift: ${preset}`);
  }
});
