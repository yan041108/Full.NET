import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

test('本地性能验证必须保持聚焦，完整矩阵只能进入夜间或手动 CI', async () => {
  const rules = await readFile(
    path.join(repositoryRoot, 'rules/performance-engineering.md'),
    'utf8'
  );

  assert.match(rules, /受影响的 Unit/);
  assert.match(rules, /1 个 SQL Server smoke 和 1 个 MySQL smoke/);
  assert.match(rules, /通常保留 2–4 个可比较样本/);
  assert.match(rules, /完整 Outbox 210 样本和 Audit 600 秒矩阵/);
  assert.match(rules, /夜间或手动 CI 性能工作流/);
  assert.match(rules, /没有代码、SQL、配置或脚本行为变化时，禁止继续刷新性能样本/);
  assert.match(rules, /Outbox 和 Jobs 的默认并发必须保持为 `1`/);
  assert.match(rules, /SQL Server 与 MySQL 都取得可重复收益/);
});
