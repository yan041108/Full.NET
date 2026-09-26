import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { verifyApplicationComposition } from './support/application-composition-probe.mjs';

function workspace() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-composition-tooling-'));
  const composition = join(root, 'src/Demo.Composition');
  mkdirSync(composition, { recursive: true });
  writeFileSync(join(composition, 'Demo.Composition.csproj'), '<Project><ItemGroup></ItemGroup></Project>');
  writeFileSync(join(composition, 'ApplicationModuleCatalog.cs'), 'private static IReadOnlyList<IFullNetModule> CreateModules() =>\r\n    [\r\n    ];');
  return root;
}

for (const failingStage of ['build', 'run', 'result']) {
  test(`application composition probe rejects ${failingStage} failure`, () => {
    const root = workspace();
    const calls = [];
    try {
      assert.throws(() => verifyApplicationComposition(root, {
        reportDirectory: join(root, 'reports'),
        run(command, args, options) {
          calls.push({ command, args, options });
          if (failingStage === 'build' || (failingStage === 'run' && calls.length === 2)) {
            return { status: 1, stdout: '', stderr: `${failingStage} failed` };
          }
          return { status: 0, stdout: 'FULLNET_APPLICATION_COMPOSITION {"roles":0}', stderr: '' };
        },
      }), failingStage === 'result' ? /incomplete runtime result/u : new RegExp(`${failingStage} failed`, 'u'));
      assert.equal(calls.length, failingStage === 'build' ? 1 : 2);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('application composition probe wires the owned project and executes its compiled assembly', () => {
  const root = workspace();
  const calls = [];
  try {
    const result = verifyApplicationComposition(root, {
      reportDirectory: join(root, 'reports'),
      run(command, args, options) {
        calls.push({ command, args, options });
        return { status: 0, stdout: 'FULLNET_APPLICATION_COMPOSITION {"roles":3,"reservedNames":6,"invalidGraphs":9}', stderr: '' };
      },
    });
    assert.deepEqual(result, { roles: 3, reservedNames: 6, invalidGraphs: 9 });
    assert.equal(calls.length, 2);
    assert.deepEqual(calls.map(({ command, args }) => [command, args[0]]), [['dotnet', 'build'], ['dotnet', 'exec']]);
    assert.ok(calls[1].args[1].endsWith('CompositionProbe.dll'));
    assert.equal(calls[0].options.cwd, root);
    assert.match(readFileSync(join(root, 'src/Demo.Composition/Demo.Composition.csproj'), 'utf8'), /Demo\.Modules\.Probe\.csproj/u);
    assert.match(readFileSync(join(root, 'src/Demo.Composition/ApplicationModuleCatalog.cs'), 'utf8'), /new Demo\.Modules\.Probe\.ProbeModule\(\)/u);
    assert.equal(JSON.parse(readFileSync(join(root, 'reports/run.json'), 'utf8')).status, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
