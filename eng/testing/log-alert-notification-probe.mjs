import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';

// 就绪和 UID 都要重新核对，避免把旧进程或其他任务的 Pod 当作恢复证据。
export function assertReplacementIdentity(before, after, namespace) {
  assert.match(namespace, /^fullnet-log-restart-[a-f0-9]{8}$/);
  for (const pod of [before, after]) {
    assert.equal(pod.metadata.namespace, namespace);
    assert.equal(pod.metadata.name, 'local-alertmanager');
    assert.ok(typeof pod.metadata.uid === 'string' && pod.metadata.uid.length > 0);
  }
  assert.notEqual(after.metadata.uid, before.metadata.uid);
  assert.ok(after.status?.conditions?.some(condition => condition.type === 'Ready' && condition.status === 'True'));
  return { previousUid: before.metadata.uid, replacementUid: after.metadata.uid, podReplaced: true, ready: true };
}

// 临时通知端点仅用于专用本地 Prometheus；CAS 检查防止恢复时覆盖他人变更。
export async function setupNotifications({ root, namespace, kube, command, waitFor, retryFailures = 0, outageMilliseconds = 0 }) {
  const monitoringNamespace = 'fullnet-local-log-monitoring';
  const prometheus = 'fullnet-log-monitor-prometheus';
  const nonce = randomUUID().replaceAll('-', '');
  const original = JSON.parse(await kube(['-n', monitoringNamespace, 'get', 'prometheus', prometheus, '-o', 'json']));
  assert.ok(!original.spec.alerting?.alertmanagers?.length, 'Local notification experiment requires no existing alerting endpoints.');
  const alerting = { alertmanagers: [{ namespace, name: 'local-alertmanager', port: 'web', apiVersion: 'v2' }] };
  let patched = false;
  let forwarding;
  let closed;
  let cursor = 0;
  let fingerprint;
  const deliveries = [];
  let retryProof;
  let restartProof;
  let outageProof;
  async function cleanup() {
    if (forwarding) {
      if (forwarding.exitCode === null && forwarding.signalCode === null) forwarding.kill();
      await closed;
      forwarding = undefined;
    }
    if (patched) {
      const current = JSON.parse(await kube(['-n', monitoringNamespace, 'get', 'prometheus', prometheus, '-o', 'json']));
      if (JSON.stringify(current.spec.alerting) === JSON.stringify(original.spec.alerting)) {
        patched = false;
        return;
      }
      await kube(['-n', monitoringNamespace, 'patch', 'prometheus', prometheus, '--type=json', '-p', JSON.stringify([
        { op: 'test', path: '/metadata/resourceVersion', value: current.metadata.resourceVersion },
        { op: 'test', path: '/spec/alerting', value: alerting },
        original.spec.alerting === undefined ? { op: 'remove', path: '/spec/alerting' }
          : { op: 'replace', path: '/spec/alerting', value: original.spec.alerting },
      ])]);
      const restored = JSON.parse(await kube(['-n', monitoringNamespace, 'get', 'prometheus', prometheus, '-o', 'json']));
      assert.deepEqual(restored.spec.alerting, original.spec.alerting);
      patched = false;
    }
  }
  try {
    const nodeImage = 'node@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1';
    const receiverImage = 'fullnet-log-webhook:local-20261001';
    const alertmanagerImage = 'quay.io/prometheus/alertmanager:v0.34.0';
    await command('docker', ['tag', nodeImage, receiverImage]);
    // Docker Desktop 的多架构索引可能缺少非本机内容；仅导出已安装的 amd64，避免 kind --all-platforms 导入失败。
    const exportDirectory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-log-notification-images-'));
    try {
      const archive = path.join(exportDirectory, 'images.tar');
      await command('docker', ['image', 'save', '--platform=linux/amd64', '--output', archive, receiverImage, alertmanagerImage]);
      await command('kind', ['load', 'image-archive', archive, '--name', 'fullnet-local']);
    } finally {
      // 目录来自本进程 mkdtemp，只清理本任务镜像归档。
      await rm(exportDirectory, { recursive: true, force: true });
    }
    const receiver = await readFile(path.join(root, 'eng/testing/log-alert-webhook-receiver.mjs'), 'utf8');
    // 默认丢弃无关告警；只有本次 namespace/Pod/规则能送到本地接收端。
    const config = `route:
  receiver: discard
  group_by: [namespace, pod, alertname]
  group_wait: 1s
  group_interval: 2s
  repeat_interval: 1h
  routes:
    - receiver: local-test
      matchers:
        - namespace="${namespace}"
        - pod="consumer-restart"
        - alertname="FullNetLogConsumerRestarting"
receivers:
  - name: discard
  - name: local-test
    webhook_configs:
      - url: http://local-webhook.${namespace}.svc:8080/sink/${nonce}
        send_resolved: true
`;
    const objects = [{ apiVersion: 'v1', kind: 'ConfigMap', metadata: { name: 'notification-test', namespace },
      data: { 'receiver.mjs': receiver, 'alertmanager.yml': config } }];
    for (const [name, image, user, port, args, env] of [
      ['local-webhook', receiverImage, 1000, 8080, ['node', '/config/receiver.mjs'],
        [{ name: 'TEST_NAMESPACE', value: namespace }, { name: 'TEST_NONCE', value: nonce },
          { name: 'TEST_FAIL_FIRST', value: String(retryFailures) },
          { name: 'TEST_HOLD_MILLISECONDS', value: String(outageMilliseconds) }]],
      ['local-alertmanager', alertmanagerImage, 65534, 9093,
        ['--config.file=/config/alertmanager.yml', '--storage.path=/data', '--cluster.listen-address='], []],
    ]) {
      objects.push({ apiVersion: 'v1', kind: 'Pod', metadata: { name, namespace, labels: { 'fullnet.io/notification-probe': name } },
        spec: { automountServiceAccountToken: false, terminationGracePeriodSeconds: 5,
          securityContext: { runAsNonRoot: true, runAsUser: user, fsGroup: user, seccompProfile: { type: 'RuntimeDefault' } },
          containers: [{ name, image, imagePullPolicy: 'Never', ...(name === 'local-webhook' ? { command: args } : { args }), env,
            ports: [{ name: 'web', containerPort: port }],
            ...(name === 'local-alertmanager' ? { readinessProbe: { httpGet: { path: '/-/ready', port: 'web' }, periodSeconds: 2 } } : {}),
            resources: { requests: { cpu: '10m', memory: '32Mi' }, limits: { cpu: '250m', memory: '128Mi' } },
            securityContext: { readOnlyRootFilesystem: true, allowPrivilegeEscalation: false, capabilities: { drop: ['ALL'] } },
            volumeMounts: [{ name: 'config', mountPath: '/config', readOnly: true }, { name: 'data', mountPath: '/data' }] }],
          volumes: [{ name: 'config', configMap: { name: 'notification-test' } }, { name: 'data', emptyDir: { sizeLimit: '16Mi' } }] } });
      objects.push({ apiVersion: 'v1', kind: 'Service', metadata: { name, namespace },
        spec: { type: 'ClusterIP', selector: { 'fullnet.io/notification-probe': name }, ports: [{ name: 'web', port, targetPort: 'web' }] } });
    }
    await kube(['apply', '-f', '-'], JSON.stringify({ apiVersion: 'v1', kind: 'List', items: objects }));
    await kube(['-n', namespace, 'wait', '--for=condition=Ready', 'pod/local-webhook', 'pod/local-alertmanager', '--timeout=120s']);
    const server = net.createServer();
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    forwarding = spawn('kubectl', ['--context', 'kind-fullnet-local', '-n', namespace, 'port-forward',
      'service/local-webhook', `${port}:8080`], { stdio: 'ignore', windowsHide: true });
    closed = new Promise(resolve => forwarding.once('close', resolve));
    forwarding.on('error', () => {});
    async function evidence(route = 'events') {
      const response = await fetch(`http://127.0.0.1:${port}/${route}/${nonce}`, { signal: AbortSignal.timeout(3000) });
      assert.equal(response.status, 200);
      return await response.json();
    }
    await waitFor('Isolated webhook API ready', async () => { await evidence(); return true; });
    const current = JSON.parse(await kube(['-n', monitoringNamespace, 'get', 'prometheus', prometheus, '-o', 'json']));
    assert.deepEqual(current.spec.alerting, original.spec.alerting, 'Alerting configuration changed during setup.');
    // 请求结果未知也要检查恢复；服务端可能已应用变更，而客户端随后超时或退出。
    patched = true;
    await kube(['-n', monitoringNamespace, 'patch', 'prometheus', prometheus, '--type=json', '-p', JSON.stringify([
      { op: 'test', path: '/metadata/resourceVersion', value: current.metadata.resourceVersion },
      { op: 'add', path: '/spec/alerting', value: alerting },
    ])]);
    return {
      async restart() {
        assert.deepEqual(deliveries, ['firing'], 'Restart experiment requires the first firing to be delivered.');
        const before = JSON.parse(await kube(['-n', namespace, 'get', 'pod', 'local-alertmanager', '-o', 'json']));
        // 删除 Pod 同时丢弃 emptyDir；验证依靠 Prometheus 重发，不能据此宣称持久队列恢复。
        await kube(['-n', namespace, 'delete', 'pod', 'local-alertmanager', '--wait=true']);
        const replacement = objects.find(object => object.kind === 'Pod' && object.metadata.name === 'local-alertmanager');
        await kube(['apply', '-f', '-'], JSON.stringify(replacement));
        await kube(['-n', namespace, 'wait', '--for=condition=Ready', 'pod/local-alertmanager', '--timeout=120s']);
        const after = JSON.parse(await kube(['-n', namespace, 'get', 'pod', 'local-alertmanager', '-o', 'json']));
        restartProof = assertReplacementIdentity(before, after, namespace);
      },
      async received(status) {
        await waitFor(`Actual ${status} webhook delivered`, async () => {
          const values = await evidence();
          const index = values.findIndex((event, index) => index >= cursor && event.status === status
            && event.namespace === namespace && event.pod === 'consumer-restart'
            && event.alertname === 'FullNetLogConsumerRestarting' && (!fingerprint || event.fingerprint === fingerprint));
          if (index < 0) return false;
          fingerprint ??= values[index].fingerprint;
          cursor = index + 1;
          deliveries.push(status);
          return true;
        });
        if (retryFailures && !retryProof) {
          const attempts = (await evidence('attempts')).slice(0, retryFailures + 1);
          assert.equal(attempts.length, retryFailures + 1);
          assert.deepEqual(attempts.map(attempt => attempt.code), [...Array(retryFailures).fill(503), 200]);
          assert.ok(attempts.every(attempt => attempt.status === 'firing' && attempt.fingerprint === fingerprint
            && attempt.namespace === namespace && attempt.pod === 'consumer-restart'));
          retryProof = { rejectedAttempts: retryFailures, responseCodes: attempts.map(attempt => attempt.code), sameFingerprint: true };
        }
        if (outageMilliseconds && !outageProof) {
          const attempts = await evidence('attempts');
          const accepted = attempts.findIndex(attempt => attempt.code === 200);
          assert.ok(accepted >= 2, 'Sustained outage must reject multiple actual attempts.');
          const window = attempts.slice(0, accepted + 1);
          assert.ok(window.every(attempt => attempt.status === 'firing' && attempt.fingerprint === fingerprint
            && attempt.namespace === namespace && attempt.pod === 'consumer-restart'
            && attempt.alertname === 'FullNetLogConsumerRestarting' && Number.isInteger(attempt.elapsedMs)));
          assert.equal(window[0].elapsedMs, 0);
          assert.ok(window.slice(0, -1).every(attempt => attempt.code === 503 && attempt.elapsedMs < outageMilliseconds));
          assert.ok(window.at(-1).elapsedMs >= outageMilliseconds);
          assert.ok(window.every((attempt, index) => !index || attempt.elapsedMs >= window[index - 1].elapsedMs));
          outageProof = { requestedMilliseconds: outageMilliseconds, rejectedAttempts: accepted,
            responseCodes: window.map(attempt => attempt.code), elapsedMilliseconds: window.map(attempt => attempt.elapsedMs),
            recoveryDeliveryMilliseconds: window.at(-1).elapsedMs, sameFingerprint: true };
        }
      },
      result: () => ({ firingDelivered: deliveries.includes('firing'), resolvedDelivered: deliveries.includes('resolved'),
        sameFingerprint: Boolean(fingerprint), deliveries, ...(retryProof ? { retryProof } : {}),
        ...(outageProof ? { outageProof } : {}),
        ...(restartProof ? { restartProof: { ...restartProof, firingRedelivered: deliveries.slice(1).includes('firing'),
          scope: 'Pod replacement with lost emptyDir; Prometheus resends active alert' } } : {}),
        scope: 'isolated in-cluster local webhook only' }),
      cleanup,
    };
  } catch (error) {
    await cleanup();
    throw error;
  }
}
