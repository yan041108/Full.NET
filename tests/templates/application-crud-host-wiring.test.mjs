import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { MODULE_ARTIFACTS } from './support/application-crud-module.mjs';
import { verifyApplicationCrudHostWiring } from './support/application-crud-host-wiring.mjs';

const entry = 'src/Demo.Modules.Catalog/CatalogModule.cs';
const project = 'src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj';
const compositionProject = 'src/Demo.Composition/Demo.Composition.csproj';
const catalog = 'src/Demo.Composition/ApplicationModuleCatalog.cs';
const stages = ['entry', 'composition', 'build', 'entry-repeat', 'composition-repeat'];

function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-host-wiring-'));
  for (const path of [entry, project, compositionProject, catalog,
    'verification/CrudGeneration/schema.json', 'verification/CrudGeneration/module-target.json',
    'src/Demo.Modules.Catalog/.fullnet/codegeneration-manifest.json', 'src/Demo.Modules.Catalog/Product.manual.cs',
    ...MODULE_ARTIFACTS.map((path) => 'src/Demo.Modules.Catalog/' + path),
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'ui/admin/src/router/index.ts',
    '.fullnet/codegeneration-manifest.json', 'backend/ProductSql.g.cs']) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), path === catalog ? 'new Demo.Modules.Probe.ProbeModule(),\n' : 'human ' + path);
  }
  return root;
}

// 模拟 SDK 故障只验证验收门禁与恢复，真实进程退出码由打包应用独立验证。
function missingSdkRunner(root, failure, normalCalls, probeCalls, exitStatus = -123) {
  const normal = runner(root, null, normalCalls);
  return (command, args, options) => {
    if (JSON.parse(readFileSync(join(root, 'global.json'), 'utf8')).sdk.version !== '99.0.100')
      return normal(command, args, options);
    probeCalls.push(args);
    if (args[0] === 'build') return { status: exitStatus, signal: null, stdout: '', stderr: '' };
    if (failure === 'mutates') writeFileSync(join(root, compositionProject), 'changed');
    return { status: 2, signal: null, stdout: '', stderr: failure === 'missing-code' ? '构建进程未返回诊断'
      : '构建进程退出码：' + (exitStatus | 0) + (failure === 'leaks' ? ' credential-probe' : '') };
  };
}

for (const failure of [null, 'missing-code', 'leaks', 'mutates']) {
  test(`missing SDK acceptance ${failure ?? 'preserves exit codes'} and restores SDK settings`, () => {
    const root = workspace();
    const settingsPath = join(root, 'global.json');
    const settings = Buffer.from('{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}');
    writeFileSync(settingsPath, settings);
    const normalCalls = [];
    const probeCalls = [];
    const options = { verifyMissingSdk: true, reportDirectory: join(root, 'evidence'),
      run: missingSdkRunner(root, failure, normalCalls, probeCalls) };
    try {
      if (failure) assert.throws(() => verifyApplicationCrudHostWiring(root, options));
      else {
        verifyApplicationCrudHostWiring(root, options);
        assert.equal(probeCalls.length, 3);
        for (const command of ['apply-module-integration', 'apply-composition-integration']) {
          assert.deepEqual(JSON.parse(readFileSync(join(root, 'evidence', command + '-sdk-failure.json'), 'utf8')),
            { buildExitCode: -123, diagnosticExitCode: -123, cliExitCode: 2, diagnostic: '构建进程退出码：-123', inputsUnchanged: true });
        }
      }
      assert.deepEqual(readFileSync(settingsPath), settings);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('Windows SDK exit status preserves its signed .NET representation', () => {
  const root = workspace();
  const settingsPath = join(root, 'global.json');
  const settings = Buffer.from('{"sdk":{"version":"10.0.100"}}');
  writeFileSync(settingsPath, settings);
  try {
    verifyApplicationCrudHostWiring(root, { verifyMissingSdk: true, reportDirectory: join(root, 'evidence'),
      run: missingSdkRunner(root, null, [], [], 2147516571) });
    for (const command of ['apply-module-integration', 'apply-composition-integration']) {
      const evidence = JSON.parse(readFileSync(join(root, 'evidence', command + '-sdk-failure.json'), 'utf8'));
      assert.equal(evidence.buildExitCode, 2147516571);
      assert.equal(evidence.diagnosticExitCode, -2147450725);
      assert.equal(evidence.diagnostic, '构建进程退出码：-2147450725');
    }
    assert.deepEqual(readFileSync(settingsPath), settings);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// 此执行器只模拟CLI写盘以验证门禁，不能证明模块/Composition/API实际编译。
function runner(root, failure, calls) {
  return (command, args, options) => {
    const stage = stages[calls.length];
    calls.push({ stage, command, args, options });
    if (stage === failure) return { status: 1, stdout: '', stderr: stage + ' failed' };
    if (stage === 'entry') {
      if (failure !== 'entry-lying') writeFileSync(join(root, entry), 'services.AddFullNetGeneratedModuleFeatures();\nendpoints.MapFullNetGeneratedModuleFeatures();');
      return { status: 0, stdout: `Update ${entry}\n` + (failure === 'missing-validation' ? '' : `Validated ModuleCompilation ${project}\n`), stderr: '' };
    }
    if (stage === 'composition') {
      writeFileSync(join(root, compositionProject), 'human ' + compositionProject + '\n../Demo.Modules.Catalog/Demo.Modules.Catalog.csproj');
      writeFileSync(join(root, catalog), 'new Demo.Modules.Probe.ProbeModule(),\nnew CatalogModule(),\n');
      if (failure === 'composition-drop-probe') writeFileSync(join(root, catalog), 'new CatalogModule(),\n');
      if (failure === 'protected-mutates') writeFileSync(join(root, 'src/Demo.Host.Api/Program.cs'), 'API lost');
      return { status: 0, stdout: `Update ${compositionProject}\nUpdate ${catalog}\nValidated CompositionCompilation ${compositionProject}\n`, stderr: '' };
    }
    if (stage === 'entry-repeat') {
      if (failure === 'entry-repeat-mutates') writeFileSync(join(root, catalog), 'catalog lost');
      return { status: 0, stdout: `Unchanged ${entry}\n`, stderr: '' };
    }
    if (stage === 'composition-repeat') {
      if (failure === 'composition-repeat-mutates') writeFileSync(join(root, 'backend/ProductSql.g.cs'), 'human SQL lost');
      return { status: 0, stdout: `Unchanged ${compositionProject}\nUnchanged ${catalog}\n`, stderr: '' };
    }
    if (stage === 'build' && failure === 'build-mutates') writeFileSync(join(root, 'src/Demo.Modules.Catalog/Generated/Product/ProductSql.g.cs'), 'generated SQL lost');
    return { status: 0, stdout: '', stderr: '' };
  };
}

for (const failure of [...stages, 'entry-lying', 'composition-drop-probe', 'protected-mutates',
  'entry-repeat-mutates', 'composition-repeat-mutates', 'build-mutates', 'missing-validation']) {
  test(`application CRUD host wiring rejects ${failure} failure`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudHostWiring(root, { reportDirectory: join(root, 'evidence'), run: runner(root, failure, calls) }));
      const stage = stages.includes(failure) ? failure
        : failure === 'missing-validation' ? 'entry' : failure === 'protected-mutates' ? 'composition'
          : failure.endsWith('-mutates') ? failure.replace('-mutates', '') : failure.startsWith('entry') ? 'entry' : 'composition';
      assert.equal(JSON.parse(readFileSync(join(root, 'evidence', stage + '.json'), 'utf8')).status, stages.includes(failure) ? 1 : 0);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('application CRUD host wiring compiles the API and is idempotent', () => {
  const root = workspace();
  const calls = [];
  try {
    assert.deepEqual(verifyApplicationCrudHostWiring(root, { reportDirectory: join(root, 'evidence'), run: runner(root, null, calls) }),
      { entryIntegrated: true, compositionIntegrated: true, apiCompiled: true, repeatUnchanged: true });
    assert.equal(calls.length, 5);
    assert.equal(calls[0].args[2], 'apply-module-entry-integration');
    assert.equal(calls[1].args[2], 'apply-composition-integration');
    assert.deepEqual(calls[2].args.slice(0, 4), ['build', join(root, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release']);
    assert.ok(calls.every(({ options }) => options.cwd === root && options.windowsHide === true));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
