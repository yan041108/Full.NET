import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { CRUD_ARTIFACTS } from './support/application-crud-generation.mjs';
import { MODULE_ARTIFACTS, verifyApplicationCrudModule } from './support/application-crud-module.mjs';

const directory = 'src/Demo.Modules.Catalog';
const project = directory + '/Demo.Modules.Catalog.csproj';
const receipt = `Applied Operation 0198f36e-f7a7-7c52-9cbb-774e67411213\nApplied HostIntegration ${project}\n`;
function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-module-tooling-'));
  const put = (path, content) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), content); };
  for (const path of [...CRUD_ARTIFACTS, '.fullnet/codegeneration-manifest.json', 'backend/Product.manual.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'ui/admin/src/router/index.ts', 'verification/CrudGeneration/schema.json']) put(path, 'human ' + path);
  put('src/Demo.Composition/Demo.Composition.csproj', '<Project><ProjectReference Include="Probe.csproj" /></Project>');
  put('src/Demo.Composition/ApplicationModuleCatalog.cs', 'new Demo.Modules.Probe.ProbeModule()');
  return { root, put };
}

// 注入执行器只证明编排和验收门禁；实际整链编译、写盘与恢复由 .NET 聚焦测试验证。
function runner({ root, put }, failure, calls) {
  return (command, args, options) => {
    const stage = calls.length >= 9 ? ['manual-repeat', 'manual-build'][calls.length - 9]
      : calls.length >= 3 ? 'conflict' : ['apply', 'build', 'repeat'][calls.length];
    calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 1, stdout: '', stderr: stage + ' failed' };
    if (stage === 'build' || stage === 'manual-build') return { status: 0, stdout: '', stderr: '' };
    if (stage === 'apply') {
      for (const path of MODULE_ARTIFACTS) put(directory + '/' + path, path + '\n');
      const entry = directory + '/CatalogModule.cs';
      put(entry, readFileSync(join(root, entry), 'utf8') + '\nservices.AddFullNetGeneratedModuleFeatures();\nendpoints.MapFullNetGeneratedModuleFeatures();');
      put('src/Demo.Composition/Demo.Composition.csproj', '<Project><ProjectReference Include="Probe.csproj" /><ProjectReference Include="../Demo.Modules.Catalog/Demo.Modules.Catalog.csproj" /></Project>');
      put('src/Demo.Composition/ApplicationModuleCatalog.cs', 'new Demo.Modules.Probe.ProbeModule()\nnew CatalogModule()');
      const artifacts = MODULE_ARTIFACTS.map((path, index) => ({ relativePath: directory + '/' + path,
        sha256: createHash('sha256').update(readFileSync(join(root, directory, path))).digest('hex'),
        ...(index === 0 ? {} : { owner: 'acme/catalog/product' }) }));
      if (failure === 'unowned') artifacts.pop();
      if (failure === 'wrong-hash') artifacts[1].sha256 = 'a'.repeat(64);
      put('.fullnet/codegeneration-manifest.json', JSON.stringify({ schemaVersion: 1, artifacts }));
      if (failure === 'project-mutates') put(project, 'project lost');
      if (failure === 'host-mutates') put('src/Demo.Host.Api/Program.cs', 'host lost');
    }
    if (stage === 'repeat' && failure === 'repeat-mutates') put(directory + '/CatalogModule.cs', 'entry lost');
    if (stage === 'manual-repeat' && failure === 'manual-repeat-mutates') put(directory + '/Product.manual.cs', 'business code lost');
    if (stage === 'conflict') {
      const edited = MODULE_ARTIFACTS.find((path) => readFileSync(join(root, directory, path), 'utf8').includes('人工修改'));
      if (failure === 'conflict-mutates') put(directory + '/Product.manual.cs', 'manual code lost');
      return { status: failure === 'conflict-accepted' ? 0 : 2, stdout: '',
        stderr: '工作区冲突：路径：' + (failure === 'wrong-path' ? 'other.cs' : directory + '/' + edited) };
    }
    return { status: 0, stdout: failure === 'missing-receipt' ? `Applied HostIntegration ${project}\n` : receipt, stderr: '' };
  };
}

test('module acceptance uses a complete Host operation and checks root ownership and preserved manual files', () => {
  const fixture = workspace(); const calls = [];
  try {
    const result = verifyApplicationCrudModule(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, null, calls), removeTestSqlComment: true });
    assert.equal(result.moduleCompiled, true);
    assert.equal(result.manualExtensionPreserved, true);
    assert.equal(result.conflictArtifacts.length, MODULE_ARTIFACTS.length);
    assert.ok(calls.filter(call => !['build', 'manual-build'].includes(call.stage)).every(call => call.args.includes('apply-host-integration')));
    assert.ok(calls.every(call => call.options.cwd === fixture.root && call.options.windowsHide === true));
    assert.equal(readFileSync(join(fixture.root, directory, 'Generated/Product/ProductSql.g.cs'), 'utf8'), 'Generated/Product/ProductSql.g.cs\n');
    assert.match(readFileSync(join(fixture.root, directory, 'Product.manual.cs'), 'utf8'), /人工模块文件/u);
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});

for (const failure of ['apply', 'build', 'repeat', 'unowned', 'wrong-hash', 'project-mutates', 'host-mutates',
  'repeat-mutates', 'manual-repeat-mutates', 'manual-build', 'missing-receipt', 'conflict-accepted', 'conflict-mutates', 'wrong-path']) {
  test(`complete module acceptance rejects ${failure}`, () => {
    const fixture = workspace(); const calls = [];
    try { assert.throws(() => verifyApplicationCrudModule(fixture.root, { reportDirectory: join(fixture.root, 'evidence'), run: runner(fixture, failure, calls) })); }
    finally { rmSync(fixture.root, { recursive: true, force: true }); }
  });
}

test('module acceptance refuses an occupied module directory before running a command', () => {
  const fixture = workspace(); const calls = [];
  try {
    mkdirSync(join(fixture.root, directory));
    assert.throws(() => verifyApplicationCrudModule(fixture.root, { run: runner(fixture, null, calls) }));
    assert.equal(calls.length, 0);
  } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});
