import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

test('本地内循环保持聚焦，完整性能矩阵可在本地分批验收', async () => {
  const rules = await readFile(
    path.join(repositoryRoot, 'rules/performance-engineering.md'),
    'utf8'
  );

  assert.match(rules, /受影响的 Unit/);
  assert.match(rules, /1 个 SQL Server smoke 和 1 个 MySQL smoke/);
  assert.match(rules, /通常保留 2–4 个可比较样本/);
  assert.match(rules, /完整 Outbox 与 Audit 长时矩阵可在本地分批执行并作为验收证据/);
  assert.match(rules, /CI 性能工作流是可选执行方式/);
  assert.match(rules, /没有代码、SQL、配置或脚本行为变化时，禁止继续刷新性能样本/);
  assert.match(rules, /Outbox 和 Jobs 的默认并发必须保持为 `1`/);
  assert.match(rules, /SQL Server 与 MySQL 都取得可重复收益/);
});
