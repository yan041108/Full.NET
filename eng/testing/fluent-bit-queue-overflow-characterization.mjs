import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const container = `fullnet-queue-overflow-${process.pid}-${randomUUID().slice(0, 8)}`;
const alias = 'fullnet_priority_forward';

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function metric(body, name) {
  const match = body.match(new RegExp(`^${name}\\{name="${alias}"\\}\\s+([0-9]+(?:\\.[0-9]+)?)`, 'm'));
  assert.ok(match, `${name} missing for ${alias}`);
  return Number(match[1]);
}

async function run() {
  if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
  try {
    // 小队列与有界 tmpfs 只刻画固定镜像的逻辑输出限额，不模拟生产流量或 emptyDir。
    docker(['run', '-d', '--name', container, '-p', '127.0.0.1::2020', '--tmpfs', '/buffer:size=10485760',
      '--mount', `type=bind,source=${root},target=/work,readonly`, image,
      '-c', '/work/tests/deployment/fluent-bit-queue-overflow-probe.conf']);
    const port = docker(['port', container, '2020/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(port, 'metrics port missing');
    let sample;
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
      try {
        const response = await fetch(`http://127.0.0.1:${port}/api/v2/metrics/prometheus`, {
          signal: AbortSignal.timeout(3000),
        });
        assert.equal(response.status, 200);
        const body = await response.text();
        sample = {
          drops: metric(body, 'fluentbit_output_dropped_records_total'),
          availablePercent: metric(body, 'fluentbit_output_chunk_available_capacity_percent'),
          successes: metric(body, 'fluentbit_output_proc_records_total'),
        };
        if (sample.drops > 0) break;
      } catch {
        // HTTP 指标端口可能尚未就绪。
      }
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    assert.ok(sample?.drops > 0, 'fixed-image Forward queue did not overflow');
    assert.equal(docker(['inspect', container, '--format', '{{.State.Status}}']), 'running');
    assert.equal(sample.successes, 0, 'unreachable Forward output reported success');
    assert.ok(sample.availablePercent > 20,
      `loss occurred only after low-capacity threshold; observed ${sample.availablePercent}%`);
    process.stdout.write(`Queue overflow characterized: outputDrops=${sample.drops}, availablePercent=${sample.availablePercent}%, successes=${sample.successes}; capacity alert is not a pre-loss guarantee.\n`);
  } finally {
    spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore' });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-queue-overflow-characterization.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Queue overflow characterization requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
