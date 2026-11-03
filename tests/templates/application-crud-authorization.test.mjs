import assert from 'node:assert/strict';
import { dirname, join } from 'node:path';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import test from 'node:test';
import { CRUD_ARTIFACTS } from './support/application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './support/application-crud-module.mjs';
import { verifyApplicationCrudAuthorization } from './support/application-crud-authorization.mjs';

const directory = 'src/Demo.Modules.Catalog';
const contributor = directory + '/CatalogAuthorizationContributor.cs';
const entry = directory + '/CatalogModule.cs';
const project = directory + '/Demo.Modules.Catalog.csproj';
const target = 'verification/CrudGeneration/authorization-target.json';
const fragment = readFileSync(new URL('./support/fixtures/application-crud-authorization.fragment.cs.fixture', import.meta.url), 'utf8');
const manual = readFileSync(new URL('./support/fixtures/application-catalog-authorization-contributor.cs.fixture', import.meta.url), 'utf8');
function workspace(failure) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-authorization-'));
  const put = (path, content) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), content); };
  for (const path of [...CRUD_ARTIFACTS, ...MODULE_ARTIFACTS.map(path => directory + '/' + path), project, directory + '/Product.manual.cs',
    'src/Demo.Composition/Demo.Composition.csproj', 'src/Demo.Composition/ApplicationModuleCatalog.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'src/Demo.Host.Api/appsettings.json',
    'ui/admin/src/router/index.ts', '.fullnet/codegeneration-manifest.json', 'backend/Product.manual.cs',
    'appsettings.json', 'fullnet-app.json', 'framework-manifest.json', 'verification/CrudGeneration/schema.json']) put(path, 'human ' + path);
  put(entry, 'services.AddOptions();\nservices.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();\nservices.AddFullNetGeneratedModuleFeatures();');
  let generated = failure === 'host-scope' ? fragment.replaceAll('AuthorizationScope.Tenant', 'AuthorizationScope.Host') : fragment;
  if (failure === 'partial-block') generated = generated.replace('// </fullnet-generated catalog.product actions>', '');
  let content = manual + '\n' + generated;
  if (failure === 'manual-lost') content = content.replace('catalog.manual.read', 'catalog.lost.read');
  if (failure === 'missing-fragment') content = manual;
  put(contributor, content);
  put('verification/CrudGeneration/module-target.json', JSON.stringify({ moduleName: 'Catalog', moduleProjectPath: project,
    moduleEntryPointPath: entry, vueRouterPath: 'ui/admin/src/router/index.ts', authorizationContributorPath: contributor }));
  return { root, put };
}

// 只验证验收脚本的检查与拒绝路径；真实贡献者编译和运行授权由对应 .NET/HTTP 场景负责。
function runner(fixture, failure, calls) {
  return (command, args, options) => {
    const stage = ['apply', 'drift', 'repeat'][calls.length]; calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 1, stderr: stage + ' failed' };
    if (failure === 'process-error') return { status: 0, error: new Error('process failed') };
    if (failure === stage + '-mutates') fixture.put(stage === 'apply' ? 'ui/admin/src/router/index.ts' : contributor, 'human content lost');
    if (stage === 'drift') return { status: failure === 'drift-accepted' ? 0 : 2, stderr: '工作区冲突：' + contributor, stdout: '' };
    return { status: 0, stdout: failure === 'missing-receipt' ? '' : `Applied Operation 0198f36e-f7a7-7c52-9cbb-774e67411213\nApplied HostIntegration ${project}\n`, stderr: '' };
  };
}

test('authorization verification preserves existing integration and rejects then restores only its own test comment', () => {
  const fixture = workspace(); const calls = [];
  try {
    assert.deepEqual(verifyApplicationCrudAuthorization(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, null, calls) }),
      { contributorIntegrated: true, repeatUnchanged: true });
    assert.equal(calls.length, 3);
    assert.ok(calls.every(call => call.args.includes('apply-host-integration')));
    const content = readFileSync(join(fixture.root, contributor), 'utf8');
    assert.match(content, /catalog\.manual\.read/u);
    assert.doesNotMatch(content, /人工授权注释/u);
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});
for (const failure of ['apply', 'repeat', 'process-error', 'missing-fragment', 'manual-lost', 'host-scope',
  'partial-block', 'apply-mutates', 'drift-mutates', 'repeat-mutates', 'drift-accepted', 'missing-receipt']) {
  test(`authorization verification refuses ${failure}`, () => {
    const fixture = workspace(failure); const calls = [];
    try { assert.throws(() => verifyApplicationCrudAuthorization(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, failure, calls) })); }
    finally { rmSync(fixture.root, { recursive: true, force: true }); }
  });
}
for (const failure of ['missing-contributor', 'occupied-target', 'client-route', 'entry-anchor', 'missing-registration']) {
  test(`authorization verification rejects ${failure} before commands`, () => {
    const fixture = workspace(); const calls = [];
    try {
      if (failure === 'missing-contributor') rmSync(join(fixture.root, contributor));
      if (failure === 'occupied-target') fixture.put(target, 'human target');
      if (failure === 'entry-anchor') fixture.put(entry, 'human entry');
      if (failure === 'missing-registration') fixture.put(entry, 'services.AddOptions();');
      if (failure === 'client-route') fixture.put('verification/CrudGeneration/module-target.json', JSON.stringify({ clientRoute: {} }));
      assert.throws(() => verifyApplicationCrudAuthorization(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, null, calls) }));
      assert.equal(calls.length, 0);
    } finally { rmSync(fixture.root, { recursive: true, force: true }); }
  });
}
