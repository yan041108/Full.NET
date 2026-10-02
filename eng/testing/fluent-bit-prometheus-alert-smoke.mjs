import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { appendFile, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fluentBitImage = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const prometheusImage = 'prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e';
const suffix = `${process.pid}-${randomUUID().slice(0, 8)}`;
const prometheusContainer = `fullnet-log-alert-prom-${suffix}`;
const fluentBitContainer = `fullnet-log-alert-bit-${suffix}`;

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function ensureImage(image) {
  if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) {
    docker(['pull', image]);
  }
}

async function writeFixture(directory) {
  await writeFile(path.join(directory, 'fluent-bit.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    HTTP_Server On
    HTTP_Listen 0.0.0.0
    HTTP_Port 2020
    storage.path /work/storage

[INPUT]
    Name tail
    Tag fullnet.priority.event
    Path /work/priority.log
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

[INPUT]
    Name tail
    Tag fullnet.b2.event
    Path /work/b2.log
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

[OUTPUT]
    Name forward
    Alias fullnet_priority_forward
    Match fullnet.priority.*
    Host 127.0.0.1
    Port 24224
    Retry_Limit 1
    storage.total_limit_size 1MB

[OUTPUT]
    Name forward
    Alias fullnet_b2_forward
    Match fullnet.b2.*
    Host 127.0.0.1
    Port 24224
    Retry_Limit 1
    storage.total_limit_size 1MB
`);
  await mkdir(path.join(directory, 'storage'));
  await Promise.all(['priority.log', 'b2.log'].map((name) => writeFile(path.join(directory, name), '')));
}

async function api(base, endpoint) {
  const response = await fetch(`${base}${endpoint}`, { signal: AbortSignal.timeout(3000) });
  assert.equal(response.status, 200, `${endpoint}: HTTP ${response.status}`);
  const body = await response.json();
  assert.equal(body.status, 'success', `${endpoint}: ${JSON.stringify(body)}`);
  return body.data;
}

async function query(base, expression) {
  const data = await api(base, `/api/v1/query?query=${encodeURIComponent(expression)}`);
  return data.result;
}

async function waitFor(description, predicate, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const result = await predicate();
      if (result) return result;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`${description} timed out${lastError ? `: ${lastError.message}` : ''}`);
}

function counterExpression(alias) {
  return `fluentbit_output_dropped_records_total{name="${alias}"}`;
}

async function run() {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-log-alert-scrape-'));
  const expectedPrefix = path.join(os.tmpdir(), 'fullnet-log-alert-scrape-');
  assert.ok(path.resolve(directory).startsWith(path.resolve(expectedPrefix)));
  try {
    await writeFixture(directory);
    ensureImage(prometheusImage);
    ensureImage(fluentBitImage);
    docker([
      'run', '-d', '--name', prometheusContainer, '-p', '127.0.0.1::9090',
      '--mount', `type=bind,source=${root},target=/rules,readonly`,
      prometheusImage, '--config.file=/rules/tests/deployment/fluent-bit-prometheus-scrape.yml',
      '--storage.tsdb.path=/tmp/prometheus',
    ]);
    const mappedPort = docker(['port', prometheusContainer, '9090/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(mappedPort, 'Docker did not publish the Prometheus localhost port');
    const base = `http://127.0.0.1:${mappedPort}`;
    await waitFor('Prometheus startup', async () => {
      await api(base, '/api/v1/status/runtimeinfo');
      return true;
    }, 30000);
    docker([
      'run', '-d', '--name', fluentBitContainer, '--network', `container:${prometheusContainer}`,
      '--mount', `type=bind,source=${directory},target=/work`,
      fluentBitImage, '-c', '/work/fluent-bit.conf',
    ]);

    const aliases = ['fullnet_priority_forward', 'fullnet_b2_forward'];
    await waitFor('Prometheus baseline scrape', async () => {
      const up = await query(base, 'up{job="fluent-bit"}');
      if (up.length !== 1 || Number(up[0].value[1]) !== 1) return false;
      const counters = await Promise.all(aliases.map((alias) => query(base, counterExpression(alias))));
      const capacities = await Promise.all(aliases.map((alias) =>
        query(base, `fluentbit_output_chunk_available_capacity_percent{name="${alias}"}`)));
      return counters.every((series) => series.length === 1 && Number(series[0].value[1]) === 0)
        && capacities.every((series) => series.length === 1 && Number(series[0].value[1]) > 0);
    }, 45000);
    const initialAlerts = await api(base, '/api/v1/alerts');
    assert.equal(initialAlerts.alerts.some((alert) => alert.labels.alertname?.includes('FluentBit') && alert.state === 'firing'), false);

    // 先取得零值样本，再注入日志；否则 increase 可能错过首次采样前的丢弃。
    await Promise.all([
      appendFile(path.join(directory, 'priority.log'), 'priority output fault\n'),
      appendFile(path.join(directory, 'b2.log'), 'best-effort output fault\n'),
    ]);
    await waitFor('Prometheus output-loss alerts', async () => {
      const counters = await Promise.all(aliases.map((alias) => query(base, counterExpression(alias))));
      if (!counters.every((series) => series.length === 1 && Number(series[0].value[1]) > 0)) return false;
      const { alerts } = await api(base, '/api/v1/alerts');
      return alerts.some((alert) => alert.labels.alertname === 'FullNetFluentBitPriorityOutputDrop'
        && alert.labels.name === aliases[0] && alert.labels.severity === 'critical' && alert.state === 'firing')
        && alerts.some((alert) => alert.labels.alertname === 'FullNetFluentBitBestEffortOutputDrop'
          && alert.labels.name === aliases[1] && alert.labels.severity === 'warning' && alert.state === 'firing');
    }, 90000);
    docker(['stop', '-t', '1', fluentBitContainer]);
    await waitFor('Fluent Bit target-down alert', async () => {
      const up = await query(base, 'up{job="fluent-bit"}');
      if (up.length !== 1 || Number(up[0].value[1]) !== 0) return false;
      const { alerts } = await api(base, '/api/v1/alerts');
      return alerts.some((alert) => alert.labels.alertname === 'FullNetFluentBitTargetDown'
        && alert.labels.job === 'fluent-bit' && alert.labels.severity === 'critical'
        && alert.state === 'firing');
    }, 100000);
    process.stdout.write('Prometheus fired both log-loss alerts and the Fluent Bit target-down alert.\n');
  } catch (error) {
    for (const name of [fluentBitContainer, prometheusContainer]) {
      const logs = spawnSync('docker', ['logs', '--tail', '80', name], { encoding: 'utf8' });
      if (logs.status === 0) process.stderr.write(`${name}:\n${logs.stdout}${logs.stderr}`);
    }
    throw error;
  } finally {
    for (const name of [fluentBitContainer, prometheusContainer]) {
      spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' });
    }
    await rm(directory, { recursive: true, force: true });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-prometheus-alert-smoke.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Live Prometheus scrape smoke requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
