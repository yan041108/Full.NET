import {logEvidence} from '../../eng/load/cache-capacity/log-evidence.mjs';
import path from 'node:path';
import os from 'node:os';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import vm from 'node:vm';
import {readMetrics, apiRole, counterDelta, summarizeSample, verifyLifecycle, missingGridSqlRoles} from '../../eng/load/cache-capacity/metrics.mjs';
import * as capacityMetrics from '../../eng/load/cache-capacity/metrics.mjs';
import {guestMemory, memoryWithinBudget, imageIdFromInspect, collectorEndpoint, stageLevels, profileLevels, privateVolumeOwnerLabel} from '../../eng/load/cache-capacity/resource-budget.mjs';
import {eligibleSample, reusableSamples, sampleIdentityEvidence, copySampleIdentityEvidence} from '../../eng/load/cache-capacity/reuse.mjs';
import {writeControl} from '../../eng/load/cache-capacity/control-files.mjs';
import {validateDatabaseBudget, assertBudgetIdentity, verifyExperimentDatabaseBudget, verifyDatabaseAdmissionRoles} from '../../eng/load/cache-capacity/database-budget.mjs';
import {collectorDnsAlias, collectorOtlpEndpoint} from '../../eng/load/cache-capacity/resource-budget.mjs';

function identityFacts() {
  return ['cache-node-one', 'cache-node-two'].map((role, index) => {
    const instance = `01a15097-2d8b-704f-ad9c-40a80f81bd8${index}`;
    const announcement = JSON.stringify({Instance: instance, Application: 'Full.NET.Host.Api', HostRole: 'Api',
      SourceContext: 'Full.NET.Hosting.Observability.LoggingResourceAnnouncementService'});
    const logFileName = 'fullnet-native-aot-' + String(index + 1).repeat(32) + '.log';
    return {role, instance, application: 'Full.NET.Host.Api', context: 'SqlServer-C0', baseAddress: `http://127.0.0.1:${31001 + index}/`,
      logFile: '/work/artifacts/native-aot/linux-x64/test-logs/' + logFileName, logFileName,
      announcement, announcementSha256: createHash('sha256').update(announcement).digest('hex')};
  });
}

test('a real UUID sample round-trips through reuse with its original parser, identities and announcement logs', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'fullnet-cache-reuse-'));
  const digest = value => createHash('sha256').update(value).digest('hex');
  const write = (file, value) => fs.writeFileSync(path.join(directory, file), JSON.stringify(value));
  try {
    const context = 'SqlServer-C0', folder = context + '/01-closed-16', facts = identityFacts();
    fs.mkdirSync(path.join(directory, folder), {recursive: true}); fs.mkdirSync(path.join(directory, 'harness'));
    fs.mkdirSync(path.join(directory, 'native-aot/linux-x64/test-logs'), {recursive: true});
    fs.copyFileSync(new URL('../../eng/load/cache-capacity/metrics.mjs', import.meta.url), path.join(directory, 'harness/metrics.mjs'));
    write(context + '/api-identities.json', facts);
    for (const fact of facts) fs.writeFileSync(path.join(directory, 'native-aot/linux-x64/test-logs', fact.logFileName), fact.announcement + '\n');
    const sql = n => facts.map(f => `fullnet_data_sql_completed_operations_total{service_name="Full.NET.Host.Api",service_instance_id="${f.instance}",statement_name="settings.grid_preference.find_by_verified_user_and_grid",provider="sql_server",operation="query_single"} ${n}`).join('\n');
    const activeRaw = facts.map(f => `http_server_active_requests{service_name="Full.NET.Host.Api",service_instance_id="${f.instance}"} 2`).join('\n');
    const rawSummary = summary(), original = summarizeSample(rawSummary, 0, [readMetrics(activeRaw, facts, context)], readMetrics(sql(2), facts, context), readMetrics(sql(12), facts, context));
    assert.equal(original.classification, 'within-local-budget');
    const bytes = JSON.stringify(rawSummary), row = {context, folder, name: 'closed-16', model: 'closed', level: 16, duration: '60s', result: original, summarySha256: digest(bytes)};
    write(folder + '/summary.json', rawSummary);
    fs.writeFileSync(path.join(directory, folder, 'before.prom'), sql(2)); fs.writeFileSync(path.join(directory, folder, 'after.prom'), sql(12));
    fs.writeFileSync(path.join(directory, folder, 'metrics-samples.prom'), '# sampled_at actual\n' + activeRaw);
    write('k6-' + context + '-1-terminal.json', {observedViaLiveChildHandle: true, exitCode: 0});
    const helperHashes = {'metrics.mjs': digest(fs.readFileSync(path.join(directory, 'harness/metrics.mjs'))), 'scenario.js': 'same', 'database-budget.json': 'a'.repeat(64)};
    const recipe = {source: 'same-source', apiSha256: 'same-elf', sqlCounterMetric: 'fullnet_data_sql_completed_operations_total'};
    write('recipe.json', recipe); write('state.json', {pid: 123, source: recipe.source, cleanupConfirmed: true, acceptance: false, helperHashes, samples: [row]});
    write('parent.json', {Pid: 123, ObservedViaLiveProcessHandle: true, ExitCode: 1});
    const reusable = await reusableSamples(directory, recipe, helperHashes, path.join(directory, 'parent.json'));
    const sample = reusable.map.get(context + '/closed-16'); assert.ok(sample);
    const target = path.join(directory, 'destination'); fs.mkdirSync(target);
    const provenance = copySampleIdentityEvidence(sample.identityEvidence, target);
    const roundTrip = sampleIdentityEvidence(directory, {...row, folder: 'destination', reusedFrom: provenance});
    assert.deepEqual(readMetrics(sql(12), roundTrip.identities, context), readMetrics(sql(12), facts, context));
    assert.throws(() => readMetrics(sql(12).replace(facts[0].instance, '01a15097-2d8b-704f-ad9c-40a80f81bd99'), roundTrip.identities, context), /Unknown API/);
    fs.appendFileSync(path.join(target, 'producer-api-cache-node-one.log'), 'changed');
    assert.throws(() => sampleIdentityEvidence(directory, {...row, folder: 'destination', reusedFrom: provenance}));
  } finally { fs.rmSync(directory, {recursive: true, force: true}); }
});

test('telemetry binds actual process announcements to context roles while raw UUID samples remain unchanged', () => {
  const facts = identityFacts(), raw = facts.map(f => `http_server_active_requests{service_name="Full.NET.Host.Api",service_instance_id="${f.instance}"} 2`).join('\n');
  const rows = readMetrics(raw, facts, 'SqlServer-C0');
  assert.deepEqual(rows.map(apiRole), ['cache-node-one', 'cache-node-two']);
  assert.deepEqual(rows.map(r => r.observedServiceInstanceId), facts.map(f => f.instance));
  assert.ok(raw.includes(facts[0].instance));
  assert.equal(readMetrics(raw).map(apiRole).filter(Boolean).length, 0);
});

test('unknown, duplicated, cross-context and mismatched process identity evidence cannot claim a role', () => {
  const facts = identityFacts(), raw = `http_server_active_requests{service_name="Full.NET.Host.Api",service_instance_id="${facts[0].instance}"} 2`;
  for (const bad of [[facts[0]], [facts[0], facts[0]], [facts[0], {...facts[1], instance: facts[0].instance}],
    [facts[0], {...facts[1], context: 'MySql-C0'}], [facts[0], {...facts[1], application: 'Other'}],
    [facts[0], {...facts[1], announcementSha256: '0'.repeat(64)}],
    [facts[0], {...facts[1], baseAddress: facts[0].baseAddress}], [facts[0], {...facts[1], logFileName: facts[0].logFileName}]])
    assert.throws(() => readMetrics(raw, bad, 'SqlServer-C0'));
  assert.throws(() => readMetrics(raw, facts, 'MySql-C0'));
  assert.throws(() => readMetrics(raw.replace(facts[0].instance, '01a15097-2d8b-704f-ad9c-40a80f81bd99'), facts, 'SqlServer-C0'), /Unknown API/);
  assert.throws(() => readMetrics(raw.replace('Full.NET.Host.Api', 'Other'), facts, 'SqlServer-C0'), /identity mismatch/);
});

test('collector transport uses a bounded owned-network DNS alias instead of the 64-character resource name', () => {
  const longName = 'fullnet-cache-capacity-' + '12345678-1234-1234-1234-123456789012' + '-otel';
  assert.equal(longName.length, 64);
  const endpoint = new URL(collectorOtlpEndpoint);
  assert.equal(endpoint.hostname, collectorDnsAlias);
  assert.ok(collectorDnsAlias.length <= 63 && /^[a-z][a-z0-9-]*$/u.test(collectorDnsAlias));
  assert.equal(endpoint.port, '4317');
});

const databaseBudget = JSON.parse(fs.readFileSync(new URL('../../eng/load/cache-capacity/database-budget.json', import.meta.url)));
test('private build volume ownership accepts only the original and isolated authentication candidate labels', () => {
  assert.equal(privateVolumeOwnerLabel(), 'fullnet.cache-sdk402.owner');
  for (const label of ['fullnet.cache-sdk402.owner', 'fullnet.cache-auth.owner'])
    assert.equal(privateVolumeOwnerLabel(label), label);
  for (const label of ['', null, {}, 'fullnet.r08h.owner', 'foreign.owner', 'fullnet.cache-auth.owner"}}'])
    assert.throws(() => privateVolumeOwnerLabel(label), /Unknown private/);
});
test('two-node database pools and permits fit both the per-process and declared server budgets', () => {
  assert.deepEqual(validateDatabaseBudget(databaseBudget), {processReserved: 40, clusterReserved: 110, apiPoolsMaximum: 80});
  for (const override of [{Enabled: false}, {PermitLimit: 40}, {ApiMaxPoolSize: 100, ExpectedMaxPoolSize: 100},
    {ServerConnectionLimit: 109}, {TotalBudget: 100}, {QueueLimit: 1001}, {AcquireTimeoutMilliseconds: NaN}])
    assert.throws(() => validateDatabaseBudget({...databaseBudget, ...override}));
});
test('budgeted profiles reject unbudgeted or differently budgeted sample reuse', () => {
  const expected = 'a'.repeat(64);
  assertBudgetIdentity(expected, expected);
  assert.throws(() => assertBudgetIdentity(undefined, expected));
  assert.throws(() => assertBudgetIdentity('b'.repeat(64), expected));
  const experiment = {databaseBudget: {profileSha256: expected, profile: databaseBudget, apiConnectionMaximumPoolSize: 40}};
  verifyExperimentDatabaseBudget(experiment, databaseBudget, expected);
  assert.throws(() => verifyExperimentDatabaseBudget({}, databaseBudget, expected));
  assert.throws(() => verifyExperimentDatabaseBudget({databaseBudget: {...experiment.databaseBudget,
    apiConnectionMaximumPoolSize: 100}}, databaseBudget, expected));
});

const metric = (name, value, instance = 'cache-node-one', labels = {}) => ({name, value,
  labels: {service_name: 'Full.NET.Host.Api', service_instance_id: instance, ...labels}});
const legacyBefore = [metric('fullnet_data_sql_executions_total', 2, undefined,
  {statement_name: 'settings.grid_preference.find_by_verified_user_and_grid'})];
const legacyAfter = [metric('fullnet_data_sql_executions_total', 12, undefined,
  {statement_name: 'settings.grid_preference.find_by_verified_user_and_grid'})];
const active = [[metric('http_server_active_requests', 3), metric('http_server_active_requests', 5, 'cache-node-two')]];

const completedMetric = 'fullnet_data_sql_completed_operations_total';
const completedRows = count => ['cache-node-one', 'cache-node-two'].map(role =>
  metric(completedMetric, count, role, {statement_name: 'settings.grid_preference.find_by_verified_user_and_grid',
    provider: 'my_sql', operation: 'query_single'}));

const before = completedRows(2), after = completedRows(12);

test('stable SQL totals retain first failure details as optional without inventing a baseline', () => {
  const newFailure = metric('fullnet_data_sql_executions_total', 1, undefined,
    {statement_name: 'settings.grid_preference.find_by_verified_user_and_grid', outcome: 'failure', failure_reason: 'application_error'});
  const result = summarizeSample(summary(), 0, active, [...legacyBefore, ...before],
    [...legacyAfter, newFailure, ...after], completedMetric);
  assert.equal(result.classification, 'within-local-budget');
  assert.equal(result.sqlCounterMetric, completedMetric);
  assert.equal(result.counterDeltas[completedMetric], 20);
  assert.equal(result.gridSqlExecutions, 20);
  assert.equal(result.counterDeltas.fullnet_data_sql_executions_total, null);
  assert.deepEqual(result.unknown, []);
  assert.ok(result.unavailableOptionalMetrics.includes('fullnet_data_sql_executions_total: Missing counter baseline'));
  const overloaded = summary('open'); overloaded.metrics.dropped_iterations.values.count = 1;
  assert.equal(summarizeSample(overloaded, 99, active, completedRows(2), completedRows(12), completedMetric).classification, 'budget-exceeded');
});

test('stable SQL totals reject missing nodes, new statements, resets, disappearing and outcome-split series', () => {
  for (const [prior, current] of [[[], completedRows(12)], [completedRows(2), []],
    [completedRows(2).slice(0, 1), completedRows(12).slice(0, 1)],
    [completedRows(12), completedRows(2)],
    [completedRows(2), [...completedRows(12), metric(completedMetric, 1, undefined, {statement_name: 'new'})]],
    [[...completedRows(2), metric(completedMetric, 1, undefined, {statement_name: 'gone'})], completedRows(12)],
    [completedRows(2), [...completedRows(12), completedRows(12)[0]]],
    [completedRows(2), completedRows(12).map(r => ({...r, labels: {...r.labels, outcome: 'success'}}))]]) {
    const result = summarizeSample(summary(), 0, active, prior, current, completedMetric);
    assert.equal(result.classification, 'Incomplete');
    assert.equal(result.counterDeltas[completedMetric], null);
    assert.ok(result.unknown.length > 0);
  }
  for (const name of ['', null, 'unknown', 'fullnet_data_sql_failures_total', 'fullnet_data_sql_executions_total'])
    assert.throws(() => summarizeSample(summary(), 0, active, before, after, name), /SQL counter/);
});

test('Grid readiness uses the explicitly selected SQL instrument and both node identities', () => {
  assert.deepEqual(missingGridSqlRoles(completedRows(2), completedMetric), []);
  assert.deepEqual(missingGridSqlRoles(legacyBefore, completedMetric), ['cache-node-one', 'cache-node-two']);
  assert.deepEqual(missingGridSqlRoles(completedRows(2).slice(0, 1), completedMetric), ['cache-node-two']);
});
test('atomic control replacement tolerates bounded sharing contention without deleting the visible command', async () => {
  const operations = []; let attempts = 0;
  const io = {writeFile: async (...args) => operations.push(['write', ...args]), rename: async (...args) => {
    operations.push(['rename', ...args]); if (++attempts < 3) throw Object.assign(Error('sharing'), {code: 'EPERM'});
  }};
  await writeControl('command.json', {id: 'new'}, io, async () => operations.push(['wait']));
  assert.equal(attempts, 3); assert.equal(operations.filter(r => r[0] === 'write').length, 1);
  assert.deepEqual(JSON.parse(operations[0][2]), {id: 'new'});
  for (const code of ['EPERM', 'ENOENT']) {
    let count = 0;
    await assert.rejects(writeControl('command.json', {}, {writeFile: async () => {}, rename: async () => {
      count++; throw Object.assign(Error(code), {code});
    }}, async () => {}), new RegExp(code));
    assert.equal(count, code === 'EPERM' ? 20 : 1);
  }
});
function summary(model = 'closed') {
  return {model, metrics: {
    http_reqs: {values: {count: 100, rate: 10}},
    fullnet_capacity_errors: {values: {rate: 0}, thresholds: {'rate<0.01': {ok: true}}},
    fullnet_capacity_payload_errors: {values: {rate: 0}},
    fullnet_capacity_authorization_errors: {values: {count: 0}},
    fullnet_capacity_valid_responses: {values: {count: 100, rate: 10}},
    fullnet_capacity_rate_limit_responses: {values: {count: 0}},
    fullnet_capacity_server_errors: {values: {count: 0}},
    fullnet_capacity_transport_errors: {values: {count: 0}},
    http_req_duration: {values: {'p(50)': 2, 'p(95)': 10, 'p(99)': 20}},
    ...(model === 'open' ? {dropped_iterations: {values: {count: 0}}} : {}),
  }};
}
test('memory budget checks host and reusable guest memory separately and fails closed on missing samples', () => {
  const guest = guestMemory('MemTotal: 20971520 kB\nMemAvailable: 17825792 kB\nCached: 8388608 kB\nSwapFree: 5242880 kB');
  const host = {FreePhysicalBytes: 6 * 1024 ** 3, FreeVirtualBytes: 6 * 1024 ** 3, Guest: guest};
  assert.equal(memoryWithinBudget(host, 'start'), true);
  assert.equal(memoryWithinBudget({...host, Guest: {MemAvailable: 12 * 1024 ** 3}}, 'start'), false);
  assert.equal(memoryWithinBudget({...host, FreeVirtualBytes: .5 * 1024 ** 3}, 'running'), false);
  assert.equal(memoryWithinBudget({...host, FreeVirtualBytes: 4 * 1024 ** 3}, 'start'), false);
  assert.equal(memoryWithinBudget({...host, Guest: {}}, 'running'), false);
  assert.throws(() => guestMemory('MemTotal: 20971520 kB'), /Missing/);
  assert.throws(() => guestMemory('MemTotal: 20971520 kB\nMemTotal: 20971520 kB'), /Duplicate/);
});
test('image inspection consumes command JSON directly and requires a unique immutable identity', () => {
  const expected = 'sha256:' + 'a'.repeat(64);
  assert.equal(imageIdFromInspect(JSON.stringify([{Id: expected, RepoTags: ['fixture:1']}])) , expected);
  for (const text of ['[]', '{}', '[{"Id":"fixture:latest"}]', 'not-json']) assert.throws(() => imageIdFromInspect(text));
});
test('a diagnosed provider boundary only narrows later profiles and never silently raises the original load', () => {
  assert.deepEqual(stageLevels('closed', 64), [1, 16, 64]);
  assert.deepEqual(stageLevels('open', 500), [50, 200, 500]);
  assert.deepEqual(stageLevels('closed'), [1, 16, 64, 128]);
  assert.deepEqual(stageLevels('open'), [50, 200, 500, 1000]);
  for (const ceiling of [0, 65, 129, 10000]) assert.throws(() => stageLevels('closed', ceiling));
  assert.throws(() => stageLevels('unknown', 64));
});
test('connection budget selection covers diagnosed levels and neighbors without rerunning unrelated low levels', () => {
  assert.deepEqual(profileLevels('MySql', 'closed', 'connection-budget-recheck'), [64, 128]);
  assert.deepEqual(profileLevels('MySql', 'open', 'connection-budget-recheck'), [200, 500]);
  assert.deepEqual(profileLevels('SqlServer', 'closed', 'connection-budget-recheck'), [64]);
  assert.deepEqual(profileLevels('SqlServer', 'open', 'connection-budget-recheck'), [500]);
  assert.deepEqual(profileLevels('MySql', 'open', 'connection-budget-recheck', 200), [200]);
  assert.throws(() => profileLevels('MySql', 'open', 'connection-budget-recheck', 50));
  assert.throws(() => profileLevels('MySql', 'open', 'unknown'));
});
test('budget evidence requires both API admission identities and rejects the old unspecified role', () => {
  const rows = ['cache-node-one', 'cache-node-two'].map(role => metric('fullnet_db_connection_admission_in_use', 0, role, {host_role: 'api'}));
  verifyDatabaseAdmissionRoles(rows);
  assert.throws(() => verifyDatabaseAdmissionRoles(rows.slice(0, 1)));
  assert.throws(() => verifyDatabaseAdmissionRoles(rows.map(r => ({...r, labels: {...r.labels, host_role: 'unknown'}}))));
});
test('Collector endpoint resolves the current loopback mapping and rejects ambiguous or remote bindings', () => {
  assert.equal(collectorEndpoint('127.0.0.1:52464\n'), 'http://127.0.0.1:52464/metrics');
  assert.notEqual(collectorEndpoint('127.0.0.1:52464'), collectorEndpoint('127.0.0.1:52465'));
  for (const value of ['0.0.0.0:52464', '127.0.0.1:0', '127.0.0.1:65536', '127.0.0.1:1\n127.0.0.1:2'])
    assert.throws(() => collectorEndpoint(value));
});
test('sample reuse requires the same profile and real safe verdict, never expired-token or resource-stopped runs', () => {
  const row = {context: 'SqlServer-S2', name: 'closed-16', model: 'closed', level: 16, duration: '60s',
    result: {sqlCounterMetric: completedMetric, classification: 'within-local-budget', exitCode: 0, authorizationErrors: 0, payloadErrorRate: 0}};
  assert.equal(eligibleSample(row, row), true);
  assert.equal(eligibleSample({...row, result: {...row.result, classification: 'budget-exceeded', exitCode: 99}}, row), true);
  assert.equal(eligibleSample(row, {...row, level: 64}), false);
  assert.equal(eligibleSample({...row, safetyViolation: {at: 'fixture'}}, row), false);
  assert.equal(eligibleSample({...row, name: 'warmup'}, {...row, name: 'warmup'}), false);
  for (const result of [{...row.result, authorizationErrors: 1}, {...row.result, payloadErrorRate: .1},
    {...row.result, exitCode: null}, {...row.result, classification: 'Incomplete'}])
    assert.equal(eligibleSample({...row, result}, row), false);
});
test('readiness requires stable Grid counters on both nodes and rejects a late node baseline', () => {
  assert.deepEqual(missingGridSqlRoles(before), []);
  assert.deepEqual(missingGridSqlRoles(before.slice(0, 1)), ['cache-node-two']);
  for (const value of [0, -1, NaN, Infinity])
    assert.deepEqual(missingGridSqlRoles([before[0], {...before[1], value}]), ['cache-node-two']);
  const result = summarizeSample(summary(), 0, active, before.slice(0, 1), after);
  assert.equal(result.classification, 'Incomplete');
  assert.ok(result.unknown.includes('Grid statement delta'));
});

test('capacity derives actual sampled in-flight from both API gauges, never VUs', () => {
  const result = summarizeSample({...summary(), vus: 10000}, 0, active, before, after);
  assert.equal(result.classification, 'within-local-budget');
  assert.equal(result.sampledApiActivePeak, 8);
  assert.match(result.activeScope, /not a simultaneous global peak/);
  assert.equal(result.gridSqlExecutions, 20);
  assert.equal(result.capacityStatus, 'Capacity-not-verified');
  const missing = summarizeSample(summary(), 0, [], before, after);
  assert.equal(missing.classification, 'Incomplete'); assert.equal(missing.sampledApiActivePeak, null);
  assert.equal(summarizeSample(summary(), 0, [[active[0][0]]], before, after).classification, 'Incomplete');
});
test('capacity preserves real failed/unknown exits, zero traffic and missing open-loop drop evidence', () => {
  assert.equal(summarizeSample(summary(), null, active, before, after).classification, 'Incomplete');
  assert.equal(summarizeSample(summary(), 1, active, before, after).classification, 'Incomplete');
  assert.equal(summarizeSample(summary(), 99, active, before, after).classification, 'budget-exceeded');
  const zero = summary(); zero.metrics.http_reqs.values.count = 0;
  assert.equal(summarizeSample(zero, 0, active, before, after).classification, 'Incomplete');
  const open = summary('open'); delete open.metrics.dropped_iterations;
  const result = summarizeSample(open, 0, active, before, after);
  assert.equal(result.classification, 'Incomplete'); assert.equal(result.droppedIterations, null);
  const dropped = summary('open'); dropped.metrics.dropped_iterations.values.count = 2;
  assert.equal(summarizeSample(dropped, 99, active, before, after).classification, 'budget-exceeded');
});
test('capacity treats malformed success and lost authorization as correctness failures', () => {
  for (const [name, field] of [['fullnet_capacity_payload_errors', 'rate'], ['fullnet_capacity_authorization_errors', 'count']]) {
    const value = summary(); value.metrics[name].values[field] = 1;
    assert.equal(summarizeSample(value, 99, active, before, after).classification, 'correctness-failure');
  }
  const slow = summary(); slow.metrics.http_req_duration.values['p(99)'] = 1001;
  assert.equal(summarizeSample(slow, 99, active, before, after).classification, 'budget-exceeded');
});
test('capacity separates rejected HTTP throughput from successful business throughput', () => {
  const s = summary();
  s.metrics.fullnet_capacity_errors.values.rate = .9;
  s.metrics.fullnet_capacity_valid_responses.values = {count: 10, rate: 1};
  s.metrics.fullnet_capacity_rate_limit_responses.values.count = 90;
  const result = summarizeSample(s, 99, active, before, after);
  assert.equal(result.classification, 'budget-exceeded');
  assert.equal(result.throughputRps, 10); assert.equal(result.successfulThroughputRps, 1);
  assert.equal(result.rateLimitedResponses, 90);
  assert.ok(result.budgetFailureKinds.includes('rate-limit'));
});
test('metric parser rejects invalid labels, resets and resource identity collisions', () => {
  assert.equal(readMetrics('metric{label="a\\\"b"} 3')[0].labels.label, 'a"b');
  assert.throws(() => readMetrics('metric{label="a",label="b"} 3'), /Duplicate/);
  assert.throws(() => counterDelta(after, before, completedMetric), /reset/);
  assert.throws(() => counterDelta([], after, completedMetric), /baseline/);
  const collision = [[{...active[0][0], labels: {...active[0][0].labels, service_name: 'Full.NET.Host.Worker'}}]];
  assert.throws(() => summarizeSample(summary(), 0, collision, before, after), /identity/);
});

test('current collector never falls back to legacy SQL totals or reuses their verdicts', () => {
  assert.equal(capacityMetrics.selectSqlCounter(), completedMetric);
  const result = summarizeSample(summary(), 0, active, legacyBefore, legacyAfter);
  assert.equal(result.classification, 'Incomplete');
  assert.equal(result.counterDeltas[completedMetric], null);
  const sample = {context: 'MySql-S2', name: 'closed-16', model: 'closed', level: 16, duration: '60s',
    result: {classification: 'within-local-budget', exitCode: 0, authorizationErrors: 0, payloadErrorRate: 0}};
  assert.equal(eligibleSample(sample, sample), false);
});

test('lifecycle evidence requires both real provider identities and zero skipped/failed cases', () => {
  const discovery = JSON.stringify({tests: [{uid: 'one', provider: 'SqlServer'}, {uid: 'two', provider: 'MySql'}]});
  const trx = '<UnitTestResult testId="one" outcome="Passed"/><UnitTestResult testId="two" outcome="Passed"/>'
    + '<Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="0" notRunnable="0" disconnected="0"/>';
  assert.equal(verifyLifecycle(discovery, trx).counters.passed, 2);
  assert.throws(() => verifyLifecycle(discovery, trx.replace('testId="two"', 'testId="other"')), /identities/);
  assert.throws(() => verifyLifecycle(discovery, trx.replace('notExecuted="0"', 'notExecuted="1"')), /counters/);
  assert.throws(() => verifyLifecycle(discovery, trx.replace('outcome="Passed"', 'outcome="Failed"')), /identities/);
});
function scenario(status, value = {gridKey: 'identity.users', schemaVersion: 1, version: 0, columns: []}, env = {}) {
  const code = fs.readFileSync(new URL('../../eng/load/cache-capacity/scenario.js', import.meta.url), 'utf8')
    .replace(/import\s+[^;]+;\s*/g, '').replace('export default function', 'function run')
    .replaceAll('export const ', 'const ').replaceAll('export function ', 'function ');
  const measurements = {}, urls = [];
  function metric(name) { this.add = v => (measurements[name] ??= []).push(v); }
  const api = vm.runInNewContext(code + '\n;({run, options, handleSummary})', {
    __ENV: {MODEL: 'closed', LEVEL: '16', URL_0: 'http://node0', URL_1: 'http://node1', TOKEN_0: 'test0', TOKEN_1: 'test1', ...env},
    __VU: 1, __ITER: 0, Rate: metric, Counter: metric,
    check: (response, checks) => Object.values(checks).every(f => f(response)),
    http: {get: (url, options) => { urls.push({url, options}); return {status, json: () => value}; }},
  });
  return {...api, measurements, urls};
}
test('capacity scenario checks status, real payload and authorization without synthetic active/dropped metrics', () => {
  for (const status of [0, 401, 403, 429, 500]) {
    const s = scenario(status); s.run();
    assert.deepEqual(s.measurements.fullnet_capacity_errors, [true]);
    assert.deepEqual(s.measurements.fullnet_capacity_authorization_errors, [status === 401 || status === 403 ? 1 : 0]);
    assert.deepEqual(s.measurements.fullnet_capacity_rate_limit_responses, [status === 429 ? 1 : 0]);
    assert.deepEqual(s.measurements.fullnet_capacity_server_errors, [status >= 500 ? 1 : 0]);
    assert.deepEqual(s.measurements.fullnet_capacity_transport_errors, [status === 0 ? 1 : 0]);
  }
  const invalid = scenario(200, {}); invalid.run(); assert.deepEqual(invalid.measurements.fullnet_capacity_payload_errors, [true]);
  const valid = scenario(200); valid.run(); assert.deepEqual(valid.measurements.fullnet_capacity_errors, [false]);
  assert.deepEqual(valid.measurements.fullnet_capacity_valid_responses, [1]);
  assert.deepEqual(Object.keys(valid.measurements).sort(), ['fullnet_capacity_authorization_errors', 'fullnet_capacity_errors', 'fullnet_capacity_payload_errors',
    'fullnet_capacity_rate_limit_responses', 'fullnet_capacity_server_errors', 'fullnet_capacity_transport_errors', 'fullnet_capacity_valid_responses']);
  const open = scenario(200, undefined, {MODEL: 'open', LEVEL: '50'});
  assert.equal(open.options.scenarios.capacity.executor, 'constant-arrival-rate');
  assert.equal(open.options.scenarios.capacity.rate, 50);
  assert.throws(() => scenario(200, undefined, {LEVEL: '10000'}), /bounded/);
});

// 正常完整构建允许全新私有卷；严格拒绝旧引用模式与错误消费者复用。
{
const file=new URL('../../eng/load/cache-capacity/run.mjs',import.meta.url);
const text=fs.readFileSync(file,'utf8');
const code=text.slice(text.indexOf('if (c.consumerReuse) {'),text.indexOf('const state = {pid:'));
function validate(c,files={}) {
 const reads=[];
 vm.runInNewContext(code,{c,path,helperHashes:{},fs:{readFileSync(file){file=file.replaceAll('\\','/');reads.push(file);if(!(file in files))throw Error('Unexpected previous-build read: '+file);return files[file];}}});
 return reads;
}
test('fresh normal full-reference build needs no previous capacity producer',()=>{
 assert.deepEqual(validate({source:'current',volume:'new',volumeOwner:'own'}),[]);
});
test('obsolete reference-only receipt is refused rather than relabelled as a full build',()=>{
 assert.throws(()=>validate({referenceReuse:{state:'forged'}}),/Reference-only reuse is unsupported/);
});
test('consumer reuse still rejects a different source or volume before reading successful exits',()=>{
 assert.throws(()=>validate({source:'current',volume:'new',volumeOwner:'own',consumerReuse:{state:'/old/state.json',buildExit:'/old/build.exit'}},{
 '/old/state.json':JSON.stringify({source:'old',cleanupConfirmed:true}),
 '/old/recipe.json':JSON.stringify({volume:'new',volumeOwner:'own'})
 }),/Reused consumer source\/volume provenance mismatch/);
});

}

const logEvidenceSha=bytes=>createHash('sha256').update(bytes).digest('hex');
test('bounded log proof hashes all bytes and counts exact UTF8 announcements across chunk and CRLF boundaries',()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'fullnet-log-evidence-')),file=path.join(dir,'raw.log'),announcement='{"Application":"Full.NET.Host.Api","message":"实例启动"}',bytes=Buffer.from('x'.repeat(1024*1024)+'\n'+announcement+'\r\nnear:'+announcement+'\n'+announcement);
 try{fs.writeFileSync(file,bytes);for(const chunk of [1,17,64*1024])assert.deepEqual(logEvidence(file,announcement,chunk),{sha256:logEvidenceSha(bytes),occurrences:2,bytes:bytes.length});assert.deepEqual(logEvidence(file),{sha256:logEvidenceSha(bytes),occurrences:0,bytes:bytes.length});}
 finally{fs.unlinkSync(file);fs.rmdirSync(dir);}
});
test('missing, duplicate, partial and unterminated CR lines retain exact whole-line semantics',()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'fullnet-log-evidence-')),file=path.join(dir,'raw.log'),announcement='{"instance":"测试"}';
 try{for(const text of ['',announcement+'\r',announcement+'x\n','x'+announcement+'\n']){fs.writeFileSync(file,text);assert.equal(logEvidence(file,announcement,3).occurrences,0);}fs.writeFileSync(file,announcement+'\r\n'+announcement+'\n');assert.equal(logEvidence(file,announcement,3).occurrences,2);assert.throws(()=>logEvidence(file,announcement,0));}
 finally{fs.unlinkSync(file);fs.rmdirSync(dir);}
});
