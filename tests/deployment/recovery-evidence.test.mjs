import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { verifyFullNetRecoveryEvidence } from '../../eng/deploy/Verify-FullNetRecoveryEvidence.mjs';

const candidate = 'a'.repeat(64);
const time = seconds => new Date(Date.UTC(2026, 10, 11, 0, 0, seconds)).toISOString();
const entry = fileURLToPath(new URL('../../eng/deploy/Verify-FullNetRecoveryEvidence.mjs', import.meta.url));

function fixture(t) {
  const directory = mkdtempSync(join(tmpdir(), 'fullnet-recovery-evidence-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const identity = { schemaVersion: 2, drillId: 'isolated-drill', candidateSha256: candidate };
  const manifest = { ...identity, drillStartedAtUtc: time(0), faultAtUtc: time(60), isolatedAtUtc: time(62),
    lastTrustedCommitAtUtc: time(58), verifiedRecoveryPointAtUtc: time(55), businessRecoveredAtUtc: time(100),
    rpoTargetSeconds: 3, rtoTargetSeconds: 40, receipts: {} };
  const writeManifest = () => writeFileSync(join(directory, 'recovery.json'), JSON.stringify(manifest));
  const write = (check, checkedAtUtc, overrides = {}) => {
    const data = JSON.stringify({ ...identity, check, outcome: 'passed', checkedAtUtc, ...overrides });
    writeFileSync(join(directory, `${check}.json`), data);
    manifest.receipts[check] = createHash('sha256').update(data).digest('hex');
    writeManifest();
  };
  write('database', time(70), { recoveredThroughUtc: time(57) });
  write('files', time(71), { recoveredThroughUtc: time(55) });
  write('keys', time(72));
  write('access', time(80), { revocationsComparedThroughUtc: time(60), unresolvedRevocations: 0 });
  write('dispatch', time(95), { unresolvedExternalOutcomes: 0 });
  return { directory, manifest, write, writeManifest, verify: () => verifyFullNetRecoveryEvidence(directory, candidate) };
}

test('joint receipts measure RPO from last commit and RTO from the fault', t => {
  const f = fixture(t);
  assert.deepEqual(f.verify(), { drillId: 'isolated-drill', rpoMs: 3000, rtoMs: 40000, rpoMet: true, rtoMet: true });
});

test('CLI returns measured values with a failure when the frozen target is missed', t => {
  const f = fixture(t);
  f.manifest.rpoTargetSeconds = 2;
  f.writeManifest();
  const result = spawnSync(process.execPath, [entry, f.directory, candidate], { encoding: 'utf8' });
  assert.equal(result.status, 1, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), { drillId: 'isolated-drill', rpoMs: 3000, rtoMs: 40000, rpoMet: false, rtoMet: true });
});

for (const [name, alter, error] of [
  ['wrong candidate', f => { f.manifest.candidateSha256 = 'b'.repeat(64); f.writeManifest(); }, /manifest identity/],
  ['missing receipt', f => rmSync(join(f.directory, 'files.json')), /files.json must be a regular JSON file/],
  ['altered receipt', f => writeFileSync(join(f.directory, 'keys.json'), '{}'), /keys receipt identity, digest or outcome/],
  ['receipt from another drill with matching digest', f => f.write('keys', time(72), { drillId: 'old-drill' }), /keys receipt identity/],
  ['failed dispatch', f => f.write('dispatch', time(95), { outcome: 'failed' }), /dispatch receipt identity, digest or outcome/],
  ['file snapshot too old', f => f.write('files', time(71), { recoveredThroughUtc: time(54) }), /files does not cover the joint recovery point/],
  ['access before isolation', f => f.write('access', time(61)), /access was checked outside the isolated recovery window/],
  ['dispatch before access', f => f.write('dispatch', time(79), { unresolvedExternalOutcomes: 0 }), /dispatch must follow access/],
  ['missing revocation reconciliation', f => f.write('access', time(80)), /access.revocationsComparedThroughUtc/],
  ['revocations checked only through the backup', f => f.write('access', time(80), { revocationsComparedThroughUtc: time(55), unresolvedRevocations: 0 }), /revocation evidence must cover the fault/],
  ['unresolved revocation', f => f.write('access', time(80), { revocationsComparedThroughUtc: time(60), unresolvedRevocations: 1 }), /unresolved or unreported revocations/],
  ['unreported external outcomes', f => f.write('dispatch', time(95)), /unresolved or unreported external outcomes/],
  ['unknown external outcome', f => f.write('dispatch', time(95), { unresolvedExternalOutcomes: 1 }), /unresolved or unreported external outcomes/],
  ['nonexistent calendar date', f => { f.manifest.faultAtUtc = '2026-02-30T00:00:00Z'; f.writeManifest(); }, /faultAtUtc is not a valid UTC timestamp/],
]) {
  test(`${name} fails recovery evidence verification`, t => {
    const f = fixture(t);
    alter(f);
    assert.throws(f.verify, error);
  });
}
