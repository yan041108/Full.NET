import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { verifyApplicationCrudSchemaSourceUpgrade } from './support/application-crud-schema-source-upgrade.mjs';
import { CRUD_ARTIFACTS, applicationCrudArtifactPath } from './support/application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './support/application-crud-module.mjs';

function fixture(vue) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-schema-upgrade-'));
  const put = (path, body) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), body); };
  const target = { moduleName: 'Catalog', moduleProjectPath: 'src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj',
    moduleEntryPointPath: 'src/Demo.Modules.Catalog/CatalogModule.cs', compositionProjectPath: 'src/Demo.Composition/Demo.Composition.csproj',
    compositionCatalogPath: 'src/Demo.Composition/ApplicationModuleCatalog.cs', authorizationContributorPath: 'src/Demo.Modules.Catalog/CatalogAuthorizationContributor.cs',
    vueRouterPath: 'ui/admin/src/router/index.ts' };
  put('verification/CrudGeneration/module-target.json', JSON.stringify(target));
  if (vue) { target.clientRoute = { routePath: '/catalog/products', vueRouteName: 'm7-catalog-products', vueComponentPath: 'ui/admin/src/views/CatalogProductsView.vue' };
    put('verification/CrudGeneration/vue-target.json', JSON.stringify(target)); }
  put('verification/CrudGeneration/schema.json', readFileSync(new URL('./support/fixtures/application-crud-schema.json', import.meta.url)));
  const shared = [target.moduleEntryPointPath, target.compositionProjectPath, target.compositionCatalogPath, target.authorizationContributorPath,
    ...(vue ? [target.vueRouterPath] : [])];
  const module = MODULE_ARTIFACTS.map(path => 'src/Demo.Modules.Catalog/' + path);
  const adopted = vue ? ['ui/admin/src/views/CatalogProductsView.vue', 'ui/admin/src/views/products.generated.ts', 'ui/admin/src/views/products-page.generated.ts'] : [];
  const paths = [...CRUD_ARTIFACTS, ...module, ...adopted, ...shared];
  for (const path of [...paths, target.moduleProjectPath, 'src/Demo.Modules.Catalog/Product.manual.cs']) put(path, 'original ' + path);
  return { root, put, target, paths, module };
}
function runner(f, failure, calls) {
  let application = 0;
  return (_command, args) => {
    calls.push(args);
    if (args[0] === 'build') return { status: 0, stdout: '', stderr: '' };
    assert.equal(args[2], 'apply-host-integration', 'upgrade must use a complete operation');
    assert.equal(args.at(-1), join(f.root, 'verification/CrudGeneration/' + (f.target.clientRoute ? 'vue-target.json' : 'module-target.json')));
    const schema = JSON.parse(readFileSync(join(f.root, 'verification/CrudGeneration/schema.json')));
    assert.equal(schema.columns.at(-1).databaseName, 'Description');
    const first = application++ === 0;
    const changed = ['backend/ProductContracts.g.cs', 'contracts/openapi/products.generated.openapi.json',
      'templates/migrations/SqlServer/CreateProduct.sql.template', 'templates/migrations/MySql/CreateProduct.sql.template'].map(applicationCrudArtifactPath);
    const contract = 'src/Demo.Modules.Catalog/Generated/Product/ProductContracts.g.cs';
    if (first) for (const path of [...changed, contract]) f.put(path, failure === 'missing-description' ? 'wrong' : 'nullable Description');
    if (first && failure === 'manual-overwrite') f.put('src/Demo.Modules.Catalog/Product.manual.cs', 'lost');
    let lines = f.paths.map(path => `${first && (changed.includes(path) || path === contract) ? 'Update' : 'Unchanged'} ${path}`);
    if (first && failure === 'missing-source') lines.shift();
    if (first && failure === 'missing-module') lines = lines.filter(line => !line.endsWith(contract));
    if (first && failure === 'wrong-prefix') lines[0] = 'Update backend/ProductContracts.g.cs';
    if (first && failure === 'binding-update') lines[lines.length - 1] = 'Update ' + f.paths.at(-1);
    if (!first && failure === 'repeat-update') lines[0] = 'Update ' + f.paths[0];
    if (failure !== 'missing-receipt') lines.push('Applied Operation 0198f36e-f7a7-7c52-9cbb-774e67411213');
    lines.push('Applied HostIntegration ' + f.target.moduleProjectPath);
    return { status: failure === 'compiler-failure' ? 2 : 0, stdout: lines.join('\n'), stderr: '' };
  };
}
for (const vue of [false, true]) test('源码升级通过完整接入保留人工文件与现有Vue绑定：' + vue, () => {
  const f = fixture(vue), calls = [];
  try {
    const result = verifyApplicationCrudSchemaSourceUpgrade(f.root, { run: runner(f, null, calls), reportDirectory: join(f.root, 'evidence') });
    assert.deepEqual(result, { nullableColumn: 'Description', sourceUpdated: 4, moduleUpdated: 1,
      repeatUnchanged: true, manualPreserved: true, apiCompiled: true, databaseMigrationApplied: false });
    assert.equal(calls.length, 3); assert.equal(calls.filter(args => args.includes('apply-host-integration')).length, 2);
  } finally { rmSync(f.root, { recursive: true, force: true }); }
});
for (const failure of ['missing-source', 'missing-module', 'wrong-prefix', 'binding-update', 'repeat-update',
  'manual-overwrite', 'missing-description', 'missing-receipt', 'compiler-failure']) test('拒绝不完整或越界的升级结果：' + failure, () => {
  const f = fixture(true);
  try { assert.throws(() => verifyApplicationCrudSchemaSourceUpgrade(f.root, { run: runner(f, failure, []), reportDirectory: join(f.root, 'evidence') })); }
  finally { rmSync(f.root, { recursive: true, force: true }); }
});

for (const cpu of ['1', '2', '4']) test('升级中的CLI及最终API编译使用显式CPU预算：' + cpu, () => {
  const f = fixture(true), calls = [], execute = runner(f, null, calls);
  try {
    verifyApplicationCrudSchemaSourceUpgrade(f.root, { buildCpuCount: cpu, reportDirectory: join(f.root, 'evidence'),
      run: (command, args, options) => {
        assert.equal(options.env.DOTNET_PROCESSOR_COUNT, cpu);
        if (args[0] === 'build') assert.ok(args.includes('-maxcpucount:' + cpu));
        return execute(command, args, options);
      } });
    assert.equal(calls.length, 3);
  } finally { rmSync(f.root, { recursive: true, force: true }); }
});
test('无效升级编译预算在修改Schema前拒绝', () => {
  const f = fixture(true), before = readFileSync(join(f.root, 'verification/CrudGeneration/schema.json'));
  try {
    assert.throws(() => verifyApplicationCrudSchemaSourceUpgrade(f.root, { buildCpuCount: '0' }), /正整数/u);
    assert.deepEqual(readFileSync(join(f.root, 'verification/CrudGeneration/schema.json')), before);
  } finally { rmSync(f.root, { recursive: true, force: true }); }
});
