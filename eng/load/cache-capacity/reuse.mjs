import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {logEvidence} from './log-evidence.mjs';
import {assertBudgetIdentity} from './database-budget.mjs';
import {selectSqlCounter, apiIdentityMap} from './metrics.mjs';
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const json = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));

// 旧样本只使用原生产进程的身份及日志，不以本轮新 UUID 解释历史原始指标。
export function sampleIdentityEvidence(directory, row) {
  const folder = path.resolve(directory, row.folder);
  assert.ok(folder.startsWith(path.resolve(directory) + path.sep));
  const identitiesFile = row.reusedFrom ? path.join(folder, 'reused-api-identities.json')
    : path.join(directory, row.context, 'api-identities.json');
  const bytes = fs.readFileSync(identitiesFile), identities = JSON.parse(bytes);
  apiIdentityMap(identities, row.context);
  let metadata;
  if (row.reusedFrom) {
    assert.equal(sha(bytes), row.reusedFrom.identitiesSha256);
    const metaBytes = fs.readFileSync(path.join(folder, 'reused-api-logs.json'));
    assert.equal(sha(metaBytes), row.reusedFrom.identityLogsSha256); metadata = JSON.parse(metaBytes);
  }
  const logs = identities.map(identity => {
    const file = row.reusedFrom ? path.join(folder, `producer-api-${identity.role}.log`)
      : path.join(directory, 'native-aot/linux-x64/test-logs', identity.logFileName);
    const proof = logEvidence(file, identity.announcement), hash = proof.sha256;
    assert.equal(proof.occurrences, 1);
    if (metadata) assert.deepEqual(metadata.find(x => x.role === identity.role), {role: identity.role, sha256: hash});
    return {role: identity.role, file, sha256: hash};
  });
  if (metadata) assert.equal(metadata.length, 2);
  return {identities, identitiesFile, identitiesSha256: sha(bytes), logs};
}

export function copySampleIdentityEvidence(evidence, target) {
  fs.copyFileSync(evidence.identitiesFile, path.join(target, 'reused-api-identities.json'));
  for (const log of evidence.logs) {
    assert.equal(logEvidence(log.file).sha256, log.sha256);
    fs.copyFileSync(log.file, path.join(target, `producer-api-${log.role}.log`));
  }
  const bytes = JSON.stringify(evidence.logs.map(({role, sha256}) => ({role, sha256})));
  fs.writeFileSync(path.join(target, 'reused-api-logs.json'), bytes);
  return {identitiesSha256: evidence.identitiesSha256, identityLogsSha256: sha(bytes)};
}

// 复用的是有真实子进程与原始指标支撑的独立样本，不把失败父批改写为成功。
export function eligibleSample(candidate, expected) {
  return !!candidate && candidate.name !== 'warmup' && !candidate.safetyViolation
    && ['within-local-budget', 'budget-exceeded'].includes(candidate.result?.classification)
    && [0, 99].includes(candidate.result?.exitCode) && candidate.result?.authorizationErrors === 0
    && candidate.result?.payloadErrorRate === 0
    && candidate.result?.sqlCounterMetric === selectSqlCounter()
    && ['context', 'name', 'model', 'level', 'duration'].every(k => candidate[k] === expected[k]);
}
export async function reusableSamples(directory, recipe, helperHashes, parentTerminal) {
  const stateBytes = fs.readFileSync(path.join(directory, 'state.json')), previous = JSON.parse(stateBytes);
  const oldRecipe = json(path.join(directory, 'recipe.json'));
  assert.equal(selectSqlCounter(oldRecipe.sqlCounterMetric), selectSqlCounter(recipe.sqlCounterMetric), 'SQL counter contract must match');
  assert.equal(previous.cleanupConfirmed, true); assert.equal(previous.source, recipe.source);
  assert.equal(oldRecipe.apiSha256, recipe.apiSha256);
  assert.equal(previous.helperHashes['scenario.js'], helperHashes['scenario.js']);
  assertBudgetIdentity(previous.helperHashes['database-budget.json'], helperHashes['database-budget.json']);
  const parentBytes = fs.readFileSync(parentTerminal), parent = JSON.parse(parentBytes.toString().replace(/^\uFEFF/, ''));
  assert.equal(parent.Pid, previous.pid); assert.equal(parent.ObservedViaLiveProcessHandle, true);
  assert.ok(Number.isInteger(parent.ExitCode));
  const currentParser = path.join(directory, 'harness/metrics.mjs');
  assert.equal(sha(fs.readFileSync(currentParser)), previous.helperHashes['metrics.mjs']);
  const map = new Map();
  for (const row of previous.samples.filter(r => eligibleSample(r, r))) {
    const folder = path.resolve(directory, row.folder);
    assert.ok(folder.startsWith(path.resolve(directory) + path.sep));
    const bytes = fs.readFileSync(path.join(folder, 'summary.json')); assert.equal(sha(bytes), row.summarySha256);
    const serial = Number(path.basename(folder).split('-')[0]);
    let parserFile = currentParser, terminalFile = path.join(directory, `k6-${row.context}-${serial}-terminal.json`);
    if (row.reusedFrom) {
      // 多批补齐时保留最初的原始解析器和真实发生器终态，并绑定中间批的完整来源，不能伪造本批终态。
      parserFile = path.join(folder, 'producer-metrics.mjs');
      terminalFile = path.join(folder, 'reused-k6-terminal.json');
      assert.equal(sha(fs.readFileSync(parserFile)), row.reusedFrom.parserSha256);
      assert.equal(sha(fs.readFileSync(terminalFile)), row.reusedFrom.terminalSha256);
      const ancestorBytes = fs.readFileSync(path.join(row.reusedFrom.directory, 'state.json'));
      assert.equal(sha(ancestorBytes), row.reusedFrom.stateSha256);
      const ancestor = JSON.parse(ancestorBytes).samples.find(s => s.folder === row.reusedFrom.originalFolder);
      assert.ok(ancestor); assert.deepEqual(ancestor.result, row.result); assert.equal(ancestor.summarySha256, row.summarySha256);
      assert.equal(row.reusedFrom.acceptedParentClaim, false);
    }
    const terminal = json(terminalFile);
    assert.equal(terminal.observedViaLiveChildHandle, true); assert.equal(terminal.exitCode, row.result.exitCode);
    const {readMetrics, summarizeSample, apiIdentityMap: originalIdentityMap} = await import(pathToFileURL(parserFile));
    assert.equal(typeof originalIdentityMap, 'function', 'Historical parser requires actual identity evidence');
    const identityEvidence = sampleIdentityEvidence(directory, row);
    originalIdentityMap(identityEvidence.identities, row.context);
    const parse = raw => readMetrics(raw, identityEvidence.identities, row.context);
    const prom = file => parse(fs.readFileSync(path.join(folder, file), 'utf8'));
    const snapshots = fs.readFileSync(path.join(folder, 'metrics-samples.prom'), 'utf8').split(/^# sampled_at .*$/m).filter(s => s.trim()).map(parse);
    assert.deepEqual(summarizeSample(JSON.parse(bytes), terminal.exitCode, snapshots, prom('before.prom'), prom('after.prom'), oldRecipe.sqlCounterMetric), row.result);
    map.set(row.context + '/' + row.name, {row, folder, terminalFile, parserFile, identityEvidence});
  }
  return {map, images: previous.images, stateSha256: sha(stateBytes), directory, parentTerminal,
    parentTerminalSha256: sha(parentBytes), actualParentExit: parent.ExitCode,
    previousAcceptance: previous.acceptance, acceptedParentClaim: false};
}

export function verifyContextPolicies(previousDirectory, currentDirectory) {
  const oldConfig = json(path.join(previousDirectory, 'experiment-config.json')), current = json(path.join(currentDirectory, 'experiment-config.json'));
  for (const key of ['provider', 'mode', 'apiInstances', 'workerInstances', 'globalApiLimitEnabled', 'globalApiPermitLimitPerMinute']) assert.deepEqual(oldConfig[key], current[key]);
  assert.deepEqual(oldConfig.databaseBudget, current.databaseBudget, 'Database pool/admission profile must match');
  const policy = dir => json(path.join(dir, 'runtime-policies.json')).find(p => p.entryName === 'settings.grid-preference');
  const a = policy(previousDirectory), b = policy(currentDirectory);
  for (const key of ['entryName', 'ownerModule', 'consistencyClass', 'l1DurationSeconds', 'l2DurationSeconds', 'jitterSeconds',
    'negativeDurationSeconds', 'failSafeEnabled', 'requiresVersionRecheck', 'maxSerializedBytes']) assert.deepEqual(a[key], b[key], key);
}
