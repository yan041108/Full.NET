import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const chart = path.join(root, 'deploy/helm/fullnet-log-consumer');
function render(...extra) {
  const args = ['template', 'log-consumer-test', chart, ...extra];
  return process.platform === 'win32'
    ? spawnSync(['helm', ...args].map(p => `"${p}"`).join(' '), { shell: true, encoding: 'utf8', cwd: root })
    : spawnSync('helm', args, { encoding: 'utf8', cwd: root });
}
const enabled = ['--set', 'enabled=true', '--set', 'experimental=true',
  '--set', 'configurationSecretName=consumer-test', '--set', 'image.tag=test'];

test('批次默认单条，允许显式 8 条且拒绝超限或非整数', () => {
  for (const size of [1, 8]) {
    const result = render(...enabled, '--set', `batchMaxRecords=${size}`);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, new RegExp(`FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS\\s+value: "${size}"`));
  }
  assert.match(render(...enabled).stdout, /FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS\s+value: "1"/);
  for (const size of [0, 9, 1.5]) assert.notEqual(render(...enabled, '--set', `batchMaxRecords=${size}`).status, 0);
});

test('消费者默认关闭，不创建网络、Secret 或工作负载', () => {
  const result = render();
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), '');
});
test('启用要求独立 Secret、显式实验确认和固定镜像', () => {
  for (const [args, reason] of [
    [['--set', 'enabled=true'], /experimental/],
    [['--set', 'enabled=true', '--set', 'experimental=true'], /configurationSecretName/],
    [['--set', 'enabled=true', '--set', 'experimental=true', '--set', 'configurationSecretName=x'], /image.tag/],
  ]) {
    const result = render(...args);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, reason);
  }
});
test('消费者使用固定 Secret 映射、只读 CA、资源边界和真实探针', () => {
  const result = render(...enabled, '--set', 'caSecretName=consumer-ca');
  assert.equal(result.status, 0, result.stderr);
  const yaml = result.stdout;
  for (const value of ['Deployment', 'NetworkPolicy', 'ClusterIP', '/health/live', '/health/ready',
    'runAsNonRoot: true', 'readOnlyRootFilesystem: true', 'automountServiceAccountToken: false',
    'terminationGracePeriodSeconds: 90', 'type: Recreate', 'type: RuntimeDefault', 'FULLNET_LOG_CONSUMER_HEALTH_PORT',
    'FULLNET_LOG_CONSUMER_EXPERIMENTAL', 'FULLNET_LOG_CONSUMER_ES_API_KEY',
    'kafka-ca.crt', 'es-ca.crt', 'readOnly: true', 'memory: 256Mi']) assert.ok(yaml.includes(value), value);
  assert.doesNotMatch(yaml, /kind: Ingress|envFrom:|NoCheck|kind: Secret|type: LoadBalancer/);
  assert.ok(!yaml.includes('Full.NET.Host.Worker'));
});
test('独立镜像为 JIT 非 root，直接运行消费者并接收 SIGTERM', async () => {
  const dockerfile = await readFile(path.join(root, 'deploy/containers/log-consumer.Dockerfile'), 'utf8');
  assert.match(dockerfile, /FullNetPublishMode=Jit/);
  assert.match(dockerfile, /aspnet:10\.0/);
  assert.match(dockerfile, /USER \$APP_UID/);
  assert.match(dockerfile, /ENTRYPOINT \["dotnet", "Full.NET.Host.LogConsumer.dll"\]/);
});

test('PodMonitor 默认关闭，显式启用只采集本消费者运维面', () => {
  assert.doesNotMatch(render(...enabled).stdout, /kind: PodMonitor/);
  const result = render(...enabled, '--set', 'podMonitor.enabled=true', '--set', 'podMonitor.labels.release=monitoring');
  assert.equal(result.status, 0, result.stderr);
  for (const expected of ['kind: PodMonitor', 'jobLabel: app.kubernetes.io/name', 'release: monitoring',
    'port: operations', 'path: /metrics', 'interval: 15s', 'scrapeTimeout: 5s']) assert.ok(result.stdout.includes(expected), expected);
});
