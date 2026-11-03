import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { withTestRun } from './test-run-context.mjs';

// 复用固定 Debezium 镜像内的 JDK 模块与 Connect API；产物仅写系统临时目录。
const cwd = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sources = ['deploy/messaging/connect-filter/src/FullNetOutboxStreamFilter.java',
  'deploy/messaging/connect-filter/test/FullNetOutboxStreamFilterTest.java'];
const files = [...sources, 'scripts/testing/build-kafka-stream-filter.mjs', 'scripts/testing/test-run-context.mjs',
  'scripts/testing/build-resource-pool.mjs'];
const sha = value => createHash('sha256').update(value).digest('hex');
const identities = async () => Promise.all(files.map(async file => ({ file, sha256: sha(await readFile(path.join(cwd, file))) })));
const image = 'sha256:4f807bd409b47268077e1142524f368db170560d5caceaee48a6a7a7fd288570';
const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-kafka-stream-filter-'));
await mkdir(path.join(directory, 'classes'), { recursive: true });
await mkdir(path.join(directory, 'service', 'META-INF', 'services'), { recursive: true });
await writeFile(path.join(directory, 'service', 'META-INF', 'services', 'org.apache.kafka.connect.transforms.Transformation'), 'net.full.connect.FullNetOutboxStreamFilter\n');
const run = args => new Promise((resolve, reject) => {
  const process = spawn('docker', args, { cwd, stdio: 'inherit', windowsHide: true });
  process.on('error', reject); process.on('close', code => code === 0 ? resolve() : reject(Error('Docker exit ' + code)));
});
const base = ['run', '--rm', '--network', 'none', '--hostname', 'fullnet-filter-build', '--add-host', 'fullnet-filter-build:127.0.0.1', '--memory', '512m', '--memory-swap', '512m', '--cpus', '1',
  '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--pids-limit', '128', '--user', '0',
  '-v', `${cwd.replaceAll('\\', '/')}:/src:ro`, '-v', `${directory.replaceAll('\\', '/')}:/out`, '--entrypoint', 'java', image];
await withTestRun({ cwd: directory, build: true, buildBudget: { cpu: 1, memoryGiB: 2 }, timeoutMs: 45 * 60_000 }, async () => {
  const before = await identities();
  await run([...base, '-m', 'jdk.compiler/com.sun.tools.javac.Main', '-source', '21', '-target', '21', '-cp', '/kafka/libs/connect-api-4.1.1.jar:/kafka/libs/kafka-clients-4.1.1.jar', '-d', '/out/classes', ...sources.map(file => '/src/' + file)]);
  await run([...base, '-cp', '/kafka/libs/*:/out/classes', 'net.full.connect.FullNetOutboxStreamFilterTest']);
  await run([...base, '-m', 'jdk.jartool/sun.tools.jar.Main', '--create', '--date=2026-01-01T00:00:00Z', '--file', '/out/fullnet-outbox-stream-filter.jar',
    '-C', '/out/classes', 'net/full/connect/FullNetOutboxStreamFilter.class', '-C', '/out/service', 'META-INF/services/org.apache.kafka.connect.transforms.Transformation']);
  if (JSON.stringify(before) !== JSON.stringify(await identities())) throw Error('过滤器源码在构建期间变化，禁止登记。');
  const jar = path.join(directory, 'fullnet-outbox-stream-filter.jar');
  await writeFile(path.join(directory, 'manifest.json'), JSON.stringify({ image, inputs: before, jar, sha256: sha(await readFile(jar)) }, null, 2));
  console.log('FULLNET_STREAM_FILTER', JSON.stringify({ directory, jar, sha256: sha(await readFile(jar)) }));
});
