import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import https from 'node:https';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyOperatorDiscovery } from './log-consumer-operator-probe.mjs';
import { runThroughputProbe } from './log-consumer-throughput-probe.mjs';
import { runSustainedProbe } from './log-consumer-sustained-probe.mjs';

// 仅操作固定本地 kind 上的随机任务资源；所有密码、证书与 API Key 都是短期测试数据。
assert.ok(process.argv.includes('--docker-desktop'), 'Specify --docker-desktop for the isolated local kind experiment.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const context = 'kind-fullnet-local';
const operatorMode = process.argv.includes('--operator');
const throughputMode = process.argv.includes('--throughput');
const sustainedMode = process.argv.includes('--sustained');
const batchArgument = process.argv.find(value => value.startsWith('--batch-records='));
const batchMaxRecords = batchArgument ? Number(batchArgument.split('=')[1]) : 1;
assert.ok(Number.isInteger(batchMaxRecords) && batchMaxRecords >= 1 && batchMaxRecords <= 8, 'Batch records must be 1..8.');
assert.ok(!batchArgument || throughputMode || sustainedMode, 'Explicit batch comparison requires a throughput experiment.');
assert.ok([operatorMode, throughputMode, sustainedMode].filter(Boolean).length <= 1, 'Run experiments separately.');
assert.ok(!sustainedMode || batchMaxRecords === 8, 'Sustained acceptance requires explicit batch records=8.');
const suffix = randomUUID().slice(0, 8);
const namespace = `fullnet-log-e2e-${suffix}`;
const kafka = `${namespace}-kafka`;
const es = `${namespace}-es`;
const crl = `${namespace}-crl`;
const prometheus = `${namespace}-prometheus`;
const imageTag = batchArgument ? 'local-batch-20261001' : 'local-20260930';
const image = `fullnet-log-consumer:${imageTag}`;
const kafkaImage = 'apache/kafka@sha256:5cc2a2fd93fa2687b44015eee04fb2c3edd9e526bd64bf8bec5ff1e268772e0e';
const esImage = 'docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026';
const nodeImage = 'node@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1';
const prometheusImage = 'prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e';
const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-log-e2e-'));
const output = path.join(root, sustainedMode ? 'artifacts/log-consumer-sustained' : batchArgument ? `artifacts/log-consumer-batch-${batchMaxRecords}` : throughputMode ? 'artifacts/log-consumer-throughput'
  : operatorMode ? 'artifacts/log-consumer-operator' : 'artifacts/log-consumer-kubernetes');
await mkdir(output, { recursive: true });
// 失败重跑不能留下上次通过的结果供门禁误读。
await rm(path.join(output, 'result.json'), { force: true });
let namespaceCreated = false;
let agent;
let report;
let forwarding;
let forwardingClosed;

async function command(executable, args, input, timeout = 120000) {
  return await new Promise((resolve, reject) => {
    const child = spawn(executable, args, { cwd: root, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    let output = '';
    // 原始 stderr 可能包含配置；失败只报告程序和退出码，不输出 Secret 响应或命令参数。
    child.stdout.on('data', data => { output += data; });
    child.stderr.resume();
    const timer = setTimeout(() => child.kill(), timeout);
    child.on('error', error => { clearTimeout(timer); reject(error); });
    child.on('close', code => {
      clearTimeout(timer);
      code === 0 ? resolve(output.trim()) : reject(new Error(`${executable} exited ${code}`));
    });
    child.stdin.on('error', () => {});
    child.stdin.end(input);
  });
}
const docker = (args, input, timeout) => command('docker', args, input, timeout);
const kube = (args, input, timeout) => command('kubectl', ['--context', context, ...args], input, timeout);
async function port() {
  const server = net.createServer();
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const value = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return value;
}
async function waitFor(label, predicate, timeout = 120000) {
  const deadline = Date.now() + timeout;
  let last;
  while (Date.now() < deadline) {
    try { if (await predicate()) return; } catch (error) { last = error; }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} timed out${last ? ` (${last.message})` : ''}`);
}
async function esRequest(portNumber, method, route, auth, body) {
  return await new Promise((resolve, reject) => {
    const request = https.request({ hostname: 'localhost', port: portNumber, path: route, method, agent,
      headers: { authorization: auth, 'content-type': 'application/json' }, timeout: 5000 }, response => {
      let text = '';
      response.on('data', data => { text += data; });
      response.on('end', () => resolve({ status: response.statusCode, body: text ? JSON.parse(text) : {} }));
    });
    request.on('error', reject);
    request.on('timeout', () => request.destroy(new Error('ES request timeout')));
    request.end(body ? JSON.stringify(body) : undefined);
  });
}
async function applySecret(name, data) {
  await kube(['apply', '-f', '-'], JSON.stringify({ apiVersion: 'v1', kind: 'Secret',
    metadata: { name, namespace }, type: 'Opaque', stringData: data }));
}

try {
  await kube(['get', 'nodes', '-o', 'name']);
  await docker(['image', 'inspect', image]);
  const kafkaPort = await port();
  const esPort = await port();
  const crlPort = await port();
  const certConfig = `[ca]
default_ca = test_ca
[test_ca]
database = /work/index.txt
new_certs_dir = /work/newcerts
certificate = /work/ca.crt
private_key = /work/ca.key
serial = /work/serial
crlnumber = /work/crlnumber
default_md = sha256
default_days = 1
default_crl_days = 1
policy = policy
x509_extensions = server
[policy]
commonName = supplied
[server]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer
subjectAltName = DNS:localhost,DNS:host.docker.internal,IP:127.0.0.1
crlDistributionPoints = URI:http://host.docker.internal:${crlPort}/ca.crl
`;
  await writeFile(path.join(directory, 'openssl.cnf'), certConfig);
  await writeFile(path.join(directory, 'generate.sh'), `set -eu
cd /work
mkdir newcerts
touch index.txt
printf '1000\n' > serial
printf '1000\n' > crlnumber
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj '/CN=FullNET Local E2E CA' -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' -keyout ca.key -out ca.crt
openssl req -new -newkey rsa:2048 -nodes -subj '/CN=host.docker.internal' -keyout server.key -out server.csr
openssl ca -batch -config openssl.cnf -in server.csr -out server.crt -notext
openssl ca -gencrl -config openssl.cnf -out ca.crl.pem
openssl crl -in ca.crl.pem -outform DER -out ca.crl
openssl pkcs12 -export -in server.crt -inkey server.key -certfile ca.crt -out server.p12 -passout pass:local-test-only
chmod 644 ca.crt server.crt server.key server.p12 ca.crl
`);
  await docker(['run', '--rm', '-v', `${directory}:/work`, '--entrypoint', 'sh',
    'fullnet-native-aot-publish-sdk:10.0', '/work/generate.sh']);
  let crlRequests = 0;
  await writeFile(path.join(directory, 'crl-server.cjs'), `const http = require('node:http'); const fs = require('node:fs');
http.createServer((req,res) => { if(req.url!='/ca.crl'){res.writeHead(404);return res.end();}
console.log('crl-request');res.setHeader('Content-Type','application/pkix-crl');res.end(fs.readFileSync('/work/ca.crl'));
}).listen(8080,'0.0.0.0');
`);
  await docker(['run', '-d', '--name', crl, '-p', `${crlPort}:8080`, '-v', `${directory}:/work:ro`,
    nodeImage, 'node', '/work/crl-server.cjs']);
  const env = {
    KAFKA_NODE_ID: '1', KAFKA_PROCESS_ROLES: 'broker,controller',
    KAFKA_LISTENERS: 'INTERNAL://0.0.0.0:9092,SSL://0.0.0.0:9093,CONTROLLER://0.0.0.0:9094',
    KAFKA_ADVERTISED_LISTENERS: `INTERNAL://localhost:9092,SSL://host.docker.internal:${kafkaPort}`,
    KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: 'INTERNAL:PLAINTEXT,SSL:SSL,CONTROLLER:PLAINTEXT',
    KAFKA_INTER_BROKER_LISTENER_NAME: 'INTERNAL', KAFKA_CONTROLLER_LISTENER_NAMES: 'CONTROLLER',
    KAFKA_CONTROLLER_QUORUM_VOTERS: '1@localhost:9094', KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: '1',
    KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: '1', KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: '1',
    KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS: '0', KAFKA_AUTO_CREATE_TOPICS_ENABLE: 'false',
    CLUSTER_ID: 'fullnet-local-e2e-kafka', KAFKA_SSL_KEYSTORE_FILENAME: 'server.p12',
    KAFKA_SSL_KEYSTORE_CREDENTIALS: 'keystore.creds', KAFKA_SSL_KEY_CREDENTIALS: 'key.creds',
    KAFKA_SSL_KEYSTORE_TYPE: 'PKCS12', KAFKA_SSL_CLIENT_AUTH: 'none',
  };
  await writeFile(path.join(directory, 'keystore.creds'), 'local-test-only');
  await writeFile(path.join(directory, 'key.creds'), 'local-test-only');
  await docker(['run', '-d', '--name', kafka, '-p', `${kafkaPort}:9093`, '-v', `${directory}:/etc/kafka/secrets:ro`,
    ...Object.entries(env).flatMap(([key, value]) => ['-e', `${key}=${value}`]), kafkaImage]);
  const password = randomUUID();
  await docker(['run', '-d', '--name', es, '-p', `${esPort}:9200`, '-v', `${directory}:/usr/share/elasticsearch/config/certs:ro`,
    '-e', 'discovery.type=single-node', '-e', 'xpack.security.enabled=true',
    '-e', 'xpack.security.autoconfiguration.enabled=false', '-e', 'xpack.security.http.ssl.enabled=true',
    '-e', 'xpack.security.http.ssl.key=certs/server.key', '-e', 'xpack.security.http.ssl.certificate=certs/server.crt',
    '-e', 'xpack.ml.enabled=false', '-e', `ELASTIC_PASSWORD=${password}`, '-e', 'ES_JAVA_OPTS=-Xms512m -Xmx512m', esImage]);
  agent = new https.Agent({ ca: await readFile(path.join(directory, 'ca.crt')), keepAlive: false });
  const basic = `Basic ${Buffer.from(`elastic:${password}`).toString('base64')}`;
  console.log('Waiting for isolated TLS Kafka and Elasticsearch.');
  await waitFor('Kafka start', async () => (await docker(['logs', kafka])).includes('Kafka Server started'));
  await waitFor('ES HTTPS authentication', async () => (await esRequest(esPort, 'GET', '/', basic)).status === 200);
  const source = 'fullnet.local.e2e.source';
  const dlq = 'fullnet.local.e2e.dlq';
  const group = 'fullnet.local.e2e.consumer';
  for (const topic of [source, dlq]) await docker(['exec', kafka, '/opt/kafka/bin/kafka-topics.sh',
    '--bootstrap-server', 'localhost:9092', '--create', '--topic', topic, '--partitions', '1', '--replication-factor', '1']);
  const occurred = new Date(Date.now() - 60000);
  const index = `fn-logs-2-diagnostic-${occurred.toISOString().slice(0, 10).replaceAll('-', '.')}`;
  assert.equal((await esRequest(esPort, 'PUT', `/${index}`, basic, {})).status, 200);
  const key = await esRequest(esPort, 'POST', '/_security/api_key', basic, {
    name: namespace, expiration: '1h', role_descriptors: { writer: { cluster: [],
      indices: [{ names: [index], privileges: ['write'] }] } },
  });
  assert.equal(key.status, 200);
  await kube(['create', 'namespace', namespace]);
  namespaceCreated = true;
  await applySecret('runtime', { bootstrapServers: `host.docker.internal:${kafkaPort}`, topics: source,
    groupId: group, dlqTopic: dlq, routes: '2:30', maxEventBytes: '4096',
    esUrl: `https://host.docker.internal:${esPort}/`, esApiKey: key.body.encoded, securityProtocol: 'Ssl' });
  const ca = await readFile(path.join(directory, 'ca.crt'), 'utf8');
  await applySecret('ca', { 'kafka-ca.crt': ca, 'es-ca.crt': ca });
  await command('kind', ['load', 'docker-image', image, '--name', 'fullnet-local']);
  await command('helm', ['upgrade', '--install', 'log-consumer', 'deploy/helm/fullnet-log-consumer',
    '--namespace', namespace, '--kube-context', context, '--set', 'enabled=true', '--set', 'experimental=true',
    '--set', `image.tag=${imageTag}`, '--set', 'image.pullPolicy=Never', '--set', `batchMaxRecords=${batchMaxRecords}`,
    '--set', 'configurationSecretName=runtime', '--set', 'caSecretName=ca',
    ...(operatorMode ? ['--set', 'podMonitor.enabled=true', '--set', 'monitoringNamespace=fullnet-local-log-monitoring'] : [])]);
  console.log('Consumer installed with Production / Online revocation checks.');
  await kube(['-n', namespace, 'rollout', 'status', 'deployment/log-consumer', '--timeout=120s'], undefined, 130000);
  const operationsPort = await port();
  const prometheusPort = await port();
  const beginForwarding = () => {
    forwarding = spawn('kubectl', ['--context', context, '-n', namespace, 'port-forward',
      'service/log-consumer', `${operationsPort}:8080`], { stdio: 'ignore', windowsHide: true });
    // 启动即保存关闭结果；被信号提前结束的子进程不能在 finally 中等待已错过的事件。
    forwardingClosed = new Promise(resolve => forwarding.once('close', resolve));
    forwarding.on('error', () => {});
  };
  beginForwarding();
  await waitFor('Actual consumer metrics endpoint', async () => {
    const response = await fetch(`http://127.0.0.1:${operationsPort}/metrics`, { signal: AbortSignal.timeout(2000) });
    return response.status === 200;
  });
  await writeFile(path.join(directory, 'prometheus.yml'), `global:
  scrape_interval: 1s
  evaluation_interval: 1s
rule_files:
  - /etc/prometheus/log-rules.yaml
scrape_configs:
  - job_name: fullnet-log-consumer
    static_configs:
      - targets: ['host.docker.internal:${operationsPort}']
`);
  await docker(['run', '-d', '--name', prometheus, '-p', `${prometheusPort}:9090`,
    '-v', `${path.join(directory, 'prometheus.yml')}:/etc/prometheus/prometheus.yml:ro`,
    '-v', `${path.join(root, 'deploy/observability/prometheus-rules.yaml')}:/etc/prometheus/log-rules.yaml:ro`, prometheusImage]);
  async function metric(expression) {
    const response = await fetch(`http://127.0.0.1:${prometheusPort}/api/v1/query?query=${encodeURIComponent(expression)}`,
      { signal: AbortSignal.timeout(3000) });
    assert.equal(response.status, 200);
    const result = await response.json();
    assert.equal(result.status, 'success');
    return result.data.result.length === 1 ? Number(result.data.result[0].value[1]) : undefined;
  }
  // UUID v7：保留随机尾部，只改版本与变体，事件 ID 必须通过真实解析器。
  const eventId = () => {
    const timestamp = Date.now().toString(16).padStart(12, '0');
    const random = randomUUID();
    return `${timestamp.slice(0, 8)}-${timestamp.slice(8)}-7${random.slice(15, 18)}-8${random.slice(20, 23)}-${random.slice(24)}`;
  };
  const id = eventId();
  const event = { '@t': occurred.toISOString(), '@mt': 'kubernetes-e2e', LogEventId: id,
    'log.class': 'diagnostic', OccurredAtUtc: occurred.toISOString(),
    ExpiresAtUtc: new Date(occurred.getTime() + 30 * 86400000).toISOString(), IndexRouteVersion: 2 };
  await docker(['exec', '-i', kafka, '/opt/kafka/bin/kafka-console-producer.sh', '--bootstrap-server', 'localhost:9092',
    '--topic', source, '--property', 'parse.key=true', '--property', 'key.separator=|'], `${id}|${JSON.stringify(event)}\npoison|not-json\n`);
  await waitFor('ES event confirmation', async () => {
    const response = await esRequest(esPort, 'GET', `/${index}/_doc/${id}`, basic);
    return response.status === 200 && response.body._source.LogEventId === id;
  });
  const deadLetter = await docker(['exec', kafka, '/opt/kafka/bin/kafka-console-consumer.sh',
    '--bootstrap-server', 'localhost:9092', '--topic', dlq, '--from-beginning', '--max-messages', '1', '--timeout-ms', '30000']);
  assert.equal(deadLetter, 'not-json', 'DLQ must contain the original rejected payload.');
  let offsets;
  await waitFor('Offset commit after ES and DLQ confirmation', async () => {
    offsets = await docker(['exec', kafka, '/opt/kafka/bin/kafka-consumer-groups.sh', '--bootstrap-server', 'localhost:9092',
      '--group', group, '--describe']);
    return new RegExp(`${source}\\s+0\\s+2\\s+2\\s+0`).test(offsets);
  });
  const pods = JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-o', 'json']));
  const pod = pods.items[0];
  assert.equal(pod.status.containerStatuses[0].restartCount, 0);
  assert.equal(pod.status.containerStatuses[0].ready, true);
  await waitFor('Prometheus scrapes committed lag and fixed confirmation results', async () =>
    await metric('up{job="fullnet-log-consumer"}') === 1
    && await metric('fullnet_log_consumer_lag_known{job="fullnet-log-consumer"}') === 1
    && await metric('fullnet_log_consumer_lag_records{job="fullnet-log-consumer"}') === 0
    && await metric('fullnet_log_consumer_delivery_results_total{stage="es",outcome="confirmed"}') === 1
    && await metric('fullnet_log_consumer_delivery_results_total{stage="dlq",outcome="confirmed"}') === 1
    && [1, 2].includes(await metric('fullnet_log_consumer_delivery_results_total{stage="offset",outcome="confirmed"}')), 60000);
  const metricOffsetConfirmed = await metric('fullnet_log_consumer_delivery_results_total{stage="offset",outcome="confirmed"}');
  if (batchMaxRecords === 1) assert.equal(metricOffsetConfirmed, 2);
  const operatorDiscovery = operatorMode
    ? await verifyOperatorDiscovery({ kube, namespace, port: await port(), waitFor }) : undefined;
  // 端口转发绑定当前 Pod；吊销测试替换 Pod 前停止，不把转发中断冒充运维面故障。
  forwarding.kill();
  await forwardingClosed;
  forwarding = undefined;
  crlRequests = (await docker(['logs', crl])).split('\n').filter(line => line === 'crl-request').length;
  assert.ok(crlRequests > 0, 'Production consumer must fetch the CRL from the certificate distribution point.');
  console.log('ES, DLQ and Offset 2 confirmed; testing revoked ES certificate.');
  await kube(['-n', namespace, 'scale', 'deployment/log-consumer', '--replicas=0']);
  await waitFor('Consumer stopped before certificate revocation', async () =>
    JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-o', 'json'])).items.length === 0);
  await writeFile(path.join(directory, 'revoke.sh'), `set -eu
cd /work
openssl ca -config openssl.cnf -revoke server.crt
openssl ca -gencrl -config openssl.cnf -out ca.crl.pem
openssl crl -in ca.crl.pem -outform DER -out ca.crl
chmod 644 ca.crl
`);
  await docker(['run', '--rm', '-v', `${directory}:/work`, '--entrypoint', 'sh',
    'fullnet-native-aot-publish-sdk:10.0', '/work/revoke.sh']);
  const recoveryId = eventId();
  const recoveryEvent = { ...event, LogEventId: recoveryId, '@mt': 'certificate-recovery' };
  await docker(['exec', '-i', kafka, '/opt/kafka/bin/kafka-console-producer.sh', '--bootstrap-server', 'localhost:9092',
    '--topic', source, '--property', 'parse.key=true', '--property', 'key.separator=|'],
  `${recoveryId}|${JSON.stringify(recoveryEvent)}\n`);
  await kube(['-n', namespace, 'scale', 'deployment/log-consumer', '--replicas=1']);
  await waitFor('Revoked certificate leaves delivery unconfirmed', async () => {
    const rejected = JSON.parse(await kube(['-n', namespace, 'get', 'pods', '-o', 'json']));
    return rejected.items.some(item => item.status.containerStatuses?.[0]?.lastState?.terminated?.exitCode === 1);
  });
  // 网络/TLS 异常由宿主失败退出 1；明确 Retry 才退出 3，二者都不能提交位点。
  const rejectedLog = await kube(['-n', namespace, 'logs', 'deployment/log-consumer', '--previous']);
  assert.ok(rejectedLog.includes('Log consumer stopped: HttpRequestException'), 'Expected sanitized TLS transport failure.');
  const rejectedOffsets = await docker(['exec', kafka, '/opt/kafka/bin/kafka-consumer-groups.sh',
    '--bootstrap-server', 'localhost:9092', '--group', group, '--describe']);
  assert.match(rejectedOffsets, new RegExp(`${source}\\s+0\\s+2\\s+3\\s+1`));
  assert.equal((await esRequest(esPort, 'GET', `/${index}/_doc/${recoveryId}`, basic)).status, 404);
  const dlqEnd = await docker(['exec', kafka, '/opt/kafka/bin/kafka-get-offsets.sh',
    '--bootstrap-server', 'localhost:9092', '--topic', dlq, '--time', 'latest']);
  assert.equal(dlqEnd, `${dlq}:0:1`, 'TLS failure must not quarantine a valid event.');
  // 签发新序列号的证书恢复 ES；保留吊销列表，不回滚 CRL 或放宽校验。
  await writeFile(path.join(directory, 'renew.sh'), `set -eu
cd /work
openssl ca -batch -config openssl.cnf -in server.csr -out server.crt -notext
chmod 644 server.crt
`);
  await docker(['run', '--rm', '-v', `${directory}:/work`, '--entrypoint', 'sh',
    'fullnet-native-aot-publish-sdk:10.0', '/work/renew.sh']);
  await docker(['restart', es]);
  await waitFor('ES renewed certificate ready', async () => (await esRequest(esPort, 'GET', '/', basic)).status === 200);
  await waitFor('Uncommitted event replay after certificate replacement', async () => {
    const response = await esRequest(esPort, 'GET', `/${index}/_doc/${recoveryId}`, basic);
    return response.status === 200 && response.body._source.LogEventId === recoveryId;
  }, 180000);
  await waitFor('Recovered Offset 3', async () => {
    const recovered = await docker(['exec', kafka, '/opt/kafka/bin/kafka-consumer-groups.sh',
      '--bootstrap-server', 'localhost:9092', '--group', group, '--describe']);
    return new RegExp(`${source}\\s+0\\s+3\\s+3\\s+0`).test(recovered);
  });
  await kube(['-n', namespace, 'rollout', 'status', 'deployment/log-consumer', '--timeout=120s'], undefined, 130000);
  // 此处验证的是测试采集桥不可达的告警，而不是伪称 Pod 本身故障；保留正式规则的 1m 等待。
  await waitFor('Prometheus target-down alert fires after test bridge interruption', async () =>
    await metric('ALERTS{alertname="FullNetLogConsumerTargetDown",alertstate="firing"}') === 1, 90000);
  beginForwarding();
  await waitFor('Prometheus target-down alert resolves after bridge recovery', async () =>
    await metric('up{job="fullnet-log-consumer"}') === 1
    && await metric('ALERTS{alertname="FullNetLogConsumerTargetDown",alertstate="firing"}') === undefined);
  crlRequests = (await docker(['logs', crl])).split('\n').filter(line => line === 'crl-request').length;
  const throughput = throughputMode ? await runThroughputProbe({ kube, docker, namespace, kafka, es, source, group, dlq,
    event, eventId, index, esPort, esRequest, basic, waitFor }) : undefined;
  const sustained = sustainedMode ? await runSustainedProbe({ kube, docker, namespace, kafka, es, source, group, dlq,
    event, eventId, index, esPort, esRequest, basic, waitFor }) : undefined;
  report = { passed: true, context, namespace, consumerImage: image, batchMaxRecords,
    environment: 'Production', revocation: 'Online', crlRequests,
    sourceRecords: 3, esDocuments: 2, dlqRecords: 1, committedOffset: 3, lag: 0,
    revokedCertificateRejected: true, rejectedOffset: 2, recoveredOffset: 3,
    prometheusScrapeVerified: true, metricLagKnown: 1, metricLagRecords: 0,
    targetDownAlertFiredAndResolved: true,
    ...(operatorDiscovery ? { operatorDiscovery } : {}),
    ...(throughput ? { throughput } : {}),
    ...(sustained ? { sustained } : {}),
    metricEsConfirmed: 1, metricDlqConfirmed: 1, metricOffsetConfirmed,
    ready: true, initialRestarts: 0, completedAt: new Date().toISOString(),
    scope: 'kind consumer with external isolated single-node Kafka/ES test containers; functional acceptance only' };
} finally {
  agent?.destroy();
  if (forwarding) {
    if (forwarding.exitCode === null && forwarding.signalCode === null) forwarding.kill();
    await forwardingClosed;
  }
  // 清理互不依赖的任务资源；删除失败必须失败关闭，不能保留 passed 工件。
  const cleanup = await Promise.allSettled([
    namespaceCreated ? kube(['delete', 'namespace', namespace, '--wait=true', '--timeout=120s'], undefined, 130000) : Promise.resolve(),
    (async () => {
      const existing = (await docker(['ps', '-a', '--format', '{{.Names}}'])).split('\n');
      const owned = [kafka, es, crl, prometheus].filter(name => existing.includes(name));
      if (owned.length) await docker(['rm', '-f', ...owned]);
    })(),
  ]);
  // 目录来自本进程 mkdtemp，清理只涉及短期测试私钥和配置。
  await rm(directory, { recursive: true, force: true });
  const failures = cleanup.filter(result => result.status === 'rejected');
  if (failures.length) throw new AggregateError(failures.map(result => result.reason), 'Local test resource cleanup failed.');
}
// 全部断言和资源清理完成后才发布可供门禁读取的通过报告。
report.cleanupCompleted = true;
await writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
