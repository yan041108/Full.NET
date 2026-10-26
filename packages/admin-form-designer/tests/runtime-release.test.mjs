import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { createRuntimeRelease, verifyRuntimeRelease } from '../runtime-release.mjs';
test('实际运行文件变化导致发布摘要不匹配，清单自身不进入循环摘要', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-vform-release-'));
  try {
    await writeFile(path.join(directory, 'main.js'), 'export const version = 1;');
    const expected = await createRuntimeRelease(directory);
    await writeFile(path.join(directory, 'runtime-release.json'), JSON.stringify(expected));
    assert.deepEqual(await verifyRuntimeRelease(directory, expected), expected);
    await writeFile(path.join(directory, 'main.js'), 'export const version = 2;');
    await assert.rejects(verifyRuntimeRelease(directory, expected), /runtime_release_mismatch/);
    await writeFile(path.join(directory, 'main.js'), 'export const version = 1;');
    await mkdir(path.join(directory, 'nested'));
    await writeFile(path.join(directory, 'nested/runtime-release.json'), 'unexpected file');
    await assert.rejects(verifyRuntimeRelease(directory, expected), /runtime_release_mismatch/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
