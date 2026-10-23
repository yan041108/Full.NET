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

  // 执行时机由集中验收规则拥有；不要锁定已经退役的逐编辑固定双库 Smoke 流程。
  assert.match(rules, /\]\(development-quality\.md#r-20261008-concentrated-acceptance[^)]*\)/u);
  assert.match(rules, /通常保留 2–4 个可比较样本/);
  assert.match(rules, /完整 Outbox、Audit 或其他长时性能矩阵只有用户明确要求全量时才执行，可在本地分批并作为对应范围验收证据/);
  assert.match(rules, /历史计划和 CI 性能工作流不构成自动执行授权/);
  assert.match(rules, /没有代码、SQL、配置或脚本行为变化时，禁止继续刷新性能样本/);
  assert.match(rules, /Outbox 和 Jobs 的默认并发必须保持为 `1`/);
  assert.match(rules, /SQL Server 与 MySQL 都取得可重复收益/);
});
