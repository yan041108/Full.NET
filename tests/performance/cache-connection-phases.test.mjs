import assert from 'node:assert/strict';
import test from 'node:test';
import {connectionPhaseTimings, sqlCommandTimings} from '../../eng/load/cache-capacity/metrics.mjs';

function sample(count, sum, extra = {}) {
  return ['cache-node-one', 'cache-node-two'].flatMap(node => ['count', 'sum'].map(suffix => ({
    name: 'fullnet_db_connection_phase_duration_seconds_' + suffix,
    value: suffix === 'count' ? count : sum,
    labels: {service_name: 'Full.NET.Host.Api', service_instance_id: node, provider: 'mysql',
      host_role: 'api', phase: 'open', access_mode: 'authorization', ...extra},
  })));
}

test('connection phases retain per-node access mode and compute only an interval mean', () => {
  const result = connectionPhaseTimings(sample(4, 2), sample(6, 2.3));
  assert.equal(result.status, 'complete');
  assert.equal(result.rows.length, 2);
  for (const row of result.rows) {
    assert.equal(row.count, 2);
    assert.ok(Math.abs(row.meanMilliseconds - 150) < 1e-8);
    assert.equal(row.accessMode, 'authorization');
    assert.equal(row.phase, 'open');
    assert.equal(row.p99, undefined);
  }
  assert.equal(connectionPhaseTimings(sample(4, 2), sample(4, 2)).rows[0].meanMilliseconds, null);
});

test('connection phase omissions, resets and inconsistent moments are never filled with zero', () => {
  assert.equal(connectionPhaseTimings([], []).status, 'unavailable');
  for (const after of [sample(3, 2.3), sample(6, 1), sample(4, 2.3), sample(4.5, 2.3),
    sample(6, NaN), sample(6, Number.MAX_VALUE), sample(6, 2.3).slice(1), [...sample(6, 2.3), sample(6, 2.3)[0]],
    sample(6, 2.3, {phase: 'unexpected'}), sample(6, 2.3, {outcome: 'failure'})]) {
    const result = connectionPhaseTimings(sample(4, 2), after);
    assert.equal(result.status, 'incomplete');
    assert.ok(result.rows.some(row => row.meanMilliseconds === null));
    assert.ok(result.unknown.length > 0);
  }
  const oneNode = sample(4, 2).slice(0, 2);
  assert.equal(connectionPhaseTimings(oneNode, oneNode).status, 'incomplete');
  assert.equal(connectionPhaseTimings([], sample(1, 0.1)).status, 'incomplete');
});

test('distinct access modes and phases remain separate and disappeared series are reported', () => {
  const before = [...sample(4, 2), ...sample(5, 3, {phase: 'admission', access_mode: 'normal'})];
  const after = [...sample(6, 2.3), ...sample(8, 3.6, {phase: 'admission', access_mode: 'normal'})];
  const result = connectionPhaseTimings(before, after);
  assert.equal(result.rows.length, 4);
  assert.equal(result.status, 'complete');
  assert.ok(Math.abs(result.rows.find(row => row.phase === 'admission').meanMilliseconds - 200) < 1e-8);
  assert.equal(connectionPhaseTimings(before, sample(6, 2.3)).status, 'incomplete');
});

const sqlSample = (count, sum) => sample(count, sum).map(row => ({
  ...row, name: row.name.replace('fullnet_db_connection_phase_duration_seconds', 'fullnet_data_sql_command_duration_milliseconds'),
  labels: {service_instance_id: row.labels.service_instance_id, service_name: row.labels.service_name,
    provider: 'my_sql', operation: 'query_single', statement_name: 'identity.find_refresh_session_by_id'},
}));

test('SQL command timing preserves milliseconds and statement identity separately from acquisition totals', () => {
  const result = sqlCommandTimings(sqlSample(4, 300), sqlSample(6, 350));
  assert.equal(result.status, 'complete');
  assert.equal(result.rows.length, 2);
  assert.equal(result.rows[0].meanMilliseconds, 25);
  assert.equal(result.rows[0].totalMilliseconds, 50);
  assert.equal(result.rows[0].statementName, 'identity.find_refresh_session_by_id');
  assert.equal(sqlCommandTimings([], []).status, 'unavailable');
  for (const after of [sqlSample(3, 350), sqlSample(6, 250), sqlSample(4, 350), sqlSample(6, NaN),
    sqlSample(6, 350).slice(1), [...sqlSample(6, 350), sqlSample(6, 350)[0]]])
    assert.equal(sqlCommandTimings(sqlSample(4, 300), after).status, 'incomplete');
  assert.equal(sqlCommandTimings([], sqlSample(1, 1)).status, 'incomplete');
  assert.equal(sqlCommandTimings(sqlSample(4, 300), sqlSample(4, 300)).rows[0].meanMilliseconds, null);
});
