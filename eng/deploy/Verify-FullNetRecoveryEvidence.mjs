import { createHash } from 'node:crypto';
import { lstatSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const sha256 = /^[a-f0-9]{64}$/u;
const utc = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/u;
const checks = ['database', 'files', 'keys', 'access', 'dispatch'];

function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

function readReceipt(directory, name) {
  const path = join(directory, name);
  const info = lstatSync(path, { throwIfNoEntry: false });
  requireCondition(info?.isFile() && info.size <= 64 * 1024, `${name} must be a regular JSON file of at most 64 KiB`);
  const bytes = readFileSync(path);
  let data;
  try { data = JSON.parse(bytes.toString('utf8')); }
  catch { throw new Error(`${name} contains invalid JSON`); }
  requireCondition(data !== null && typeof data === 'object' && !Array.isArray(data), `${name} must contain a JSON object`);
  return { data, digest: createHash('sha256').update(bytes).digest('hex') };
}

function time(value, field) {
  requireCondition(typeof value === 'string' && utc.test(value), `${field} must be a UTC timestamp`);
  const instant = Date.parse(value);
  const canonical = value.includes('.') ? value : value.replace('Z', '.000Z');
  requireCondition(Number.isFinite(instant) && new Date(instant).toISOString() === canonical, `${field} is not a valid UTC timestamp`);
  return instant;
}

function target(value, field) {
  requireCondition(Number.isSafeInteger(value) && value >= 0 && Number.isSafeInteger(value * 1000), `${field} must be non-negative integer seconds`);
  return value * 1000;
}

/** 仅复核同一场演练的脱敏回执，计算时间指标。运行验证由回执的签发步骤完成。 */
export function verifyFullNetRecoveryEvidence(directory, expectedCandidateSha256) {
  requireCondition(typeof expectedCandidateSha256 === 'string' && sha256.test(expectedCandidateSha256), 'Expected a trusted candidate SHA-256');
  const { data: manifest } = readReceipt(directory, 'recovery.json');
  requireCondition(manifest.schemaVersion === 2 && manifest.candidateSha256 === expectedCandidateSha256
    && typeof manifest.drillId === 'string' && /^[a-z0-9][a-z0-9-]{0,63}$/u.test(manifest.drillId), 'Invalid recovery manifest identity');
  const started = time(manifest.drillStartedAtUtc, 'drillStartedAtUtc');
  const fault = time(manifest.faultAtUtc, 'faultAtUtc');
  const isolated = time(manifest.isolatedAtUtc, 'isolatedAtUtc');
  const restored = time(manifest.businessRecoveredAtUtc, 'businessRecoveredAtUtc');
  const lastCommit = time(manifest.lastTrustedCommitAtUtc, 'lastTrustedCommitAtUtc');
  const recoveryPoint = time(manifest.verifiedRecoveryPointAtUtc, 'verifiedRecoveryPointAtUtc');
  requireCondition(started <= fault && started <= isolated && fault <= restored && isolated <= restored
    && recoveryPoint <= lastCommit && lastCommit <= fault, 'Recovery timeline is out of order');
  const rpoTarget = target(manifest.rpoTargetSeconds, 'rpoTargetSeconds');
  const rtoTarget = target(manifest.rtoTargetSeconds, 'rtoTargetSeconds');

  // 固定文件名和摘要阻止将其他演练的单项成功回执拼接进来。
  const checked = new Map();
  for (const check of checks) {
    const { data, digest } = readReceipt(directory, `${check}.json`);
    requireCondition(manifest.receipts?.[check] === digest && data.schemaVersion === 2 && data.check === check
      && data.drillId === manifest.drillId && data.candidateSha256 === expectedCandidateSha256 && data.outcome === 'passed',
    `${check} receipt identity, digest or outcome is invalid`);
    const checkedAt = time(data.checkedAtUtc, `${check}.checkedAtUtc`);
    requireCondition(checkedAt >= Math.max(fault, isolated) && checkedAt <= restored, `${check} was checked outside the isolated recovery window`);
    if (check === 'database' || check === 'files') {
      const recoveredThrough = time(data.recoveredThroughUtc, `${check}.recoveredThroughUtc`);
      requireCondition(recoveryPoint <= recoveredThrough && recoveredThrough <= fault, `${check} does not cover the joint recovery point`);
    }
    if (check === 'access') {
      const comparedThrough = time(data.revocationsComparedThroughUtc, 'access.revocationsComparedThroughUtc');
      requireCondition(comparedThrough >= fault && comparedThrough <= checkedAt, 'Access revocation evidence must cover the fault and precede its check');
      requireCondition(data.unresolvedRevocations === 0, 'Access has unresolved or unreported revocations');
    }
    if (check === 'dispatch') {
      requireCondition(data.unresolvedExternalOutcomes === 0, 'Dispatch has unresolved or unreported external outcomes');
    }
    checked.set(check, checkedAt);
  }
  requireCondition(checked.get('access') >= Math.max(checked.get('database'), checked.get('files'), checked.get('keys'))
    && checked.get('dispatch') >= checked.get('access'), 'Access must follow data and key checks, and dispatch must follow access');

  const rpoMs = lastCommit - recoveryPoint;
  const rtoMs = restored - fault;
  return { drillId: manifest.drillId, rpoMs, rtoMs, rpoMet: rpoMs <= rpoTarget, rtoMet: rtoMs <= rtoTarget };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    requireCondition(process.argv.length === 4, 'Usage: node Verify-FullNetRecoveryEvidence.mjs <evidence-directory> <candidate-sha256>');
    const result = verifyFullNetRecoveryEvidence(process.argv[2], process.argv[3]);
    console.log(JSON.stringify(result));
    if (!result.rpoMet || !result.rtoMet) process.exitCode = 1;
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
