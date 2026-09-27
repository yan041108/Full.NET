import assert from 'node:assert/strict';
import { dirname, join } from 'node:path';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import test from 'node:test';
import { CRUD_ARTIFACTS } from './support/application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './support/application-crud-module.mjs';
import { verifyApplicationCrudAuthorization } from './support/application-crud-authorization.mjs';

const moduleRoot = 'src/Demo.Modules.Catalog';
const contributor = moduleRoot + '/CatalogAuthorizationContributor.cs';
const entry = moduleRoot + '/CatalogModule.cs';
const project = moduleRoot + '/Demo.Modules.Catalog.csproj';
const target = 'verification/CrudGeneration/authorization-target.json';
const fragment = readFileSync(new URL('./support/fixtures/application-crud-authorization.fragment.cs.fixture', import.meta.url), 'utf8');
function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-authorization-'));
  for (const path of [...CRUD_ARTIFACTS, ...MODULE_ARTIFACTS.map((path) => moduleRoot + '/' + path),
    moduleRoot + '/.fullnet/codegeneration-manifest.json', entry, project, moduleRoot + '/Product.manual.cs',
    'src/Demo.Composition/Demo.Composition.csproj', 'src/Demo.Composition/ApplicationModuleCatalog.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'src/Demo.Host.Api/appsettings.json',
    'ui/admin/src/router/index.ts', '.fullnet/codegeneration-manifest.json', 'backend/Product.manual.cs',
    'appsettings.json', 'fullnet-app.json', 'framework-manifest.json', 'verification/CrudGeneration/schema.json']) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), path === entry ? 'services.AddOptions();\nservices.AddFullNetGeneratedModuleFeatures();' : 'human ' + path);
  }
  writeFileSync(join(root, 'verification/CrudGeneration/module-target.json'), JSON.stringify({ moduleName: 'Catalog',
    moduleProjectPath: project, moduleEntryPointPath: entry, vueRouterPath: 'ui/admin/src/router/index.ts' }));
  return root;
}

// 模拟写盘只验证编排与保护门禁，不能证明实际候选编译或权威目录已物化。
function runner(root, failure, calls) {
  return (command, args, options) => {
    const stage = calls.length === 0 ? 'apply' : 'repeat';
    calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 2, stdout: '', stderr: stage + ' failed' };
    if (failure === 'process-error') return { status: 0, error: new Error('failed process'), stdout: '' };
    if (stage === 'apply') {
      let content = readFileSync(join(root, contributor), 'utf8') + '\n' + fragment;
      if (failure === 'lying') content = readFileSync(join(root, contributor), 'utf8');
      if (failure === 'manual-lost') content = content.replace('catalog.manual.read', 'catalog.lost.read');
      if (failure === 'host-scope') content = content.replaceAll('AuthorizationScope.Tenant', 'AuthorizationScope.Host');
      if (failure === 'partial-block') content = content.replace('// </fullnet-generated catalog.product actions>', '');
      writeFileSync(join(root, contributor), content);
      if (failure === 'apply-mutates') writeFileSync(join(root, 'ui/admin/src/router/index.ts'), 'router lost');
    } else if (failure === 'repeat-mutates') writeFileSync(join(root, contributor), 'manual comment lost');
    return { status: 0, stdout: failure === 'missing-marker' ? '' : `Applied HostIntegration ${project}\n`, stderr: '' };
  };
}

test('application CRUD authorization integrates and preserves manual edits on repeat', () => {
  const root = workspace();
  const calls = [];
  try {
    assert.deepEqual(verifyApplicationCrudAuthorization(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }),
      { contributorIntegrated: true, repeatUnchanged: true });
    assert.equal(calls.length, 2);
    assert.ok(calls.every(({ args, options }) => args[2] === 'apply-host-integration' && options.cwd === root && options.windowsHide === true));
    const targetData = JSON.parse(readFileSync(join(root, target), 'utf8'));
    assert.equal(targetData.authorizationContributorPath, contributor);
    assert.equal(Object.hasOwn(targetData, 'clientRoute'), false);
    assert.match(readFileSync(join(root, entry), 'utf8'), /AddSingleton<Full\.NET\.Modules\.Identity\.Contracts\.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>/);
    assert.match(readFileSync(join(root, contributor), 'utf8'), /人工授权注释，重复接入不得改写/);
    assert.match(readFileSync(join(root, contributor), 'utf8'), /catalog\.manual\.read/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

for (const failure of ['apply', 'repeat', 'process-error', 'lying', 'manual-lost', 'host-scope',
  'partial-block', 'apply-mutates', 'repeat-mutates', 'missing-marker']) {
  test(`application CRUD authorization rejects ${failure}`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudAuthorization(root, { reportDirectory: join(root, 'evidence'), run: runner(root, failure, calls) }));
      assert.ok(calls.length > 0);
    } finally { rmSync(root, { recursive: true, force: true }); }
  });
}

for (const failure of ['occupied-contributor', 'occupied-target', 'client-route', 'entry-anchor']) {
  test(`application CRUD authorization refuses ${failure} before writing or executing`, () => {
    const root = workspace();
    const calls = [];
    try {
      if (failure === 'occupied-contributor') writeFileSync(join(root, contributor), 'human contributor');
      if (failure === 'occupied-target') writeFileSync(join(root, target), 'human target');
      if (failure === 'entry-anchor') writeFileSync(join(root, entry), 'human entry');
      if (failure === 'client-route') {
        const original = join(root, 'verification/CrudGeneration/module-target.json');
        writeFileSync(original, JSON.stringify({ ...JSON.parse(readFileSync(original, 'utf8')), clientRoute: {} }));
      }
      const originalEntry = readFileSync(join(root, entry));
      assert.throws(() => verifyApplicationCrudAuthorization(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }));
      assert.equal(calls.length, 0);
      assert.deepEqual(readFileSync(join(root, entry)), originalEntry);
    } finally { rmSync(root, { recursive: true, force: true }); }
  });
}
