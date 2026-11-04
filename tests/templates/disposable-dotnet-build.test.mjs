import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { runConcentratedAcceptance, withTestRun, testRunEnvironment } from '../../scripts/testing/test-run-context.mjs';
import { runDotnet } from './support/created-app-real-stack.mjs';
import { verifyApplicationComposition } from './support/application-composition-probe.mjs';
import { verifyApplicationCrudGeneration } from './support/application-crud-generation.mjs';

test('生成应用 dotnet 调用继承集中验收环境并保留运行时配置', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-dotnet-environment-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const flags = ['UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER'];
  const before = flags.map(name => process.env[name]);
  await runConcentratedAcceptance(t, async () => {
    let called = false;
    runDotnet(['build', 'Application.csproj'], root, { DOTNET_ENVIRONMENT: 'Development' }, 300_000, undefined,
      (command, args, options) => {
        called = true;
        assert.equal(command, 'dotnet'); assert.deepEqual(args, ['build', 'Application.csproj']);
        assert.deepEqual(flags.map(name => options.env[name]), ['false', '1', '0']);
        assert.equal(options.env.TEMP, testRunEnvironment().TEMP);
        assert.equal(options.env.DOTNET_ENVIRONMENT, 'Development');
        assert.equal(options.cwd, root);
        return { status: 0 };
      });
    assert.equal(called, true);
  }, { cwd: root, lock: false, heavy: false });
  assert.deepEqual(flags.map(name => process.env[name]), before);
  await withTestRun({ cwd: root, lock: false }, async () => {
    runDotnet(['build', 'Application.csproj'], root, {}, 300_000, undefined,
      (_, __, options) => { assert.deepEqual(flags.map(name => options.env[name]), before); return { status: 0 }; });
  });
});

test('应用编排与 CRUD 直接探针继承集中验收构建环境', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-direct-dotnet-environment-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await runConcentratedAcceptance(t, async () => {
    for (const [name, verify] of [['composition', verifyApplicationComposition], ['crud', verifyApplicationCrudGeneration]]) {
      const appRoot = path.join(root, name);
      if (name === 'composition') {
        const composition = path.join(appRoot, 'src/Demo.Composition');
        await mkdir(composition, { recursive: true });
        await writeFile(path.join(composition, 'Demo.Composition.csproj'), await readFile(new URL(
          '../../templates/fullnet-app/src/FullNetAppNameToken.Composition/FullNetAppNameToken.Composition.csproj', import.meta.url)));
        await writeFile(path.join(composition, 'ApplicationModuleCatalog.cs'),
          'private static IReadOnlyList<IFullNetModule> CreateModules() => [];');
      }
      const stop = new Error('首个真实进程边界已验证，无需执行生成器');
      let called = false;
      assert.throws(() => verify(appRoot, { reportDirectory: path.join(appRoot, 'evidence'),
        run(command, args, options) {
          called = true;
          assert.equal(command, 'dotnet'); assert.equal(args[0], 'build');
          assert.deepEqual(['UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER']
            .map(key => options.env?.[key]), ['false', '1', '0']);
          assert.equal(options.env.TEMP, testRunEnvironment().TEMP);
          assert.equal(options.cwd, appRoot);
          throw stop;
        } }), error => error === stop);
      assert.equal(called, true);
    }
  }, { cwd: root, lock: false, heavy: false });
});
