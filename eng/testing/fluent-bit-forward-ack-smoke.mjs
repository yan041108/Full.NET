import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { appendFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const noAckImage = 'node:24.21.0-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1';
const suffix = `${process.pid}-${randomUUID().slice(0, 8)}`;
const network = `fullnet-log-ack-${suffix}`;
const receiver = `fullnet-log-ack-receiver-${suffix}`;
const sender = `fullnet-log-ack-sender-${suffix}`;
const ids = ['0199aabb-ccdd-7000-8000-000000000021', '0199aabb-ccdd-7000-8000-000000000022'];
const unacknowledgedId = '0199aabb-ccdd-7000-8000-000000000023';

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function selectedOutputs(values) {
  const blocks = [...values.matchAll(/^    \[OUTPUT\]\r?\n[\s\S]*?(?=^    \[OUTPUT\]|^  customParsers:)/gm)]
    .map((match) => match[0]);
  return ['fullnet_priority_forward', 'fullnet_b2_forward'].map((alias) => {
    const block = blocks.find((candidate) => candidate.includes(`Alias               ${alias}`));
    assert.ok(block, `${alias} missing`);
    assert.match(block, /Require_ack_response\s+On/);
    // 本地接收端使用独立 Docker 网络；仅替换地址和传输，其他输出选项取自候选配置。
    return block.replace(/^    /gm, '')
      .replace(/^    Host\s+.+$/m, `    Host ${receiver}`)
      .replace(/^    tls\s+On\r?\n/m, '')
      .replace(/^    tls\.verify\s+On\r?\n/m, '');
  }).join('\n');
}

async function waitFor(description, predicate, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      if (await predicate()) return;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${description} timed out${lastError ? `: ${lastError.message}` : ''}`);
}

async function senderMetrics(port) {
  const response = await fetch(`http://127.0.0.1:${port}/api/v1/metrics/prometheus`, {
    signal: AbortSignal.timeout(2000),
  });
  assert.equal(response.status, 200);
  return response.text();
}

function outputCount(metrics, metric, alias) {
  const value = metrics.match(new RegExp(`^fluentbit_output_${metric}\\{name="${alias}"\\}\\s+(\\d+)`, 'm'));
  assert.ok(value, `${metric} missing for ${alias}`);
  return Number(value[1]);
}

async function run() {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-log-forward-ack-'));
  let networkCreated = false;
  try {
    const values = await readFile(path.join(root, 'deploy/observability/fluent-bit-values.yaml'), 'utf8');
    const candidateSync = values.match(/^        storage\.sync\s+(normal|full)\s*$/m)?.[1];
    assert.ok(candidateSync, 'Collector candidate must declare storage.sync');
    await Promise.all(['sender-buffer', 'receiver-buffer', 'out'].map((name) => mkdir(path.join(directory, name))));
    await Promise.all(['priority.log', 'b2.log'].map((name) => writeFile(path.join(directory, name), '')));
    await writeFile(path.join(directory, 'receiver.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    storage.path /work/receiver-buffer

[INPUT]
    Name forward
    Listen 0.0.0.0
    Port 24224
    storage.type filesystem

[OUTPUT]
    Name file
    Match *
    Path /work/out
    File accepted.jsonl
    Format plain
`);
    await writeFile(path.join(directory, 'sender.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    HTTP_Server On
    HTTP_Listen 0.0.0.0
    HTTP_Port 2020
    storage.path /work/sender-buffer
    storage.sync ${candidateSync}

[INPUT]
    Name tail
    Tag fullnet.priority.event
    Path /work/priority.log
    DB /work/priority.db
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

[INPUT]
    Name tail
    Tag fullnet.b2.event
    Path /work/b2.log
    DB /work/b2.db
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

${selectedOutputs(values)}`);
    if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
    docker(['network', 'create', network]);
    networkCreated = true;
    docker(['run', '-d', '--name', receiver, '--network', network,
      '--mount', `type=bind,source=${directory},target=/work`, image, '-c', '/work/receiver.conf']);
    docker(['run', '-d', '--name', sender, '--network', network, '-p', '127.0.0.1::2020',
      '--mount', `type=bind,source=${directory},target=/work`, image, '-c', '/work/sender.conf']);
    let mappedPort = docker(['port', sender, '2020/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(mappedPort, 'Docker did not publish the sender metrics port');
    await Promise.all([
      appendFile(path.join(directory, 'priority.log'), `${ids[0]} priority\n`),
      appendFile(path.join(directory, 'b2.log'), `${ids[1]} ordinary\n`),
    ]);
    await waitFor('Forward ACK receiver records', async () => {
      const body = await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8');
      return ids.every((id) => body.includes(id));
    });
    await waitFor('ACK-completed sender outputs', async () => {
      const metrics = await senderMetrics(mappedPort);
      return ['fullnet_priority_forward', 'fullnet_b2_forward'].every((alias) => {
        return outputCount(metrics, 'proc_records_total', alias) === 1
          && outputCount(metrics, 'dropped_records_total', alias) === 0;
      });
    });
    const body = await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8');
    for (const id of ids) assert.equal(body.split(id).length - 1, 1, `${id} duplicated`);

    // 故意只读取 TCP 数据而不发 Forward ACK，核对发送端不会计入成功。
    if (spawnSync('docker', ['image', 'inspect', noAckImage], { stdio: 'ignore' }).status !== 0) docker(['pull', noAckImage]);
    docker(['rm', '-f', receiver]);
    docker(['run', '-d', '--name', receiver, '--network', network,
      '--entrypoint', 'node', noAckImage, '-e',
      `require('node:net').createServer(socket => { let received = ''; socket.on('data', chunk => { received += chunk.toString('latin1'); if (received.includes('${unacknowledgedId}')) console.log('NO_ACK_ID ${unacknowledgedId}'); }); }).listen(24224, '0.0.0.0')`]);
    await appendFile(path.join(directory, 'priority.log'), `${unacknowledgedId} pending\n`);
    await waitFor('no-ACK receiver event', () => docker(['logs', receiver]).includes(`NO_ACK_ID ${unacknowledgedId}`), 30000);
    await waitFor('missing-ACK retry', async () => {
      const metrics = await senderMetrics(mappedPort);
      return outputCount(metrics, 'retries_total', 'fullnet_priority_forward') > 0;
    }, 60000);
    const withoutAck = await senderMetrics(mappedPort);
    assert.equal(outputCount(withoutAck, 'proc_records_total', 'fullnet_priority_forward'), 1);
    assert.equal(outputCount(withoutAck, 'dropped_records_total', 'fullnet_priority_forward'), 0);
    assert.equal((await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8')).includes(unacknowledgedId), false);

    // 在接收端已读到数据而未回 ACK 的窗口杀死采集器；删除源文件以排除 Tail 重读。
    docker(['kill', '--signal=KILL', sender]);
    docker(['rm', sender]);
    await Promise.all(['priority.log', 'b2.log'].map((name) => rm(path.join(directory, name))));

    await writeFile(path.join(directory, 'receiver-buffered.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    storage.path /work/receiver-buffer
    storage.sync full
    storage.checksum on

[INPUT]
    Name forward
    Listen 0.0.0.0
    Port 24224
    storage.type filesystem

[OUTPUT]
    Name forward
    Match *
    Host 127.0.0.1
    Port 1
    Retry_Limit False
`);
    docker(['rm', '-f', receiver]);
    docker(['run', '-d', '--name', receiver, '--network', network,
      '--mount', `type=bind,source=${directory},target=/work`, image, '-c', '/work/receiver-buffered.conf']);
    docker(['run', '-d', '--name', sender, '--network', network, '-p', '127.0.0.1::2020',
      '--mount', `type=bind,source=${directory},target=/work`, image, '-c', '/work/sender.conf']);
    mappedPort = docker(['port', sender, '2020/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(mappedPort, 'Docker did not publish the restarted sender metrics port');
    await waitFor('pre-ACK sender crash recovery and buffered receiver ACK', async () => {
      const metrics = await senderMetrics(mappedPort);
      return outputCount(metrics, 'proc_records_total', 'fullnet_priority_forward') === 1
        && outputCount(metrics, 'dropped_records_total', 'fullnet_priority_forward') === 0
        && outputCount(metrics, 'proc_records_total', 'fullnet_b2_forward') === 0;
    }, 60000);
    assert.equal((await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8')).includes(unacknowledgedId), false);
    docker(['kill', '--signal=KILL', receiver]);
    docker(['rm', receiver]);
    await writeFile(path.join(directory, 'receiver-recovery.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    storage.path /work/receiver-buffer
    storage.sync full
    storage.checksum on

[INPUT]
    Name forward
    Listen 0.0.0.0
    Port 24224
    storage.type filesystem

[OUTPUT]
    Name file
    Match *
    Path /work/out
    File accepted.jsonl
    Format plain
`);
    docker(['run', '-d', '--name', receiver, '--network', network,
      '--mount', `type=bind,source=${directory},target=/work`, image, '-c', '/work/receiver-recovery.conf']);
    await waitFor('ACKed receiver backlog recovery', async () => {
      const recovered = await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8');
      return recovered.includes(unacknowledgedId);
    }, 30000);
    const recovered = await readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8');
    assert.equal(recovered.split(unacknowledgedId).length - 1, 1);
    process.stdout.write('Forward ACK, pre-ACK sender SIGKILL recovery, and ACKed receiver SIGKILL recovery passed.\n');
  } catch (error) {
    for (const name of [sender, receiver]) {
      const logs = spawnSync('docker', ['logs', '--tail', '60', name], { encoding: 'utf8' });
      if (logs.status === 0) process.stderr.write(`${name}:\n${logs.stdout}${logs.stderr}`);
    }
    throw error;
  } finally {
    for (const name of [sender, receiver]) spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' });
    if (networkCreated) spawnSync('docker', ['network', 'rm', network], { stdio: 'ignore' });
    await rm(directory, { recursive: true, force: true });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-forward-ack-smoke.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Forward ACK smoke requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
