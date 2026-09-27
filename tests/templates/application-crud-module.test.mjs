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

// 注入执行器只证明编排，真实候选与模块编译由独立应用 Actions 验证。
function runner(root, failure, calls) {
  return (command, args, options) => {
    const stage = ['apply', 'build', 'repeat', 'conflict'][calls.length];
    calls.push({ stage, command, args, options });
    if (stage === failure) return { status: 1, stdout: '', stderr: stage + ' failed' };
    if (stage === 'build') return { status: 0, stdout: '', stderr: '' };
    const moduleRoot = join(root, 'src/Demo.Modules.Catalog');
    if (stage === 'apply') {
      for (const path of [...MODULE_ARTIFACTS, '.fullnet/codegeneration-manifest.json']) {
        mkdirSync(dirname(join(moduleRoot, path)), { recursive: true });
        writeFileSync(join(moduleRoot, path), path + '\n');
      }
      if (failure === 'apply-mutates-project') writeFileSync(join(root, moduleProject), 'project lost');
    }
    if (stage === 'repeat' && failure === 'repeat-mutates') writeFileSync(join(moduleRoot, 'CatalogModule.cs'), 'entry lost');
    if (stage === 'conflict') {
      if (failure === 'conflict-mutates') writeFileSync(join(moduleRoot, 'Generated/Product/ProductSql.g.cs'), 'customization lost');
      if (failure === 'host-mutates') writeFileSync(join(root, 'src/Demo.Composition/ApplicationModuleCatalog.cs'), 'catalog lost');
      return { status: failure === 'conflict-accepted' ? 0 : 2,
        stdout: failure === 'wrong-conflict' ? 'Conflict Generated/Other.g.cs\n' : 'Conflict Generated/Product/ProductSql.g.cs\n', stderr: '' };
    }
    return { status: 0, stdout: MODULE_ARTIFACTS.map((path) => `${stage === 'repeat' ? 'Unchanged' : 'Create'} ${path}`).join('\n')
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
    assert.deepEqual(result, { artifacts: 6, moduleCompiled: true, conflictRejected: true });
    assert.deepEqual(calls.map(({ command, args }) => [command, args[0]]), [['dotnet', 'exec'], ['dotnet', 'build'], ['dotnet', 'exec'], ['dotnet', 'exec']]);
    assert.ok(calls[0].args.includes('apply-module-integration'));
    assert.ok(calls.every(({ options }) => options.cwd === root && options.windowsHide === true));
    const project = readFileSync(join(root, moduleProject), 'utf8');
    assert.match(project, /\.\.\/\.\.\/framework\/fullnet\/src\/BuildingBlocks\/Full.NET.Modularity/);
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
