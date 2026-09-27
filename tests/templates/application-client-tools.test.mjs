import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { copyApplicationClientTools } from '../../scripts/templates/application-client-tools.mjs';

const files = ['scripts/openapi/generate-fullnet-client.mjs', 'scripts/openapi/validate-client-generation-readiness.mjs',
  'contracts/openapi/fullnet-client-v1.openapi.json', 'contracts/openapi/client-generation-manifest-v1.json'];
function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-client-tools-'));
  const bundleRoot = join(root, 'bundle'); const appRoot = join(root, 'application');
  try {
    for (const file of files) {
      mkdirSync(dirname(join(bundleRoot, file)), { recursive: true });
      const bytes = file.startsWith('scripts/') ? readFileSync(new URL('../../' + file, import.meta.url))
        : file.endsWith('manifest-v1.json') ? '{"publicOperationIds":[]}'
          : readFileSync(new URL('../openapi/fixtures/client-generation/valid.openapi.json', import.meta.url));
      writeFileSync(join(bundleRoot, file), bytes);
    }
    return action({ bundleRoot, appRoot });
  } finally { rmSync(root, { recursive: true, force: true }); }
}
test('packaged client tools run from application paths without the original repository', () => fixture(({ bundleRoot, appRoot }) => {
  copyApplicationClientTools(bundleRoot, appRoot);
  const tool = join(appRoot, '.fullnet-tools/openapi/generate-fullnet-client.mjs');
  const result = spawnSync(process.execPath, [tool], { cwd: tmpdir(), encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, result.stderr);
  const output = join(appRoot, 'packages/client-contracts/src/generated/operations.generated.ts');
  assert.match(readFileSync(output, 'utf8'), /identityListHostUsers/u);
  const check = spawnSync(process.execPath, [tool, '--check'], { cwd: tmpdir(), encoding: 'utf8', windowsHide: true });
  assert.equal(check.status, 0, check.stderr);
  assert.deepEqual(readFileSync(tool), readFileSync(join(bundleRoot, files[0])));
}));
test('missing bundle tool refuses distribution before any application writes', () => fixture(({ bundleRoot, appRoot }) => {
  rmSync(join(bundleRoot, files[1]));
  assert.throws(() => copyApplicationClientTools(bundleRoot, appRoot), /source/u);
  assert.equal(existsSync(appRoot), false);
}));
test('occupied application tool refuses distribution without overwriting it', () => fixture(({ bundleRoot, appRoot }) => {
  const occupied = join(appRoot, '.fullnet-tools/openapi/generate-fullnet-client.mjs');
  mkdirSync(dirname(occupied), { recursive: true }); writeFileSync(occupied, 'human');
  assert.throws(() => copyApplicationClientTools(bundleRoot, appRoot), /occupied/u);
  assert.equal(readFileSync(occupied, 'utf8'), 'human');
  assert.equal(existsSync(join(appRoot, 'contracts')), false);
}));
test('an occupied contract parent refuses distribution before copying tools', () => fixture(({ bundleRoot, appRoot }) => {
  mkdirSync(appRoot); writeFileSync(join(appRoot, 'contracts'), 'human');
  assert.throws(() => copyApplicationClientTools(bundleRoot, appRoot), /occupied/u);
  assert.equal(existsSync(join(appRoot, '.fullnet-tools')), false);
  assert.equal(readFileSync(join(appRoot, 'contracts'), 'utf8'), 'human');
}));
