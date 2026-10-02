import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setupNotifications } from './log-alert-notification-probe.mjs';

// 仅操作随机本地测试命名空间；真实消费者故意缺少配置，在创建网络客户端前失败。
assert.ok(process.argv.includes('--docker-desktop'), 'Specify --docker-desktop for the isolated local experiment.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const context = 'kind-fullnet-local';
const namespace = `fullnet-log-restart-${randomUUID().slice(0, 8)}`;
const notificationMode = process.argv.includes('--notifications');
const retryMode = process.argv.includes('--notification-retry');
const notificationRestartMode = process.argv.includes('--notification-restart');
const outageMode = process.argv.includes('--notification-outage');
assert.ok(!retryMode || notificationMode, '--notification-retry requires --notifications');
assert.ok(!notificationRestartMode || notificationMode, '--notification-restart requires --notifications');
assert.ok(!(retryMode && notificationRestartMode), 'Run retry and replacement experiments separately.');
assert.ok(!outageMode || (notificationMode && !retryMode && !notificationRestartMode), 'Run outage with --notifications alone.');
const output = path.join(root, outageMode ? 'artifacts/log-consumer-notification-outage'
  : notificationRestartMode ? 'artifacts/log-consumer-notification-restart'
  : retryMode ? 'artifacts/log-consumer-notification-retry'
  : notificationMode ? 'artifacts/log-consumer-notifications' : 'artifacts/log-consumer-restart-alert');
const image = 'fullnet-log-consumer:local-20260930';
await mkdir(output, { recursive: true });
await rm(path.join(output, 'result.json'), { force: true });
let created = false;
let forwarding;
let closed;
let report;
let notifications;
async function command(executable, args, input, timeout = 120000) {
  return await new Promise((resolve, reject) => {
    const child = spawn(executable, args, { cwd: root, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    let result = '';
    child.stdout.on('data', data => { result += data; });
    child.stderr.resume();
    const timer = setTimeout(() => child.kill(), timeout);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', code => {
      clearTimeout(timer);
      code === 0 ? resolve(result.trim()) : reject(new Error(`${executable} exited ${code}`));
    });
    child.stdin.on('error', () => {});
    child.stdin.end(input);
  });
}
const kube = (args, input) => command('kubectl', ['--context', context, ...args], input);
async function waitFor(label, predicate, timeout = 180000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    try { if (await predicate()) return; } catch { /* 短暂 API/采集不可达不计为成功。 */ }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} timed out`);
}
async function query(expression, port) {
  const response = await fetch(`http://127.0.0.1:${port}/api/v1/query?query=${encodeURIComponent(expression)}`,
    { signal: AbortSignal.timeout(3000) });
  assert.equal(response.status, 200);
  const value = await response.json();
  assert.equal(value.status, 'success');
  return value.data.result;
}
try {
  await kube(['-n', 'fullnet-local-log-monitoring', 'rollout', 'status', 'deployment/fullnet-log-monitor-kube-state-metrics', '--timeout=120s']);
  const server = net.createServer();
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  forwarding = spawn('kubectl', ['--context', context, '-n', 'fullnet-local-log-monitoring', 'port-forward',
    'service/fullnet-log-monitor-prometheus', `${port}:9090`], { stdio: 'ignore', windowsHide: true });
  closed = new Promise(resolve => forwarding.once('close', resolve));
  forwarding.on('error', () => {});
  await waitFor('Prometheus API ready', async () => { await query('up', port); return true; });
  await kube(['create', 'namespace', namespace]);
  created = true;
  if (notificationMode) notifications = await setupNotifications({ root, namespace, kube, command, waitFor,
    retryFailures: retryMode ? 2 : 0, outageMilliseconds: outageMode ? 60000 : 0 });
  // 使用仓库正式表达式而非缩短阈值；Kubernetes 真实重启和 Prometheus 真实采样建立 increase。
  const rules = await readFile(path.join(root, 'deploy/observability/prometheus-rules.yaml'), 'utf8');
  const match = rules.match(/- alert: FullNetLogConsumerRestarting\r?\n\s+expr: \|\r?\n([\s\S]*?)\r?\n        labels:/);
  assert.ok(match, 'Official restart rule must exist.');
  const expr = match[1].split(/\r?\n/).map(line => line.trim()).join('\n');
  await kube(['apply', '-f', '-'], JSON.stringify({ apiVersion: 'monitoring.coreos.com/v1', kind: 'PrometheusRule',
    metadata: { name: 'consumer-restart', namespace, labels: { 'fullnet.io/log-smoke': 'selected' } },
    spec: { groups: [{ name: 'fullnet-local-consumer-restart', rules: [{ alert: 'FullNetLogConsumerRestarting', expr,
      labels: { severity: 'critical' }, annotations: { summary: 'Log consumer repeated restart may hide short-lived failures' } }] }] } }));
  const pod = { apiVersion: 'v1', kind: 'Pod', metadata: { name: 'consumer-restart', namespace,
    labels: { 'app.kubernetes.io/name': 'fullnet-log-consumer' } }, spec: {
    automountServiceAccountToken: false, restartPolicy: 'Always', terminationGracePeriodSeconds: 1,
    securityContext: { runAsNonRoot: true, runAsUser: 1654, seccompProfile: { type: 'RuntimeDefault' } },
    containers: [{ name: 'consumer', image, imagePullPolicy: 'Never',
      env: [{ name: 'FULLNET_LOG_CONSUMER_EXPERIMENTAL', value: 'true' }],
      resources: { requests: { cpu: '10m', memory: '32Mi' }, limits: { cpu: '250m', memory: '128Mi' } },
      securityContext: { readOnlyRootFilesystem: true, allowPrivilegeEscalation: false, capabilities: { drop: ['ALL'] } } }] } };
  await command('kind', ['load', 'docker-image', image, '--name', 'fullnet-local']);
  await kube(['apply', '-f', '-'], JSON.stringify(pod));
  // 在最初失败时确认错误类型，避免后期 --previous 日志可读性影响类型验证。
  await waitFor('Initial failed consumer log becomes readable', async () => {
    const logs = await kube(['--request-timeout=5s', '-n', namespace, 'logs', 'consumer-restart']);
    return logs.includes('Log consumer stopped: ArgumentException');
  }, 60000);
  const selector = `namespace="${namespace}",pod="consumer-restart"`;
  const labelQuery = `kube_pod_labels{${selector},label_app_kubernetes_io_name="fullnet-log-consumer"}`;
  const restartQuery = `kube_pod_container_status_restarts_total{${selector},container="consumer"}`;
  const alertQuery = `ALERTS{${selector},alertname="FullNetLogConsumerRestarting",alertstate="firing"}`;
  await waitFor('Actual workload namespace and application label exported', async () => (await query(labelQuery, port)).length === 1);
  console.log('Actual kube-state-metrics workload labels confirmed; waiting for real container restarts.');
  let restartCount;
  await waitFor('Real restart threshold and alert firing', async () => {
    const metrics = await query(restartQuery, port);
    restartCount = metrics.length === 1 ? Number(metrics[0].value[1]) : undefined;
    return restartCount >= 3 && (await query(alertQuery, port)).length === 1;
  }, 300000);
  if (notifications) await notifications.received('firing');
  if (notificationRestartMode) {
    await notifications.restart();
    await notifications.received('firing');
  }
  const actualPod = JSON.parse(await kube(['-n', namespace, 'get', 'pod', 'consumer-restart', '-o', 'json']));
  assert.ok(actualPod.status.containerStatuses[0].restartCount >= 3);
  assert.equal(actualPod.status.containerStatuses[0].lastState.terminated.exitCode, 1);
  // 同一真实重启容器改为非消费者标签，证明关联过滤生效；不把“无指标”当作未触发。
  await kube(['-n', namespace, 'label', 'pod/consumer-restart', 'app.kubernetes.io/name=unrelated-local-probe', '--overwrite']);
  await waitFor('Non-consumer label excluded from actual alert', async () =>
    (await query(`kube_pod_labels{${selector},label_app_kubernetes_io_name="unrelated-local-probe"}`, port)).length === 1
    && (await query(restartQuery, port)).length === 1 && (await query(alertQuery, port)).length === 0);
  if (notifications) await notifications.received('resolved');
  await kube(['-n', namespace, 'label', 'pod/consumer-restart', 'app.kubernetes.io/name=fullnet-log-consumer', '--overwrite']);
  await waitFor('Consumer label restores firing alert', async () => (await query(alertQuery, port)).length === 1);
  if (notifications) await notifications.received('firing');
  await kube(['-n', namespace, 'delete', 'pod', 'consumer-restart', '--wait=true']);
  await waitFor('Alert resolves after test Pod removal', async () =>
    (await query(restartQuery, port)).length === 0 && (await query(alertQuery, port)).length === 0);
  if (notifications) await notifications.received('resolved');
  report = { passed: true, context, namespace, consumerImage: image, restartCount,
    workloadNamespacePreserved: true, applicationLabelExported: true, officialRuleFired: true,
    unrelatedLabelExcluded: true, restoredLabelFired: true, removedPodAlertResolved: true,
    ...(notifications ? { notifications: notifications.result() } : {}),
    scope: 'actual consumer startup failure and Kubernetes restart monitoring; no healthy consumer recovery or capacity claim',
    completedAt: new Date().toISOString() };
} finally {
  if (forwarding) {
    if (forwarding.exitCode === null && forwarding.signalCode === null) forwarding.kill();
    await closed;
  }
  // 恢复与命名空间清理各自尝试，任何失败都禁止发布 passed 工件。
  const cleanup = await Promise.allSettled([
    notifications ? notifications.cleanup() : Promise.resolve(),
    created ? kube(['delete', 'namespace', namespace, '--wait=true', '--timeout=120s']) : Promise.resolve(),
  ]);
  const failures = cleanup.filter(result => result.status === 'rejected');
  if (failures.length) throw new AggregateError(failures.map(result => result.reason), 'Notification test cleanup failed');
}
report.cleanupCompleted = true;
if (notificationMode) report.alertingConfigurationRestored = true;
await writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
