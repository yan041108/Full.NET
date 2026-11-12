import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { compareFullNetWebhookRecoveryEvidence as compare } from '../../eng/deploy/Compare-FullNetWebhookRecoveryEvidence.mjs';

const id = suffix => `00000000-0000-7000-8000-00000000000${suffix}`;
const delivery = { tenantId: id(1), subscriptionId: id(2), eventId: id(3), deliveryId: id(4), payloadSha256: 'a'.repeat(64) };
const applied = { ...delivery, outcome: 'applied' };

test('only a matching receiver commitment confirms the restored delivery', () => {
  const result = compare([delivery], [{ ...applied, payloadSha256: applied.payloadSha256.toUpperCase() }]);
  assert.deepEqual(result.confirmedApplied, [{ tenantId: id(1), subscriptionId: id(2), eventId: id(3), deliveryId: id(4) }]);
  assert.deepEqual(result.unresolved, []);
  assert.equal(result.unresolvedExternalOutcomes, 0);
});

for (const [name, deliveries, receipts, reason] of [
  ['missing receipt', [delivery], [], 'receipt-missing'],
  ['unknown outcome', [delivery], [{ ...applied, outcome: 'unknown' }], 'receiver-outcome-unknown'],
  ['cross-tenant receipt', [delivery], [{ ...applied, tenantId: id(5) }], 'identity-or-payload-mismatch'],
  ['different subscription', [delivery], [{ ...applied, subscriptionId: id(5) }], 'identity-or-payload-mismatch'],
  ['different event', [delivery], [{ ...applied, eventId: id(5) }], 'identity-or-payload-mismatch'],
  ['different body', [delivery], [{ ...applied, payloadSha256: 'b'.repeat(64) }], 'identity-or-payload-mismatch'],
  ['receipt after recovery point', [], [applied], 'restored-delivery-missing'],
]) {
  test(`${name} remains unresolved`, () => {
    const result = compare(deliveries, receipts);
    assert.deepEqual(result.confirmedApplied, []);
    assert.equal(result.unresolvedExternalOutcomes, 1);
    assert.equal(result.unresolved[0].reason, reason);
  });
}

test('duplicate and malformed evidence fails before producing a result', () => {
  assert.throws(() => compare([delivery], [applied, applied]), /duplicate delivery ID/);
  assert.throws(() => compare([delivery, delivery], [applied]), /duplicate delivery ID/);
  assert.throws(() => compare([null], [applied]), /invalid identity/);
  assert.throws(() => compare([delivery], [{ ...applied, outcome: 'failed' }]), /invalid outcome/);
});

test('CLI returns unresolved evidence with a nonzero exit code', t => {
  const directory = mkdtempSync(join(tmpdir(), 'fullnet-webhook-evidence-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const restored = join(directory, 'deliveries.json');
  const receiver = join(directory, 'receipts.json');
  writeFileSync(restored, JSON.stringify([delivery]));
  writeFileSync(receiver, '[]');
  const entry = fileURLToPath(new URL('../../eng/deploy/Compare-FullNetWebhookRecoveryEvidence.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [entry, restored, receiver], { encoding: 'utf8' });
  assert.equal(result.status, 1, result.stderr);
  assert.equal(JSON.parse(result.stdout).unresolvedExternalOutcomes, 1);
});
