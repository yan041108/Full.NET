import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

const profiles = ['2k', '5k', '10k', 'soak'];
const scenarios = [
  'read-heavy',
  'mixed-write',
  'cache-recovery',
  'audit-logging',
  'outbox-jobs-backlog',
];

const evidenceKeys = [
  'application_metrics',
  'load_generator_metrics',
  'pod_metrics',
  'node_metrics',
  'database_metrics',
  'redis_cache_metrics',
  'redis_realtime_metrics',
  's3_metrics',
  'collector_metrics',
  'actual_active_requests',
  'arrival_rate_dropped_iterations',
  'threadpool_queue_thread_count',
  'allocation_rate',
  'gc_pause_gen2',
  'socket_httpclient',
  'db_connection_pool_wait',
  'log_audit_worker_backlog',
  'image_digest',
  'git_sha',
  'helm_values',
  'hardware',
  'database_parameters',
  'redis_parameters',
  'data_scale',
  'load_model',
  'raw_results_uri',
];

test('capacity harness files exist', async () => {
  const files = [
    'eng/load/k6/lib/config.js',
    'eng/load/k6/lib/metrics.js',
    'eng/load/validate-profiles.mjs',
    'eng/load/README.md',
    'deploy/load/k6-test-run.yaml',
    'docs/verification/high-concurrency-capacity-certification-template.md',
    ...profiles.map((name) => `eng/load/profiles/${name}.json`),
    ...scenarios.map((name) => `eng/load/k6/scenarios/${name}.js`),
  ];
  for (const file of files) {
    assert.equal(await exists(file), true, `${file} missing`);
  }
});

test('profiles keep Capacity-not-verified and never equate VU to in-flight', async () => {
  for (const name of profiles) {
    const profile = JSON.parse(await read(`eng/load/profiles/${name}.json`));
    assert.equal(profile.capacityStatus, 'Capacity-not-verified');
    assert.equal(profile.treatVuAsActualInFlight, false);
    assert.deepEqual(profile.executionOrderGate, ['2k', '5k', '10k', 'soak']);
    assert.ok(profile.closedLoop, `${name} missing closedLoop`);
    assert.ok(profile.openLoop, `${name} missing openLoop`);
    assert.ok(profile.providers.includes('SqlServer'));
    assert.ok(profile.providers.includes('MySql'));
    for (const scenario of scenarios) {
      assert.ok(profile.scenariosRequired.includes(scenario));
    }
  }

  const tenK = JSON.parse(await read('eng/load/profiles/10k.json'));
  assert.equal(tenK.targetInFlight, 10000);
});

test('metrics and certification template require Incomplete when evidence missing', async () => {
  const metrics = await read('eng/load/k6/lib/metrics.js');
  for (const key of evidenceKeys) {
    assert.ok(metrics.includes(key), `metrics.js missing ${key}`);
  }
  assert.ok(metrics.includes("status: 'Incomplete'"));

  const template = await read(
    'docs/verification/high-concurrency-capacity-certification-template.md'
  );
  assert.ok(template.includes('Capacity-not-verified'));
  assert.ok(template.includes('Incomplete'));
  assert.ok(template.includes('actual_active_requests'));
  assert.ok(template.includes('2K → 5K → 10K → Soak') || template.includes('2K -> 5K -> 10K -> Soak'));
});

test('k6 TestRun stays out of ordinary CI and marks Capacity-not-verified', async () => {
  const yaml = await read('deploy/load/k6-test-run.yaml');
  assert.ok(yaml.includes('Capacity-not-verified'));
  assert.ok(yaml.includes('dedicated capacity'));
  assert.ok(!yaml.toLowerCase().includes('github.com/actions'));

  const packageJson = JSON.parse(await read('package.json'));
  assert.equal(
    packageJson.scripts['test:load-profiles'],
    'node eng/load/validate-profiles.mjs && node --test tests/performance/load-profile-contract.test.mjs'
  );
});

test('validate-profiles.mjs exits 0 for current profiles', () => {
  const result = spawnSync(
    process.execPath,
    [path.join(repositoryRoot, 'eng/load/validate-profiles.mjs')],
    { encoding: 'utf8' }
  );
  assert.equal(result.status, 0, result.stderr || result.stdout);
  assert.match(result.stdout, /VU!=in-flight/);
});

test('roadmap retains Capacity-not-verified for 10K certification', async () => {
  const roadmap = await read('docs/roadmap/capability-status.md');
  assert.ok(roadmap.includes('Capacity-not-verified'));
  assert.ok(roadmap.includes('eng/load'));
});
