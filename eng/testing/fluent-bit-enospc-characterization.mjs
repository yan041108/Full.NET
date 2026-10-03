import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const container = `fullnet-enospc-${process.pid}-${randomUUID().slice(0, 8)}`;

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

async function waitFor(description, predicate, timeoutMs = 20000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if (await predicate()) return;
    } catch {
      // HTTP 服务或日志可能尚未就绪。
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${description} timed out`);
}

function metric(body, name, alias) {
  const match = body.match(new RegExp(`^${name}\\{name="${alias}"\\}\\s+(\\d+)`, 'm'));
  assert.ok(match, `${name} missing for ${alias}`);
  return Number(match[1]);
}

async function run() {
  if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
  try {
    // tmpfs 只复现文件系统 ENOSPC，不模拟 Kubernetes emptyDir sizeLimit 的驱逐时序。
    docker(['run', '-d', '--name', container, '-p', '127.0.0.1::2020', '--tmpfs', '/buffer:size=65536',
      '--mount', `type=bind,source=${root},target=/work,readonly`, image,
      '-c', '/work/tests/deployment/fluent-bit-enospc-probe.conf']);
    const port = docker(['port', container, '2020/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(port, 'metrics port missing');
    await waitFor('filesystem ENOSPC', () => {
      const logs = spawnSync('docker', ['logs', '--tail', '60', container], { encoding: 'utf8' });
      assert.equal(logs.status, 0);
      const output = `${logs.stdout}${logs.stderr}`;
      return /No space left on device/.test(output)
        && /\[input chunk\] error writing data from dummy\.0 instance/.test(output);
    });
    assert.equal(docker(['inspect', container, '--format', '{{.State.Status}}']), 'running');
    const alias = 'fullnet_priority_forward';
    let metrics;
    await waitFor('Forward retry metric', async () => {
      const response = await fetch(`http://127.0.0.1:${port}/api/v2/metrics/prometheus`, {
        signal: AbortSignal.timeout(3000),
      });
      assert.equal(response.status, 200);
      metrics = await response.text();
      return metric(metrics, 'fluentbit_output_retries_total', alias) > 0;
    });
    const retries = metric(metrics, 'fluentbit_output_retries_total', alias);
    const successes = metric(metrics, 'fluentbit_output_proc_records_total', alias);
    const drops = metric(metrics, 'fluentbit_output_dropped_records_total', alias);
    const input = 'dummy.0';
    const ingested = metric(metrics, 'fluentbit_input_records_total', input);
    const paused = metric(metrics, 'fluentbit_input_ingestion_paused', input);
    const overlimit = metric(metrics, 'fluentbit_input_storage_overlimit', input);
    assert.ok(retries > 0, 'unreachable Forward output did not retry');
    assert.equal(successes, 0, 'unreachable Forward output reported success');
    // 记录当前固定镜像的可观测缺口；此断言通过不表示满盘可靠性门禁通过。
    assert.equal(drops, 0, 'fixed-image ENOSPC observation changed; reassess the monitoring contract');
    assert.equal(paused, 0, 'fixed-image input pause signal changed; reassess the monitoring contract');
    assert.equal(overlimit, 0, 'fixed-image storage overlimit signal changed; reassess the monitoring contract');
    process.stdout.write(`ENOSPC characterized: process running, retries=${retries}, successes=${successes}, outputDrops=${drops}, inputRecords=${ingested}, inputPaused=${paused}, inputOverlimit=${overlimit}; emptyDir gate remains closed.\n`);
  } finally {
    spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore' });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-enospc-characterization.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('ENOSPC characterization requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
