import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const vendor = new URL('../vendor/vform3/', import.meta.url);
test('固定上游的全部 338 个来源文件保持逐字节一致', async () => {
  const manifest = JSON.parse(await readFile(new URL('manifest.json', vendor), 'utf8'));
  assert.equal(manifest.commit, 'c67479e496bab56a93a3dff168a4f529d8293c67');
  const source = new URL('upstream/', vendor);
  const files = await readdir(source, { recursive: true, withFileTypes: true });
  assert.equal(files.filter(f => f.isFile()).length, manifest.files.length);
  for (const file of manifest.files) {
    const data = await readFile(new URL(file.path, source));
    assert.equal(data.length, file.bytes, file.path);
    assert.equal(createHash('sha256').update(data).digest('hex'), file.sha256, file.path);
  }
});
