import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { MODULE_ARTIFACTS, verifyApplicationCrudModule } from './support/application-crud-module.mjs';

const moduleProject = 'src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj';
const untouched = ['src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj',
  'src/Demo.Composition/Demo.Composition.csproj', 'src/Demo.Composition/ApplicationModuleCatalog.cs',
  'ui/admin/src/router/index.ts', '.fullnet/codegeneration-manifest.json', 'backend/ProductSql.g.cs'];

function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-module-tooling-'));
  for (const path of [...untouched, 'verification/CrudGeneration/schema.json']) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), 'human ' + path);
  }
  return root;
}

// 注入执行器只证明编排，真实候选与模块编译由独立应用双库真实栈验证。
function runner(root, failure, calls) {
  return (command, args, options) => {
    const stage = calls.length >= 9 ? ['manual-repeat', 'manual-build'][calls.length - 9]
      : calls.length >= 3 ? 'conflict' : ['apply', 'build', 'repeat'][calls.length];
    calls.push({ stage, command, args, options });
    if (stage === failure) return { status: 1, stdout: '', stderr: stage + ' failed' };
    if (stage === 'build' || stage === 'manual-build') return { status: 0, stdout: '', stderr: '' };
    const moduleRoot = join(root, 'src/Demo.Modules.Catalog');
    if (stage === 'apply') {
      for (const path of [...MODULE_ARTIFACTS, '.fullnet/codegeneration-manifest.json']) {
        mkdirSync(dirname(join(moduleRoot, path)), { recursive: true });
        writeFileSync(join(moduleRoot, path), path + '\n');
      }
      if (failure === 'apply-mutates-project') writeFileSync(join(root, moduleProject), 'project lost');
    }
    if (stage === 'repeat' && failure === 'repeat-mutates') writeFileSync(join(moduleRoot, 'CatalogModule.cs'), 'entry lost');
    if (stage === 'manual-repeat' && failure === 'manual-repeat-mutates') {
      writeFileSync(join(moduleRoot, 'Product.manual.cs'), 'business code lost');
    }
    if (stage === 'conflict') {
      const editedPath = MODULE_ARTIFACTS.find((path) => readFileSync(join(moduleRoot, path), 'utf8').includes('人工修改'));
      if (calls.length === 9 && failure === 'last-conflict-mutates') writeFileSync(join(moduleRoot, 'Product.manual.cs'), 'manual code lost');
      if (failure === 'conflict-mutates') writeFileSync(join(moduleRoot, 'Generated/Product/ProductSql.g.cs'), 'customization lost');
      if (failure === 'host-mutates') writeFileSync(join(root, 'src/Demo.Composition/ApplicationModuleCatalog.cs'), 'catalog lost');
      return { status: failure === 'conflict-accepted' || (calls.length === 9 && failure === 'last-conflict-accepted') ? 0 : 2,
        stdout: editedPath === MODULE_ARTIFACTS[0] ? '' : failure === 'wrong-conflict' ? 'Conflict Generated/Other.g.cs\n'
          : MODULE_ARTIFACTS.map((path) => `${path === editedPath ? 'Conflict' : 'Unchanged'} ${path}`).join('\n') + '\n',
        stderr: editedPath === MODULE_ARTIFACTS[0] ? (failure === 'wrong-conflict' ? 'wrong path' : '工作区冲突：模块聚合注册桥缺失或被修改。 路径：' + editedPath + '\n') : '' };
    }
    return { status: 0, stdout: MODULE_ARTIFACTS.map((path) => `${stage.endsWith('repeat') ? 'Unchanged' : 'Create'} ${path}`).join('\n')
      + (failure === 'missing-compilation-marker' ? '\n' : '\nValidated ModuleCompilation ' + moduleProject + '\n'), stderr: '' };
  };
}

for (const failure of ['apply', 'build', 'repeat', 'conflict-accepted', 'apply-mutates-project', 'repeat-mutates',
  'conflict-mutates', 'host-mutates', 'wrong-conflict', 'missing-compilation-marker']) {
  test(`application generated module rejects ${failure}`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'), run: runner(root, failure, calls) }));
      const stage = ['conflict-accepted', 'conflict-mutates', 'host-mutates', 'wrong-conflict'].includes(failure) ? 'conflict'
        : failure === 'missing-compilation-marker' ? 'apply' : failure.split('-')[0];
      const expectedStatus = ['conflict-mutates', 'host-mutates', 'wrong-conflict'].includes(failure) ? 2
        : ['conflict-accepted', 'apply-mutates-project', 'repeat-mutates', 'missing-compilation-marker'].includes(failure) ? 0 : 1;
      assert.equal(JSON.parse(readFileSync(join(root, 'evidence', stage + '.json'), 'utf8')).status, expectedStatus);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('application generated module compiles and preserves module and host content', () => {
  const root = workspace();
  const calls = [];
  try {
    const result = verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) });
    assert.equal(result.manualExtensionPreserved, true,
      'developer-owned business code must survive regeneration and module integration');
    assert.deepEqual(result, { artifacts: 6, moduleCompiled: true, conflictRejected: true, manualExtensionPreserved: true,
      conflictArtifacts: [...MODULE_ARTIFACTS.filter((path) => !path.endsWith('ProductSql.g.cs')), 'Generated/Product/ProductSql.g.cs'] });
    assert.deepEqual(calls.map(({ command, args }) => [command, args[0]]), [['dotnet', 'exec'], ['dotnet', 'build'], ['dotnet', 'exec'],
      ...MODULE_ARTIFACTS.map(() => ['dotnet', 'exec']), ['dotnet', 'exec'], ['dotnet', 'build']]);
    assert.ok(calls[0].args.includes('apply-module-integration'));
    assert.ok(calls.every(({ options }) => options.cwd === root && options.windowsHide === true));
    const project = readFileSync(join(root, moduleProject), 'utf8');
    assert.match(project, /\.\.\/\.\.\/framework\/fullnet\/src\/BuildingBlocks\/Full.NET.Modularity/);
    assert.match(project, /<ProjectReference Condition="'\$\(FullNetAotAnalysis\)' == 'true' or '\$\(FullNetPublishMode\)' == 'NativeAot' or '\$\(PublishAot\)' == 'true'" Include="[^"\n]+\/Full\.NET\.Data\.Dapper\/Full\.NET\.Data\.Dapper\.csproj"/u,
      '生成模块须在 Native 编译条件下引用静态 SQL 物化器实现。');
    assert.doesNotMatch(project, /G:|github_fork/);
    for (const path of untouched) assert.equal(readFileSync(join(root, path), 'utf8'), 'human ' + path);
    assert.match(readFileSync(join(root, 'src/Demo.Modules.Catalog/Generated/Product/ProductSql.g.cs'), 'utf8'), /人工修改/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('application generated module refuses an occupied module directory before running commands', () => {
  const root = workspace();
  const calls = [];
  try {
    mkdirSync(join(root, 'src/Demo.Modules.Catalog'));
    writeFileSync(join(root, 'src/Demo.Modules.Catalog/manual.txt'), 'human');
    assert.throws(() => verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }));
    assert.equal(calls.length, 0);
    assert.equal(readFileSync(join(root, 'src/Demo.Modules.Catalog/manual.txt'), 'utf8'), 'human');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('application module regeneration rejects developer-owned business code overwrite', () => {
  const root = workspace();
  const calls = [];
  try {
    assert.throws(() => verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'),
      run: runner(root, 'manual-repeat-mutates', calls) }), /developer-owned business code/u);
    assert.equal(JSON.parse(readFileSync(join(root, 'evidence/manual-repeat.json'), 'utf8')).status, 0);
    assert.equal(calls.at(-1).stage, 'manual-repeat');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('explicit fixture cleanup removes only the SQL test comment after conflict protection', () => {
  const root = workspace();
  const calls = [];
  try {
    verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'),
      removeTestSqlComment: true, run: runner(root, null, calls) });
    assert.equal(readFileSync(join(root, 'src/Demo.Modules.Catalog/Generated/Product/ProductSql.g.cs'), 'utf8'),
      'Generated/Product/ProductSql.g.cs\n');
    assert.equal(readFileSync(join(root, 'backend/ProductSql.g.cs'), 'utf8'), 'human backend/ProductSql.g.cs');
    assert.match(readFileSync(join(root, 'src/Demo.Modules.Catalog/Product.manual.cs'), 'utf8'), /人工模块文件/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('manual edits in every generated module artifact are rejected without overwriting other files', () => {
  const root = workspace();
  const calls = [];
  try {
    const result = verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'),
      removeTestSqlComment: true, run: runner(root, null, calls) });
    assert.equal(calls.filter(({ stage }) => stage === 'conflict').length, MODULE_ARTIFACTS.length);
    assert.deepEqual([...result.conflictArtifacts].sort(), [...MODULE_ARTIFACTS].sort());
    for (const path of MODULE_ARTIFACTS) assert.equal(readFileSync(join(root, 'src/Demo.Modules.Catalog', path), 'utf8'), path + '\n');
  } finally { rmSync(root, { recursive: true, force: true }); }
});

for (const failure of ['last-conflict-accepted', 'last-conflict-mutates']) {
  test(`all-artifact conflict acceptance rejects ${failure} at the final command`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudModule(root, { reportDirectory: join(root, 'evidence'),
        removeTestSqlComment: true, run: runner(root, failure, calls) }));
      assert.equal(calls.length, 9);
      const evidence = JSON.parse(readFileSync(join(root, 'evidence/conflict-6.json'), 'utf8'));
      assert.equal(evidence.status, failure === 'last-conflict-accepted' ? 0 : 2);
      assert.ok(evidence.stdout.split('\n').includes('Conflict Generated/Product/ProductSql.g.cs'));
    } finally { rmSync(root, { recursive: true, force: true }); }
  });
}
