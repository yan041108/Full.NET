import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

const host = 'src/Demo.Host.Migrator';
const providers = ['SqlServer', 'MySql'];
const anchor = 'builder.Services.AddApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);';
const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');

// 仅验收应用显式采纳这一对草稿；不是框架脚本自动发现或业务 SQL 审计器。
export function prepareApplicationBusinessMigrations(appRoot) {
  const schema = JSON.parse(readFileSync(join(appRoot, 'verification/CrudGeneration/schema.json'), 'utf8'));
  for (const [key, value] of Object.entries({ ownerKey: 'acme', moduleKey: 'catalog', entityKey: 'product', databaseTableName: 'acme_catalog_product' })) {
    assert.equal(schema[key], value, 'unexpected application migration ownership: ' + key);
  }
  const outputs = {};
  const sources = {};
  for (const provider of providers) {
    const path = `templates/migrations/${provider}/CreateProduct.sql.template`;
    const bytes = readFileSync(join(appRoot, path));
    assert.ok(bytes.toString('utf8').trim(), 'empty migration draft: ' + path);
    sources[path] = hash(bytes);
    outputs[`${host}/Migrations/${provider}/001_CreateProduct.sql`] = bytes;
  }
  const runner = readFileSync(new URL('./fixtures/application-migration-runner.cs.fixture', import.meta.url));
  outputs[`${host}/ApplicationMigrationRunner.cs`] = runner;
  sources['application-migration-runner.cs.fixture'] = hash(runner);
  const projectPath = `${host}/Demo.Host.Migrator.csproj`;
  const programPath = `${host}/Program.cs`;
  const statePath = join(appRoot, `${host}/application-migrations.acceptance.json`);
  const targetPaths = [...Object.keys(outputs), projectPath, programPath];
  if (existsSync(statePath)) {
    const state = JSON.parse(readFileSync(statePath, 'utf8'));
    assert.deepEqual(state.sources, sources, 'migration draft or runner changed after adoption');
    assert.deepEqual(Object.keys(state.targets).sort(), targetPaths.sort(), 'unexpected adoption target set');
    for (const path of targetPaths) {
      assert.equal(hash(readFileSync(join(appRoot, path))), state.targets[path], 'protected adopted migration drift: ' + path);
      if (outputs[path]) assert.equal(state.targets[path], hash(outputs[path]), 'unexpected adopted migration content');
    }
    return state;
  }
  for (const path of Object.keys(outputs)) assert.equal(existsSync(join(appRoot, path)), false, 'adoption requires unused path: ' + path);
  const program = readFileSync(join(appRoot, programPath), 'utf8');
  const project = readFileSync(join(appRoot, projectPath), 'utf8');
  assert.equal(program.split(anchor).length, 2, 'ambiguous application registration anchor');
  assert.equal(program.includes('ApplicationMigrationRunner'), false, 'existing migration registration');
  assert.equal(project.split('</Project>').length, 2, 'ambiguous Migrator project');
  assert.equal(project.includes('acme.catalog.Migrations.'), false, 'existing application migration resources');
  const registration = `
// 应用迁移先完成框架迁移，再执行显式登记的业务 SQL；失败时工作流不得播种。
builder.Services.AddSingleton<Full.NET.Migrations.DbUp.DbUpMigrationRunner>();
builder.Services.AddSingleton<Full.NET.Migrations.DbUp.IDatabaseMigrationRunner>(services =>
    new Demo.Host.Migrator.ApplicationMigrationRunner(
        services.GetRequiredService<Full.NET.Migrations.DbUp.DbUpMigrationRunner>(),
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Full.NET.Data.Abstractions.DatabaseOptions>>(),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadScript("acme.catalog.Migrations.SqlServer.001_CreateProduct.sql"),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadScript("acme.catalog.Migrations.MySql.001_CreateProduct.sql"),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadOptionalScript("acme.catalog.Migrations.SqlServer.002_AddProductDescription.sql"),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadOptionalScript("acme.catalog.Migrations.MySql.002_AddProductDescription.sql")));
`;
  outputs[programPath] = Buffer.from('using Microsoft.Extensions.DependencyInjection;\n' + program.replace(anchor, anchor + registration));
  const resources = providers.map((provider) => `    <EmbeddedResource Include="Migrations/${provider}/001_CreateProduct.sql" LogicalName="acme.catalog.Migrations.${provider}.001_CreateProduct.sql" />`).join('\n');
  outputs[projectPath] = Buffer.from(project.replace('</Project>', `  <ItemGroup>\n${resources}\n  </ItemGroup>\n</Project>`));
  const state = { scriptsPerProvider: 1, sources, targets: Object.fromEntries(Object.entries(outputs).map(([path, bytes]) => [path, hash(bytes)])) };
  // 所有碰撞、草稿与宿主结构检查均在第一笔写入之前完成。
  for (const [path, bytes] of Object.entries(outputs)) {
    mkdirSync(dirname(join(appRoot, path)), { recursive: true });
    writeFileSync(join(appRoot, path), bytes);
  }
  writeFileSync(statePath, JSON.stringify(state, null, 2) + '\n');
  return state;
}

// 应用显式采纳增量脚本；已部署的 001 与人工代码按原摘要保护，新的建表草案不能回写旧迁移。
export function prepareApplicationBusinessSchemaUpgrade(appRoot) {
  const schema = JSON.parse(readFileSync(join(appRoot, 'verification/CrudGeneration/schema.json'), 'utf8'));
  const original = JSON.parse(readFileSync(new URL('./fixtures/application-crud-schema.json', import.meta.url), 'utf8'));
  assert.deepEqual(schema, { ...original, columns: [...original.columns,
    { databaseName: 'Description', clrPropertyName: 'Description', jsonPropertyName: 'description',
      scalarType: 'String', isNullable: true, maxLength: 500 }] }, 'unexpected schema upgrade');
  const initialState = JSON.parse(readFileSync(join(appRoot, `${host}/application-migrations.acceptance.json`), 'utf8'));
  assert.equal(initialState.scriptsPerProvider, 1, 'expected adopted 001 migrations');
  const projectPath = `${host}/Demo.Host.Migrator.csproj`;
  const statePath = join(appRoot, `${host}/application-schema-upgrade.acceptance.json`);
  for (const [path, digest] of Object.entries(initialState.targets)) {
    if (path === projectPath && existsSync(statePath)) continue;
    assert.equal(hash(readFileSync(join(appRoot, path))), digest, 'adopted migration drift: ' + path);
  }
  const outputs = {};
  const sources = {};
  for (const provider of providers) {
    const source = new URL(`./fixtures/application-upgrade-${provider}.sql.fixture`, import.meta.url);
    const bytes = readFileSync(source);
    sources[provider] = hash(bytes);
    outputs[`${host}/Migrations/${provider}/002_AddProductDescription.sql`] = bytes;
  }
  if (existsSync(statePath)) {
    const state = JSON.parse(readFileSync(statePath, 'utf8'));
    assert.deepEqual(state.sources, sources, 'upgrade script changed after adoption');
    for (const [path, digest] of Object.entries(state.targets)) {
      assert.equal(hash(readFileSync(join(appRoot, path))), digest, 'adopted upgrade drift: ' + path);
    }
    return state;
  }
  const project = readFileSync(join(appRoot, projectPath), 'utf8');
  assert.equal(project.includes('002_AddProductDescription.sql'), false,
    'upgrade registration already exists without acceptance state');
  const resources = providers.map(provider =>
    `    <EmbeddedResource Include="Migrations/${provider}/002_AddProductDescription.sql" LogicalName="acme.catalog.Migrations.${provider}.002_AddProductDescription.sql" />`).join('\n');
  outputs[projectPath] = Buffer.from(project.replace('</Project>', `  <ItemGroup>\n${resources}\n  </ItemGroup>\n</Project>`));
  for (const path of Object.keys(outputs).filter(path => path !== projectPath)) {
    assert.equal(existsSync(join(appRoot, path)), false, 'upgrade requires unused path: ' + path);
  }
  assert.equal(project.split('</Project>').length, 2, 'ambiguous Migrator project');
  const state = { scriptsPerProvider: 2, sources,
    targets: Object.fromEntries(Object.entries(outputs).map(([path, bytes]) => [path, hash(bytes)])) };
  for (const [path, bytes] of Object.entries(outputs)) {
    mkdirSync(dirname(join(appRoot, path)), { recursive: true });
    writeFileSync(join(appRoot, path), bytes);
  }
  writeFileSync(statePath, JSON.stringify(state, null, 2) + '\n');
  return state;
}

export function verifyApplicationMigrationResult(stdout, firstExecution, expectedApplicationScripts = firstExecution ? 1 : 0) {
  const prefix = 'FULLNET_APPLICATION_MIGRATIONS ';
  const markers = stdout.split(/\r?\n/u).filter((line) => line.startsWith(prefix));
  assert.equal(markers.length, 1, 'expected exactly one application migration result');
  const result = JSON.parse(markers[0].slice(prefix.length));
  assert.ok(Number.isSafeInteger(result.frameworkScripts) && result.frameworkScripts >= 0, 'invalid framework migration count');
  assert.equal(result.applicationScripts, expectedApplicationScripts, 'unexpected application migration count');
  if (firstExecution) assert.ok(result.frameworkScripts > 0, 'fresh database did not execute framework migrations');
  else assert.equal(result.frameworkScripts, 0, 'repeat unexpectedly executed framework migrations');
  return result;
}
