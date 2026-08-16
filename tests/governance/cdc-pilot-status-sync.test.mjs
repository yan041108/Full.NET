import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

const activeDocsRequiringPilotAlignment = [
  'README.md',
  'docs/operations/messaging-runtime-topology.md',
  'docs/operations/cdc-kafka-event-delivery.md',
  'docs/operations/outbox-worker-topology.md',
  'docs/roadmap/adminnet-feature-parity.md'
];

const stalePilotPhrases = [
  /Designing\s*\/\s*Shadow-only/i,
  /仍为\s*`Designing`/,
  /当前状态仍为\s*`Designing`/,
  /当前仅完成 ADR\/Spec\/计划/
];

const historicalAllowlist = [
  'docs/verification/cursor-delivery-review-2026-08-09.md',
  'docs/superpowers/plans/2026-08-09-messaging-worker-polling-backpressure-repair.md',
  'docs/superpowers/plans/2026-08-08-transactional-outbox-cdc-kafka.md'
];

test('capability-status 将 CDC Delivery 标记为 Build-verified / Pilot', async () => {
  const capabilityStatus = await read('docs/roadmap/capability-status.md');
  assert.match(
    capabilityStatus,
    /CDC Relay \/ Kafka[\s\S]*?Build-verified \/ Pilot/
  );
});

test('活跃 ops/README 文档不得仍写 Designing/Shadow-only 与 Pilot 矛盾', async () => {
  for (const relativePath of activeDocsRequiringPilotAlignment) {
    const content = await read(relativePath);
    for (const pattern of stalePilotPhrases) {
      assert.doesNotMatch(
        content,
        pattern,
        `${relativePath} 仍包含过时 CDC 状态表述 ${pattern}`
      );
    }
  }
});

test('活跃 ops/README 文档应提及 Build-verified / Pilot 或等价 Pilot 表述', async () => {
  for (const relativePath of activeDocsRequiringPilotAlignment) {
    const content = await read(relativePath);
    assert.match(
      content,
      /Build-verified\s*\/\s*Pilot|已达\s*`Build-verified\s*\/\s*Pilot`/,
      `${relativePath} 缺少 Pilot 状态表述`
    );
  }
});

test('历史 verification/plan 允许保留 Shadow-only 快照但需有 superseded 说明', async () => {
  for (const relativePath of historicalAllowlist) {
    const content = await read(relativePath);
    if (/Shadow-only|仍为\s*`Designing`/i.test(content)) {
      assert.match(
        content,
        /superseded|2026-08-16|Task 6|Build-verified \/ Pilot/i,
        `${relativePath} 含历史 Shadow-only 表述但缺少 superseded 说明`
      );
    }
  }
});
