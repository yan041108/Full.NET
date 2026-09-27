import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { generateFullNetClient } from '../../scripts/openapi/generate-fullnet-client.mjs';

async function fixture(action) {
  const root = await mkdtemp(join(tmpdir(), 'fullnet-client-manifest-'));
  try {
    const document = JSON.parse(await readFile(new URL('./fixtures/client-generation/valid.openapi.json', import.meta.url), 'utf8'));
    const inputPath = join(root, 'application.openapi.json');
    const manifestPath = join(root, 'application-manifest.json');
    const outputDirectory = join(root, 'generated');
    await action({ root, document, inputPath, manifestPath, outputDirectory });
  } finally { await rm(root, { recursive: true, force: true }); }
}

test('application client generation uses its explicitly supplied public operation manifest', async () => fixture(async (f) => {
  const operation = f.document.paths['/api/v1/identity/users'].get;
  operation.operationId = 'applicationPublicList'; operation.security = [];
  await writeFile(f.inputPath, JSON.stringify(f.document));
  await writeFile(f.manifestPath, JSON.stringify({ publicOperationIds: ['applicationPublicList'] }));
  const before = await readFile(f.inputPath);
  await generateFullNetClient(f);
  assert.equal((await readdir(f.outputDirectory)).length, 4);
  assert.match(await readFile(join(f.outputDirectory, 'operations.generated.ts'), 'utf8'), /applicationPublicList/u);
  assert.deepEqual(await readFile(f.inputPath), before);
  const cli = spawnSync(process.execPath, [fileURLToPath(new URL('../../scripts/openapi/generate-fullnet-client.mjs', import.meta.url)),
    '--input', f.inputPath, '--manifest', f.manifestPath, '--output', f.outputDirectory, '--check'], { encoding: 'utf8', windowsHide: true });
  assert.equal(cli.status, 0, cli.stderr);
}));

for (const publicOperationIds of ['all', null, [17], [''], [' spaced '], ['duplicate', 'duplicate']]) {
  test(`invalid application manifest is rejected before writes: ${JSON.stringify(publicOperationIds)}`, async () => fixture(async (f) => {
    await writeFile(f.inputPath, JSON.stringify(f.document));
    await writeFile(f.manifestPath, JSON.stringify({ publicOperationIds }));
    await assert.rejects(() => generateFullNetClient(f), /publicOperationIds/u);
    await assert.rejects(() => readdir(f.outputDirectory), { code: 'ENOENT' });
  }));
}

test('an application manifest cannot authorize an unlisted anonymous operation', async () => fixture(async (f) => {
  f.document.paths['/api/v1/identity/users'].get.security = [];
  await writeFile(f.inputPath, JSON.stringify(f.document));
  await writeFile(f.manifestPath, JSON.stringify({ publicOperationIds: [] }));
  await assert.rejects(() => generateFullNetClient(f));
  await assert.rejects(() => readdir(f.outputDirectory), { code: 'ENOENT' });
}));

for (const args of [['--manifest'], ['--manifest', '--check']]) {
  test(`CLI refuses missing manifest values: ${args.join(' ')}`, () => {
    const result = spawnSync(process.execPath, [fileURLToPath(new URL('../../scripts/openapi/generate-fullnet-client.mjs', import.meta.url)), ...args],
      { encoding: 'utf8', windowsHide: true });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /--manifest 缺少文件路径/u);
  });
}
