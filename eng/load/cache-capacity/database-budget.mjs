import assert from 'node:assert/strict';
import {apiRole} from './metrics.mjs';

// 与生产相同的每 API 池/许可预算；额外声明一个未运行 Worker 的池，保守预留运维空间。
export function validateDatabaseBudget(profile) {
  assert.equal(profile.Profile, 'two-api-pool40-v1'); assert.equal(profile.Version, 1);
  assert.equal(profile.Enabled, true); assert.equal(profile.HostRole, 1);
  const nonnegative = ['QueueLimit', 'HealthReserve', 'CriticalWorkerReserve', 'AuthorizationReadReserve', 'MigrationReserve'];
  const positive = ['ServerConnectionLimit', 'PermitLimit', 'AcquireTimeoutMilliseconds', 'ExpectedMaxPoolSize',
    'ApiMaxReplicas', 'ApiMaxPoolSize', 'WorkerMaxReplicas', 'WorkerMaxPoolSize', 'TotalBudget'];
  for (const key of [...positive, ...nonnegative])
    assert.ok(Number.isSafeInteger(profile[key]) && profile[key] >= (positive.includes(key) ? 1 : 0), key);
  assert.equal(profile.ApiMaxReplicas, 2); assert.equal(profile.ApiMaxPoolSize, 40);
  assert.equal(profile.ApiMaxPoolSize, profile.ExpectedMaxPoolSize);
  assert.ok(profile.QueueLimit <= 1000 && profile.AcquireTimeoutMilliseconds <= 60000);
  const process = profile.PermitLimit + profile.HealthReserve + profile.CriticalWorkerReserve + profile.AuthorizationReadReserve;
  const cluster = profile.ApiMaxReplicas * profile.ApiMaxPoolSize + profile.WorkerMaxReplicas * profile.WorkerMaxPoolSize + profile.MigrationReserve;
  assert.ok(process <= profile.ExpectedMaxPoolSize, 'Process permits exceed pool');
  assert.ok(cluster <= profile.TotalBudget, 'Cluster pools exceed total budget');
  assert.ok(profile.TotalBudget <= profile.ServerConnectionLimit, 'Budget exceeds declared server limit');
  return {processReserved: process, clusterReserved: cluster, apiPoolsMaximum: profile.ApiMaxReplicas * profile.ApiMaxPoolSize};
}

export function assertBudgetIdentity(previous, current) {
  assert.match(current ?? '', /^[a-f0-9]{64}$/, 'Missing current database budget identity');
  assert.equal(previous, current, 'Database budget changed or missing; unbudgeted samples cannot be reused');
}

export function verifyExperimentDatabaseBudget(experiment, profile, expectedHash) {
  validateDatabaseBudget(profile);
  assertBudgetIdentity(experiment.databaseBudget?.profileSha256, expectedHash);
  assert.deepEqual(experiment.databaseBudget.profile, profile);
  assert.equal(experiment.databaseBudget.apiConnectionMaximumPoolSize, profile.ExpectedMaxPoolSize);
}

export function verifyDatabaseAdmissionRoles(rows) {
  const gauges = rows.filter(r => r.name === 'fullnet_db_connection_admission_in_use' && apiRole(r));
  assert.equal(new Set(gauges.map(apiRole)).size, 2, 'Both API admission gauges are required');
  assert.ok(gauges.every(r => r.labels.host_role === 'api' && Number.isFinite(r.value) && r.value >= 0), 'Expected API admission role');
}
