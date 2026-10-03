import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';

// 复用本地专用 Prometheus；只改随机测试命名空间，不配置静态目标或转发消费者端口。
export async function verifyOperatorDiscovery({ kube, namespace, port, waitFor }) {
  const monitorNamespace = 'fullnet-local-log-monitoring';
  await kube(['-n', monitorNamespace, 'get', 'prometheus', 'fullnet-log-monitor-prometheus']);
  const forwarding = spawn('kubectl', ['--context', 'kind-fullnet-local', '-n', monitorNamespace,
    'port-forward', 'service/fullnet-log-monitor-prometheus', `${port}:9090`],
  { stdio: 'ignore', windowsHide: true });
  const closed = new Promise(resolve => forwarding.once('close', resolve));
  forwarding.on('error', () => {});
  async function targets() {
    const response = await fetch(`http://127.0.0.1:${port}/api/v1/targets`, { signal: AbortSignal.timeout(3000) });
    assert.equal(response.status, 200);
    const result = await response.json();
    assert.equal(result.status, 'success');
    return result.data.activeTargets.filter(target => target.scrapePool === `podMonitor/${namespace}/log-consumer/0`);
  }
  async function absentForObservationWindow() {
    await waitFor('Excluded consumer target removed from loaded configuration', async () => (await targets()).length === 0);
    // 覆盖 Operator 重调谐及 Prometheus 配置重载，不能把刚启动时的空目标误计为排除成功。
    const deadline = Date.now() + 20000;
    while (Date.now() < deadline) {
      assert.equal((await targets()).length, 0, 'Excluded PodMonitor must not become an active target.');
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
  }
  let selected;
  async function active() {
    await waitFor('Operator discovers the real consumer Pod', async () => {
      selected = await targets();
      return selected.length === 1 && selected[0].health === 'up';
    });
  }
  try {
    await waitFor('Operator Prometheus API ready', async () => { await targets(); return true; });
    // 先取得真实 up 正例，再逐层排除并恢复；不能把尚未调谐的初始空目标当作负例通过。
    await kube(['-n', namespace, 'get', 'podmonitor', 'log-consumer']);
    await kube(['label', 'namespace', namespace, 'fullnet.io/log-smoke=selected', '--overwrite']);
    await kube(['-n', namespace, 'label', 'podmonitor/log-consumer', 'fullnet.io/log-smoke=selected', '--overwrite']);
    await active();
    await kube(['-n', namespace, 'label', 'podmonitor/log-consumer', 'fullnet.io/log-smoke=excluded', '--overwrite']);
    await absentForObservationWindow();
    await kube(['-n', namespace, 'label', 'podmonitor/log-consumer', 'fullnet.io/log-smoke=selected', '--overwrite']);
    await active();
    await kube(['label', 'namespace', namespace, 'fullnet.io/log-smoke=excluded', '--overwrite']);
    await absentForObservationWindow();
    await kube(['label', 'namespace', namespace, 'fullnet.io/log-smoke=selected', '--overwrite']);
    await active();
    const target = selected[0];
    assert.equal(target.labels.job, 'fullnet-log-consumer');
    assert.equal(target.labels.namespace, namespace);
    const pods = JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-l', 'app.kubernetes.io/instance=log-consumer', '-o', 'json']));
    assert.equal(pods.items.length, 1);
    assert.equal(target.scrapeUrl, `http://${pods.items[0].status.podIP}:8080/metrics`);
    const expression = `fullnet_log_consumer_lag_known{namespace="${namespace}",job="fullnet-log-consumer"}`;
    await waitFor('Operator scrapes actual consumer lag metrics', async () => {
      const response = await fetch(`http://127.0.0.1:${port}/api/v1/query?query=${encodeURIComponent(expression)}`,
        { signal: AbortSignal.timeout(3000) });
      assert.equal(response.status, 200);
      const result = await response.json();
      return result.status === 'success' && result.data.result.length === 1 && result.data.result[0].value[1] === '1';
    });
    return { verified: true, namespaceExclusionVerified: true, monitorLabelExclusionVerified: true,
      directPodScrapeVerified: true, job: target.labels.job, scrapePool: target.scrapePool };
  } finally {
    if (forwarding.exitCode === null && forwarding.signalCode === null) forwarding.kill();
    await closed;
  }
}
