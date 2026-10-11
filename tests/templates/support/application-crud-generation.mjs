import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const CRUD_ARTIFACTS = [
  'backend/ProductContracts.g.cs',
  'backend/ProductSql.g.cs',
  'backend/ProductAuthorizationContributor.fragment.cs',
  'backend/ProductEndpoint.g.cs',
  'backend/ProductFeature.g.cs',
  'backend/ProductRecord.g.cs',
  'clients/vue/products-page.generated.ts',
  'clients/vue/products.generated.ts',
  'clients/vue/productsView.vue',
  'contracts/openapi/products.generated.openapi.json',
  'reports/products.generation.json',
  'templates/migrations/MySql/CreateProduct.sql.template',
  'templates/migrations/SqlServer/CreateProduct.sql.template',
  'templates/tests/ProductMigrationIntegrationTests.cs.template',
];

// 只在隔离的独立应用内运行其自带 CLI，不编译生成业务，也不修改受管框架源码。
export function verifyApplicationCrudGeneration(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud'),
} = {}) {
  const manifestPath = '.fullnet/codegeneration-manifest.json';
  const manualPath = 'backend/Product.manual.cs';
  const schemaPath = join(appRoot, 'verification/CrudGeneration/schema.json');
  const ownedPaths = [...CRUD_ARTIFACTS, manifestPath, manualPath];
  for (const path of [...ownedPaths.map((path) => join(appRoot, path)), schemaPath]) {
    assert.equal(existsSync(path), false, 'CRUD acceptance requires an unused application path: ' + path);
  }
  mkdirSync(join(appRoot, 'verification/CrudGeneration'), { recursive: true });
  writeFileSync(schemaPath, readFileSync(new URL('./fixtures/application-crud-schema.json', import.meta.url)));
  mkdirSync(join(appRoot, 'backend'), { recursive: true });
  const manual = '// 人工业务文件，不能被再生成删除。\n';
  writeFileSync(join(appRoot, manualPath), manual);
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, expectedStatus = 0) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({
      args, status: result.status, signal: result.signal, error: result.error?.message,
      stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, expectedStatus, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  const cliRoot = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli');
  execute('build', ['build', join(cliRoot, 'Full.NET.CodeGeneration.Cli.csproj'), '-c', 'Release', '-v', 'quiet']);
  const args = ['exec', join(cliRoot, 'bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll'),
    '--schema', schemaPath, '--workspace', appRoot];
  const expectActions = (stdout, kind) => {
    const lines = stdout.split(/\r?\n/u).filter(Boolean).sort();
    assert.deepEqual(lines, CRUD_ARTIFACTS.map((path) => kind + ' ' + path).sort(), 'incomplete CRUD generation plan');
  };
  expectActions(execute('preview', args), 'Create');
  for (const path of [...CRUD_ARTIFACTS, manifestPath]) assert.equal(existsSync(join(appRoot, path)), false, 'preview wrote ' + path);
  assert.equal(readFileSync(join(appRoot, manualPath), 'utf8'), manual, 'preview changed manual content');
  expectActions(execute('apply', [...args, '--apply']), 'Create');
  const capture = () => new Map(ownedPaths.map((path) => [path, readFileSync(join(appRoot, path))]));
  const generated = capture();
  assert.equal(generated.get(manualPath).toString('utf8'), manual);
  expectActions(execute('repeat', [...args, '--apply']), 'Unchanged');
  assert.deepEqual(capture(), generated, 'repeat changed generated or manual content');
  const sqlPath = 'backend/ProductSql.g.cs';
  writeFileSync(join(appRoot, sqlPath), Buffer.concat([generated.get(sqlPath), Buffer.from('\n// 人工修改，必须拒绝覆盖。\n')]));
  const customized = capture();
  const conflict = execute('conflict', [...args, '--apply'], 2);
  assert.ok(conflict.split(/\r?\n/u).includes('Conflict ' + sqlPath), 'missing exact SQL conflict');
  assert.deepEqual(capture(), customized, 'conflict overwrote application content or manifest');
  // 冲突检查结束后仅撤销本轮注释，后续模块和 Vue 再生成使用原始受管产物。
  writeFileSync(join(appRoot, sqlPath), generated.get(sqlPath));
  assert.deepEqual(capture(), generated, 'test comment cleanup changed unrelated application content');
  const result = { artifacts: CRUD_ARTIFACTS.length, conflictRejected: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
