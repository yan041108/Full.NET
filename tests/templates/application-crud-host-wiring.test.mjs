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

for (const exitStatus of [-123, 2147516571]) {
  test('缺失SDK保留有符号退出码并恢复原配置：' + exitStatus, () => {
    const fixture = workspace(), calls = [], original = Buffer.from('{"sdk":{"version":"10.0.100"}}');
    fixture.put('global.json', original);
    const normal = runner(fixture, null, calls);
    const run = (command, args, options) => {
      if (JSON.parse(readFileSync(join(fixture.root, 'global.json'), 'utf8')).sdk.version !== '99.0.100') return normal(command, args, options);
      return args[0] === 'build' ? {status:exitStatus,stdout:'',stderr:''}
        : {status:2,stdout:'',stderr:'构建进程退出码：' + (exitStatus | 0)};
    };
    try {
      verifyApplicationCrudHostWiring(fixture.root, {run,verifyMissingSdk:true,reportDirectory:join(fixture.root,'evidence')});
      assert.deepEqual(readFileSync(join(fixture.root,'global.json')),original);
      const result=JSON.parse(readFileSync(join(fixture.root,'evidence/apply-host-integration-sdk-failure.json'),'utf8'));
      assert.equal(result.buildExitCode,exitStatus);assert.equal(result.diagnosticExitCode,exitStatus | 0);assert.equal(result.inputsUnchanged,true);
    } finally {rmSync(fixture.root,{recursive:true,force:true});}
  });
}

test('SDK诊断断言失败仍保存原始候选错误并恢复配置', () => {
  const fixture = workspace(), original = Buffer.from('{"sdk":{"version":"10.0.100"}}');
  fixture.put('global.json', original);
  const run = (_command, args) => args[0] === 'build' ? { status: -123, stdout: '', stderr: '' }
    : { status: 2, stdout: '', stderr: '整链接入编译失败。' };
  try {
    assert.throws(() => verifyApplicationCrudHostWiring(fixture.root, { run, verifyMissingSdk: true,
      reportDirectory: join(fixture.root, 'evidence') }), /signed SDK exit code/u);
    const result = JSON.parse(readFileSync(join(fixture.root, 'evidence/apply-host-integration-sdk-diagnostic.json'), 'utf8'));
    assert.equal(result.buildExitCode, -123); assert.equal(result.candidateExitCode, 2);
    assert.equal(result.stderr, '整链接入编译失败。');
    assert.deepEqual(readFileSync(join(fixture.root, 'global.json')), original);
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});
