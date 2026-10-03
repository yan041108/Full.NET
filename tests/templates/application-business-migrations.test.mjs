import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { prepareApplicationBusinessMigrations, prepareApplicationBusinessSchemaUpgrade, verifyApplicationMigrationResult } from './support/application-business-migrations.mjs';

const host = 'src/Demo.Host.Migrator';
const anchor = 'builder.Services.AddApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);';
function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-business-migration-'));
  const put = (path, content) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), content); };
  put(`${host}/Program.cs`, `using Demo.Composition;\n${anchor}\nreturn 0;\n`);
  put(`${host}/Demo.Host.Migrator.csproj`, '<Project Sdk="Microsoft.NET.Sdk">\n</Project>\n');
  put('verification/CrudGeneration/schema.json', readFileSync(new URL('./support/fixtures/application-crud-schema.json', import.meta.url)));
  for (const provider of ['SqlServer', 'MySql']) put(`templates/migrations/${provider}/CreateProduct.sql.template`, `-- ${provider}\nCREATE TABLE acme_catalog_product (Id INT);\n`);
  put('framework/fullnet/manual.txt', 'protected framework');
  put('backend/Product.manual.cs', '// 必须保留的人工文件。');
  const capture = () => {
    const result = {};
    const visit = (directory, prefix = '') => {
      for (const item of readdirSync(directory, { withFileTypes: true })) {
        const path = prefix + item.name;
        if (item.isDirectory()) visit(join(directory, item.name), path + '/');
        else result[path] = readFileSync(join(directory, item.name)).toString('base64');
      }
    };
    visit(root); return result;
  };
  try { action({ root, put, capture }); } finally { rmSync(root, { recursive: true, force: true }); }
}

test('adopts exactly one reviewed draft per provider and preserves application and framework sources', () => fixture(({ root, capture }) => {
  const before = capture();
  const result = prepareApplicationBusinessMigrations(root);
  assert.equal(result.scriptsPerProvider, 1);
  const after = capture();
  for (const [path, bytes] of Object.entries(before)) {
    if (!path.startsWith(host + '/')) assert.equal(after[path], bytes, path);
  }
  for (const provider of ['SqlServer', 'MySql']) {
    assert.equal(readFileSync(join(root, `${host}/Migrations/${provider}/001_CreateProduct.sql`), 'utf8'), readFileSync(join(root, `templates/migrations/${provider}/CreateProduct.sql.template`), 'utf8'));
    assert.ok(readFileSync(join(root, `${host}/Demo.Host.Migrator.csproj`), 'utf8').includes(`LogicalName="acme.catalog.Migrations.${provider}.001_CreateProduct.sql"`));
  }
  assert.ok(readFileSync(join(root, `${host}/Program.cs`), 'utf8').includes('ApplicationMigrationRunner'));
  prepareApplicationBusinessMigrations(root);
  assert.deepEqual(capture(), after, 'repeat must preserve all bytes');
}));

test('missing provider draft fails before any writes', () => fixture(({ root, capture }) => {
  rmSync(join(root, 'templates/migrations/MySql/CreateProduct.sql.template'));
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('manual adopted SQL drift is protected', () => fixture(({ root, put, capture }) => {
  prepareApplicationBusinessMigrations(root);
  put(`${host}/Migrations/MySql/001_CreateProduct.sql`, '-- reviewed manual change');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('ambiguous Program anchor fails before any writes', () => fixture(({ root, put, capture }) => {
  put(`${host}/Program.cs`, anchor + '\n' + anchor);
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('unexpected ownership fails before any writes', () => fixture(({ root, put, capture }) => {
  put('verification/CrudGeneration/schema.json', '{"ownerKey":"fn"}');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('manual Program drift after adoption is protected', () => fixture(({ root, put, capture }) => {
  prepareApplicationBusinessMigrations(root);
  put(`${host}/Program.cs`, '// 人工修改的迁移入口。');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('changed source draft cannot rewrite an adopted migration', () => fixture(({ root, put, capture }) => {
  prepareApplicationBusinessMigrations(root);
  put('templates/migrations/SqlServer/CreateProduct.sql.template', '-- 新草稿不能覆盖已登记版本。');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('schema upgrade adds reviewed paired 002 scripts without changing adopted 001 or manual files', () => fixture(({ root, put, capture }) => {
  prepareApplicationBusinessMigrations(root);
  const schema = JSON.parse(readFileSync(join(root, 'verification/CrudGeneration/schema.json'), 'utf8'));
  schema.columns.push({ databaseName: 'Description', clrPropertyName: 'Description',
    jsonPropertyName: 'description', scalarType: 'String', isNullable: true, maxLength: 500 });
  put('verification/CrudGeneration/schema.json', JSON.stringify(schema));
  const before = capture();
  const result = prepareApplicationBusinessSchemaUpgrade(root);
  assert.equal(result.scriptsPerProvider, 2);
  const after = capture();
  for (const [path, bytes] of Object.entries(before)) {
    if (path === `${host}/Demo.Host.Migrator.csproj` || path === `${host}/Program.cs`
      || path === `${host}/ApplicationMigrationRunner.cs`) continue;
    assert.equal(after[path], bytes, `upgrade changed ${path}`);
  }
  for (const provider of ['SqlServer', 'MySql']) {
    const path = `${host}/Migrations/${provider}/002_AddProductDescription.sql`;
    assert.match(readFileSync(join(root, path), 'utf8'), /Description/u);
    assert.match(readFileSync(join(root, `${host}/Demo.Host.Migrator.csproj`), 'utf8'),
      new RegExp(`acme\\.catalog\\.Migrations\\.${provider}\\.002_AddProductDescription\\.sql`, 'u'));
  }
  assert.match(readFileSync(join(root, `${host}/Demo.Host.Migrator.csproj`), 'utf8'),
    /<ItemGroup>\s*<EmbeddedResource[^>]+002_AddProductDescription\.sql[^>]*\/>\s*<EmbeddedResource[^>]+002_AddProductDescription\.sql[^>]*\/>\s*<\/ItemGroup>/u,
    'new migration resources must be inside an MSBuild ItemGroup');
  assert.deepEqual(prepareApplicationBusinessSchemaUpgrade(root), result);
  assert.deepEqual(capture(), after, 'upgrade repeat changed adopted files');
}));
test('schema upgrade rejects missing provider and adopted 001 drift before writing', () => fixture(({ root, put, capture }) => {
  prepareApplicationBusinessMigrations(root);
  const schema = JSON.parse(readFileSync(join(root, 'verification/CrudGeneration/schema.json'), 'utf8'));
  schema.columns.push({ databaseName: 'Description', clrPropertyName: 'Description',
    jsonPropertyName: 'description', scalarType: 'String', isNullable: true, maxLength: 500 });
  put('verification/CrudGeneration/schema.json', JSON.stringify(schema));
  put(`${host}/Migrations/MySql/001_CreateProduct.sql`, '-- unexpected drift');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessSchemaUpgrade(root));
  assert.deepEqual(capture(), before);
}));
test('preexisting resource registration fails before adoption writes', () => fixture(({ root, put, capture }) => {
  put(`${host}/Demo.Host.Migrator.csproj`, '<Project><EmbeddedResource LogicalName="acme.catalog.Migrations.MySql.001_CreateProduct.sql" /></Project>');
  const before = capture();
  assert.throws(() => prepareApplicationBusinessMigrations(root));
  assert.deepEqual(capture(), before);
}));
test('runtime evidence distinguishes first execution and repeat journal', () => {
  assert.deepEqual(verifyApplicationMigrationResult('log\nFULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":123,"applicationScripts":1}\n', true), { frameworkScripts: 123, applicationScripts: 1 });
  assert.deepEqual(verifyApplicationMigrationResult('FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":0,"applicationScripts":0}', false), { frameworkScripts: 0, applicationScripts: 0 });
  assert.deepEqual(verifyApplicationMigrationResult('FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":0,"applicationScripts":1}', false, 1), { frameworkScripts: 0, applicationScripts: 1 });
});
test('missing, duplicate and incorrect migration evidence is rejected', () => {
  for (const stdout of ['', 'FULLNET_APPLICATION_MIGRATIONS {}', 'FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":0,"applicationScripts":1}', 'FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":3,"applicationScripts":0}', 'FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":1,"applicationScripts":1}\nFULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":1,"applicationScripts":1}']) {
    assert.throws(() => verifyApplicationMigrationResult(stdout, true));
  }
  assert.throws(() => verifyApplicationMigrationResult('FULLNET_APPLICATION_MIGRATIONS {"frameworkScripts":1,"applicationScripts":0}', false));
});
