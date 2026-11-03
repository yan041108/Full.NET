import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { CRUD_ARTIFACTS } from './support/application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './support/application-crud-module.mjs';
import { verifyApplicationCrudHostWiring } from './support/application-crud-host-wiring.mjs';

const directory = 'src/Demo.Modules.Catalog';
const project = directory + '/Demo.Modules.Catalog.csproj';
function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-host-wiring-'));
  const put = (path, value) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), value); };
  for (const path of [project, ...MODULE_ARTIFACTS.map(path => directory + '/' + path), ...CRUD_ARTIFACTS,
    directory + '/Product.manual.cs', directory + '/CatalogAuthorizationContributor.cs', '.fullnet/codegeneration-manifest.json',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'ui/admin/src/router/index.ts', 'verification/CrudGeneration/schema.json']) put(path, 'human ' + path);
  put(directory + '/CatalogModule.cs', 'services.AddFullNetGeneratedModuleFeatures();\nendpoints.MapFullNetGeneratedModuleFeatures();\nservices.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();');
  put('src/Demo.Composition/ApplicationModuleCatalog.cs', 'new CatalogModule()\nnew Demo.Modules.Probe.ProbeModule()');
  put('src/Demo.Composition/Demo.Composition.csproj', '<ProjectReference Include="../Demo.Modules.Catalog/Demo.Modules.Catalog.csproj" />');
  put('verification/CrudGeneration/module-target.json', JSON.stringify({ authorizationContributorPath: directory + '/CatalogAuthorizationContributor.cs' }));
  return { root, put };
}
function runner(fixture, failure, calls) {
  return (command, args, options) => {
    const stage = ['repeat', 'build', 'repeat-after-build'][calls.length]; calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 1, stderr: stage + ' failed' };
    if (failure === stage + '-mutates') fixture.put('src/Demo.Host.Api/Program.cs', 'host lost');
    return { status: 0, stdout: failure === 'missing-receipt' ? '' : `Applied Operation 0198f36e-f7a7-7c52-9cbb-774e67411213\nApplied HostIntegration ${project}\n`, stderr: '' };
  };
}
test('host wiring verifies preexisting complete integration, builds the API, and preserves all sources', () => {
  const fixture = workspace(); const calls = [];
  try {
    assert.deepEqual(verifyApplicationCrudHostWiring(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, null, calls) }),
      { entryIntegrated: true, compositionIntegrated: true, apiCompiled: true, repeatUnchanged: true });
    assert.equal(calls.length, 3);
    assert.ok(calls[0].args.includes('apply-host-integration'));
    assert.equal(calls[1].args[0], 'build');
    assert.ok(calls[2].args.includes('apply-host-integration'));
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});
for (const failure of ['repeat', 'build', 'repeat-after-build', 'repeat-mutates', 'build-mutates', 'repeat-after-build-mutates', 'missing-receipt']) {
  test(`host verification refuses ${failure}`, () => {
    const fixture = workspace(); const calls = [];
    try { assert.throws(() => verifyApplicationCrudHostWiring(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, failure, calls) })); }
    finally { rmSync(fixture.root, { recursive: true, force: true }); }
  });
}
test('missing generated entry is rejected before running commands', () => {
  const fixture = workspace(); const calls = [];
  try {
    fixture.put(directory + '/CatalogModule.cs', 'human entry');
    assert.throws(() => verifyApplicationCrudHostWiring(fixture.root, { run: runner(fixture, null, calls) }));
    assert.equal(calls.length, 0);
    assert.equal(readFileSync(join(fixture.root, directory, 'CatalogModule.cs'), 'utf8'), 'human entry');
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});
