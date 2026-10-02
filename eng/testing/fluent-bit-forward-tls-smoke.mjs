import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { appendFile, copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fluentBitImage = 'cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c';
const certImage = 'alpine:3.20@sha256:d9e853e87e55526f6b2917df91a2115c36dd7c696a35be12163d44e6e2a4b6bc';
const suffix = `${process.pid}-${randomUUID().slice(0, 8)}`;
const network = `fullnet-log-tls-${suffix}`;
const receiver = `fullnet-log-tls-receiver-${suffix}`;
const sender = `fullnet-log-tls-sender-${suffix}`;
const goodIds = ['0199aabb-ccdd-7000-8000-000000000031', '0199aabb-ccdd-7000-8000-000000000032'];
const rejectedId = '0199aabb-ccdd-7000-8000-000000000033';
const hostnameRejectedId = '0199aabb-ccdd-7000-8000-000000000034';
const b2RejectedId = '0199aabb-ccdd-7000-8000-000000000035';
const b2HostnameRejectedId = '0199aabb-ccdd-7000-8000-000000000036';

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return result.stdout.trim();
}

function pullIfMissing(image) {
  if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
}

async function waitFor(description, predicate, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if (await predicate()) return;
    } catch {
      // 文件或指标端口可能尚未就绪。
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${description} timed out`);
}

function selectedOutputs(values, host) {
  const blocks = [...values.matchAll(/^    \[OUTPUT\]\r?\n[\s\S]*?(?=^    \[OUTPUT\]|^  customParsers:)/gm)]
    .map((match) => match[0]);
  return ['fullnet_priority_forward', 'fullnet_b2_forward'].map((alias) => {
    const block = blocks.find((candidate) => candidate.includes(`Alias               ${alias}`));
    assert.ok(block, `${alias} missing`);
    for (const option of [/Require_ack_response\s+On/, /tls\s+On/, /tls\.verify\s+On/, /tls\.verify_hostname\s+On/, /tls\.ca_file\s+\/fluent-bit\/tls\/ca\.crt/]) {
      assert.match(block, option, `${alias} must keep its production TLS/ACK option`);
    }
    // 只替换测试网络地址；证书路径、ACK、重试和 TLS 选项均来自候选配置。
    return block.replace(/^    /gm, '')
      .replace(/^    Host\s+.+$/m, `    Host ${host}`)
      .trimEnd() + '\n';
  }).join('\n');
}

async function configureSender(directory, values, logName, dbName, host = 'receiver', b2LogName = 'b2.log', b2DbName = 'b2.db') {
  const sync = values.match(/^        storage\.sync\s+(normal|full)\s*$/m)?.[1];
  assert.ok(sync, 'Collector candidate must declare storage.sync');
  await writeFile(path.join(directory, 'sender.conf'), `[SERVICE]
    Flush 1
    Log_Level info
    HTTP_Server On
    HTTP_Listen 0.0.0.0
    HTTP_Port 2020
    storage.path /work/sender-buffer
    storage.sync ${sync}

[INPUT]
    Name tail
    Tag fullnet.priority.event
    Path /work/${logName}
    DB /work/${dbName}
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

[INPUT]
    Name tail
    Tag fullnet.b2.event
    Path /work/${b2LogName}
    DB /work/${b2DbName}
    Read_From_Head On
    Refresh_Interval 1
    storage.type filesystem

${selectedOutputs(values, host)}`);
}

async function readAccepted(directory) {
  return readFile(path.join(directory, 'out', 'accepted.jsonl'), 'utf8');
}

async function run() {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-log-forward-tls-'));
  let networkCreated = false;
  try {
    const values = await readFile(path.join(root, 'deploy/observability/fluent-bit-values.yaml'), 'utf8');
    await Promise.all(['out', 'sender-buffer', 'tls-good', 'tls-bad'].map((name) => mkdir(path.join(directory, name))));
    await writeFile(path.join(directory, 'receiver.ext'), 'subjectAltName=DNS:receiver\nbasicConstraints=CA:FALSE\nextendedKeyUsage=serverAuth\nkeyUsage=digitalSignature,keyEncipherment\n');
    pullIfMissing(certImage);
    pullIfMissing(fluentBitImage);
    // 证书只存在于临时目录；测试私有 CA 与生产信任根完全隔离。
    docker(['run', '--rm', '--mount', `type=bind,source=${directory},target=/work`, '-w', '/work', certImage,
      'sh', '-ec', 'apk add --no-cache openssl >/dev/null; openssl req -x509 -newkey rsa:2048 -sha256 -nodes -keyout ca.key -out ca.crt -days 1 -subj /CN=FullNETTestCA -addext basicConstraints=critical,CA:TRUE >/dev/null 2>&1; openssl req -newkey rsa:2048 -nodes -keyout receiver.key -out receiver.csr -subj /CN=receiver >/dev/null 2>&1; openssl x509 -req -in receiver.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out receiver.crt -days 1 -sha256 -extfile receiver.ext >/dev/null 2>&1; openssl req -x509 -newkey rsa:2048 -sha256 -nodes -keyout bad-ca.key -out bad-ca.crt -days 1 -subj /CN=UntrustedTestCA >/dev/null 2>&1; chmod 644 receiver.key']);
    await Promise.all([
      copyFile(path.join(directory, 'ca.crt'), path.join(directory, 'tls-good', 'ca.crt')),
      copyFile(path.join(directory, 'bad-ca.crt'), path.join(directory, 'tls-bad', 'ca.crt')),
    ]);
    await writeFile(path.join(directory, 'receiver.conf'), `[SERVICE]
    Flush 1
    Log_Level info

[INPUT]
    Name forward
    Listen 0.0.0.0
    Port 24224
    tls On
    tls.crt_file /work/receiver.crt
    tls.key_file /work/receiver.key

[OUTPUT]
    Name file
    Match *
    Path /work/out
    File accepted.jsonl
    Format plain
`);
    docker(['network', 'create', network]);
    networkCreated = true;
    docker(['run', '-d', '--name', receiver, '--network', network, '--network-alias', 'receiver', '--network-alias', 'wrong-receiver',
      '--mount', `type=bind,source=${directory},target=/work`, fluentBitImage, '-c', '/work/receiver.conf']);
    await Promise.all(['priority.log', 'b2.log'].map((name) => writeFile(path.join(directory, name), '')));
    await configureSender(directory, values, 'priority.log', 'priority.db');
    docker(['run', '-d', '--name', sender, '--network', network, '-p', '127.0.0.1::2020',
      '--mount', `type=bind,source=${directory},target=/work`,
      '--mount', `type=bind,source=${path.join(directory, 'tls-good')},target=/fluent-bit/tls,readonly`,
      fluentBitImage, '-c', '/work/sender.conf']);
    const port = docker(['port', sender, '2020/tcp']).match(/127\.0\.0\.1:(\d+)/)?.[1];
    assert.ok(port, 'sender metrics port missing');
    await Promise.all([
      appendFile(path.join(directory, 'priority.log'), `${goodIds[0]} priority\n`),
      appendFile(path.join(directory, 'b2.log'), `${goodIds[1]} ordinary\n`),
    ]);
    await waitFor('TLS Forward receiver records', async () => {
      const body = await readAccepted(directory);
      return goodIds.every((id) => body.includes(id));
    });
    await waitFor('TLS Forward ACK metrics', async () => {
      const response = await fetch(`http://127.0.0.1:${port}/api/v1/metrics/prometheus`, { signal: AbortSignal.timeout(2000) });
      if (!response.ok) return false;
      const metrics = await response.text();
      return ['fullnet_priority_forward', 'fullnet_b2_forward'].every((alias) => {
        const count = metrics.match(new RegExp(`^fluentbit_output_proc_records_total\\{name="${alias}"\\}\\s+(\\d+)`, 'm'));
        return count && Number(count[1]) === 1;
      });
    });
    const accepted = await readAccepted(directory);
    for (const id of goodIds) assert.equal(accepted.split(id).length - 1, 1, `${id} duplicated`);

    docker(['rm', '-f', sender]);
    await writeFile(path.join(directory, 'rejected.log'), '');
    await writeFile(path.join(directory, 'b2-rejected.log'), '');
    await configureSender(directory, values, 'rejected.log', 'rejected.db', 'receiver', 'b2-rejected.log', 'b2-rejected.db');
    docker(['run', '-d', '--name', sender, '--network', network, '-p', '127.0.0.1::2020',
      '--mount', `type=bind,source=${directory},target=/work`,
      '--mount', `type=bind,source=${path.join(directory, 'tls-bad')},target=/fluent-bit/tls,readonly`,
      fluentBitImage, '-c', '/work/sender.conf']);
    await Promise.all([
      appendFile(path.join(directory, 'rejected.log'), `${rejectedId} untrusted\n`),
      appendFile(path.join(directory, 'b2-rejected.log'), `${b2RejectedId} untrusted\n`),
    ]);
    await waitFor('untrusted CA rejection', async () => {
      const logs = spawnSync('docker', ['logs', sender], { encoding: 'utf8' });
      assert.equal(logs.status, 0);
      const body = `${logs.stdout}${logs.stderr}`;
      return /certificate (?:verify|verification|validation) failed/i.test(body)
        && ['fullnet_priority_forward', 'fullnet_b2_forward'].every((alias) =>
          body.includes(`[output:forward:${alias}] no upstream connections available`));
    });
    const afterUntrusted = await readAccepted(directory);
    for (const id of [rejectedId, b2RejectedId]) {
      assert.ok(!afterUntrusted.includes(id), `receiver accepted ${id} through an untrusted CA`);
    }

    docker(['rm', '-f', sender]);
    await writeFile(path.join(directory, 'wrong-host.log'), '');
    await writeFile(path.join(directory, 'b2-wrong-host.log'), '');
    await configureSender(directory, values, 'wrong-host.log', 'wrong-host.db', 'wrong-receiver', 'b2-wrong-host.log', 'b2-wrong-host.db');
    docker(['run', '-d', '--name', sender, '--network', network,
      '--mount', `type=bind,source=${directory},target=/work`,
      '--mount', `type=bind,source=${path.join(directory, 'tls-good')},target=/fluent-bit/tls,readonly`,
      fluentBitImage, '-c', '/work/sender.conf']);
    await Promise.all([
      appendFile(path.join(directory, 'wrong-host.log'), `${hostnameRejectedId} mismatched-host\n`),
      appendFile(path.join(directory, 'b2-wrong-host.log'), `${b2HostnameRejectedId} mismatched-host\n`),
    ]);
    await waitFor('certificate hostname rejection', async () => {
      const logs = spawnSync('docker', ['logs', sender], { encoding: 'utf8' });
      assert.equal(logs.status, 0);
      const body = `${logs.stdout}${logs.stderr}`;
      return /hostname|host name|name mismatch/i.test(body)
        && ['fullnet_priority_forward', 'fullnet_b2_forward'].every((alias) =>
          body.includes(`[output:forward:${alias}] no upstream connections available`));
    });
    const afterWrongHost = await readAccepted(directory);
    for (const id of [hostnameRejectedId, b2HostnameRejectedId]) {
      assert.ok(!afterWrongHost.includes(id), `receiver accepted ${id} with a mismatched certificate hostname`);
    }
    process.stdout.write('TLS Forward ACK, untrusted CA rejection, and hostname rejection passed.\n');
  } finally {
    for (const name of [sender, receiver]) spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' });
    if (networkCreated) spawnSync('docker', ['network', 'rm', network], { stdio: 'ignore' });
    await rm(directory, { recursive: true, force: true });
  }
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/fluent-bit-forward-tls-smoke.mjs [--docker-desktop]\n');
} else if (process.platform !== 'linux' && !process.argv.includes('--docker-desktop')) {
  throw new Error('Forward TLS smoke requires Linux CI or explicit --docker-desktop.');
} else {
  await run();
}
